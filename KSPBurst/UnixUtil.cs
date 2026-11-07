using System;
using System.Collections.Generic;
using System.IO;
using JetBrains.Annotations;
using Mono.Unix.Native;

namespace KSPBurst
{
    /// <summary>
    ///     Helpers for running the burst compiler on platforms other than Windows.
    /// </summary>
    internal static class UnixUtil
    {
        private const string MonoExecutable = "mono";

        // Common install locations, used when the game was started with a PATH that doesn't include mono.
        private static readonly string[] FallbackMonoDirs = {"/usr/bin", "/usr/local/bin"};

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

            const FilePermissions mode = FilePermissions.S_IRWXU | FilePermissions.S_IRGRP | FilePermissions.S_IXGRP |
                                         FilePermissions.S_IROTH | FilePermissions.S_IXOTH;

            foreach (string file in files)
            {
                if (Syscall.chmod(file, mode) != 0)
                    KSPBurst.LogError($"Failed to make {file} executable: {Stdlib.GetLastError()}");
            }
        }
    }
}
