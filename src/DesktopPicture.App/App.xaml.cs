using System;
using System.Threading;
using System.Windows;
using DesktopPicture.Config;
using DesktopPicture.Host;
using DesktopPicture.Interop;
using DesktopPicture.Logging;
using DesktopPicture.UI;

namespace DesktopPicture;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private const string MutexName = "DesktopPicture_SingleInstance_Mutex_2026";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool isFirstInstance;
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out isFirstInstance);
        }
        catch
        {
            isFirstInstance = true;
        }

        if (!isFirstInstance)
        {
            AppLogger.Instance.Warn("Another instance of Desktop Picture is already running. Exiting.");
            Shutdown();
            return;
        }

        AppLogger.Instance.Info("=== Desktop Picture Starting (Phase 0 Prototype) ===");

        try
        {
            uint access = NativeMethods.DESKTOP_READOBJECTS | NativeMethods.DESKTOP_CREATEWINDOW |
                          NativeMethods.DESKTOP_ENUMERATE | NativeMethods.DESKTOP_WRITEOBJECTS |
                          NativeMethods.DESKTOP_SWITCHDESKTOP;
            var hInputDesktop = NativeMethods.OpenInputDesktop(0, false, access);
            if (hInputDesktop == IntPtr.Zero)
            {
                hInputDesktop = NativeMethods.OpenDesktop("Default", 0, false, access);
            }
            if (hInputDesktop != IntPtr.Zero)
            {
                NativeMethods.SetThreadDesktop(hInputDesktop);
            }

            // 1. Load Settings
            var settings = SettingsService.Instance.Current;
            AppLogger.Instance.Info($"Settings loaded. Configured widgets: {settings.Widgets.Count}");

            // 2. Sync Windows Autostart Registry state if enabled
            AutoStartManager.SyncOnStartup();

            // 3. Initialize Tray Icon
            TrayIconService.Instance.Initialize();

            // 4. Initialize Widgets
            WidgetController.Instance.Initialize();

            AppLogger.Instance.Info("Initialization completed successfully.");

            // Flush startup working set back to OS after 3 seconds
            System.Threading.Tasks.Task.Delay(3000).ContinueWith(_ => DesktopPicture.Diagnostics.MemoryOptimizer.TrimWorkingSet());
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Fatal error during startup", ex);
            MessageBox.Show($"程序启动失败: {ex.Message}\n请查看日志: %LocalAppData%\\PhotoWidget\\logs", "PhotoWidget 错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Instance.Info("=== PhotoWidget Shutting Down ===");

        try
        {
            foreach (var pair in WidgetController.Instance.ActiveWindows)
            {
                var win = pair.Value;
                if (!double.IsNaN(win.Left) && !double.IsNaN(win.Top))
                {
                    win.Config.LeftDip = win.Left;
                    win.Config.TopDip = win.Top;
                    win.Config.WidthDip = win.Width;
                    win.Config.HeightDip = win.Height;
                }
            }
            SettingsService.Instance.SaveImmediate();

            WidgetController.Instance.CloseAll();
            SettingsService.Instance.Dispose();
            TrayIconService.Instance.Dispose();
            DesktopHostManager.Instance.Dispose();
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Error during shutdown cleanup", ex);
        }

        if (_singleInstanceMutex != null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
            }
            catch { }
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }
}
