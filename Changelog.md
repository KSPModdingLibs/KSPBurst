# Changelog

## Unreleased

## 1.7.4.5
* Strip `-preview` markers from file versions.

## 1.7.4.4
* Added all version info metadata to `Unity.*` dlls.

## 1.7.4.3
* Upgraded Unity.Collections to v0.8.0-preview.5
* Upgraded Unity.Jobs to 0.2.9-preview.15
* Upgraded Unity.Mathematics to 1.2.6
* KSPBurst now includes a randomized hash so that old locked versions do not
  result in errors.
* KSPBurst is now a bit more resilient to cases where the burst lib is locked.
* KSPBurst now checks the hash of the burst dll in case it has been changed by
  other KSP instances sharing the same KSP_x64_Data folder.
* Burst-compiled code now has minimal debug info enabled by default.

## 1.7.4.2
* KSPBurst now only compiles DLLs that have a KSPAssemblyDependency on KSPBurst
  or that otherwise opt-in by specifying a `KSPBURST_ASSEMBLY` config node.
* Fixed an issue where large installs would create a command line larger than
  the maximum on windows (32767 characters).
* Fixed an issue introduced in 1.7.4.1 where the first game start would not
  use burst-compiled methods, despite compilation actually succeeding.

## 1.7.4.1
* Updated Burst to 1.7.4
* Better logging when the compiler failed to start
* Make sure the static constructor for BurstCompiler is invoked on the main thread

## 1.5.5.2

* Fix race condition that could freeze KSP loading if the burst compilation output any logs

## 1.5.5.1

* Fix burst status not set on error
* Paths in burst arguments are now relative to KSP directory to avoid hitting `Process` character limit on large installs
* Ignore plugins in `KSP_x64_Data\Managed` for `root-assembly` to further reduce command size

## 1.5.5.0

* Update `Unity.Burst` to version 1.5.5
* Allow setting `root-assembly` and `assembly-folder` arguments from config
* Burst compilation is now skipped if command line arguments and plugins haven't changed since the last time
* Fixed crash when logging error messages from a worker thread if KSPLog is set to display them on screen

## 1.5.4.1

* Fixed KSP 1.12 failing to load plugins on linux
* Fixed error when KSP has multiple loaded assemblies at the same path

## 1.5.4.0

* Updated `Unity.Burst` to version 1.5.4
* Added missing version resources to `Unity.Burst.Unsafe.dll` and `System.Runtime.CompilerServices.Unsafe.dll` to fix
  issue with KSP 1.12

## 1.5.0.0

* Initial release
