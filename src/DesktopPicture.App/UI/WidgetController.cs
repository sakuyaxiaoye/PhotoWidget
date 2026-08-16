using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using DesktopPicture.Config;
using DesktopPicture.Decoding;
using DesktopPicture.Logging;

namespace DesktopPicture.UI;

public sealed class WidgetController
{
    private static readonly Lazy<WidgetController> _instance = new(() => new WidgetController());
    public static WidgetController Instance => _instance.Value;

    private readonly Dictionary<string, WidgetWindow> _activeWindows = new();
    public const int MaxWidgets = 4;

    public IReadOnlyDictionary<string, WidgetWindow> ActiveWindows => _activeWindows;

    public void Initialize()
    {
        var settings = SettingsService.Instance.Current;
        if (settings.Widgets.Count == 0)
        {
            var defaultWidget = new WidgetConfig
            {
                Id = Guid.NewGuid().ToString("D"),
                Name = "图片组件 1",
                WidthDip = 480,
                HeightDip = 270,
                IntervalSeconds = 60,
                LeftDip = 40,
                TopDip = 40,
                Visible = true
            };
            settings.Widgets.Add(defaultWidget);
            SettingsService.Instance.SaveImmediate();
        }

        foreach (var widgetConfig in settings.Widgets.Take(MaxWidgets))
        {
            if (widgetConfig.Visible)
            {
                ShowWidget(widgetConfig);
            }
        }
    }

    public WidgetWindow? ShowWidget(WidgetConfig config)
    {
        if (_activeWindows.TryGetValue(config.Id, out var existing))
        {
            existing.Show();
            existing.ApplyPositionAndAttach();
            return existing;
        }

        try
        {
            var window = new WidgetWindow(config);
            _activeWindows[config.Id] = window;
            window.Show();
            return window;
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"Failed to show widget {config.Name} ({config.Id})", ex);
            return null;
        }
    }

    public void HideWidget(string id)
    {
        if (_activeWindows.TryGetValue(id, out var window))
        {
            window.Hide();
        }
    }

    public bool CreateWidget(string? name = null)
    {
        var settings = SettingsService.Instance.Current;
        if (settings.Widgets.Count >= MaxWidgets)
        {
            AppLogger.Instance.Warn($"Cannot create more than {MaxWidgets} widgets.");
            return false;
        }

        int nextIndex = settings.Widgets.Count + 1;
        var config = new WidgetConfig
        {
            Id = Guid.NewGuid().ToString("D"),
            Name = name ?? $"图片组件 {nextIndex}",
            WidthDip = 480,
            HeightDip = 270,
            IntervalSeconds = 60,
            LeftDip = 40 + (nextIndex - 1) * 60,
            TopDip = 40 + (nextIndex - 1) * 60,
            Visible = true
        };

        settings.Widgets.Add(config);
        SettingsService.Instance.SaveImmediate();

        ShowWidget(config);
        return true;
    }

    public void DeleteWidget(string id)
    {
        var settings = SettingsService.Instance.Current;
        var config = settings.Widgets.FirstOrDefault(w => w.Id == id);
        if (config != null)
        {
            settings.Widgets.Remove(config);
            SettingsService.Instance.SaveImmediate();
        }

        if (_activeWindows.TryGetValue(id, out var window))
        {
            window.Close();
            _activeWindows.Remove(id);
        }

        StartupPreviewCache.Instance.DeletePreview(id);
    }

    public void ToggleVisibility(string id)
    {
        var settings = SettingsService.Instance.Current;
        var config = settings.Widgets.FirstOrDefault(w => w.Id == id);
        if (config == null) return;

        config.Visible = !config.Visible;
        SettingsService.Instance.SaveImmediate();

        if (config.Visible)
        {
            ShowWidget(config);
        }
        else
        {
            if (_activeWindows.TryGetValue(id, out var window))
            {
                window.Close();
                _activeWindows.Remove(id);
            }
        }
    }

    public void TogglePause(string id)
    {
        var settings = SettingsService.Instance.Current;
        var config = settings.Widgets.FirstOrDefault(w => w.Id == id);
        if (config == null) return;

        config.Paused = !config.Paused;
        SettingsService.Instance.SaveImmediate();

        if (_activeWindows.TryGetValue(id, out var window))
        {
            window.UpdateUiState();
        }
    }

    public void HandleDisplayTopologyChanged()
    {
        AppLogger.Instance.Info("WidgetController: Handling display topology change. Verifying all widget positions.");
        foreach (var window in _activeWindows.Values)
        {
            try
            {
                window.ApplyPositionAndAttach();
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Warn($"WidgetController: Error repositioning window: {ex.Message}");
            }
        }
        SettingsService.Instance.ScheduleSave(250);
    }

    public void SetSystemPowerPaused(bool paused)
    {
        AppLogger.Instance.Info($"WidgetController: System power/session state changed. SystemPowerPaused={paused}");
        foreach (var window in _activeWindows.Values)
        {
            try
            {
                if (paused)
                {
                    window.Scheduler?.SetPaused(true);
                }
                else if (!window.Config.Paused)
                {
                    window.Scheduler?.SetPaused(false);
                }
            }
            catch { }
        }
    }

    public void CloseAll()
    {
        foreach (var window in _activeWindows.Values.ToList())
        {
            try
            {
                window.Close();
            }
            catch { }
        }
        _activeWindows.Clear();
    }
}
