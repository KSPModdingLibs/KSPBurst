using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Unity.Jobs.LowLevel.Unsafe;

namespace KSPBurst.Patches;

[HarmonyPatch]
internal static class JobsUtility_CreateJobReflectionData
{
    // Each version of Unity has different overloads of this method, so we patch all of them.
    static IEnumerable<MethodBase> TargetMethods()
    {
        return typeof(JobsUtility)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == nameof(JobsUtility.CreateJobReflectionData));
    }

    static void Prefix(object[] __args)
    {
#if KSP_RETOOLED
        if (!__args.OfType<System.Type>().Any(Retooled.RetooledAssemblies.IsModType))
            return;
#endif

        CompilerGuard.WaitForCompiler("JobsUtility.CreateJobReflectionData");
    }
}
