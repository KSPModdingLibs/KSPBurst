using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using JetBrains.Annotations;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable ReturnTypeCanBeEnumerable.Global

namespace KSPBurst
{
    public static class AssemblyUtil
    {
        public const string CacheName = "." + PathUtil.ModName + ".cache";
        public const char Separator = ';';

        [NotNull]
        public static AssemblyLoader.LoadedAssembly[] LoadedPlugins()
        {
            // use KSP assembly loader to get mod plugins, any invalid plugins like Principia have already been skipped
            // also skip any weird plugins without extensions
            return AssemblyLoader.loadedAssemblies
                .Where(assembly => assembly.path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        [NotNull]
        public static AssemblyVersion[] LoadedPluginVersions()
        {
            // merge url into name so that multiple DLLs with the same name but different paths can be distinguished
            return LoadedPlugins().Select(assembly => new AssemblyVersion
            {
                // use dllName instead of name since it's not guaranteed to be unique as it's set from KSPAssembly attribute
                Url = $"{assembly.url}/{assembly.dllName}", Guid = assembly.assembly.VersionId(),
                Version = assembly.assembly.GetName().Version
            }).ToArray();
        }

        [NotNull]
        public static string[] KspAndPluginAssemblyPaths(bool includeKsp = true, [CanBeNull] string rootDir = null)
        {
            IEnumerable<string> plugins = LoadedPlugins().Select(assembly => assembly.path);
            if (includeKsp)
                plugins = plugins.Concat(PathUtil.Glob(Path.Combine(PathUtil.DataDir, "Managed"), "*.dll"));

            string[] paths = plugins.ToArray();

            if (string.IsNullOrEmpty(rootDir)) return paths;

            rootDir = Path.GetFullPath(rootDir);
            for (var i = 0; i < paths.Length; ++i)
                paths[i] = PathUtil.GetRelativePath(paths[i], rootDir);

            return paths;
        }

        public static Guid VersionId([NotNull] this Assembly assembly)
        {
            if (assembly is null) throw new ArgumentNullException(nameof(assembly));
            // https://stackoverflow.com/a/66601671/13262469
            return assembly.ManifestModule.ModuleVersionId;
        }

        public static void CachePluginVersions([NotNull] IEnumerable<AssemblyVersion> versions,
            [NotNull] string directory, [NotNull] string cacheFilename = CacheName)
        {
            if (versions is null) throw new ArgumentNullException(nameof(versions));
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            if (string.IsNullOrWhiteSpace(cacheFilename))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(cacheFilename));

            using var file = new StreamWriter(Path.Combine(directory, cacheFilename));

            // Numerical versions are useless, only the binary hash is important to check for changes
            foreach (AssemblyVersion version in versions)
                file.WriteLine("{0}" + Separator + "{1}", version.Url, version.Guid);
        }

        public static void CachePluginVersions([NotNull] IEnumerable<AssemblyVersionChange> changes,
            [NotNull] string directory, [NotNull] string cacheFilename = CacheName)
        {
            if (changes is null) throw new ArgumentNullException(nameof(changes));

            CachePluginVersions(changes.Where(change => change.Loaded is not null).Select(change =>
                    new AssemblyVersion {Url = change.Url, Guid = (Guid) change.Loaded, Version = change.Version}),
                directory, cacheFilename);
        }

        public static void DeleteCache([NotNull] string directory, [NotNull] string cacheFilename = CacheName)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            if (string.IsNullOrWhiteSpace(cacheFilename))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(cacheFilename));
            string filename = Path.Combine(directory, cacheFilename);

            if (File.Exists(filename)) File.Delete(filename);
        }

        [NotNull]
        public static AssemblyVersion[] LoadPluginVersionsFromCache([NotNull] string directory,
            [NotNull] string cacheFilename = CacheName)
        {
            if (directory is null) throw new ArgumentNullException(nameof(directory));
            if (string.IsNullOrWhiteSpace(cacheFilename))
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(cacheFilename));

            string filename = Path.Combine(directory, cacheFilename);
            if (!File.Exists(filename)) return Array.Empty<AssemblyVersion>();

            using var file = new StreamReader(filename);

