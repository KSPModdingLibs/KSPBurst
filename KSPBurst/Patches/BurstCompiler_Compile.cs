using System;
using System.Reflection;
using HarmonyLib;
using Unity.Burst;

namespace KSPBurst.Patches;

// We can't patch the innermost Burst.Compile method, since doing so causes the
// static constructor of BurstCompilerHelper to run. Instead, we patch this
// outer one. All the calls go through it anyway so this tends not to be an
// issue.
[HarmonyPatch]
internal static class BurstCompiler_Compile
{
    static MethodBase TargetMethod()
    {
#if KSP_RETOOLED
        return AccessTools.Method(typeof(BurstCompiler), "Compile", [typeof(object)]);
#else
        return AccessTools.Method(typeof(BurstCompiler), "Compile", [typeof(object), typeof(bool)]);
#endif
    }

    static void Prefix(object[] __args)
    {
#if KSP_RETOOLED
        if (!(__args[0] is Delegate target && Retooled.RetooledAssemblies.IsModType(target.Method.DeclaringType)))
            return;
#endif

        CompilerGuard.WaitForCompiler("BurstCompiler.Compile");
    }
}
