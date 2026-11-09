using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using JetBrains.Annotations;

namespace KSPBurst
{
    /// <summary>
    ///     Helpers for running the burst compiler on platforms other than Windows.
    /// </summary>
    internal static class UnixUtil
    {
        private const string MonoExecutable = "mono";

        // rwxr-xr-x
        private const uint ExecutableMode = 0x1ED;

        // Common install locations, used when the game was started with a PATH that doesn't include mono. This is the
        // usual case on macOS, where apps launched from Finder or Steam only get /usr/bin:/bin:/usr/sbin:/sbin.
        // The official macOS package installs into Mono.framework, and Homebrew uses /opt/homebrew on Apple Silicon.
        private static readonly string[] FallbackMonoDirs =
        {
            "/usr/bin",
            "/usr/local/bin",
            "/opt/homebrew/bin",
            "/Library/Frameworks/Mono.framework/Versions/Current/Commands"
        };

        // We call chmod directly because Unity 6's Managed folder doesn't include Mono.Posix.
        [DllImport("libc", SetLastError = true)]
        private static extern int chmod(string path, uint mode);

        /// <summary>
        ///     Finds the system mono executable.
        /// </summary>
        /// <returns>The full path to mono, or null if it could not be found</returns>
        [CanBeNull]
        internal static string FindMono()
        {
            List<string> directories = new();
            string path = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(path))
                directories.AddRange(path.Split(new[] {Path.PathSeparator}, StringSplitOptions.RemoveEmptyEntries));
            directories.AddRange(FallbackMonoDirs);

            foreach (string directory in directories)
            {
                string candidate = Path.Combine(directory, MonoExecutable);
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        ///     Sets the executable bit on the burst compiler and the native tools it runs. Zip extraction doesn't preserve
        ///     it. This runs before every launch, instead of during extraction, so that compilers which were extracted by
        ///     an earlier version or installed manually are covered too.
        /// </summary>
        /// <param name="burstExecutable">Full path to bcl.exe</param>
        internal static void MarkCompilerExecutable([NotNull] string burstExecutable)
        {
            if (burstExecutable is null) throw new ArgumentNullException(nameof(burstExecutable));

            // no executable bit on windows
            if (PathUtil.SelectByPlatform(true, false, false))
                return;

            string runtimeDir = Path.GetDirectoryName(burstExecutable) ?? string.Empty;
            string hostDir = Path.Combine(runtimeDir, PathUtil.SelectByPlatform(string.Empty, "hostlin", "hostmac"));
            string libraryPattern = PathUtil.SelectByPlatform(string.Empty, "libburst-*.so", "libburst-*.dylib");

            List<string> files = [burstExecutable];
            files.AddRange(Directory.GetFiles(runtimeDir, libraryPattern));
            if (Directory.Exists(hostDir))
                files.AddRange(Directory.GetFiles(hostDir));

            foreach (string file in files)
            {
                if (chmod(file, ExecutableMode) != 0)
                    KSPBurst.LogError($"Failed to make {file} executable: errno {Marshal.GetLastWin32Error()}");
            }
        }
    }
}
