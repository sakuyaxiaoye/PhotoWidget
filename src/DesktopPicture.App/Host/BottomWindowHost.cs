using System;
using System.Collections.Generic;
using DesktopPicture.Interop;
using DesktopPicture.Logging;

namespace DesktopPicture.Host;

public sealed class BottomWindowHost : IDesktopHost
{
    public string Name => "BottomWindowHost";
    public DesktopHostHealth Health => DesktopHostHealth.Degraded;

    private readonly HashSet<IntPtr> _attachedWindows = new();

    public AttachResult Attach(IntPtr widgetHwnd)
    {
        if (widgetHwnd == IntPtr.Zero || !NativeMethods.IsWindow(widgetHwnd))
        {
            return AttachResult.Failed(Name, "Invalid widget HWND.");
        }

        try
        {
            // Ensure detached from any parent
            NativeMethods.SetParent(widgetHwnd, IntPtr.Zero);

            // Set WS_POPUP style, remove WS_CHILD
            int style = (int)NativeMethods.GetWindowLongPtr(widgetHwnd, NativeMethods.GWL_STYLE);
            style &= ~NativeMethods.WS_CHILD;
            style |= NativeMethods.WS_POPUP;
            NativeMethods.SetWindowLongPtr(widgetHwnd, NativeMethods.GWL_STYLE, new IntPtr(style));

            // Set WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            int exStyle = (int)NativeMethods.GetWindowLongPtr(widgetHwnd, NativeMethods.GWL_EXSTYLE);
            exStyle |= (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
            NativeMethods.SetWindowLongPtr(widgetHwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

            // Position at HWND_BOTTOM
            NativeMethods.SetWindowPos(
                widgetHwnd,
                NativeMethods.HWND_BOTTOM,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_FRAMECHANGED |
                NativeMethods.SWP_SHOWWINDOW);

            _attachedWindows.Add(widgetHwnd);
            AppLogger.Instance.Warn($"BottomWindowHost: Attached widget HWND 0x{widgetHwnd:X} as degraded bottom window.");
            return AttachResult.Succeeded(Name);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"BottomWindowHost: Error attaching widget 0x{widgetHwnd:X}", ex);
            return AttachResult.Failed(Name, ex.Message);
        }
    }

    public void Detach(IntPtr widgetHwnd)
    {
        _attachedWindows.Remove(widgetHwnd);
    }

    public void ReattachAll(string reason)
    {
        AppLogger.Instance.Info($"BottomWindowHost: ReattachAll requested. Reason: {reason}");
        var windows = new List<IntPtr>(_attachedWindows);
        foreach (var hwnd in windows)
        {
            if (NativeMethods.IsWindow(hwnd))
            {
                Attach(hwnd);
            }
            else
            {
                _attachedWindows.Remove(hwnd);
            }
        }
    }

    public NativeMethods.RECT GetDesktopBounds()
    {
        return new NativeMethods.RECT(0, 0, 1920, 1080);
    }

    public void Dispose()
    {
        _attachedWindows.Clear();
    }
}
