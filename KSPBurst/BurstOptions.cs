using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using JetBrains.Annotations;

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable ReturnTypeCanBeEnumerable.Global

namespace KSPBurst
{
    public static class BurstOptions
    {
        public static Platform CurrentPlatform =>
            PathUtil.SelectByPlatform(Platform.Windows, Platform.Linux, Platform.macOS);

        [NotNull]
        public static List<string> LoadArgs([NotNull] ConfigNode node, [CanBeNull] string rootDir)
        {
            if (node is null) throw new ArgumentNullException(nameof(node));
            if (string.IsNullOrEmpty(rootDir)) rootDir = Directory.GetCurrentDirectory();

            List<string> args = new()
            {
                $"--platform={CurrentPlatform}",
                $"--output=\"{PathUtil.GetRelativePath(PathUtil.OutputLibrary, rootDir)}\""
            };
            args.AddRange(Options.Select(option => option.MakeOption(node.GetValue(option.Name)))
                .Where(option => !string.IsNullOrEmpty(option)));

            // multiple options
            foreach (IOption option in MultiOptions)
                args.AddRange(node.GetValuesList(option.Name).Select(value => option.MakeOption(value))
                    .Where(o => !string.IsNullOrEmpty(o)));

            AddRootAssemblies(args, AssemblyUtil.KspAndPluginAssemblyPaths(false, rootDir));
            string kspPluginDir = Path.Combine(PathUtil.DataDir, "Managed");
            args.Add($"--assembly-folder=\"{PathUtil.GetRelativePath(kspPluginDir, rootDir)}\"");

            return args;
        }

        public static void AddRootAssemblies([NotNull] ICollection<string> args,
            [NotNull] IEnumerable<string> assemblyPaths)
        {
            if (args is null) throw new ArgumentNullException(nameof(args));
            if (assemblyPaths is null) throw new ArgumentNullException(nameof(assemblyPaths));

            // use hashset to store directories of all libraries
            HashSet<string> assemblyFolders = new();
            foreach (string path in assemblyPaths)
            {
                string parent = Path.GetDirectoryName(path);
                args.Add($"--root-assembly=\"{path}\"");
                assemblyFolders.Add(parent);
            }

            foreach (string assemblyFolder in assemblyFolders) args.Add($"--assembly-folder=\"{assemblyFolder}\"");
        }

        public class FlagOption : IOption
        {
            public FlagOption([NotNull] string name)
            {
                Name = name ?? throw new ArgumentNullException(nameof(name));
            }

            public string Name { get; }

            public string MakeOption(string value)
            {
                if (!string.IsNullOrEmpty(value) && bool.TryParse(value, out bool set) && set)
                    return $"--{Name}";

                return null;
            }
        }

        public class EnumOption<T> : IOption where T : Enum
        {
            public EnumOption([NotNull] string name)
            {
                Name = name ?? throw new ArgumentNullException(nameof(name));
            }

            public string Name { get; }

            public string MakeOption(string value)
            {
                if (string.IsNullOrEmpty(value))
                    return null;

                try
                {
                    object option = Enum.Parse(typeof(T), value, true);
                    return $"--{Name}={option}";
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }

                return null;
            }
        }

        public class StringOption : IOption
        {
            public StringOption([NotNull] string name)
            {
                Name = name ?? throw new ArgumentNullException(nameof(name));
            }

            public string Name { get; }

            public string MakeOption(string value)
            {
                return !string.IsNullOrEmpty(value) ? $"--{Name}={value}" : null;
            }
        }

        // ReSharper disable InconsistentNaming
        public enum Platform
        {
            Windows,
            macOS,
            Linux
        }

        public enum Target
        {
            [UsedImplicitly] Auto,
            [UsedImplicitly] X86_SSE2,
            [UsedImplicitly] X86_SSE4,
            [UsedImplicitly] X64_SSE2,
            [UsedImplicitly] X64_SSE4,
            [UsedImplicitly] AVX,
            [UsedImplicitly] AVX2
        }

