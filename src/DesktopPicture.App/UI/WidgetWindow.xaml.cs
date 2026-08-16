using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using DesktopPicture.Config;
using DesktopPicture.Display;
using DesktopPicture.Host;
using DesktopPicture.Interop;
using DesktopPicture.Logging;
using DesktopPicture.Playback;

namespace DesktopPicture.UI;

public partial class WidgetWindow : Window
{
    private readonly WidgetConfig _config;
    private IntPtr _hwnd = IntPtr.Zero;
    private HwndSource? _hwndSource;
    private string? _currentFilePath;

    private WidgetScheduler? _scheduler;

    public WidgetConfig Config => _config;
    public IntPtr Handle => _hwnd;
    public WidgetScheduler? Scheduler => _scheduler;

    public WidgetWindow(WidgetConfig config)
    {
        InitializeComponent();
        _config = config;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = _config.WidthDip;
        Height = _config.HeightDip;
        Left = _config.LeftDip;
        Top = _config.TopDip;

        UpdateUiInfo();

        Loaded += OnLoaded;
        Closed += OnClosed;
        LocationChanged += OnWindowLocationChanged;
        SizeChanged += OnWindowSizeChanged;
        ContentHost.SizeChanged += (s, e) => UpdateContentClip();
        MouseEnter += OnMouseEnterWindow;
        MouseLeave += OnMouseLeaveWindow;
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        if (IsLoaded && !double.IsNaN(Left) && !double.IsNaN(Top))
        {
            _config.LeftDip = Left;
            _config.TopDip = Top;
            SettingsService.Instance.ScheduleSave(250);
        }
    }

    private void OnMouseEnterWindow(object sender, MouseEventArgs e)
    {
        HoverToolbar.IsHitTestVisible = true;
        var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(150));
        HoverToolbar.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void OnMouseLeaveWindow(object sender, MouseEventArgs e)
    {
        HoverToolbar.IsHitTestVisible = false;
        var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(250));
        HoverToolbar.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            _config.WidthDip = e.NewSize.Width;
            _config.HeightDip = e.NewSize.Height;
            if (!double.IsNaN(Left) && !double.IsNaN(Top))
            {
                _config.LeftDip = Left;
                _config.TopDip = Top;
            }
            UpdateUiInfo();
            SettingsService.Instance.ScheduleSave(250);

