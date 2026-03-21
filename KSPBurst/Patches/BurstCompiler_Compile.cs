using System;
using HarmonyLib;
using Unity.Burst;
using UnityEngine;

namespace KSPBurst.Patches;

// We can't patch the innermost Burst.Compile method, since doing so causes the
// static constructor of BurstCompilerHelper to run. Instead, we patch this
// outer one. All the calls go through it anyway so this tends not to be an
// issue.
[HarmonyPatch(
    typeof(BurstCompiler),
    nameof(BurstCompiler.Compile),
    [typeof(object), typeof(bool)]
)]
internal static class BurstCompiler_Compile
{
    static void Prefix()
    {
        var task = KSPBurst.CompilerTask;
        if (task is null)
        {
            Debug.LogError($"[KSPBurst] BurstCompiler.Compile called before KSPBurst compilation was started. This will break KSPBurst. Call stack:\n{Environment.StackTrace}");
            return;
        }

        if (task.IsCompleted)
            return;

        Debug.LogWarning($"[KSPBurst] BurstCompiler.Compile called before KSPBurst compilation completed. Blocking until the compiler task completes.");

        try
        {
            task.Wait();
        }
        catch { }
    }
}
