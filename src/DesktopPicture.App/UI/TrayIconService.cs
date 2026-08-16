using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using DesktopPicture.Config;
using DesktopPicture.Host;
using DesktopPicture.Interop;
using DesktopPicture.Logging;

namespace DesktopPicture.UI;

public sealed class TrayIconService : IDisposable
{
    private static readonly Lazy<TrayIconService> _instance = new(() => new TrayIconService());
    public static TrayIconService Instance => _instance.Value;

    private const uint WM_TRAYICON = 0x8001;
    private const uint TRAY_ICON_ID = 1001;

    private HwndSource? _messageWindow;
    private IntPtr _hwnd = IntPtr.Zero;
    private IntPtr _hIcon = IntPtr.Zero;
    private bool _isAdded = false;
    private ContextMenu? _contextMenu;

    public void Initialize()
    {
        var parameters = new HwndSourceParameters("DesktopPictureTrayMsgWindow")
        {
            WindowStyle = 0,
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW,
            Width = 0,
            Height = 0
        };
        parameters.SetPosition(0, 0);
        parameters.SetSize(0, 0);

        _messageWindow = new HwndSource(parameters);
        _hwnd = _messageWindow.Handle;
        _messageWindow.AddHook(TrayWndProc);

        DesktopHostManager.Instance.InitializeMessageHook(_messageWindow);

        CreateTrayIcon();
        AddNotifyIcon();
    }

    private void CreateTrayIcon()
    {
        try
        {
            string pngPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app.png");
            if (File.Exists(pngPath))
            {
                using var srcBmp = (Bitmap)System.Drawing.Image.FromFile(pngPath);
                using var trayBmp = new Bitmap(16, 16, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(trayBmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.Clear(Color.Transparent);
                    g.DrawImage(srcBmp, 0, 0, 16, 16);
                }
                _hIcon = trayBmp.GetHicon();
                return;
            }
        }
        catch { }

        // Fallback procedural icon
        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.FromArgb(37, 99, 235));
            g.FillEllipse(brush, 1, 1, 14, 14);

            using var whiteBrush = new SolidBrush(Color.White);
            g.FillRectangle(whiteBrush, 4, 6, 8, 6);
            g.FillPolygon(whiteBrush, new[]
            {
                new System.Drawing.Point(6, 4),
                new System.Drawing.Point(10, 4),
                new System.Drawing.Point(9, 6),
                new System.Drawing.Point(7, 6)
            });
            using var innerBrush = new SolidBrush(Color.FromArgb(37, 99, 235));
            g.FillEllipse(innerBrush, 6, 8, 4, 3);
        }

