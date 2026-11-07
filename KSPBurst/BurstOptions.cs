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
        public static List<string> LoadArgs(
            [NotNull] ConfigNode node,
            [NotNull] AssemblyLoader.LoadedAssembly[] burstPlugins,
            [CanBeNull] string rootDir
        )
        {
            if (node is null) throw new ArgumentNullException(nameof(node));
            if (burstPlugins is null) throw new ArgumentNullException(nameof(burstPlugins));
            if (string.IsNullOrEmpty(rootDir)) rootDir = Directory.GetCurrentDirectory();

            List<string> args = new()
            {
                $"--platform={CurrentPlatform}",
                $"--output={PathUtil.GetRelativePath(PathUtil.OutputLibrary, rootDir)}"
            };
            args.AddRange(Options.Select(option => option.MakeOption(node.GetValue(option.Name)))
                .Where(option => !string.IsNullOrEmpty(option)));

            args.AddRange(SelectTargets(node.GetValuesList("target")).Select(target => $"--target={target}"));

            // multiple options
            foreach (IOption option in MultiOptions)
                args.AddRange(node.GetValuesList(option.Name).Select(value => option.MakeOption(value))
                    .Where(o => !string.IsNullOrEmpty(o)));

#if KSP_RETOOLED
            // The library that ships with the game already has all of the burst code that isn't from a mod. Following
            // references would also reach Unity.Entities, which this version of the compiler can't compile.
            args.RemoveAll(arg => arg.StartsWith("--include-root-assembly-references"));
            args.Add("--include-root-assembly-references=false");

            // The compiler can't read the mods' own files correctly, so it gets copies. RetooledAssemblies explains why.
            AddRootAssemblies(args, Retooled.RetooledAssemblies.RootPaths(burstPlugins, rootDir));
            args.Add($"--assembly-folder={PathUtil.GetRelativePath(Retooled.RetooledAssemblies.Folder, Path.GetFullPath(rootDir))}");
#else
            // only compile assemblies that declare a dependency on KSPBurst (with config overrides applied)
            AddRootAssemblies(args, AssemblyUtil.BurstPluginAssemblyPaths(burstPlugins, rootDir));
            // but provide all plugin directories so references can be resolved
            AddAssemblyFolders(args, AssemblyUtil.PluginAssemblyPaths(rootDir));
#endif
            string kspPluginDir = AssemblyUtil.GetKspPluginDir();
            args.Add($"--assembly-folder={PathUtil.GetRelativePath(kspPluginDir, rootDir)}");

            return args;
        }

        // Picks the targets to compile for, out of the targets in the config. The game only ever uses the best target
        // that the CPU supports, so we compile only that one. This makes compilation faster. When we can't tell what
        // the CPU supports, we compile all of the targets in the config, like before.
        [NotNull]
        public static List<Target> SelectTargets([NotNull] IEnumerable<string> values)
        {
            if (values is null) throw new ArgumentNullException(nameof(values));

            List<Target> targets = new();
            foreach (string value in values)
            {
                try
                {
                    targets.Add((Target) Enum.Parse(typeof(Target), value, true));
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogException(e);
                }
            }

            // Auto lets the compiler choose, so we leave that alone.
            if (targets.Count <= 1 || targets.Contains(Target.Auto))
                return targets;

            CpuLevel? cpuLevel = CpuFeatures.Detect();
            if (cpuLevel is null)
            {
                KSPBurst.Log("Could not detect the CPU features, so we compile for all of the configured targets");
                return targets;
            }

            List<Target> supported = targets.Where(target => TargetLevel(target) <= cpuLevel).ToList();
            if (supported.Count == 0)
            {
                KSPBurst.LogError($"The CPU only supports {cpuLevel}, and none of the configured targets " +
                                  $"({string.Join(", ", targets)}) run on it. We compile for all of them anyway.");
                return targets;
            }

            Target best = supported.OrderByDescending(TargetLevel).First();
            KSPBurst.Log($"The CPU supports {cpuLevel}, so we only compile for {best}");
            return [best];
        }

        private static CpuLevel TargetLevel(Target target)
        {
            return target switch
            {
                Target.X86_SSE4 or Target.X64_SSE4 => CpuLevel.Sse4,
                Target.AVX => CpuLevel.Avx,
                Target.AVX2 => CpuLevel.Avx2,
                _ => CpuLevel.Sse2
            };
        }

        public static void AddRootAssemblies(
            [NotNull] ICollection<string> args,
            [NotNull] IEnumerable<string> assemblyPaths
        )
        {
            if (args is null) throw new ArgumentNullException(nameof(args));
            if (assemblyPaths is null) throw new ArgumentNullException(nameof(assemblyPaths));

            foreach (string path in assemblyPaths)
                args.Add($"--root-assembly={path}");
        }

        public static void AddAssemblyFolders(
            [NotNull] ICollection<string> args,
            [NotNull] IEnumerable<string> assemblyPaths
        )
        {
            if (args is null) throw new ArgumentNullException(nameof(args));
            if (assemblyPaths is null) throw new ArgumentNullException(nameof(assemblyPaths));

            HashSet<string> assemblyFolders = new();
            foreach (string path in assemblyPaths)
                assemblyFolders.Add(Path.GetDirectoryName(path));

            foreach (string assemblyFolder in assemblyFolders)
                args.Add($"--assembly-folder={assemblyFolder}");
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
            // new EnumOption<Target>("target"), // special case, see SelectTargets
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
