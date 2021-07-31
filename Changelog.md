# Changelog

## 1.5.4.2

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
