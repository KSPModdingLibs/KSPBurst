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

* [Unity.Burst 1.5.5](https://docs.unity3d.com/Packages/com.unity.burst@1.5/manual/index.html)  
* [Unity.Mathematics 1.2.1](https://docs.unity3d.com/Packages/com.unity.mathematics@1.2/manual/index.html)  
* [Unity.Collections 0.1.1-preview](https://docs.unity3d.com/Packages/com.unity.collections@0.1/manual/index.html)  
* [Unity.Jobs 0.1.1-preview](https://docs.unity3d.com/Packages/com.unity.jobs@0.1/manual/index.html)  

[comment]: # (end_packages)

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
                               EmbeddedLinux|GameCoreXboxOne|GameCoreXboxSeries>
                               . Default: Windows
      --backend=VALUE        The backend name. Default: `burst-llvm-11`
      --safety-checks        Enable safety checks. Default for safety checks:
                               Disabled
      --disable-safety-checks
                             Disable safety checks. Default for safety checks:
                               Disabled
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
                               Function|Analysis|IRPassAnalysis|ILPre|All>
                               Default: Function
      --format=VALUE         Object format <Elf|Coff|MachO|Wasm> Default: Elf
      --debugtrap            Inserts a debug trap on the first instruction of
                               the entry point function. Default: False
      --disable-vectors      Disable SIMD Vector types special codegen (float4,
                               float2...). Default: False
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
      --use-platform-sdk-linkers
                             Use platform compiler tool chains for building
                               desktop platforms (requires MSVC/XCode/Gcc/Clang)
                               , also has no cross platform support : Default:
                               false
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
                               Default is 13
      --assembly-folder=VALUE
                             Assembly folders (separated by ; or multiple
                               options)
      --method=VALUE         Full methodname with optional hash (separated by --
                               )
      --type=VALUE           A type to decompile all static public methods from.
                                A hash will be generated for each method
      --assembly=VALUE       An assembly path to look for the type
      --group                Start a new group of methods
      --verbose              Display methods being compiled. Default: false
      --root-assembly=VALUE  Root assembly for finding compile target methods
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
                             A semicolon seperated list of paths to search for
                               pdbs, in addition to the same folder as the
                               assembly.
      --warmup               Run a warmup pass of the compile to amortize the
                               cost of the JIT Compile. Default False
      --help                 Show Help
```

[comment]: # (end_bcl_usage)

## Building

### Prerequisites

1. KSP installation
2. python
3. Unity Editor 2019.2.2f1 (if building Unity packages)

### Configuration

Tools read mod configuration options from `config.json`, users should create `config.json.user` and override the values
there.

### Tools

`tools` contains python scripts:

| Script          | Description                                                                                                      |
| --------------- | ---------------------------------------------------------------------------------------------------------------- |
| `post_build.py` | copy libraries to the mod directory, archive the Burst package if it exists and copy the mod to KSP installation |
| `version.py`    | update version information in source files and ReadMe                                                            |
| `package.py`    | package mod into an archive at `archives/`, outputs 2 versions, one with and one without the Burst compiler      |

### Building the Mod

1. Clone KSPBurst  
2. Create `Directory.Build.props.user` in the root directory with

    ```xml
    <Project>
      <PropertyGroup>
        <KSP_DIR>path to KSP</KSP_DIR>
      </PropertyGroup>
    </Project>
    ```

   Depending on the platform you may also need to set `DATA_DIRNAME` to match your installation.
3. If not building Unity packages, go to to step 7
4. Open Unity project at `Unity/KSPBurst` in Unity Editor  
5. Build the Unity project and note the build directory
6. Create `config.json.user` in root directory with

    ```json
    {
      "unityBuildDir": "<relative path to unity build directory in step 5>"
    }
    ```

7. Build `KSPBurst` with your IDE or from command line, the mod will be copied your KSP installation. If the burst
   package is present in Unity directory, it may take a while to archive it the first time.

   * Note: `Unity.Burst.Unsafe.dll` is missing version resource so KSP 1.12 crashes on loading, only way to fix this
     issue that also works on Linux is to add a new version resource from Visual Studio manually, `ResourceHacker` will
     not be enough. For this reason, `Unity.Burst.Unsafe.dll` is not copied from the Unity build directory
     automatically.

## License

Unity plugins are licensed under under the Unity Companion License for Unity-dependent projects--see [Unity Companion
License](http://www.unity3d.com/legal/licenses/Unity_Companion_License).  
KSPBurst is licensed under the MIT license

## Notes

`System.IO.Compression` and `System.IO.Compression.FileSystem` plugins are used for archive decompression and are
bundled with Unity Editor.  
NuGet package `Microsoft.Extensions.FileSystemGlobbing` is used for glob pattern matching.
