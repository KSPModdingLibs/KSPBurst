using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;
using UnityEngine;

// ReSharper disable MemberCanBePrivate.Global

namespace KSPBurst
{
    public static class PathUtil
    {
        public const string LibraryName = "lib_burst_generated";
        public const string ModName = "KSPBurst";
        [NotNull] public static string DataDir { get; private set; } = string.Empty;
        [NotNull] public static string KspDir { get; private set; } = string.Empty;
        [NotNull] public static string ModDir { get; private set; } = string.Empty;
        [NotNull] public static string ModFolderName { get; private set; } = string.Empty;
        [NotNull] public static string ModLogsDir { get; private set; } = string.Empty;
        [NotNull] public static string OutputLibrary { get; private set; } = string.Empty;
        [NotNull] public static string NativeLibraryExtension { get; private set; } = string.Empty;
        [NotNull] public static string OutputLibraryPath { get; private set; } = string.Empty;

        [CanBeNull]
        public static string FindOutputLibrary()
        {
            string library = OutputLibraryPath;
            return File.Exists(library) ? library : null;
        }

        // ReSharper disable once ReturnTypeCanBeEnumerable.Global
        [NotNull]
        public static string[] Glob([NotNull] string directory, [NotNull] string pattern)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));

            var folder = new DirectoryInfo(directory);
            var matcher = new Matcher();
            matcher.AddInclude(pattern);
            return Glob(folder, matcher);
        }

        // ReSharper disable once UnusedMember.Global
        [NotNull]
        public static string[] Glob([NotNull] string directory, [NotNull] IEnumerable<string> patterns)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            if (patterns is null) throw new ArgumentNullException(nameof(patterns));

            var folder = new DirectoryInfo(directory);
            var matcher = new Matcher();
            foreach (string pattern in patterns) matcher.AddInclude(pattern);

            return Glob(folder, matcher);
        }

        public static bool HasExtension([NotNull] string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));

            int dotIndex = name.LastIndexOf('.');
            if (dotIndex == -1) return false;

            for (int i = dotIndex + 1; i < name.Length; ++i)
                if (!char.IsNumber(name[i]))
                    return true;

            return true;
        }

        [NotNull]
        public static Version PackageVersion([NotNull] string name)
        {
            if (name is null) throw new ArgumentNullException(nameof(name));

            if (HasExtension(name))
                name = Path.GetFileNameWithoutExtension(name);

            int index = name.LastIndexOf('@');
            return index < 0 ? new Version(0, 0, 0, 0) : new Version(name.Substring(index + 1));
        }

        [CanBeNull]
        public static string SelectGreatestVersion([NotNull] this IEnumerable<string> paths)
        {
            if (paths is null) throw new ArgumentNullException(nameof(paths));

            return paths.OrderByDescending(PackageVersion).FirstOrDefault();
        }

        [NotNull]
        public static string[] Glob([NotNull] DirectoryInfo folder, [NotNull] Matcher matcher)
        {
            if (folder is null) throw new ArgumentNullException(nameof(folder));
            if (matcher is null) throw new ArgumentNullException(nameof(matcher));

            PatternMatchingResult result = matcher.Execute(new DirectoryInfoWrapper(folder));
            return result.HasMatches
                ? result.Files.Select(match => Path.Combine(folder.FullName, match.Path)).ToArray()
                : Array.Empty<string>();
        }

        // https://stackoverflow.com/a/703292/13262469
        [NotNull]
        public static string GetRelativePath([NotNull] string filespec, [NotNull] string folder)
        {
            if (filespec is null) throw new ArgumentNullException(nameof(filespec));
            if (folder is null) throw new ArgumentNullException(nameof(folder));

            var pathUri = new Uri(filespec);
            // Folders must end in a slash
            if (!folder.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                folder += Path.DirectorySeparatorChar;
            }

            var folderUri = new Uri(folder);
            return Uri.UnescapeDataString(folderUri.MakeRelativeUri(pathUri).ToString()
                .Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        ///     Initialize static directory variables. Has to be called from the main thread since Unity is anal about calling even
        ///     simplest native methods from worker threads
        /// </summary>
        internal static void Initialize()
        {
            DataDir = Application.dataPath;
            KspDir = Path.GetFullPath(DataDir + SelectByPlatform("/..", "/..", "/../.."));
            ModDir = Path.GetFullPath($"{Assembly.GetExecutingAssembly().Location}/../..");
            ModFolderName = Path.GetFileName(ModDir);
            ModLogsDir = Path.Combine(KspDir, "Logs", ModName);
            OutputLibrary = Path.Combine(DataDir, "Plugins", LibraryName);
            NativeLibraryExtension = SelectByPlatform(".dll", ".so", ".bundle");
            OutputLibraryPath = OutputLibrary + NativeLibraryExtension;
        }

        internal static T SelectByPlatform<T>(T windows, T linux, T osx)
        {
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.WindowsPlayer:
                    return windows;
                case RuntimePlatform.LinuxEditor:
                case RuntimePlatform.LinuxPlayer:
                    return linux;
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.OSXPlayer:
                    return osx;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