        public enum FloatPrecision
        {
            [UsedImplicitly] Standard,
            [UsedImplicitly] High,
            [UsedImplicitly] Medium,
            [UsedImplicitly] Low
        }

        public enum FloatMode
        {
            [UsedImplicitly] Default,
            [UsedImplicitly] Strict,
            [UsedImplicitly] Deterministic,
            [UsedImplicitly] Fast
        }

        public enum Dump
        {
            [UsedImplicitly] None,
            [UsedImplicitly] IL,
            [UsedImplicitly] Backend,
            [UsedImplicitly] IR,
            [UsedImplicitly] IROptimized,
            [UsedImplicitly] Asm,
            [UsedImplicitly] Function,
            [UsedImplicitly] Analysis,
            [UsedImplicitly] IRPassAnalysis,
            [UsedImplicitly] ILPre,
            [UsedImplicitly] All
        }

        public enum Format
        {
            [UsedImplicitly] Elf,
            [UsedImplicitly] Coff,
            [UsedImplicitly] MachO,
            [UsedImplicitly] Wasm
        }

        public enum Debug
        {
            [UsedImplicitly] None,
            [UsedImplicitly] Full,
            [UsedImplicitly] LineOnly
        }

        public enum Output
        {
            [UsedImplicitly] SingleLibrary,
            [UsedImplicitly] LibraryPerJob
        }
        // ReSharper restore InconsistentNaming

        // ReSharper disable StringLiteralTypo
        [NotNull]
        public static IReadOnlyList<IOption> Options { get; } = new List<IOption>
        {
            // new EnumOption<Platform>("platform"), // special case
            new StringOption("backend"),
            new FlagOption("safety-checks"),
            new FlagOption("disable-opt"),
            new FlagOption("fastmath"),
            new StringOption("opt-level"),
            new FlagOption("opt-for-size"),
            new EnumOption<FloatPrecision>("float-precision"),
            new EnumOption<FloatMode>("float-mode"),
            new EnumOption<Format>("format"),
            new FlagOption("debugtrap"),
            new FlagOption("disable-vectors"),
            new EnumOption<Debug>("debug"),
            new FlagOption("debugMode"),
            new FlagOption("generate-static-linkage-methods"),
            new FlagOption("generate-job-marshalling-methods"),
            new StringOption("temp-folder"),
            new FlagOption("enable-direct-external-linking"),
            new FlagOption("use-platform-sdk-linkers"),
            new FlagOption("keep-intermediate-files"),
            new FlagOption("nolink"),
            new FlagOption("no-native-toolchain"),
            new FlagOption("emit-llvm-objects"),
            new StringOption("key-folder"),
            new StringOption("decode-folder"),
            new StringOption("threads"),
            new FlagOption("verbose"),
            new StringOption(
                "include-root-assembly-references"), // it's either true of false but still expects a string...
            new FlagOption("validate-external-tool-chain"),
            new StringOption("patch-assemblies-into"),
            new StringOption("pinvoke-name"),
            new FlagOption("only-static-methods"),
            new StringOption("method-prefix"),
            new StringOption("chunk-size"),
            new FlagOption("log-timings"),
            new FlagOption("enable-guard"),
            new FlagOption("print-monopinvokecallbackmissing-message"),
            new EnumOption<Output>("output-mode"),
            new StringOption("always-create-output"), // another string option that should be flag
            new StringOption("cache_directory"),
            new FlagOption("only-list-methods")
        };

        [NotNull]
        public static IReadOnlyList<IOption> MultiOptions { get; } = new List<IOption>
        {
            new EnumOption<Target>("target"),
            new EnumOption<Dump>("dump"),
            new StringOption("disable-warnings"),
            new StringOption("compilation-defines"),
            new StringOption("linker-options"),
            new StringOption("pdb-search-paths"),
            new StringOption("root-assembly"),
            new StringOption("assembly-folder")
        };
        // ReSharper restore StringLiteralTypo
    }
}
