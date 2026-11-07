#if KSP_RETOOLED
extern alias Cecil;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using Cecil::Mono.Cecil;

namespace KSPBurst.Retooled
{
    // Writes temporary copies of mod assemblies for the burst compiler to read on Retooled. Mods keep running from
    // their own files, and nothing in GameData changes.
    //
    // We need the copies because the bundled compiler is older than Unity 6, which moved many types to other
    // assemblies. For example, a mod that was built for stock KSP says that IJob is in UnityEngine.CoreModule. In
    // Unity 6 that assembly only forwards IJob to UnityEngine.ManagedKernelModule. The copies solve four problems
    // that this causes:
    //
    // 1. The compiler doesn't follow type forwarders. It uses the assembly name that is written in the mod. This
    //    means that it doesn't recognize the mod's jobs, and that the hashes that it computes for methods don't
    //    match the hashes that the game computes. To fix this, each copy names the assembly that actually defines
    //    each type.
    //
    // 2. The compiler only looks for job interfaces in Unity.Burst, in UnityEngine.CoreModule, and in assemblies
    //    that reference one of them. The kernel module doesn't reference either one, so we write a copy of it with
    //    an added reference to Unity.Burst. The kernel module isn't a root assembly, so the compiler doesn't compile
    //    any code from it.
    //
    // 3. The compiler needs types like BurstString for strings and logging, and it looks for them in the assembly
    //    named Unity.Burst. That assembly now only contains forwarders. If the compiler can't find those types, then
    //    one Debug.Log call or exception message in any job fails the whole compile. To fix this, we write a copy of
    //    UnityEngine.BurstModule that is named Unity.Burst. The mod copies still name the real module, so the hashes
    //    don't change.
    //
    // 4. The compiler finds an assembly by its file name, which doesn't work for Mod.Retooled.1.13.dll or
    //    Mod.relinked. We name each copy after its assembly, and we make it from the file that the game actually
    //    loaded. The compiler also doesn't see our Plugins folder this way, so it can't pick up the 2019.4 packages
    //    that we ship there.
    internal static class RetooledAssemblies
    {
        private const string KernelModule = "UnityEngine.ManagedKernelModule";
        private const string BurstModule = "UnityEngine.BurstModule";
        private const string BurstAssembly = "Unity.Burst";

        // The copies go in this folder, which is inside the folder that the compiler was extracted to.
        [NotNull] internal static string Folder { get; private set; } = string.Empty;

        [NotNull] private static string GameDataDir => Path.Combine(PathUtil.KspDir, "GameData");

        // Call this first, with the root folder of the extracted burst package.
        internal static void Initialize([NotNull] string packageRoot)
        {
            Folder = Path.Combine(packageRoot, "mod-temp");
        }

        // Returns true if the type comes from a mod, or has a generic argument that comes from a mod. The wrapper type
        // of a job is an example of the second case.
        //
        // Only mods need to wait for the compiler. The game's own burst code is in the library that ships with the game,
        // and the game starts using it before KSPBurst starts.
        internal static bool IsModType([CanBeNull] Type type)
        {
            if (type is null) return false;
            if (IsFromMod(type.Assembly)) return true;
            return type.IsGenericType && type.GetGenericArguments().Any(IsModType);
        }

        // True for an assembly from a mod, as opposed to the game or Unity.
        private static bool IsFromMod([CanBeNull] Assembly assembly)
        {
            string location = assembly?.Location;
            return !string.IsNullOrEmpty(location) && IsInGameData(location);
        }

