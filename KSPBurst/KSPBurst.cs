using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using UniLinq;
using Unity.Burst;
using UnityEngine;
using Debug = UnityEngine.Debug;

// ReSharper disable MemberCanBePrivate.Global

namespace KSPBurst
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class KSPBurst : MonoBehaviour
    {
        public enum CompilerStatus
        {
            NotStarted,
            Started,
            Completed,
            Error
        }

        public const string BclRelativePath = "package/.Runtime/bcl.exe";
        public const string BurstPackagePattern = "*burst*@*";

        private static Thread _mainThread;
        private static readonly List<string> LogMessages = new();
        private static readonly List<string> ErrorMessages = new();

        [CanBeNull] private string _pluginBackup;
        [CanBeNull] private Task<string> _pluginHashTask;
        [NotNull] public static string ExtractDir { get; private set; } = string.Empty;
        public static CompilerStatus Status { get; private set; } = CompilerStatus.NotStarted;

        internal static bool InMainThread => Thread.CurrentThread == _mainThread;

        private void Awake()
        {
            _mainThread = Thread.CurrentThread;

            PathUtil.Initialize();
            ExtractDir = Path.Combine(PathUtil.KspDir, "PluginData");
        }

        private void Start()
        {
            // hide the burst plugin from unity until burst compiler finishes
            string library = PathUtil.OutputLibraryPath;

            // Generate a new backup name with every invocation so that permission issues
            // on old backups do not break everything.
            string hash = Guid.NewGuid().ToString("N").Substring(0, 8);
            string backup = $"{library}.{hash}.bak";

            // Make a best-effort attempt to clean up old backups.
            try
            {
                DeleteOldBackups();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[KSPBurst] Failed to delete old backups: {e}");
            }

            _pluginBackup = null;

            try
            {
                File.Move(library, backup);
                Log($"Backed up burst generated plugin to {backup}");
                _pluginBackup = backup;
                _pluginHashTask = Task.Run(() => ComputeFileHash(backup));
            }
            catch (FileNotFoundException)
            {
                // The plugin doesn't exist, nothing we need to do.
            }
            catch (Exception e)
            {
                LogError($"Failed to backup burst plugin: {e}");
                
                try
                {
                    File.Delete(library);
                }
                catch (Exception e2)
                {
                    LogError($"Failed to delete burst plugin! Loaded plugin may be invalid: {e2}");
                }
            }

            // if there's no ModuleManager, generate burst immediately since config options cannot be patched
            if (!AssemblyLoader.loadedAssemblies.Any(a => a.assembly.GetName().Name.Contains("ModuleManager")))
                ModuleManagerPostLoad();

            LoadingScreen.Instance.loaders.Add(gameObject.AddComponent<BurstLoadingSystem>());
        }

        public void ModuleManagerPostLoad()
        {
            // only run once since assemblies cannot be reloaded mid-game
            if (Status != CompilerStatus.NotStarted) return;

            StartCoroutine(BurstCompile());
        }

        private void DeleteOldBackups()
        {
            string library = PathUtil.OutputLibraryPath;
            string dir = Path.GetDirectoryName(library);
            string name = Path.GetFileName(library);

            foreach (string backup in Directory.GetFiles(dir, $"{name}.*.bak"))
            {
                try
                {
                    File.Delete(backup);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[KSPBurst] Failed to delete old backup file {backup}: {e}");
                }
            }
        }
        private IEnumerator BurstCompile()
        {
            Status = CompilerStatus.Started;

            // run burst in a separate thread to avoid slowing down KSP loading times even more
            Task<BurstCompilerResult> task = Task.Factory.StartNew(Generate);

            // wait for burst to complete
            while (!task.IsCompleted)
            {
                yield return null;
            }

            // Note this must not be in the loop above because the container is not threadsafe
            FlushMessages();

            // set the status before anything else can throw errors
            switch (task.Status)
            {
                case TaskStatus.Created:
                case TaskStatus.WaitingForActivation:
                case TaskStatus.WaitingToRun:
                case TaskStatus.Running:
                case TaskStatus.WaitingForChildrenToComplete:
                    Status = CompilerStatus.Started;
                    break;
                case TaskStatus.RanToCompletion:
                    Status = CompilerStatus.Completed;
                    break;
                case TaskStatus.Canceled:
                case TaskStatus.Faulted:
                    Status = CompilerStatus.Error;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            // accessing task.Result with non-null exception throws the exception
            if (task.Exception is not null)
            {
                LogError("Burst compiler terminated with an exception");
                Debug.LogException(task.Exception);
            }
            else
            {
                if (!string.IsNullOrEmpty(task.Result.Stdout)) LogFormat("Burst stdout:\n{0}", task.Result.Stdout);
                if (!string.IsNullOrEmpty(task.Result.Stderr)) LogErrorFormat("Burst stderr:\n{0}", task.Result.Stderr);

                // check the status
                if (!string.IsNullOrEmpty(task.Result.ErrorMessage) || task.Result.ExitCode != 0)
                {
                    LogError("Burst compiler had errors");
                    if (!string.IsNullOrEmpty(task.Result.ErrorMessage))
                        LogError(task.Result.ErrorMessage);
                    if (task.Result.ExitCode != 0)
                        LogErrorFormat("Burst compiler exited with a non-zero exit code: {0}",
                            task.Result.ExitCode);
                }
            }

            if (!string.IsNullOrEmpty(_pluginBackup) && File.Exists(_pluginBackup))
            {
                if (!File.Exists(PathUtil.OutputLibraryPath))
                {
                    File.Move(_pluginBackup, PathUtil.OutputLibraryPath);
                    LogFormat("Plugin not generated, restoring backup");
                }
                else
                {
                    // old plugin is no longer needed
                    File.Delete(_pluginBackup);
                    Log($"Deleted burst generated plugin backup {_pluginBackup}");
                }
            }

            // Force static constructors to run, because otherwise they could be invoked on an async thread.
            // This needs to happen after burst compilation completes but before anything else
            // attempts to compile a function pointer on a background thread.
            if (BurstCompiler.IsEnabled)
                Log("Burst compilation is enabled");
            else
                Log("Burst compilation is disabled");


            // destroy this object since burst will not be called again
            Destroy(this);
        }

        private BurstCompilerResult Generate()
        {
            BurstCompilerResult result = new();

            // extract compiler
            string errorString = UnpackBurstCompiler(out string burstExecutable);
            if (!string.IsNullOrEmpty(errorString) || string.IsNullOrEmpty(burstExecutable))
            {
                result.ErrorMessage = $"Burst package not found: {errorString}";
                return result;
            }

            string cacheDir = GetPackageRoot(burstExecutable);

            // load config and resolve which assemblies to compile
            ConfigNode node = GameDatabase.Instance.GetConfigNode($"{PathUtil.ModFolderName}/{PathUtil.ModName}/{PathUtil.ModName}");
            AssemblyLoader.LoadedAssembly[] burstPlugins = AssemblyUtil.LoadedBurstPlugins();
            burstPlugins = AssemblyUtil.ApplyAssemblyOverrides(burstPlugins, GameDatabase.Instance.GetConfigNodes("KSPBURST_ASSEMBLY"));

            // check if any plugins have changed since last time
            AssemblyUtil.AssemblyVersionChange[] changes = CollectPluginChanges(cacheDir, burstPlugins);

            // load burst command line args
            string cwd = Directory.GetCurrentDirectory();
            List<string> args = BurstOptions.LoadArgs(node, burstPlugins, cwd);

            // output command line arguments to log files
            string logDir = PathUtil.ModLogsDir;
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            var argStr = $"{burstExecutable}\n  {string.Join("\n  ", args)}";
            var cliFile = $"{logDir}/command_line.log";
            var hashFile = $"{logDir}/plugin_hash.txt";

            bool NeedsRebuild()
            {
                if (changes.AnyChanges())
                {
                    Log("Mod DLLs have changed. Rebuilding...");
                    return true;
                }
                if (string.IsNullOrEmpty(_pluginBackup))
                    return true;
                if (!File.Exists(_pluginBackup))
                    return true;

                string lastArgs = File.Exists(cliFile)
                    ? File.ReadAllText(cliFile)
                    : null;
                if (lastArgs != argStr)
                {
                    Log("Burst compiler args have changed since last run. Rebuilding...");
                    return true;
                }
                
                string lastHash = File.Exists(hashFile)
                    ? File.ReadAllText(hashFile)
                    : null;
                string currentHash = _pluginHashTask?.Result;

                if (string.IsNullOrEmpty(lastHash) || string.IsNullOrEmpty(currentHash))
                    return true;
                if (lastHash != currentHash)
                {
                    Log("Burst library changed on disk unexpectedly! Rebuilding...");
                    return true;
                }

                Log("Nothing has changed since last run, skipping burst generation");
                return false;
            }

            if (!NeedsRebuild())
            {
                // nothing changed and backup exists, skip expensive burst invocation
                return result;
            }

            // save command line arguments for later inspection/next run
            File.WriteAllText(cliFile, argStr);

            // clean up log files from any previous compilation before starting a new one
            File.Delete($"{logDir}/KSPBurst-stdout.log");
            File.Delete($"{logDir}/KSPBurst-stderr.log");
            File.Delete(hashFile);

            LogFormat("Burst called with arguments in {0}:\n{1}", cwd, argStr);
            result = RunBurstCompiler(burstExecutable, args, cwd, logDir, cacheDir);

            if (!string.IsNullOrEmpty(result.ErrorMessage) || result.ExitCode != 0)
                return result;

            // changes found and burst completed successfully, cache plugin versions
            AssemblyUtil.CachePluginVersions(changes, cacheDir);

            // save hash of the newly generated library so the next run can detect external overwrites
            if (File.Exists(PathUtil.OutputLibraryPath))
            {
                var hash = ComputeFileHash(PathUtil.OutputLibraryPath);
                if (hash is not null)
                    File.WriteAllText(hashFile, hash);
            }

            return result;
        }

        // ReSharper disable once ReturnTypeCanBeEnumerable.Local
        [NotNull]
        private static AssemblyUtil.AssemblyVersionChange[] CollectPluginChanges([NotNull] string cacheDir,
            [NotNull] AssemblyLoader.LoadedAssembly[] burstPlugins)
        {
            if (string.IsNullOrWhiteSpace(cacheDir))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(cacheDir));
            if (burstPlugins is null) throw new ArgumentNullException(nameof(burstPlugins));

            AssemblyLoader.LoadedAssembly[] dependencies = AssemblyUtil.GetDependencies(burstPlugins);
            AssemblyUtil.AssemblyVersion[] loadedVersions =
                AssemblyUtil.LoadedPluginVersions(burstPlugins.Concat(dependencies).ToArray());
            AssemblyUtil.AssemblyVersion[] cachedVersions = AssemblyUtil.LoadPluginVersionsFromCache(cacheDir);
            AssemblyUtil.AssemblyVersionChange[] changes = AssemblyUtil.ComputeChanges(loadedVersions, cachedVersions);
            LogFormat(
                """
                
                KSPBurst requires that DLLs wanting to be burst-compiled must have either a KSPAssemblyDependency
                on KSPBurst or manually opt-in to being compiled by declaring a KSPBURST_ASSEMBLY config.
                If you aren't seeing your DLL below this is likely the reason why.
                See https://github.com/KSPModdingLibs/KSPBurst for more information.

                Plugins found:
                {0}
                """,
                AssemblyUtil.Format(changes)
            );

            return changes;
        }

        private static BurstCompilerResult RunBurstCompiler([NotNull] string burstExecutable,
            [NotNull] IEnumerable<string> args, [CanBeNull] string workingDir, [NotNull] string logDir,
            [NotNull] string cacheDir)
        {
            if (burstExecutable is null) throw new ArgumentNullException(nameof(burstExecutable));
            if (args is null) throw new ArgumentNullException(nameof(args));
            if (logDir is null) throw new ArgumentNullException(nameof(logDir));
            if (cacheDir is null) throw new ArgumentNullException(nameof(cacheDir));

            BurstCompilerResult result = new();

            // write args to a file to avoid exceeding Windows' 32768 character command line limit
            string argFile = Path.Combine(cacheDir, "argfile.txt");
            File.WriteAllText(argFile, string.Join("\n", args));

            // run burst
            var info = new ProcessStartInfo(burstExecutable, $"\"@{argFile}\"")
            {
                CreateNoWindow = true, // don't need terminal popping up
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false, // needed for stream redirection
                WorkingDirectory = string.IsNullOrEmpty(workingDir) ? Directory.GetCurrentDirectory() : workingDir
            };
            using var process = new Process {StartInfo = info};
            bool started;

            try
            {
                started = process.Start();
            }
            catch (Exception ex)
            {
                LogError(ex.ToString());
                LogErrorFormat(
                    "Are you missing mono installation? https://www.mono-project.com/download/stable/#download-{0}",
                    PathUtil.SelectByPlatform("win", "lin", "mac"));

                throw;
            }

            if (!started)
            {
                // propagate error to main thread
                result.ErrorMessage = $"Failed to start burst compiler '{burstExecutable}'";
                return result;
            }

            // read from the streams while burst is running to avoid filling stream buffers
            using var outputRedirect = new StreamRedirect($"{logDir}/KSPBurst-stdout.log");
            using var errorRedirect = new StreamRedirect($"{logDir}/KSPBurst-stderr.log");

            process.OutputDataReceived += outputRedirect.Write;
            process.ErrorDataReceived += errorRedirect.Write;

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // wait for the process to complete
            process.WaitForExit();

            result.Stdout = outputRedirect.ToString();
            result.Stderr = errorRedirect.ToString();
            result.ExitCode = process.ExitCode;

            result.ErrorMessage = string.IsNullOrEmpty(PathUtil.FindOutputLibrary())
                ? "Burst did not generate a library"
                : null;

            return result;
        }

        [CanBeNull]
        private static string UnpackBurstCompiler([CanBeNull] out string burstExecutable)
        {
            burstExecutable = null;
            string modDir = PathUtil.ModDir;

            // use the latest burst package archive
            string archive = Directory.GetFiles(modDir, BurstPackagePattern).Where(Compression.IsArchive)
                .SelectGreatestVersion();

            // if archive is not found, check for existing compiler
            if (string.IsNullOrEmpty(archive))
            {
                burstExecutable = FindExistingBurstCompiler(ExtractDir);
                if (string.IsNullOrEmpty(burstExecutable))
                    return $"Could not find burst package archive in {modDir} or directory in {ExtractDir}";

                Log($"Using existing burst package from {burstExecutable}");
                return null;
            }

            string archiveName = Path.GetFileName(archive);

            // append assembly version to the extracted directory so that new versions may re-extract burst
            string burstDir =
                $"{ExtractDir}/{PathUtil.ModName}@{Assembly.GetExecutingAssembly().GetName().Version}-{Path.GetFileNameWithoutExtension(archiveName)}";

            if (Directory.Exists(burstDir))
            {
                if (ContainsBurstCompiler(burstDir))
                {
                    Log($"Burst package destination '{burstDir}' already exists, not extracting");
                    burstExecutable = Path.Combine(burstDir, BclRelativePath);
                    return null;
                }

                // try looking for existing version
                burstExecutable = FindExistingBurstCompiler(ExtractDir);
                if (string.IsNullOrEmpty(burstExecutable))
                    return $"Burst package destination '{burstDir}' already exists but it doesn't contain burst compiler and one wasn't found in {ExtractDir}";

                Log($"Burst package destination '{burstDir}' already exists but it doesn't contain burst compiler, using one from {burstExecutable}");
                return null;
            }

            // have to extract the archive, clean up old extracted files first
            List<string> cleaned = Compression.CleanOldFiles(ExtractDir);
            if (cleaned.Count > 0)
                LogFormat("Directories cleaned: {0}", string.Join(", ", cleaned));
            Compression.ExtractArchive(archive, burstDir);
            Log($"{archive} extracted to {burstDir}");

            if (ContainsBurstCompiler(burstDir))
            {
                burstExecutable = Path.Combine(burstDir, BclRelativePath);
                return null;
            }

            // archive doesn't contain burst compiler? Look for existing one
            burstExecutable = FindExistingBurstCompiler(ExtractDir);
            if (string.IsNullOrEmpty(burstExecutable))
                return $"{archive} doesn't contain burst compiler and one wasn't found in {ExtractDir}";

            Log($"{archive} doesn't contain burst compiler, using one from {burstExecutable}");
            return null;
        }

        /// <summary>
        ///     Returns the burst package root directory from the full path to bcl.exe.
        ///     e.g. .../KSPBurst@1.7.4.6-com.unity.burst@1.7.4/package/.Runtime/bcl.exe
        ///       -> .../KSPBurst@1.7.4.6-com.unity.burst@1.7.4
        /// </summary>
        [CanBeNull]
        private static string GetPackageRoot([CanBeNull] string bclPath)
        {
            // bcl.exe -> .Runtime -> package -> root
            return Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(bclPath)));
        }

        private static bool ContainsBurstCompiler([CanBeNull] string directory)
        {
            return directory is not null && File.Exists(Path.Combine(directory, BclRelativePath));
        }

        [CanBeNull]
        private static string FindExistingBurstCompiler([NotNull] string directory)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));

            var paths = PathUtil.Glob(directory, $"*burst*/{BclRelativePath}")
                .OrderByDescending(PathUtil.PackageVersion)
                .ToArray();

            var message = new StringBuilder($"Found {paths.Length} potential compiler install{(paths.Length == 1 ? "" : "s")}:");
            foreach (var path in paths)
                message.AppendFormat("\n    {0}", path);
            Log(message.ToString());

            // any burst pattern is fine
            return paths.SelectGreatestVersion();
        }

        internal static void FlushMessages()
        {
            foreach (string message in LogMessages)
                Log(message);
            foreach (string message in ErrorMessages)
                LogError(message);

            LogMessages.Clear();
            ErrorMessages.Clear();
        }

        internal static void Log([CanBeNull] string message)
        {
            Debug.LogFormat($"[{PathUtil.ModName}]: " + "{0}", message);
        }

        [StringFormatMethod("format")]
        internal static void LogFormat([NotNull] string format, params object[] args)
        {
            if (format is null) throw new ArgumentNullException(nameof(format));
            if (!InMainThread)
            {
                LogMessages.Add(string.Format(format, args));
                return;
            }

            Debug.LogFormat($"[{PathUtil.ModName}]: " + format, args);
        }

        internal static void LogError([CanBeNull] string message)
        {
            if (!InMainThread)
            {
                ErrorMessages.Add(message);
                return;
            }

            Debug.LogErrorFormat($"[{PathUtil.ModName}]: " + "{0}", message);
        }

        [StringFormatMethod("format")]
        internal static void LogErrorFormat([NotNull] string format, params object[] args)
        {
            if (format is null) throw new ArgumentNullException(nameof(format));
            if (!InMainThread)
            {
                ErrorMessages.Add(string.Format(format, args));
                return;
            }

            Debug.LogErrorFormat($"[{PathUtil.ModName}]: " + format, args);
        }

        [CanBeNull]
        private static string ComputeFileHash([NotNull] string path)
        {
            try
            {
                using var sha = SHA256.Create();
                using var stream = File.OpenRead(path);
                byte[] hash = sha.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[{PathUtil.ModName}] Failed to hash plugin file '{path}': {e.Message}");
                return null;
            }
        }

        private struct BurstCompilerResult
        {
            public int ExitCode;
            [CanBeNull] public string Stdout;
            [CanBeNull] public string Stderr;
            [CanBeNull] public string ErrorMessage;
        }

        private class StreamRedirect : IDisposable
        {
            [NotNull] private readonly StringBuilder _builder = new();
            [CanBeNull] private readonly string _filename;
            [CanBeNull] private StreamWriter _writer;

            public StreamRedirect([CanBeNull] string filename)
            {
                _filename = filename;
            }

            public bool IsEmpty => _builder.Length == 0;

            public void Dispose()
            {
                _writer?.Dispose();
            }

            public override string ToString()
            {
                return IsEmpty ? string.Empty : _builder.ToString();
            }

            public void Write([CanBeNull] object sender, [CanBeNull] DataReceivedEventArgs e)
            {
                if (string.IsNullOrEmpty(e?.Data)) return;
                _builder.AppendLine(e.Data);

                // lazily construct writer so that the file doesn't change until any data has been received if any
                if (_writer is null && !string.IsNullOrEmpty(_filename)) _writer = new StreamWriter(_filename);
                _writer?.WriteLine(e.Data);
            }
        }
    }
}
