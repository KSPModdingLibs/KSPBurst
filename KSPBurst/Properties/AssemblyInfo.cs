using System.Reflection;
using System.Runtime.InteropServices;

// General Information about an assembly is controlled through the following
// set of attributes. Change these attribute values to modify the information
// associated with an assembly.
[assembly: AssemblyTitle("KSPBurst")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("")]
[assembly: AssemblyProduct("KSPBurst")]
[assembly: AssemblyCopyright("Copyright ©  2021")]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]

// The following GUID is for the ID of the typelib if this project is exposed to COM
[assembly: Guid("62AF95D6-FE71-485C-92C2-3F6B79F26B28")]

// Version information for an assembly consists of the following four values:
//
//      Major Version
//      Minor Version
//      Build Number
//      Revision
//
// You can specify all the values or you can default the Build and Revision Numbers
// by using the '*' as shown below:
// [assembly: AssemblyVersion("1.0.*")]
[assembly: AssemblyVersion("1.7.4.9")]
[assembly: AssemblyFileVersion("1.7.4.9")]
[assembly: KSPAssembly("KSPBurst", 1, 7, 4)]

// Other cases use a KSPAssemblyDependency on KSPBurst to load after these libraries
// so we make sure that doing so implies a dependency on the other libraries.
[assembly: KSPAssemblyDependency("Unity.Burst.Unsafe", 0, 0)]
[assembly: KSPAssemblyDependency("Unity.Mathematics", 0, 0)]
[assembly: KSPAssemblyDependency("Microsoft.Extensions.FileSystemGlobbing", 0, 0)]
[assembly: KSPAssemblyDependency("System.IO.Compression", 0, 0)]
[assembly: KSPAssemblyDependency("System.Runtime.CompilerServices.Unsafe", 0, 0)]
[assembly: KSPAssemblyDependency("System.Runtime", 0, 0)]
[assembly: KSPAssemblyDependency("Unity.Burst", 0, 0)]
[assembly: KSPAssemblyDependency("System.IO.Compression.FileSystem", 0, 0)]
[assembly: KSPAssemblyDependency("Unity.Collections", 0, 0)]
[assembly: KSPAssemblyDependency("Unity.Jobs", 0, 0)]