        private static bool IsInGameData([NotNull] string path)
        {
            return Path.GetFullPath(path).StartsWith(GameDataDir + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        // AssemblyLoader also has entries for libraries that the game provides. A config node can add one of those to
        // the burst plugins, as DependencyCompat.cfg does for Unity.Burst, so we filter them out here.
        [NotNull]
        private static IEnumerable<AssemblyLoader.LoadedAssembly> Mods(
            [NotNull] IEnumerable<AssemblyLoader.LoadedAssembly> plugins)
        {
            return plugins.Where(plugin => IsInGameData(plugin.path));
        }

        [NotNull]
        private static string CopyPath([NotNull] string assemblyName)
        {
            return Path.Combine(Folder, assemblyName + ".dll");
        }

        [NotNull]
        internal static IEnumerable<string> RootPaths([NotNull] AssemblyLoader.LoadedAssembly[] burstPlugins,
            [NotNull] string rootDir)
        {
            rootDir = Path.GetFullPath(rootDir);
            return Mods(burstPlugins).Select(plugin =>
                PathUtil.GetRelativePath(CopyPath(plugin.assembly.GetName().Name), rootDir));
        }

        // Writes the burst plugins, what they depend on, the kernel module, and the Unity.Burst copy to the folder.
        internal static void Prepare([NotNull] AssemblyLoader.LoadedAssembly[] burstPlugins)
        {
            if (Directory.Exists(Folder))
                Directory.Delete(Folder, recursive: true);
            Directory.CreateDirectory(Folder);

            AssemblyLoader.LoadedAssembly[] mods = Mods(burstPlugins)
                .Concat(Mods(AssemblyUtil.GetDependencies(burstPlugins)))
                .Distinct()
                .ToArray();

            string managedDir = AssemblyUtil.GetKspPluginDir();

            using var resolver = new DefaultAssemblyResolver();
            resolver.RemoveSearchDirectory(".");
            resolver.RemoveSearchDirectory("bin");
            // We add the Managed folder first so that the game's assemblies win over the copies in GameData.
            resolver.AddSearchDirectory(managedDir);
            foreach (string directory in mods.Select(mod => Path.GetDirectoryName(mod.path)).Distinct())
                resolver.AddSearchDirectory(directory);

            foreach (AssemblyLoader.LoadedAssembly mod in mods)
            {
                // If the game loaded a relinked copy, then Location is the path of that copy.
                string source = string.IsNullOrEmpty(mod.assembly.Location) ? mod.path : mod.assembly.Location;
                string destination = CopyPath(mod.assembly.GetName().Name);

                try
                {
                    using AssemblyDefinition assembly = Read(source, resolver);
                    RetargetReferences(assembly.MainModule);
                    assembly.Write(destination);
                }
                catch (Exception e)
                {
                    KSPBurst.LogError($"Failed to rewrite {source} for the burst compiler, using it as is: {e.Message}");
                    File.Copy(source, destination, overwrite: true);
                }
            }

            string kernelModule = Path.Combine(managedDir, KernelModule + ".dll");
            using (AssemblyDefinition assembly = Read(kernelModule, resolver))
            {
                assembly.MainModule.AssemblyReferences.Add(new AssemblyNameReference(BurstAssembly, new Version()));
                assembly.Write(CopyPath(KernelModule));
            }

            string burstModule = Path.Combine(managedDir, BurstModule + ".dll");
            using (AssemblyDefinition assembly = Read(burstModule, resolver))
            {
                assembly.Name.Name = BurstAssembly;
                assembly.MainModule.Name = BurstAssembly + ".dll";
                assembly.Write(CopyPath(BurstAssembly));
            }
        }

        // Deletes the folder. If that fails then we only log it, because the next compile replaces the folder.
        internal static void Cleanup()
        {
            try
            {
                if (Directory.Exists(Folder))
                    Directory.Delete(Folder, recursive: true);
            }
            catch (Exception e)
            {
                KSPBurst.LogError($"Failed to clean up temporary directory {Folder}");
                KSPBurst.LogException(e);
            }
        }

        [NotNull]
        private static AssemblyDefinition Read([NotNull] string path, [NotNull] IAssemblyResolver resolver)
        {
            return AssemblyDefinition.ReadAssembly(path, new ReaderParameters {AssemblyResolver = resolver});
        }

        private static void RetargetReferences([NotNull] ModuleDefinition module)
        {
            foreach (TypeReference type in module.GetTypeReferences().ToArray())
            {
                // A nested type has the same assembly as the type that contains it, so we only change the outer type.
                if (type.IsNested || type.Scope is not AssemblyNameReference scope) continue;

                AssemblyNameDefinition defining = TryResolve(type)?.Module.Assembly.Name;
                if (defining is null || defining.Name == scope.Name) continue;

                AssemblyNameReference reference =
                    module.AssemblyReferences.FirstOrDefault(r => r.Name == defining.Name);
                if (reference is null)
                {
                    reference = new AssemblyNameReference(defining.Name, defining.Version)
                    {
                        PublicKeyToken = defining.PublicKeyToken
                    };
                    module.AssemblyReferences.Add(reference);
                }

                type.Scope = reference;
            }
        }

        // A mod can reference assemblies that aren't installed. We leave those references alone. The compiler reports
        // an error if the mod's burst code needs one of them.
        [CanBeNull]
        private static TypeDefinition TryResolve([NotNull] TypeReference type)
        {
            try
            {
                return type.Resolve();
            }
            catch (AssemblyResolutionException)
            {
                return null;
            }
        }
    }
}
#endif
