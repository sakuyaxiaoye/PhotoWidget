using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DesktopPicture.Interop;
using DesktopPicture.Logging;

namespace DesktopPicture.Display;

public sealed record MonitorInfo(
    IntPtr Handle,
    string DeviceName,
    NativeMethods.RECT MonitorRect,
    NativeMethods.RECT WorkRect,
    bool IsPrimary,
    uint DpiX,
    uint DpiY)
{
    public double ScaleX => DpiX / 96.0;
    public double ScaleY => DpiY / 96.0;
}

public sealed class DisplayCoordinateService
{
    private static readonly Lazy<DisplayCoordinateService> _instance = new(() => new DisplayCoordinateService());
    public static DisplayCoordinateService Instance => _instance.Value;

    public List<MonitorInfo> GetAllMonitors()
    {
        var monitors = new List<MonitorInfo>();

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref NativeMethods.RECT lprcMonitor, IntPtr dwData) =>
        {
            var mi = new NativeMethods.MONITORINFOEX();
            mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));

            if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
            {
                uint dpiX = 96;
                uint dpiY = 96;
                try
                {
                    if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out dpiY) != 0)
                    {
                        dpiX = 96;
                        dpiY = 96;
                    }
                }
                catch
                {
                    dpiX = 96;
                    dpiY = 96;
                }

                bool isPrimary = (mi.dwFlags & 1) != 0;
                monitors.Add(new MonitorInfo(
                    hMonitor,
                    mi.szDevice,
                    mi.rcMonitor,
                    mi.rcWork,
                    isPrimary,
                    dpiX,
                    dpiY));
            }
            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    public MonitorInfo? GetPrimaryMonitor()
    {
        var monitors = GetAllMonitors();
        return monitors.Find(m => m.IsPrimary) ?? (monitors.Count > 0 ? monitors[0] : null);
    }

    public MonitorInfo? GetMonitorForWindow(IntPtr hwnd)
    {
        var hMonitor = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return GetPrimaryMonitor();

        var mi = new NativeMethods.MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
        if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
        {
            uint dpiX = 96;
            uint dpiY = 96;
            try
            {
                if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out dpiY) != 0)
                {
                    dpiX = 96;
                    dpiY = 96;
                }
            }
            catch
            {
                dpiX = 96;
                dpiY = 96;
            }

            return new MonitorInfo(hMonitor, mi.szDevice, mi.rcMonitor, mi.rcWork, (mi.dwFlags & 1) != 0, dpiX, dpiY);
        }

        return GetPrimaryMonitor();
    }

    public MonitorInfo? GetMonitorForPoint(int physicalX, int physicalY)
    {
        var pt = new NativeMethods.POINT(physicalX, physicalY);
        var hMonitor = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return GetPrimaryMonitor();

        var mi = new NativeMethods.MONITORINFOEX();
        mi.cbSize = Marshal.SizeOf(typeof(NativeMethods.MONITORINFOEX));
        if (NativeMethods.GetMonitorInfo(hMonitor, ref mi))
        {
            uint dpiX = 96, dpiY = 96;
            try
            {
                if (NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out dpiX, out dpiY) != 0)
                {
                    dpiX = 96;
                    dpiY = 96;
                }
            }
            catch { }

            return new MonitorInfo(hMonitor, mi.szDevice, mi.rcMonitor, mi.rcWork, (mi.dwFlags & 1) != 0, dpiX, dpiY);
        }
        return GetPrimaryMonitor();
    }

    public NativeMethods.RECT GetVirtualDesktopBounds()
    {
        var monitors = GetAllMonitors();
        if (monitors.Count == 0)
        {
            return new NativeMethods.RECT(0, 0, 1920, 1080);
        }

        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;

        foreach (var m in monitors)
        {
            left = Math.Min(left, m.MonitorRect.Left);
            top = Math.Min(top, m.MonitorRect.Top);
            right = Math.Max(right, m.MonitorRect.Right);
            bottom = Math.Max(bottom, m.MonitorRect.Bottom);
        }

        return new NativeMethods.RECT(left, top, right, bottom);
    }

    public (double LeftDip, double TopDip) EnsureVisibleOnScreen(double leftDip, double topDip, double widthDip, double heightDip, string? preferredMonitorId = null)
    {
        var monitors = GetAllMonitors();
        if (monitors.Count == 0) return (leftDip, topDip);

        // Approximate physical center point based on primary scale for monitor hit-test
        var primary = GetPrimaryMonitor() ?? monitors[0];
        int approxPhysicalX = (int)(leftDip * primary.ScaleX + (widthDip * primary.ScaleX) / 2);
        int approxPhysicalY = (int)(topDip * primary.ScaleY + (heightDip * primary.ScaleY) / 2);

        var currentMonitor = GetMonitorForPoint(approxPhysicalX, approxPhysicalY) ?? primary;

        // Convert monitor work area to DIPs
        double workLeftDip = currentMonitor.WorkRect.Left / currentMonitor.ScaleX;
        double workTopDip = currentMonitor.WorkRect.Top / currentMonitor.ScaleY;
        double workRightDip = currentMonitor.WorkRect.Right / currentMonitor.ScaleX;
        double workBottomDip = currentMonitor.WorkRect.Bottom / currentMonitor.ScaleY;

        // Check if rectangle intersects meaningfully with any monitor
        bool intersectsAny = false;
        foreach (var m in monitors)
        {
            double mLeft = m.MonitorRect.Left / m.ScaleX;
            double mTop = m.MonitorRect.Top / m.ScaleY;
            double mRight = m.MonitorRect.Right / m.ScaleX;
            double mBottom = m.MonitorRect.Bottom / m.ScaleY;

            if (leftDip < mRight && leftDip + widthDip > mLeft &&
                topDip < mBottom && topDip + heightDip > mTop)
            {
                intersectsAny = true;
                break;
            }
        }

        if (!intersectsAny)
        {
            // Reset safely to primary work area
            AppLogger.Instance.Warn($"Widget at ({leftDip}, {topDip}) is completely off-screen. Resetting safely to primary monitor.");
            double primLeftDip = primary.WorkRect.Left / primary.ScaleX + 40;
            double primTopDip = primary.WorkRect.Top / primary.ScaleY + 40;
            return (primLeftDip, primTopDip);
        }

        return (leftDip, topDip);
    }

    public (double SnappedLeft, double SnappedTop) SnapToEdges(
        double leftDip,
        double topDip,
        double widthDip,
        double heightDip,
        IntPtr hwnd,
        double snapDistanceDip = 24.0)
    {
        var monitors = GetAllMonitors();
        if (monitors.Count == 0) return (leftDip, topDip);

        var primary = GetPrimaryMonitor() ?? monitors[0];
        int approxPhysicalX = (int)(leftDip * primary.ScaleX + (widthDip * primary.ScaleX) / 2);
        int approxPhysicalY = (int)(topDip * primary.ScaleY + (heightDip * primary.ScaleY) / 2);

        var currentMonitor = (hwnd != IntPtr.Zero ? GetMonitorForWindow(hwnd) : null) ??
                             GetMonitorForPoint(approxPhysicalX, approxPhysicalY) ?? primary;

        double scaleX = currentMonitor.ScaleX;
        double scaleY = currentMonitor.ScaleY;

        double workLeftDip = currentMonitor.WorkRect.Left / scaleX;
        double workTopDip = currentMonitor.WorkRect.Top / scaleY;
        double workRightDip = currentMonitor.WorkRect.Right / scaleX;
        double workBottomDip = currentMonitor.WorkRect.Bottom / scaleY;

        double newLeft = leftDip;
        double newTop = topDip;
        double rightDip = leftDip + widthDip;
        double bottomDip = topDip + heightDip;

        // Snap Left Edge
        if (Math.Abs(leftDip - workLeftDip) <= snapDistanceDip)
        {
            newLeft = workLeftDip;
        }
        // Snap Right Edge
        else if (Math.Abs(rightDip - workRightDip) <= snapDistanceDip)
        {
            newLeft = workRightDip - widthDip;
        }

        // Snap Top Edge
        if (Math.Abs(topDip - workTopDip) <= snapDistanceDip)
        {
            newTop = workTopDip;
        }
        // Snap Bottom Edge
        else if (Math.Abs(bottomDip - workBottomDip) <= snapDistanceDip)
        {
            newTop = workBottomDip - heightDip;
        }

        return (newLeft, newTop);
    }
}
