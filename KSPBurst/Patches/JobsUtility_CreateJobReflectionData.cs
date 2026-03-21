using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace KSPBurst.Patches;

[HarmonyPatch(typeof(JobsUtility))]
internal static class JobsUtility_CreateJobReflectionData
{
    static IEnumerable<MethodInfo> TargetMethods()
    {
        yield return SymbolExtensions.GetMethodInfo(() => 
            JobsUtility.CreateJobReflectionData(default(Type), default(Type), default(JobType), null));
        yield return SymbolExtensions.GetMethodInfo(() =>
            JobsUtility.CreateJobReflectionData(default(Type), default(JobType), null, null, null));
    }

    static void Prefix()
    {
        var task = KSPBurst.CompilerTask;
        if (task is null)
        {
            Debug.LogError($"[KSPBurst] JobsUtility.CreateJobReflectionData called before KSPBurst compilation was started. This will break KSPBurst. Call stack:\n{Environment.StackTrace}");
            return;
        }

        if (task.IsCompleted)
            return;

        Debug.LogWarning($"[KSPBurst] JobsUtility.CreateJobReflectionData called before KSPBurst compilation completed. Blocking until the compiler task completes. Call stack:\n{Environment.StackTrace}");

        try
        {
            task.Wait();
        }
        catch { }
    }
}
