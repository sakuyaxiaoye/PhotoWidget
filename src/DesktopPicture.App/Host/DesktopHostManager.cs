using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using DesktopPicture.Interop;
using DesktopPicture.Logging;

namespace DesktopPicture.Host;

public sealed class DesktopHostManager : IDisposable
{
    private static readonly Lazy<DesktopHostManager> _instance = new(() => new DesktopHostManager());
    public static DesktopHostManager Instance => _instance.Value;

    private readonly object _lock = new();
    private IDesktopHost _currentHost;
    private readonly HashSet<IntPtr> _registeredWidgets = new();
    private readonly uint _taskbarCreatedMsg;
    private HwndSource? _messageHookHwndSource;
    private Timer? _reattachDebounceTimer;

    public IDesktopHost CurrentHost => _currentHost;
    public DesktopHostHealth CurrentHealth => _currentHost.Health;

    public event Action<DesktopHostHealth>? HealthChanged;

    public DesktopHostManager()
    {
        _taskbarCreatedMsg = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        if (IsLiveWallpaperRunning())
        {
            AppLogger.Instance.Info("DesktopHostManager: Live wallpaper engine (Wallpaper Engine/Lively) detected. Using BottomWindowHost to prevent wallpaper flicker and overlay above live wallpaper.");
            _currentHost = new BottomWindowHost();
        }
        else
        {
            var explorerHost = new ExplorerDesktopHost();
            if (explorerHost.Health == DesktopHostHealth.Healthy)
            {
                _currentHost = explorerHost;
            }
            else
            {
                AppLogger.Instance.Warn("DesktopHostManager: ExplorerDesktopHost unavailable. Falling back to BottomWindowHost.");
                _currentHost = new BottomWindowHost();
            }
        }
    }

