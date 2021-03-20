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
                {Url = $"{assembly.url}/{assembly.name}", Guid = assembly.assembly.VersionId()}).ToArray();
        }

        [NotNull]
        public static string[] KspAndPluginAssemblyPaths()
        {
            return LoadedPlugins().Select(assembly => assembly.path)
                .Concat(PathUtil.Glob(Path.Combine(PathUtil.DataDir, "Managed"), "*.dll")).ToArray();
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

            foreach (AssemblyVersion version in versions)
                file.WriteLine("{0}" + Separator + "{1}", version.Url, version.Guid);
        }

        public static void CachePluginVersions([NotNull] IEnumerable<AssemblyVersionChange> changes,
            [NotNull] string directory, [NotNull] string cacheFilename = CacheName)
        {
            if (changes is null) throw new ArgumentNullException(nameof(changes));
            
            CachePluginVersions(changes.Where(change => change.Loaded is not null).Select(change =>
                new AssemblyVersion {Url = change.Url, Guid = (Guid) change.Loaded}), directory, cacheFilename);
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

                int index = line.IndexOf(Separator);
                if (index == -1) continue;

                versions.Add(new AssemblyVersion
                {
                    Url = line.Substring(0, index),
                    Guid = Guid.Parse(line.Substring(index + 1))
                });
            }

            return versions.ToArray();
        }

        [NotNull]
        public static string Format([NotNull] IEnumerable<AssemblyVersionChange> versions)
        {
            if (versions is null) throw new ArgumentNullException(nameof(versions));
            const string format = "  {0} {1,-100}{2,-36} {3,-36}";

            StringBuilder sb = StringBuilderCache.Acquire();
            sb.AppendFormat(format, " ", "Assembly Url", "Cached Guid", "Guid").AppendLine();

            static string ToString(Guid? guid)
            {
                return guid is null ? string.Empty : guid.ToString();
            }

            foreach (AssemblyVersionChange version in versions)
                sb.AppendFormat(format,
                    version.Loaded == version.Cached ? " " : "x",
                    version.Url, ToString(version.Cached), ToString(version.Loaded)).AppendLine();

            var str = sb.ToString();
            sb.Release();

            return str;
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
                    Cached = null
                }).ToArray();
            }

            if (loadedVersions is null)
                return cachedVersions.Select(version => new AssemblyVersionChange
                {
                    Url = version.Url,
                    Cached = version.Guid,
                    Loaded = null
                }).ToArray();

            Dictionary<string, AssemblyVersion> loaded = loadedVersions.ToDictionary(version => version.Url);
            Dictionary<string, AssemblyVersion> saved = cachedVersions.ToDictionary(version => version.Url);
            HashSet<string> names = loaded.Keys.ToHashSet();
            names.UnionWith(saved.Keys);

            AssemblyVersionChange[] changes = names.Select(name => new AssemblyVersionChange
            {
                Url = name,
                Loaded = null,
                Cached = null
            }).ToArray();

            for (var i = 0; i < changes.Length; ++i)
            {
                if (loaded.TryGetValue(changes[i].Url, out AssemblyVersion version))
                    changes[i].Loaded = version.Guid;
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
        }

        public struct AssemblyVersionChange
        {
            public string Url;
            public Guid? Loaded;
            public Guid? Cached;
        }
    }
}