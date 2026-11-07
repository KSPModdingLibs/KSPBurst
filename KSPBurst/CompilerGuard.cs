using System;
using UnityEngine;

namespace KSPBurst;

internal static class CompilerGuard
{
    // Blocks until burst compilation has completed, so that the caller gets burst compiled code. The caller
    // argument is the name of the patched method, which we use in log messages.
    internal static void WaitForCompiler(string caller)
    {
        var task = KSPBurst.CompilerTask;
        if (task is null)
        {
            Debug.LogError($"[KSPBurst] {caller} called before KSPBurst compilation was started. This will break KSPBurst. Call stack:\n{Environment.StackTrace}");
            return;
        }

        if (!task.IsCompleted)
        {
            Debug.LogWarning($"[KSPBurst] {caller} called before KSPBurst compilation completed. Blocking until the compiler task completes.");

            try
            {
                task.Wait();
            }
            catch { }
        }

#if KSP_RETOOLED
        // We are blocking the main thread here, so the coroutine that normally loads the library can't run. The caller
        // needs the library right now, so we load it ourselves.
        if (KSPBurst.InMainThread)
            KSPBurst.LoadLibrary();
#endif
    }
}
