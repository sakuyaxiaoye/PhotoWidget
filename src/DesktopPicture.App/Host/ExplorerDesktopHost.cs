using System;
using System.Collections.Generic;
using System.Text;
using DesktopPicture.Interop;
using DesktopPicture.Logging;

namespace DesktopPicture.Host;

public sealed class ExplorerDesktopHost : IDesktopHost
{
    public string Name => "ExplorerDesktopHost";

    private IntPtr _progmanHwnd = IntPtr.Zero;
    private IntPtr _workerWHwnd = IntPtr.Zero;
    private readonly HashSet<IntPtr> _attachedWindows = new();
    private DesktopHostHealth _health = DesktopHostHealth.Unavailable;

    public DesktopHostHealth Health => _health;

    public ExplorerDesktopHost()
    {
        InitializeHost();
    }

    public bool InitializeHost()
    {
        IntPtr hInputDesktop = IntPtr.Zero;

        // Try GetShellWindow first, then FindWindow
        _progmanHwnd = NativeMethods.GetShellWindow();
        if (_progmanHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_progmanHwnd))
        {
            _progmanHwnd = NativeMethods.FindWindow("Progman", null);
        }

        if (_progmanHwnd == IntPtr.Zero)
        {
            uint access = NativeMethods.DESKTOP_READOBJECTS | NativeMethods.DESKTOP_CREATEWINDOW |
                          NativeMethods.DESKTOP_ENUMERATE | NativeMethods.DESKTOP_WRITEOBJECTS |
                          NativeMethods.DESKTOP_SWITCHDESKTOP;

            hInputDesktop = NativeMethods.OpenInputDesktop(0, false, access);
            if (hInputDesktop == IntPtr.Zero)
            {
                hInputDesktop = NativeMethods.OpenDesktop("Default", 0, false, access);
            }

            if (hInputDesktop != IntPtr.Zero)
            {
                NativeMethods.SetThreadDesktop(hInputDesktop);
                _progmanHwnd = NativeMethods.GetShellWindow();
                if (_progmanHwnd == IntPtr.Zero)
                {
                    _progmanHwnd = NativeMethods.FindWindow("Progman", null);
                }
            }
        }

        try
        {
            if (_progmanHwnd == IntPtr.Zero)
            {
                AppLogger.Instance.Warn("ExplorerDesktopHost: Progman/Shell window not found.");
                _health = DesktopHostHealth.Unavailable;
                return false;
            }

            // Send 0x052C to Progman to instruct Explorer to create WorkerW
            NativeMethods.SendMessageTimeout(
                _progmanHwnd,
                0x052C,
                new IntPtr(0x0000000D),
                IntPtr.Zero,
                NativeMethods.SMTO_NORMAL,
                1000,
                out _);

            IntPtr workerW = IntPtr.Zero;

            // Iterate through all top-level windows to locate WorkerW containing SHELLDLL_DefView, or the wallpaper WorkerW
            NativeMethods.EnumWindows((hWnd, lParam) =>
            {
                var sb = new StringBuilder(256);
                NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
                if (sb.ToString() == "WorkerW")
                {
                    var shellDll = NativeMethods.FindWindowEx(hWnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (shellDll != IntPtr.Zero)
                    {
                        // Found WorkerW with SHELLDLL_DefView; find the next sibling WorkerW
                        workerW = NativeMethods.FindWindowEx(IntPtr.Zero, hWnd, "WorkerW", null);
                    }
                }
                return true;
            }, IntPtr.Zero);

            // Fallback: If no sibling WorkerW is found, check if Progman has SHELLDLL_DefView or use Progman/WorkerW directly
            if (workerW == IntPtr.Zero)
            {
                NativeMethods.EnumWindows((hWnd, lParam) =>
                {
                    var sb = new StringBuilder(256);
                    NativeMethods.GetClassName(hWnd, sb, sb.Capacity);
                    if (sb.ToString() == "WorkerW")
                    {
                        workerW = hWnd;
                        return false; // stop enumeration
                    }
                    return true;
                }, IntPtr.Zero);
            }

            if (workerW == IntPtr.Zero)
            {
                // Fallback to Progman if WorkerW cannot be identified
                workerW = _progmanHwnd;
            }

            if (workerW != IntPtr.Zero && NativeMethods.IsWindow(workerW))
            {
                _workerWHwnd = workerW;
                _health = DesktopHostHealth.Healthy;
                AppLogger.Instance.Info($"ExplorerDesktopHost: Initialized successfully. Progman: 0x{_progmanHwnd:X}, HostWorkerW: 0x{_workerWHwnd:X}");
                return true;
            }

            _health = DesktopHostHealth.Unavailable;
            AppLogger.Instance.Warn("ExplorerDesktopHost: Failed to find a valid WorkerW or Progman host.");
            return false;
        }
        catch (Exception ex)
        {
            _health = DesktopHostHealth.Unavailable;
            AppLogger.Instance.Error("ExplorerDesktopHost: Exception during host initialization", ex);
            return false;
        }
    }