        _hIcon = bitmap.GetHicon();
    }

    public void AddNotifyIcon()
    {
        if (_hwnd == IntPtr.Zero) return;

        var nid = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONDATA)),
            hWnd = _hwnd,
            uID = TRAY_ICON_ID,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = _hIcon,
            szTip = $"桌面照片组件 (PhotoWidget)"
        };

        _isAdded = NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref nid);
        if (!_isAdded)
        {
            // Retry with modify in case it exists
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref nid);
        }

        AppLogger.Instance.Info($"TrayIconService: Notify icon added (status: {_isAdded}).");
    }

    public void UpdateTooltip(string text)
    {
        if (_hwnd == IntPtr.Zero) return;

        var nid = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONDATA)),
            hWnd = _hwnd,
            uID = TRAY_ICON_ID,
            uFlags = NativeMethods.NIF_TIP,
            szTip = text.Length > 127 ? text[..127] : text
        };

        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref nid);
    }

    public void RemoveNotifyIcon()
    {
        if (_hwnd != IntPtr.Zero && _isAdded)
        {
            var nid = new NativeMethods.NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(NativeMethods.NOTIFYICONDATA)),
                hWnd = _hwnd,
                uID = TRAY_ICON_ID
            };
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref nid);
            _isAdded = false;
        }
    }

    private IntPtr TrayWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_TRAYICON)
        {
            uint lMsg = (uint)lParam.ToInt32();
            if (lMsg == NativeMethods.WM_RBUTTONUP || lMsg == NativeMethods.WM_LBUTTONUP)
            {
                ShowContextMenu();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        _contextMenu = new ContextMenu();

        var titleItem = new MenuItem
        {
            Header = "桌面照片组件 (PhotoWidget)",
            IsEnabled = false,
            FontWeight = FontWeights.Bold
        };
        _contextMenu.Items.Add(titleItem);

        var hostStatusItem = new MenuItem
        {
            Header = $"桌面宿主: {DesktopHostManager.Instance.CurrentHost.Name} ({(DesktopHostManager.Instance.CurrentHealth == DesktopHostHealth.Healthy ? "正常" : "降级")})",
            IsEnabled = false
        };
        _contextMenu.Items.Add(hostStatusItem);

        _contextMenu.Items.Add(new Separator());

        // Create Widget Option
        var settings = SettingsService.Instance.Current;
        var addWidgetItem = new MenuItem
        {
            Header = $"新建桌面组件 ({settings.Widgets.Count}/{WidgetController.MaxWidgets})",
            IsEnabled = settings.Widgets.Count < WidgetController.MaxWidgets
        };
        addWidgetItem.Click += (s, e) =>
        {
            WidgetController.Instance.CreateWidget();
        };
        _contextMenu.Items.Add(addWidgetItem);

        _contextMenu.Items.Add(new Separator());

        // Widget List
        foreach (var widget in settings.Widgets)
        {
            var widgetSubMenu = new MenuItem
            {
                Header = $"{widget.Name} ({(widget.Visible ? "显示中" : "已隐藏")})"
            };

            var toggleVisItem = new MenuItem
            {
                Header = widget.Visible ? "隐藏组件" : "显示组件"
            };
            toggleVisItem.Click += (s, e) => WidgetController.Instance.ToggleVisibility(widget.Id);
            widgetSubMenu.Items.Add(toggleVisItem);

            var togglePauseItem = new MenuItem
            {
                Header = widget.Paused ? "继续自动轮播" : "暂停自动轮播"
            };
            togglePauseItem.Click += (s, e) => WidgetController.Instance.TogglePause(widget.Id);
            widgetSubMenu.Items.Add(togglePauseItem);

            var switchNowItem = new MenuItem
            {
                Header = "切换下一张"
            };
            switchNowItem.Click += (s, e) =>
            {
                if (WidgetController.Instance.ActiveWindows.TryGetValue(widget.Id, out var win))
                {
                    win.Scheduler?.SwitchNext(isManual: true);
                }
            };
            widgetSubMenu.Items.Add(switchNowItem);

            var configItem = new MenuItem
            {
                Header = "组件设置..."
            };
            configItem.Click += (s, e) =>
            {
                var settingsWin = new SettingsWindow(widget);
                settingsWin.ShowDialog();
            };
            widgetSubMenu.Items.Add(configItem);

            widgetSubMenu.Items.Add(new Separator());

            var deleteItem = new MenuItem
            {
                Header = "删除此组件",
                Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F87171"))
            };
            deleteItem.Click += (s, e) => WidgetController.Instance.DeleteWidget(widget.Id);
            widgetSubMenu.Items.Add(deleteItem);

            _contextMenu.Items.Add(widgetSubMenu);
        }

        _contextMenu.Items.Add(new Separator());

        // Auto start
        bool isAutoStart = AutoStartManager.IsAutoStartEnabled() || settings.StartWithWindows;
        var autoStartItem = new MenuItem
        {
            Header = $"开机自启动: {(isAutoStart ? "已开启" : "已关闭")}"
        };
        autoStartItem.Click += (s, e) =>
        {
            bool newState = !AutoStartManager.IsAutoStartEnabled();
            AutoStartManager.SetAutoStart(newState);
            autoStartItem.Header = $"开机自启动: {(newState ? "已开启" : "已关闭")}";
        };
        _contextMenu.Items.Add(autoStartItem);

        // Open App Data Directory
        var openDirItem = new MenuItem
        {
            Header = "打开配置目录 (AppData)"
        };
        openDirItem.Click += (s, e) =>
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPicture");
            if (Directory.Exists(appData))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", appData) { UseShellExecute = true });
            }
        };
        _contextMenu.Items.Add(openDirItem);

        _contextMenu.Items.Add(new Separator());

        // Exit
        var exitItem = new MenuItem
        {
            Header = "退出程序"
        };
        exitItem.Click += (s, e) =>
        {
            Application.Current.Shutdown();
        };
        _contextMenu.Items.Add(exitItem);

        NativeMethods.SetForegroundWindow(_hwnd);
        _contextMenu.IsOpen = true;
    }

    public void Dispose()
    {
        RemoveNotifyIcon();

        if (_hIcon != IntPtr.Zero)
        {
            NativeMethods.DestroyIcon(_hIcon);
            _hIcon = IntPtr.Zero;
        }

        if (_messageWindow != null)
        {
            _messageWindow.RemoveHook(TrayWndProc);
            _messageWindow.Dispose();
            _messageWindow = null;
        }
    }
}