            List<AssemblyVersion> versions = new();
            for (;;)
            {
                string line = file.ReadLine();

                if (line is null) break;

                string[] parts = line.Split(Separator);
                if (parts.Length < 2) continue;

                versions.Add(new AssemblyVersion
                {
                    Url = parts[0],
                    Guid = Guid.Parse(parts[1]),
                    Version = null
                });
            }

            return versions.ToArray();
        }

        [NotNull]
        public static string Format([NotNull] IEnumerable<AssemblyVersionChange> versions)
        {
            if (versions is null) throw new ArgumentNullException(nameof(versions));
            const string format = "  {0} {1,-100}{2,-16}{3,-36} {4,-36}";

            StringBuilder sb = StringBuilderCache.Acquire();
            sb.AppendFormat(format, " ", "Assembly Url", "Version", "Cached Guid", "Guid").AppendLine();

            static string ToString(Guid? guid)
            {
                return guid is null ? string.Empty : guid.ToString();
            }

            foreach (AssemblyVersionChange version in versions)
                sb.AppendFormat(format,
                    version.Loaded == version.Cached ? " " : "x",
                    version.Url, version.Version?.ToString() ?? string.Empty,
                    ToString(version.Cached), ToString(version.Loaded)).AppendLine();

            var str = sb.ToString();
            sb.Release();

            return str;
        }

        public static Dictionary<string, AssemblyVersion> VersionDictionary(IEnumerable<AssemblyVersion> versions)
        {
            Dictionary<string, AssemblyVersion> dict = new();

            foreach (AssemblyVersion version in versions)
            {
                string key = version.Url;
                if (!dict.TryGetValue(key, out AssemblyVersion previous))
                    dict.Add(key, version);
                else if (previous.Version is null ||
                         version.Version is not null && version.Version > previous.Version)
                    // use the most recent version
                    dict[key] = version;
            }

            return dict;
        }

        [NotNull]
        public static AssemblyVersionChange[] ComputeChanges([CanBeNull] IEnumerable<AssemblyVersion> loadedVersions,
            [CanBeNull] IEnumerable<AssemblyVersion> cachedVersions)
        {
            if (cachedVersions is null)
            {
                if (loadedVersions is null) return Array.Empty<AssemblyVersionChange>();

                return loadedVersions.Select(version => new AssemblyVersionChange
                {
                    Url = version.Url,
                    Loaded = version.Guid,
                    Cached = null,
                    Version = version.Version
                }).ToArray();
            }

            if (loadedVersions is null)
                return cachedVersions.Select(version => new AssemblyVersionChange
                {
                    Url = version.Url,
                    Cached = version.Guid,
                    Loaded = null,
                    Version = version.Version
                }).ToArray();

            Dictionary<string, AssemblyVersion> loaded = VersionDictionary(loadedVersions);
            Dictionary<string, AssemblyVersion> saved = VersionDictionary(cachedVersions);
            HashSet<string> names = loaded.Keys.ToHashSet();
            names.UnionWith(saved.Keys);

            AssemblyVersionChange[] changes = names.Select(name => new AssemblyVersionChange
            {
                Url = name,
                Loaded = null,
                Cached = null,
                Version = null
            }).ToArray();

            for (var i = 0; i < changes.Length; ++i)
            {
                if (loaded.TryGetValue(changes[i].Url, out AssemblyVersion version))
                {
                    changes[i].Loaded = version.Guid;
                    changes[i].Version = version.Version;
                }

                if (saved.TryGetValue(changes[i].Url, out version))
                    changes[i].Cached = version.Guid;
            }

            return changes.ToArray();
        }

        public static bool AnyChanges([CanBeNull] this IEnumerable<AssemblyVersionChange> changes)
        {
            return changes is not null && changes.Any(change => change.Loaded != change.Cached);
        }

        public struct AssemblyVersion
        {
            public string Url;
            public Guid Guid;
            [CanBeNull] public Version Version;
        }

        public struct AssemblyVersionChange
        {
            public string Url;
            public Guid? Loaded;
            public Guid? Cached;
            [CanBeNull] public Version Version;
        }
    }
}
