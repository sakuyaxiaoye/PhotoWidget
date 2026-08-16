using System;
using System.Diagnostics;
using DesktopPicture.Interop;
using DesktopPicture.Logging;

namespace DesktopPicture.Diagnostics;

public static class MemoryOptimizer
{
    private static long _switchCount = 0;

    public static void TrimWorkingSet()
    {
        try
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            var processHandle = Process.GetCurrentProcess().Handle;
            NativeMethods.SetProcessWorkingSetSize(processHandle, (IntPtr)(-1), (IntPtr)(-1));
            NativeMethods.EmptyWorkingSet(processHandle);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn($"MemoryOptimizer: TrimWorkingSet failed: {ex.Message}");
        }
    }

    public static void OnImageSwitched()
    {
        long count = System.Threading.Interlocked.Increment(ref _switchCount);
        // Periodically trim working set every 4 image transitions
        if (count % 4 == 0)
        {
            System.Threading.Tasks.Task.Run(() => TrimWorkingSet());
        }
    }
}