    public AttachResult Attach(IntPtr widgetHwnd)
    {
        if (widgetHwnd == IntPtr.Zero || !NativeMethods.IsWindow(widgetHwnd))
        {
            return AttachResult.Failed(Name, "Invalid widget HWND.");
        }

        if (_workerWHwnd == IntPtr.Zero || !NativeMethods.IsWindow(_workerWHwnd))
        {
            if (!InitializeHost())
            {
                return AttachResult.Failed(Name, "Explorer host window is unavailable.");
            }
        }

        try
        {
            // Set parent to WorkerW/Progman
            NativeMethods.SetParent(widgetHwnd, _workerWHwnd);

            // Update window styles to WS_CHILD | WS_CLIPSIBLINGS, remove WS_POPUP
            int style = (int)NativeMethods.GetWindowLongPtr(widgetHwnd, NativeMethods.GWL_STYLE);
            style |= (NativeMethods.WS_CHILD | NativeMethods.WS_CLIPSIBLINGS);
            style &= ~NativeMethods.WS_POPUP;
            NativeMethods.SetWindowLongPtr(widgetHwnd, NativeMethods.GWL_STYLE, new IntPtr(style));

            // Set extended window style: WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            int exStyle = (int)NativeMethods.GetWindowLongPtr(widgetHwnd, NativeMethods.GWL_EXSTYLE);
            exStyle |= (NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);
            NativeMethods.SetWindowLongPtr(widgetHwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(exStyle));

            // Apply frame changes and show window
            NativeMethods.SetWindowPos(
                widgetHwnd,
                NativeMethods.HWND_TOP,
                0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_FRAMECHANGED |
                NativeMethods.SWP_SHOWWINDOW);

            _attachedWindows.Add(widgetHwnd);
            AppLogger.Instance.Info($"ExplorerDesktopHost: Attached widget HWND 0x{widgetHwnd:X} to host 0x{_workerWHwnd:X}");
            return AttachResult.Succeeded(Name);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"ExplorerDesktopHost: Error attaching widget 0x{widgetHwnd:X}", ex);
            return AttachResult.Failed(Name, ex.Message);
        }
    }

    public void Detach(IntPtr widgetHwnd)
    {
        if (widgetHwnd == IntPtr.Zero) return;

        try
        {
            if (_attachedWindows.Contains(widgetHwnd))
            {
                NativeMethods.SetParent(widgetHwnd, IntPtr.Zero);

                int style = (int)NativeMethods.GetWindowLongPtr(widgetHwnd, NativeMethods.GWL_STYLE);
                style &= ~NativeMethods.WS_CHILD;
                style |= NativeMethods.WS_POPUP;
                NativeMethods.SetWindowLongPtr(widgetHwnd, NativeMethods.GWL_STYLE, new IntPtr(style));

                NativeMethods.SetWindowPos(
                    widgetHwnd,
                    IntPtr.Zero,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);

                _attachedWindows.Remove(widgetHwnd);
                AppLogger.Instance.Info($"ExplorerDesktopHost: Detached widget HWND 0x{widgetHwnd:X}");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"ExplorerDesktopHost: Error detaching widget 0x{widgetHwnd:X}", ex);
        }
    }

    public void ReattachAll(string reason)
    {
        AppLogger.Instance.Info($"ExplorerDesktopHost: ReattachAll requested. Reason: {reason}");
        InitializeHost();

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
        if (_workerWHwnd != IntPtr.Zero && NativeMethods.IsWindow(_workerWHwnd))
        {
            if (NativeMethods.GetWindowRect(_workerWHwnd, out var rect))
            {
                return rect;
            }
        }
        return new NativeMethods.RECT(0, 0, 1920, 1080);
    }

    public void Dispose()
    {
        var windows = new List<IntPtr>(_attachedWindows);
        foreach (var hwnd in windows)
        {
            Detach(hwnd);
        }
        _attachedWindows.Clear();
    }
}