            // Re-decode picture at new size so it fits sharply
            _scheduler?.RedecodeCurrent();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_hwnd);
        _hwndSource?.AddHook(WndProc);

        ApplyPositionAndAttach();

        // Initialize and start playback scheduler
        _scheduler = new WidgetScheduler(_config, this);
        _scheduler.Start();
    }

    public const int Windows11CornerRadius = 12;

    private void UpdateContentClip()
    {
        if (ContentHost.ActualWidth <= 0 || ContentHost.ActualHeight <= 0) return;
        int radius = _config.EnableCornerRadius ? Windows11CornerRadius : 0;
        CardBorder.CornerRadius = new CornerRadius(radius);

        ContentHost.Clip = new System.Windows.Media.RectangleGeometry(
            new Rect(0, 0, ContentHost.ActualWidth, ContentHost.ActualHeight),
            radius,
            radius);
    }

    public void ApplyPositionAndAttach()
    {
        if (_hwnd == IntPtr.Zero) return;

        var (safeLeft, safeTop) = DisplayCoordinateService.Instance.EnsureVisibleOnScreen(
            _config.LeftDip,
            _config.TopDip,
            _config.WidthDip,
            _config.HeightDip,
            _config.MonitorId);

        _config.LeftDip = safeLeft;
        _config.TopDip = safeTop;

        Left = safeLeft;
        Top = safeTop;
        Width = _config.WidthDip;
        Height = _config.HeightDip;

        DesktopHostManager.Instance.AttachWidget(_hwnd);
        UpdateUiInfo();
    }

    public void UpdateUiInfo()
    {
        WidgetNameText.Text = _config.Name;
        CoordsText.Text = $"位置: ({(int)_config.LeftDip}, {(int)_config.TopDip}) | 尺寸: {(int)_config.WidthDip} × {(int)_config.HeightDip}";
        StatusBadge.Text = _config.Paused ? "已暂停" : "运行中";

        UpdateContentClip();
    }

    public void UpdateUiState() => UpdateUiInfo();

    private bool _isPrimaryActive = true;

    public void DisplayImage(BitmapSource bitmap, string fullPath)
    {
        _currentFilePath = fullPath;
        ImageContainer.Visibility = Visibility.Visible;
        PlaceholderContainer.Visibility = Visibility.Collapsed;

        string fname = Path.GetFileName(fullPath);
        FilenameText.Text = fname;

        HoverTitleText.Text = "照片";
        HoverPathText.Text = fullPath;

        // Dynamic Color Extraction matching image palette per user design
        var palette = AdaptivePalette.FromBitmap(bitmap);
        HoverToolbar.Background = palette.BackgroundBrush;
        HoverToolbar.BorderBrush = palette.BorderBrush;
        HoverTitleText.Foreground = palette.TitleForeground;
        HoverPathText.Foreground = palette.SubtitleForeground;
        BtnPrev.Foreground = palette.IconForeground;
        BtnNext.Foreground = palette.IconForeground;
        BtnOpenImage.Foreground = palette.IconForeground;
        BtnFolder.Foreground = palette.IconForeground;
        BtnMore.Foreground = palette.IconForeground;

        // Perform smooth hardware-accelerated 350ms cross-fade transition
        if (ImagePrimary.Source == null)
        {
            ImagePrimary.Source = bitmap;
            ImagePrimary.Opacity = 1.0;
            ImageSecondary.Opacity = 0.0;
            _isPrimaryActive = true;
        }
        else if (_isPrimaryActive)
        {
            ImageSecondary.Source = bitmap;
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            fadeIn.Completed += (s, e) =>
            {
                ImagePrimary.Source = bitmap;
                ImagePrimary.Opacity = 1.0;
                ImageSecondary.Opacity = 0.0;
                ImageSecondary.BeginAnimation(UIElement.OpacityProperty, null);
                ImagePrimary.BeginAnimation(UIElement.OpacityProperty, null);
                _isPrimaryActive = true;
            };
            _isPrimaryActive = false;
            ImageSecondary.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            ImagePrimary.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
        else
        {
            ImagePrimary.Source = bitmap;
            var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };
            fadeIn.Completed += (s, e) =>
            {
                ImageSecondary.Source = bitmap;
                ImageSecondary.Opacity = 1.0;
                ImagePrimary.Opacity = 0.0;
                ImagePrimary.BeginAnimation(UIElement.OpacityProperty, null);
                ImageSecondary.BeginAnimation(UIElement.OpacityProperty, null);
                _isPrimaryActive = false;
            };
            _isPrimaryActive = true;
            ImagePrimary.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            ImageSecondary.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
    }

    public void ShowPlaceholder(string title, string subtitle)
    {
        ImageContainer.Visibility = Visibility.Collapsed;
        PlaceholderContainer.Visibility = Visibility.Visible;
        PlaceholderTitle.Text = title;
        PlaceholderSub.Text = subtitle;
        StatusBadge.Text = "待配置";
    }

    public void ShowScanningState()
    {
        ImageContainer.Visibility = Visibility.Collapsed;
        PlaceholderContainer.Visibility = Visibility.Visible;
        PlaceholderTitle.Text = "🔍 正在极速索引图库...";
        PlaceholderSub.Text = $"目录: {_config.RootPath}";
        StatusBadge.Text = "索引中";
    }

    public void UpdateCandidateCount(int count)
    {
        StatsText.Text = $"候选图片: {count:N0} 张";
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        base.OnPreviewMouseWheel(e);
        if (e.Delta > 0)
        {
            _scheduler?.SwitchPrevious();
        }
        else if (e.Delta < 0)
        {
            _scheduler?.SwitchNext(isManual: true);
        }
        e.Handled = true;
    }

    private void PrevImage_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _scheduler?.SwitchPrevious();
    }

    private void NextImage_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        _scheduler?.SwitchNext(isManual: true);
    }

    private void MoreMenu_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowWidgetContextMenu();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        var win = new SettingsWindow(_config);
        win.ShowDialog();
    }

    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo(_currentFilePath)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Warn($"Failed to open image '{_currentFilePath}': {ex.Message}");
            }
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        try
        {
            if (!string.IsNullOrEmpty(_currentFilePath) && File.Exists(_currentFilePath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_currentFilePath}\"")
                {
                    UseShellExecute = true
                });
            }
            else if (Directory.Exists(_config.RootPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_config.RootPath}\"")
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn($"Failed to open folder for '{_currentFilePath}': {ex.Message}");
        }
    }

    private const int ResizeBorderThickness = 12;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        uint uMsg = (uint)msg;

        if (uMsg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }
        else if (uMsg == NativeMethods.WM_NCHITTEST)
        {
            int x = (short)(lParam.ToInt64() & 0xFFFF);
            int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

            if (NativeMethods.GetWindowRect(hwnd, out var rect))
            {
                bool left = x >= rect.Left && x < rect.Left + ResizeBorderThickness;
                bool right = x < rect.Right && x >= rect.Right - ResizeBorderThickness;
                bool top = y >= rect.Top && y < rect.Top + ResizeBorderThickness;
                bool bottom = y < rect.Bottom && y >= rect.Bottom - ResizeBorderThickness;

                if (top && left) { handled = true; return new IntPtr(NativeMethods.HTTOPLEFT); }
                if (top && right) { handled = true; return new IntPtr(NativeMethods.HTTOPRIGHT); }
                if (bottom && left) { handled = true; return new IntPtr(NativeMethods.HTBOTTOMLEFT); }
                if (bottom && right) { handled = true; return new IntPtr(NativeMethods.HTBOTTOMRIGHT); }
                if (left) { handled = true; return new IntPtr(NativeMethods.HTLEFT); }
                if (right) { handled = true; return new IntPtr(NativeMethods.HTRIGHT); }
                if (top) { handled = true; return new IntPtr(NativeMethods.HTTOP); }
                if (bottom) { handled = true; return new IntPtr(NativeMethods.HTBOTTOM); }
            }
        }
        return IntPtr.Zero;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (e.OriginalSource is DependencyObject dep)
        {
            var btn = FindParent<Button>(dep);
            if (btn != null) return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            try
            {
                DragMove();

                var (safeLeft, safeTop) = DisplayCoordinateService.Instance.EnsureVisibleOnScreen(
                    Left,
                    Top,
                    _config.WidthDip,
                    _config.HeightDip);

                var (snappedLeft, snappedTop) = DisplayCoordinateService.Instance.SnapToEdges(
                    safeLeft,
                    safeTop,
                    _config.WidthDip,
                    _config.HeightDip,
                    _hwnd);

                _config.LeftDip = snappedLeft;
                _config.TopDip = snappedTop;
                Left = snappedLeft;
                Top = snappedTop;

                SettingsService.Instance.ScheduleSave(250);
                UpdateUiInfo();
            }
            catch { }
            e.Handled = true;
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        ShowWidgetContextMenu();
        e.Handled = true;
    }

    private void ShowWidgetContextMenu()
    {
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
            StaysOpen = false,
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0B0F19")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A374D")),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4),
            FontSize = 12
        };

        menu.Closed += (s, e) =>
        {
            this.ContextMenu = null;
        };

        var titleItem = new MenuItem
        {
            Header = _config.Name,
            IsEnabled = false,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#38BDF8"))
        };
        menu.Items.Add(titleItem);
        menu.Items.Add(new Separator { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")), Margin = new Thickness(0, 3, 0, 3) });

        var settingsItem = new MenuItem
        {
            Header = "组件设置...",
            FontWeight = FontWeights.SemiBold
        };
        settingsItem.Click += (s, e) =>
        {
            var win = new SettingsWindow(_config);
            win.ShowDialog();
        };
        menu.Items.Add(settingsItem);

        var prevItem = new MenuItem
        {
            Header = "上一张 (历史记录)"
        };
        prevItem.Click += (s, e) =>
        {
            _scheduler?.SwitchPrevious();
        };
        menu.Items.Add(prevItem);

        var switchNowItem = new MenuItem
        {
            Header = "切换下一张"
        };
        switchNowItem.Click += (s, e) =>
        {
            _scheduler?.SwitchNext(isManual: true);
        };
        menu.Items.Add(switchNowItem);

        var togglePauseItem = new MenuItem
        {
            Header = _config.Paused ? "继续自动轮播" : "暂停自动轮播"
        };
        togglePauseItem.Click += (s, e) =>
        {
            WidgetController.Instance.TogglePause(_config.Id);
            UpdateUiInfo();
        };
        menu.Items.Add(togglePauseItem);

        menu.Items.Add(new Separator { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")), Margin = new Thickness(0, 3, 0, 3) });

        var openImageItem = new MenuItem
        {
            Header = "打开图片原图"
        };
        openImageItem.Click += (s, e) => OpenImage_Click(s, e);
        menu.Items.Add(openImageItem);

        var openFolderItem = new MenuItem
        {
            Header = "打开所在文件夹"
        };
        openFolderItem.Click += (s, e) => OpenFolder_Click(s, e);
        menu.Items.Add(openFolderItem);

        menu.Items.Add(new Separator { Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")), Margin = new Thickness(0, 3, 0, 3) });

        var addWidgetItem = new MenuItem
        {
            Header = $"新建照片组件 ({SettingsService.Instance.Current.Widgets.Count}/{WidgetController.MaxWidgets})",
            IsEnabled = SettingsService.Instance.Current.Widgets.Count < WidgetController.MaxWidgets
        };
        addWidgetItem.Click += (s, e) => WidgetController.Instance.CreateWidget();
        menu.Items.Add(addWidgetItem);

        var hideItem = new MenuItem
        {
            Header = "隐藏此组件"
        };
        hideItem.Click += (s, e) => WidgetController.Instance.HideWidget(_config.Id);
        menu.Items.Add(hideItem);

        var deleteItem = new MenuItem
        {
            Header = "删除此组件",
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"))
        };
        deleteItem.Click += (s, e) => WidgetController.Instance.DeleteWidget(_config.Id);
        menu.Items.Add(deleteItem);

        NativeMethods.SetForegroundWindow(_hwnd);
        this.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T typed) return typed;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (!double.IsNaN(Left) && !double.IsNaN(Top))
        {
            _config.LeftDip = Left;
            _config.TopDip = Top;
            _config.WidthDip = Width;
            _config.HeightDip = Height;
            SettingsService.Instance.SaveImmediate();
        }

        _scheduler?.Dispose();
        _scheduler = null;

        if (_hwnd != IntPtr.Zero)
        {
            DesktopHostManager.Instance.DetachWidget(_hwnd);
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource?.Dispose();
            _hwndSource = null;
            _hwnd = IntPtr.Zero;
        }
    }
}
