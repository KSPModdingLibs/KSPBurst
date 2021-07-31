using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using UniLinq;
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
            _pluginBackup = library + ".bak";

            if (File.Exists(library))
            {
                // delete possible remnant from the last failed time
                if (File.Exists(_pluginBackup)) File.Delete(_pluginBackup);

                File.Move(library, _pluginBackup);
                Log($"Backed up burst generated plugin to {_pluginBackup}");
            }
            else if (!File.Exists(_pluginBackup))
            {
                _pluginBackup = null;
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

        private IEnumerator BurstCompile()
        {
            Status = CompilerStatus.Started;

            // run burst in a separate thread to avoid slowing down KSP loading times even more
            Task<BurstCompilerResult> task = Task.Factory.StartNew(Generate);

            // wait for burst to complete
            while (!task.IsCompleted)
            {
                FlushMessages();
                yield return null;
            }

            FlushMessages();
            if (!string.IsNullOrEmpty(task.Result.Stdout)) LogFormat("Burst stdout:\n{0}", task.Result.Stdout);
            if (!string.IsNullOrEmpty(task.Result.Stderr)) LogErrorFormat("Burst stderr:\n{0}", task.Result.Stderr);

            // check the status
            if (task.Exception is not null || !string.IsNullOrEmpty(task.Result.ErrorMessage) ||
                task.Result.ExitCode != 0)
            {
                LogError("Burst compiler had errors");
                if (task.Exception is not null)
                {
                    Debug.LogException(task.Exception);
                }
                else
                {
                    if (!string.IsNullOrEmpty(task.Result.ErrorMessage))
                        LogError(task.Result.ErrorMessage);
                    if (task.Result.ExitCode != 0)
                        LogErrorFormat("Burst compiler exited with a non-zero exit code: {0}", task.Result.ExitCode);
                }

                Status = CompilerStatus.Error;
            }
            else
            {
                Status = CompilerStatus.Completed;
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

            // destroy this object since burst will not be called again
            Destroy(this);
        }

        private BurstCompilerResult Generate()
        {
            BurstCompilerResult result = new();

            // extract compiler
            string errorString = UnpackBurstCompiler(out string packageDir);
            if (!string.IsNullOrEmpty(errorString) || string.IsNullOrEmpty(packageDir))
            {
                result.ErrorMessage = $"Burst package not found: {errorString}";
                return result;
            }

            // check if any plugins have changed since last time
            AssemblyUtil.AssemblyVersionChange[] changes = CollectPluginChanges(packageDir);

            // load burst command line args
            ConfigNode node = GameDatabase.Instance.GetConfigNode($"{PathUtil.ModFolderName}/{PathUtil.ModName}");
            List<string> args = BurstOptions.LoadArgs(node);

            // output command line arguments to log files
            string logDir = PathUtil.ModLogsDir;
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            string burstExecutable = Path.Combine(packageDir, BclRelativePath);
            var argStr = $"{burstExecutable}\n  {string.Join("\n  ", args)}";
            var cliFile = $"{logDir}/command_line.log";
            string lastRunArgs = null;
            if (File.Exists(cliFile))
                lastRunArgs = File.ReadAllText(cliFile);

            if (!changes.AnyChanges() && !string.IsNullOrEmpty(_pluginBackup) && File.Exists(_pluginBackup) &&
                lastRunArgs == argStr)
            {
                // nothing changed and backup exists, skip expensive burst invocation
                Log(lastRunArgs == argStr
                    ? "Burst compiler arguments haven't changed since last time, skipping burst generation"
                    : "No plugin changes detected, skipping burst generation");
                return result;
            }

            // save command line arguments for later inspection/next run
            File.WriteAllText(cliFile, argStr);
            result = RunBurstCompiler(burstExecutable, args, logDir);

            if (string.IsNullOrEmpty(result.ErrorMessage) && result.ExitCode == 0)
                // changes found and burst completed successfully, cache plugin versions
                AssemblyUtil.CachePluginVersions(changes, packageDir);

            return result;
        }

        // ReSharper disable once ReturnTypeCanBeEnumerable.Local
        [NotNull]
        private static AssemblyUtil.AssemblyVersionChange[] CollectPluginChanges([NotNull] string cacheDir)
        {
            if (string.IsNullOrWhiteSpace(cacheDir))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(cacheDir));

            AssemblyUtil.AssemblyVersion[] loadedVersions = AssemblyUtil.LoadedPluginVersions();
            AssemblyUtil.AssemblyVersion[] cachedVersions = AssemblyUtil.LoadPluginVersionsFromCache(cacheDir);
            AssemblyUtil.AssemblyVersionChange[] changes = AssemblyUtil.ComputeChanges(loadedVersions, cachedVersions);
            LogFormat("Plugins found:\n{0}", AssemblyUtil.Format(changes));

            return changes;
        }

        private static BurstCompilerResult RunBurstCompiler([NotNull] string burstExecutable,
            [NotNull] IEnumerable<string> args, string logDir)
        {
            BurstCompilerResult result = new();

            // run burst
            var info = new ProcessStartInfo(burstExecutable, string.Join(" ", args))
            {
                CreateNoWindow = true, // don't need terminal popping up
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false // needed for stream redirection
            };
            using var process = new Process {StartInfo = info};
            bool started;

            try
            {
                started = process.Start();
            }
            catch (Win32Exception)
            {
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
            using var outputRedirect = new StreamRedirect($"{logDir}/info.log");
            using var errorRedirect = new StreamRedirect($"{logDir}/error.log");

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
        private static string UnpackBurstCompiler([CanBeNull] out string burstDir)
        {
            burstDir = null;
            string modDir = PathUtil.ModDir;

            // use the latest burst package archive
            string archive = Directory.GetFiles(modDir, BurstPackagePattern).Where(Compression.IsArchive)
                .SelectGreatestVersion();

            // if archive is not found, check for existing compiler
            if (string.IsNullOrEmpty(archive))
            {
                burstDir = FindExistingBurstCompiler(ExtractDir);
                if (string.IsNullOrEmpty(burstDir))
                    return $"Could not find burst package archive in {modDir} or directory in {ExtractDir}";

                Log($"Using existing burst package from {burstDir}");
                return null;
            }

            string archiveName = Path.GetFileName(archive);

            // append assembly version to the extracted directory so that new versions may re-extract burst
            burstDir =
                $"{ExtractDir}/{PathUtil.ModName}@{Assembly.GetExecutingAssembly().GetName().Version}-{Path.GetFileNameWithoutExtension(archiveName)}";

            if (Directory.Exists(burstDir))
            {
                if (ContainsBurstCompiler(burstDir))
                {
                    Log($"Burst package destination '{burstDir}' already exists, not extracting");
                    return null;
                }

                // try looking for existing version
                string archivedDir = burstDir;
                burstDir = FindExistingBurstCompiler(ExtractDir);
                if (string.IsNullOrEmpty(burstDir))
                    return
                        $"Burst package destination '{archivedDir}' already exists but it doesn't contain burst compiler and one wasn't found in {ExtractDir}";

                Log(
                    $"Burst package destination '{archivedDir}' already exists but it doesn't contain burst compiler, using one from {burstDir}");
                return null;
            }

            // have to extract the archive, clean up old extracted files first
            List<string> cleaned = Compression.CleanOldFiles(ExtractDir);
            if (cleaned.Count > 0)
                LogFormat("Directories cleaned: {0}", string.Join(", ", cleaned));
            Compression.ExtractArchive(archive, burstDir);
            Log($"{archive} extracted to {burstDir}");

            if (ContainsBurstCompiler(burstDir)) return null;

            // archive doesn't contain burst compiler? Look for existing one
            burstDir = FindExistingBurstCompiler(ExtractDir);
            if (string.IsNullOrEmpty(burstDir))
                return $"{archive} doesn't contain burst compiler and one wasn't found in {ExtractDir}";

            Log($"{archive} doesn't contain burst compiler, using one from {burstDir}");
            return null;
        }

        private static bool ContainsBurstCompiler([CanBeNull] string directory)
        {
            return directory is not null && File.Exists(Path.Combine(directory, BclRelativePath));
        }

        [CanBeNull]
        private static string FindExistingBurstCompiler([NotNull] string directory)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));

            // any burst pattern is fine
            return PathUtil.Glob(directory, $"*burst*/{BclRelativePath}")
                .Select(Path.GetDirectoryName)
                .SelectGreatestVersion();
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
