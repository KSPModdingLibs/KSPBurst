using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
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

        [CanBeNull] private string _pluginBackup;
        [NotNull] public static string ExtractDir { get; private set; } = string.Empty;
        public static CompilerStatus Status { get; private set; } = CompilerStatus.NotStarted;

        private void Awake()
        {
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
            Task<string> task = Task.Factory.StartNew(Generate);

            // wait for burst to complete
            while (!task.IsCompleted) yield return null;

            // check the status
            if (task.Exception is not null || !string.IsNullOrEmpty(task.Result))
            {
                LogError("Burst compiler terminated in an error");
                if (task.Exception is not null)
                    Debug.LogException(task.Exception);
                else
                    LogError(task.Result);

                Status = CompilerStatus.Error;

                // move back the old plugin to where it was
                if (!string.IsNullOrEmpty(_pluginBackup))
                {
                    Log($"Restoring burst generated plugin from backup {_pluginBackup}");
                    File.Move(_pluginBackup, PathUtil.OutputLibraryPath);
                }
            }
            else
            {
                Status = CompilerStatus.Completed;

                // old plugin is no longer needed
                if (!string.IsNullOrEmpty(_pluginBackup) && File.Exists(_pluginBackup))
                {
                    File.Delete(_pluginBackup);
                    Log($"Deleted burst generated plugin backup {_pluginBackup}");
                }
            }

            // destroy this object since burst will not be called again
            Destroy(this);
        }

        [CanBeNull]
        private string Generate()
        {
            // extract compiler
            string errorString = UnpackBurstCompiler(out string packageDir);
            if (!string.IsNullOrEmpty(errorString) || string.IsNullOrEmpty(packageDir))
                return $"Burst package not found: {errorString}";

            // check if any plugins have changed since last time
            AssemblyUtil.AssemblyVersionChange[] changes = CollectPluginChanges(packageDir);
            if (!changes.AnyChanges() && !string.IsNullOrEmpty(_pluginBackup) && File.Exists(_pluginBackup))
            {
                // nothing changed and backup exists, rename backup to original name and skip expensive burst invocation
                Log("No plugin changes detected, skipping burst generation");
                File.Move(_pluginBackup, PathUtil.OutputLibraryPath);
                return null;
            }

            string message = RunBurstCompiler(packageDir);

            if (message is null)
            {
                // changes found and burst completed successfully, cache plugin versions
                AssemblyUtil.CachePluginVersions(changes, packageDir);
            }

            return message;
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

        [CanBeNull]
        private static string RunBurstCompiler([NotNull] string directory)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));

            // load burst command line args
            ConfigNode node = GameDatabase.Instance.GetConfigNode($"{PathUtil.ModFolderName}/{PathUtil.ModName}");
            List<string> args = BurstOptions.LoadArgs(node);

            // output command line arguments to log files
            string logDir = PathUtil.ModLogsDir;
            if (!Directory.Exists(logDir))
                Directory.CreateDirectory(logDir);

            string burstExecutable = Path.Combine(directory, BclRelativePath);
            var argStr = $"{burstExecutable}\n  {string.Join("\n  ", args)}";
            File.WriteAllText($"{logDir}/command_line.log", argStr);
            LogFormat("Burst arguments:\n{0}", argStr);

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
                // propagate error to main thread
                return $"Failed to start burst compiler '{burstExecutable}'";

            // read from the streams while burst is running to avoid filling stream buffers
            using var outputRedirect = new StreamRedirect($"{logDir}/info.log");
            using var errorRedirect = new StreamRedirect($"{logDir}/error.log");

            process.OutputDataReceived += outputRedirect.Write;
            process.ErrorDataReceived += errorRedirect.Write;

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // wait for the process to complete
            process.WaitForExit();

            // print outputs to KSP log
            if (!outputRedirect.IsEmpty)
                LogFormat("Burst output:\n{0}", outputRedirect);

            if (!errorRedirect.IsEmpty)
                LogErrorFormat("Burst error:\n{0}", errorRedirect);

            // non-zero exit code means an error
            if (process.ExitCode != 0)
                return $"Burst compiler exited with code {process.ExitCode}";

            return string.IsNullOrEmpty(PathUtil.FindOutputLibrary()) ? "Burst did not generate a library" : null;
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
            Compression.CleanOldFiles(ExtractDir);
            Compression.ExtractArchive(archive, burstDir);

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

        internal static void Log([CanBeNull] string message)
        {
            Debug.LogFormat($"[{PathUtil.ModName}]: " + "{0}", message);
        }

        [StringFormatMethod("format")]
        internal static void LogFormat([NotNull] string format, params object[] args)
        {
            if (format is null) throw new ArgumentNullException(nameof(format));
            Debug.LogFormat($"[{PathUtil.ModName}]: " + format, args);
        }

        internal static void LogError([CanBeNull] string message)
        {
            Debug.LogErrorFormat($"[{PathUtil.ModName}]: " + "{0}", message);
        }

        [StringFormatMethod("format")]
        internal static void LogErrorFormat([NotNull] string format, params object[] args)
        {
            if (format is null) throw new ArgumentNullException(nameof(format));
            Debug.LogErrorFormat($"[{PathUtil.ModName}]: " + format, args);
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
                return _builder.ToString();
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