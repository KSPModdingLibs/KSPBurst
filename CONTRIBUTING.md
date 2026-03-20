# Contributing

## Bug Reports

If you are reporting a bug please make sure to include `KSP.log` and/or `Player.log`
along with your current mod list. For best results, follow the instructions at
[How to Get Support][0].

[0]: https://forum.kerbalspaceprogram.com/topic/163863-how-to-get-support/

## Building

### Prerequisites

- The `dotnet` CLI
- A KSP installation
- Python 3 (for packaging and version tooling)
- Unity Editor 2019.4.14f1 (only required when building Unity packages)

### KSP Directory

Create a `KSPBurst.props.user` file in the repository root:

```xml
<Project>
  <PropertyGroup>
    <ReferencePath>path\to\KSP</ReferencePath>
  </PropertyGroup>
</Project>
```

Replace `path\to\KSP` with the path to your KSP installation.

### Unity Packages (optional)

If you need to rebuild the Unity packages (e.g. updating Burst, Mathematics, Collections,
or Jobs):

1. Open the Unity project at `Unity/KSPBurst` in Unity Editor 2019.4.14f1.
2. If you hit compile errors related to undefined build targets, change the failing
   `#if` guards to `#if false`.
3. Build the project. The default output path (`Unity/KSPBurst/Build`) is already set
   in `config.json`; only create a `config.json.user` override if you chose a different
   output directory:

   ```json
   {
     "unityBuildDir": "<relative path to the unity build directory>"
   }
   ```

If you are not changing Unity packages and have built them at least once, you can
skip this section entirely.

### Building the Mod

```sh
dotnet build            # debug build
dotnet build -c Release # release build
```

The build will copy the mod output to your KSP installation automatically. If a
Burst compiler package is present in the Unity build directory, it will be archived
on the first build — this may take a moment.

> **Tip:** To avoid manually copying files every time, create a junction (Windows)
> or symlink (Linux/macOS) from your KSP `GameData` folder to the build output:
>
> ```batch
> :: Run in an admin cmd.exe prompt, inside your GameData directory
> mklink /j 000_KSPBurst C:\path\to\KSPBurst\repo\GameData\000_KSPBurst
> ```
>
> On Linux or macOS use `ln -s` instead.

### Packaging

To produce release archives under `archives/`:

```sh
python tools/package.py
```

This outputs two versions: one with the Burst compiler and one without (Lite).

### Updating the Version

```sh
python tools/version.py
```

This updates version information in source files and the README. Do not edit
version numbers by hand.

## Implementation Notes
A couple notes for future people who might need to maintain KSPBurst:

* Unity.Burst.Unsafe.dll has had some manual patching done to it to make it work
  on linux. If you use the one from the unity build it will work fine on windows
  but will cause KSP to hang on load on linux and MacOS.

  The build does not change it and it is in your best interest not to change it
  either unless you want to spend a number of days trying to debug what is wrong.

* The DLLs in the windows build do not have appropriate version info. This also
  breaks KSP so we manually patch in appropriate version data using the build
  targets in `KSPBurst.MSBuildTasks.targets`.

  This is done automatically by the build, but be aware of it in the future in
  case you ever need to change it.