    public static bool IsLiveWallpaperRunning()
    {
        try
        {
            var processNames = new[]
            {
                "wallpaper32", "wallpaper64",
                "wallpaperservice32", "wallpaperservice64",
                "webwallpaper32", "webwallpaper64",
                "ui32", "ui64",
                "lively", "upupoo"
            };
            foreach (var name in processNames)
            {
                if (System.Diagnostics.Process.GetProcessesByName(name).Length > 0)
                {
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    public void InitializeMessageHook(HwndSource hwndSource)
    {
        _messageHookHwndSource = hwndSource;
        _messageHookHwndSource.AddHook(WndProcHook);
        try
        {
            NativeMethods.WTSRegisterSessionNotification(hwndSource.Handle, NativeMethods.NOTIFY_FOR_THIS_SESSION);
        }
        catch { }
        AppLogger.Instance.Info("DesktopHostManager: Win32 message hook and session notification registered.");
    }

    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        uint uMsg = (uint)msg;

        if (uMsg == _taskbarCreatedMsg)
        {
            AppLogger.Instance.Info("DesktopHostManager: Received TaskbarCreated message (Explorer restarted).");
            ScheduleReattach("TaskbarCreated", delayMs: 1500);
        }
        else if (uMsg == NativeMethods.WM_DISPLAYCHANGE)
        {
            AppLogger.Instance.Info("DesktopHostManager: Received WM_DISPLAYCHANGE message.");
            ScheduleReattach("WM_DISPLAYCHANGE", delayMs: 500);
            try
            {
                UI.WidgetController.Instance.HandleDisplayTopologyChanged();
            }
            catch { }
        }
        else if (uMsg == NativeMethods.WM_SETTINGCHANGE)
        {
            ScheduleReattach("WM_SETTINGCHANGE", delayMs: 800);
        }
        else if (uMsg == NativeMethods.WM_WTSSESSION_CHANGE)
        {
            int eventType = wParam.ToInt32();
            if (eventType == NativeMethods.WTS_SESSION_LOCK)
            {
                AppLogger.Instance.Info("DesktopHostManager: Windows session locked (Win+L). Pausing all widget playback to save HDD life and energy.");
                UI.WidgetController.Instance.SetSystemPowerPaused(true);
            }
            else if (eventType == NativeMethods.WTS_SESSION_UNLOCK)
            {
                AppLogger.Instance.Info("DesktopHostManager: Windows session unlocked. Resuming widget playback.");
                UI.WidgetController.Instance.SetSystemPowerPaused(false);
            }
        }
        else if (uMsg == NativeMethods.WM_POWERBROADCAST)
        {
            int powerEvent = wParam.ToInt32();
            if (powerEvent == NativeMethods.PBT_APMSUSPEND)
            {
                AppLogger.Instance.Info("DesktopHostManager: System suspending. Pausing widget playback.");
                UI.WidgetController.Instance.SetSystemPowerPaused(true);
            }
            else if (powerEvent == NativeMethods.PBT_APMRESUMEAUTOMATIC)
            {
                AppLogger.Instance.Info("DesktopHostManager: System resumed from suspend. Resuming widget playback.");
                UI.WidgetController.Instance.SetSystemPowerPaused(false);
            }
        }

        return IntPtr.Zero;
    }

    public void ScheduleReattach(string reason, int delayMs = 1000)
    {
        lock (_lock)
        {
            _reattachDebounceTimer?.Dispose();
            _reattachDebounceTimer = new Timer(_ =>
            {
                ReattachAll(reason);
            }, null, delayMs, Timeout.Infinite);
        }
    }

    public AttachResult AttachWidget(IntPtr widgetHwnd)
    {
        lock (_lock)
        {
            _registeredWidgets.Add(widgetHwnd);
            var result = _currentHost.Attach(widgetHwnd);

            if (!result.Success && _currentHost is ExplorerDesktopHost)
            {
                AppLogger.Instance.Warn("Explorer attach failed. Switching to BottomWindowHost fallback.");
                _currentHost.Dispose();
                _currentHost = new BottomWindowHost();
                HealthChanged?.Invoke(_currentHost.Health);
                result = _currentHost.Attach(widgetHwnd);
            }

            return result;
        }
    }

    public void DetachWidget(IntPtr widgetHwnd)
    {
        lock (_lock)
        {
            _registeredWidgets.Remove(widgetHwnd);
            _currentHost.Detach(widgetHwnd);
        }
    }

    public void ReattachAll(string reason)
    {
        lock (_lock)
        {
            AppLogger.Instance.Info($"DesktopHostManager: Reattaching all {_registeredWidgets.Count} widgets. Reason: {reason}");

            // NEVER try Explorer host if Wallpaper Engine / live wallpaper is running to avoid sending 0x052C
            if (_currentHost is BottomWindowHost && !IsLiveWallpaperRunning())
            {
                var explorerHost = new ExplorerDesktopHost();
                if (explorerHost.Health == DesktopHostHealth.Healthy)
                {
                    _currentHost.Dispose();
                    _currentHost = explorerHost;
                    HealthChanged?.Invoke(_currentHost.Health);
                }
            }

            _currentHost.ReattachAll(reason);

            if (_currentHost.Health == DesktopHostHealth.Unavailable && _currentHost is ExplorerDesktopHost)
            {
                AppLogger.Instance.Warn("DesktopHostManager: ExplorerHost degraded/unavailable after reattach. Switching to BottomWindowHost.");
                _currentHost.Dispose();
                _currentHost = new BottomWindowHost();
                HealthChanged?.Invoke(_currentHost.Health);
                _currentHost.ReattachAll(reason);
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _reattachDebounceTimer?.Dispose();
            _reattachDebounceTimer = null;

            if (_messageHookHwndSource != null)
            {
                _messageHookHwndSource.RemoveHook(WndProcHook);
                _messageHookHwndSource = null;
            }

            _currentHost.Dispose();
            _registeredWidgets.Clear();
        }
    }
}
