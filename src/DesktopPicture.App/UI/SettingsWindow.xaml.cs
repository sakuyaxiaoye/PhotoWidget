using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using DesktopPicture.Config;
using DesktopPicture.Display;
using Microsoft.Win32;

namespace DesktopPicture.UI;

public partial class SettingsWindow : Window
{
    private readonly WidgetConfig _config;

    public SettingsWindow(WidgetConfig config)
    {
        InitializeComponent();
        _config = config;

        DialogHeaderTitle.Text = $"⚙️ 配置 - {_config.Name}";
        NameInput.Text = _config.Name;
        RootPathInput.Text = _config.RootPath;
        WidthInput.Text = ((int)_config.WidthDip).ToString();
        HeightInput.Text = ((int)_config.HeightDip).ToString();
        IntervalInput.Text = _config.IntervalSeconds.ToString();
        PausedCheckbox.IsChecked = _config.Paused;
        VisibleCheckbox.IsChecked = _config.Visible;
        EnableCornerRadiusCheckbox.IsChecked = _config.EnableCornerRadius;
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择图片根目录",
            InitialDirectory = Directory.Exists(_config.RootPath) ? _config.RootPath : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog(this) == true)
        {
            RootPathInput.Text = dialog.FolderName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        string name = string.IsNullOrWhiteSpace(NameInput.Text) ? _config.Name : NameInput.Text.Trim();
        string rootPath = RootPathInput.Text.Trim();

        if (!double.TryParse(WidthInput.Text, out double newWidth)) newWidth = _config.WidthDip;
        if (!double.TryParse(HeightInput.Text, out double newHeight)) newHeight = _config.HeightDip;
        if (!int.TryParse(IntervalInput.Text, out int newInterval)) newInterval = _config.IntervalSeconds;

        newWidth = Math.Clamp(newWidth, 90, 3840);
        newHeight = Math.Clamp(newHeight, 90, 3840);
        newInterval = Math.Clamp(newInterval, 5, 86400);

        bool pathChanged = !string.Equals(_config.RootPath, rootPath, StringComparison.OrdinalIgnoreCase);
        bool sizeChanged = Math.Abs(_config.WidthDip - newWidth) > 0.1 || Math.Abs(_config.HeightDip - newHeight) > 0.1;

        if (sizeChanged)
        {
            // Centered resize anchoring per SPEC 5.3
            double centerX = _config.LeftDip + _config.WidthDip / 2.0;
            double centerY = _config.TopDip + _config.HeightDip / 2.0;

            _config.WidthDip = newWidth;
            _config.HeightDip = newHeight;
            _config.LeftDip = centerX - newWidth / 2.0;
            _config.TopDip = centerY - newHeight / 2.0;

            var (safeLeft, safeTop) = DisplayCoordinateService.Instance.EnsureVisibleOnScreen(
                _config.LeftDip,
                _config.TopDip,
                _config.WidthDip,
                _config.HeightDip);

            _config.LeftDip = safeLeft;
            _config.TopDip = safeTop;
        }

        _config.Name = name;
        _config.RootPath = rootPath;
        _config.IntervalSeconds = newInterval;
        _config.Paused = PausedCheckbox.IsChecked == true;
        _config.Visible = VisibleCheckbox.IsChecked == true;
        _config.EnableCornerRadius = EnableCornerRadiusCheckbox.IsChecked == true;

        SettingsService.Instance.SaveImmediate();

        // Apply changes to live active window
        if (WidgetController.Instance.ActiveWindows.TryGetValue(_config.Id, out var win))
        {
            if (!_config.Visible)
            {
                WidgetController.Instance.HideWidget(_config.Id);
            }
            else
            {
                win.ApplyPositionAndAttach();
                win.Scheduler?.UpdateInterval(_config.IntervalSeconds);
                win.Scheduler?.SetPaused(_config.Paused);

                if (pathChanged)
                {
                    win.Scheduler?.RescanAndPlay();
                }
                else if (sizeChanged)
                {
                    win.Scheduler?.RedecodeCurrent();
                }
            }
        }
        else if (_config.Visible)
        {
            WidgetController.Instance.ShowWidget(_config);
        }

        Close();
    }

    private void PresetSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            var parts = tag.Split(',');
            if (parts.Length == 2)
            {
                WidthInput.Text = parts[0];
                HeightInput.Text = parts[1];
            }
        }
    }

    private void PresetInterval_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag)
        {
            IntervalInput.Text = tag;
        }
    }

    private void SwapOrientation_Click(object sender, RoutedEventArgs e)
    {
        string currentW = WidthInput.Text;
        string currentH = HeightInput.Text;
        WidthInput.Text = currentH;
        HeightInput.Text = currentW;
    }

    private void SwitchNow_Click(object sender, RoutedEventArgs e)
    {
        if (WidgetController.Instance.ActiveWindows.TryGetValue(_config.Id, out var win))
        {
            win.Scheduler?.SwitchNext(isManual: true);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
