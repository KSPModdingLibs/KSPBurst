# KSPBurst

Burst compiler for Kerbal Space Program  

KSPBurst by itself will not provide any performance benefits. Mods will need to use Unity job system and decorate the
jobs with `[BurstCompile]` to get any improvements.  

The Burst compiler is archived to prevent KSP from trying to load its dynamic libraries. The archive is extracted the
first time the mod runs to `<path to KSP>/PluginData/KSPBurst@<mod version>-<archive name>`. Burst standard outputs can
be found in `<path to KSP>/Logs/KSPBurst/` and `KSP.log`.

You can find forum post [here](https://forum.kerbalspaceprogram.com/index.php?/topic/201112-*).

## Installation

Download the latest release from the [GitHub releases](https://github.com/KSPModdingLibs/KSPBurst/releases) and extract the
archive into your KSP directory. `plugins_only` version does not contain the compiler, use it only if KSPBurst is a hard
dependency and download size is an issue.

Alternatively, KSPBurst can be installed from CKAN using `KSPBurst` identifier. `plugins_only` version is indexed as
`KSPBurst-Lite`.

Compiler version requires Mono, you can download it from [here](https://www.mono-project.com/download/stable/).

Burst compiler version can be changed by replacing existing `com.unity.burst@<version>.zip` archive with a different
one. The mod expects the archive to follow `<package name>@<package version>.<extension>` naming scheme, where `<package
name>` contains `burst`.  If a matching archive was not found, KSPBurst will default to using a compiler matching `<path
to KSP>/PluginData/*burst*/package/.Runtime/bcl.exe` with the greatest package version.

Burst packages can be found [here](https://download.packages.unity.com/com.unity.burst/).

## Modders

KSPBurst can be bundled with other mods. Bundling `plugins_only` version will keep the file size down but users will
need to download the compiler version for Burst benefits.

Burst compatible Unity plugins and their dependencies are also bundled:  

[comment]: # (begin_packages)

* [Unity.Burst 1.7.4](https://docs.unity3d.com/Packages/com.unity.burst@1.7/manual/index.html)  
* [Unity.Mathematics 1.2.6](https://docs.unity3d.com/Packages/com.unity.mathematics@1.2/manual/index.html)  
* [Unity.Collections 0.8.0-preview.5](https://docs.unity3d.com/Packages/com.unity.collections@0.8/manual/index.html)  
* [Unity.Jobs 0.2.9-preview.15](https://docs.unity3d.com/Packages/com.unity.jobs@0.2/manual/index.html)  

[comment]: # (end_packages)

As of 1.7.4.2 KSPBurst will only automatically compile DLLs that have a `KSPAssemblyDependency` on KSPBurst.
Make sure to add a `KSPAssemblyDependency` attribute on KSPBurst to your DLL or else your jobs or methods
will not be burst-compiled.

If you cannot take a direct dependency on KSPBurst for whatever reason, you can manually indicate that
KSPBurst should compile your DLL by creating a `KSPBURST_ASSEMBLY` config node like so:

```
KSPBURST_ASSEMBLY
{
  name = <your DLL's KSPAssemblyName>
}
```

For more details on actually writing burst-compiled code see [the wiki](https://github.com/KSPModdingLibs/KSPBurst/wiki).

## Configuration Options

All configuration options present in `KSPBurst.cfg` map directly to `bcl.exe` command line options. If `ModuleManager`
is present, patched options will be used.

[comment]: # (begin_bcl_usage)

```text
Usage: bcl.exe [options]
       bcl.exe --platform=<platform> --assembly=<PathToAssembly.dll_or_exe> --type=<TypeName>
       bcl.exe --platform=<platform> --assembly-folder=<path1;path2> --method=<FullMethodName[--MethodHash];method2>
       bcl.exe --validate-external-tool-chain --platform=<platform>
      --platform=VALUE       Target Platform <Windows|macOS|Linux|Android|iOS|
                               PS4|XboxOne|Wasm|UWP|Lumin|Switch|Stadia|tvOS|
                               EmbeddedLinux|GameCoreXboxOne|GameCoreXboxSeries|
                               PS5>. Default: Windows
      --backend=VALUE        The backend name. Default: `burst-llvm-12`
      --global-safety-checks-setting=VALUE
                             Global safety checks setting <Off|On|ForceOn.
                               Default: Off
      --disable-safety-checks
                             Disable safety checks. Default: Disabled
      --disable-opt          Disable `ir-opt` and `cpu-opt` optimizations
      --fastmath             Enable fast math optimizations
      --target=VALUE         Target CPU <Auto|X86_SSE2|X86_SSE4|X64_SSE2|X64_
                               SSE4|AVX|AVX2|WASM32|ARMV7A_NEON32|ARMV8A_
                               AARCH64|THUMB2_NEON32|ARMV8A_AARCH64_HALFFP>.
                               Can be specified multiple times for enabling
                               more than one target. Default: Auto
      --opt-level=VALUE      Optimization level. Default: 3
      --opt-for-size         Optimizes for size instead of performance. Default:
                                False
      --float-precision=VALUE
                             Precision CPU <Standard|High|Medium|Low> Default:
                               Standard
      --float-mode=VALUE     Math options <Default|Strict|Deterministic|Fast>
                               Default: Default
      --dump=VALUE           Dump flags <None|IL|Backend|IR|IROptimized|Asm|
                               Function|Analysis|IRPassAnalysis|ILPre|
                               IRPerEntryPoint|All> Default: Function
      --format=VALUE         Object format <Elf|Coff|MachO|Wasm> Default: Elf
      --debugtrap            Inserts a debug trap on the first instruction of
                               the entry point function. Default: False
      --disable-vectors      Disable SIMD Vector types special codegen (float4,
                               float2...). Default: False
      --generate-link-xml=VALUE
                             Generate a link.xml as part of the build process.
                               Default:
      --debug=VALUE          Enables generation of debug info <None|Full|
                               LineOnly> - PDB, DWARF -. Default: None
      --debugMode            Enables debuggability for code generation using a
                               native debugger. Default: False
      --generate-static-linkage-methods
                             Enables the generation of static linkage methods.
                               Default: False
      --generate-job-marshalling-methods
                             Enables the generation of job marshalling methods.
                               Default: False
      --temp-folder=VALUE    The temporary directory to use. Defaults to C:/
                               Users/<username>/AppData/Local/Temp/
      --disable-warnings=VALUE
                             Warnings to disable (separated by ;)  e.g. BC1370;
                               BC1322
      --compilation-defines=VALUE
                             Compilation defines to use for building (seperated
                               by ;)  e.g. UNITY_2020_1;NET_2_0
      --linker-options=VALUE Additional settings to be consumed by the native
                               linkers (seperated by ;)
      --enable-direct-external-linking
                             Link external calls directly instead of using
                               burst.initialize. Default: False
      --enable-autolayout-fallback-check
                             Enables validation that structs are managed-
                               sequential. Default: False
      --output=VALUE         Output path for the generated shared library.
                               Default: lib_burst_generated
      --keep-intermediate-files
                             Keep intermediate files along the shared library
                               generated final file. Default: False
      --nolink               Don't link the final object file to a shared
                               library but let the object file to be the output.
                                Default: false
      --no-native-toolchain  Don't look for a native toolchain. Useful if you
                               want to provide your own.
      --emit-llvm-objects    Forces output of object files to be LLVM bitcode
                               rather than native objects.
      --key-folder=VALUE     Key file folder location - required for some
                               platforms. Default:
      --decode-folder=VALUE  Decode folder location - required for some
                               platforms. Default: <Current working dir>
      --threads=VALUE        Number of compiler threads working concurrently.
                               Default is 9
      --safety-checks        Enable safety checks. Default: Disabled
      --assembly-folder=VALUE
                             Assembly folders (specify multiple times for
                               multiple folders)
      --method=VALUE         Full methodname with optional hash (separated by --
                               )
      --type=VALUE           A type to decompile all static public methods from.
                                A hash will be generated for each method
      --assembly=VALUE       An assembly path to look for the type (specify
                               multiple times for multiple paths)
      --group                Start a new group of methods
      --verbose              Display methods being compiled. Default: false
      --root-assembly=VALUE  Root assembly for finding compile target methods (
                               specify multiple times for multiple roots)
      --include-root-assembly-references=VALUE
                             Recursively scan root assembly references for
                               target methods. If this is false, only target
                               methods from the root assembly will be compiled.
                               Default is True
      --validate-external-tool-chain
                             Don't attempt to build anything, just check that
                               the current target and host are correctly
                               configured for linking
      --patch-assemblies-into=VALUE
                             Produce patched managed assemblies and put them in
                               this folder
      --pinvoke-name=VALUE   Patch assemblies with pinvokes to this name
      --only-static-methods  Compile only static methods and not Execute
                               methods of job producer interfaces
      --method-prefix=VALUE  Add a prefix to the names of generated methods
      --chunk-size=VALUE     Number of methods to compile per threads working
                               concurrently. Default is 3
      --log-timings          Log timings. Default False
      --enable-guard         Enable guard asserts. Default False
      --execute-method-name=VALUE
                             Name of Execute method. Used in DOTS Runtime,
                               where an extra wrapper Execute method is
                               generated.
      --print-monopinvokecallbackmissing-message
                             Print a warning if a compiled function pointer is
                               missing MonoPInvokeCallbackAttribute (needed for
                               IL2CPP). Default: false
      --output-mode=VALUE    Output mode <SingleLibrary|LibraryPerJob> Default:
                               SingleLibrary
      --always-create-output=VALUE
                             Always create output library. If this is false and
                               no target methods are found, no output library
                               will be created. Default True
      --cache-directory=VALUE
                             Cache directory. Default
      --only-list-methods    Only list the methods to compile. Outputs like '
                               assembly.dll, method'. Default False
      --pdb-search-paths=VALUE
                             path to search for pdbs, in addition to the same
                               folder as the assembly. (Specify multiple times
                               for multiple paths)
      --warmup               Run a warmup pass of the compile to amortize the
                               cost of the JIT Compile. Default False
      --help                 Show Help
```

[comment]: # (end_bcl_usage)

## Building

### Prerequisites

1. The `dotnet` CLI
2. A KSP installation
3. Python 3 (for packaging and version tooling)
4. Unity Editor 2019.4.14f1 (only if rebuilding Unity packages)

### Configuration

Tools read mod configuration options from `config.json`; create `config.json.user` in the repository root to override
values locally.

### Tools

`tools` contains Python scripts:

| Script       | Description                                                                                                 |
| ------------ | ----------------------------------------------------------------------------------------------------------- |
| `version.py` | update version information in source files and ReadMe                                                       |
| `package.py` | package mod into an archive at `archives/`, outputs 2 versions, one with and one without the Burst compiler |

### Building the Mod

See [CONTRIBUTING.md](./CONTRIBUTING.md).

## License

Unity plugins are licensed under under the Unity Companion License for Unity-dependent projects--see [Unity Companion
License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).  
KSPBurst is licensed under the MIT license

## Notes

`System.IO.Compression` and `System.IO.Compression.FileSystem` plugins are used for archive decompression and are
bundled with Unity Editor.  
NuGet package `Microsoft.Extensions.FileSystemGlobbing` is used for glob pattern matching.
