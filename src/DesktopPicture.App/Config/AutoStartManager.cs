using System;
using System.Diagnostics;
using System.IO;
using DesktopPicture.Logging;
using Microsoft.Win32;

namespace DesktopPicture.Config;

public static class AutoStartManager
{
    private const string RunRegistryKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryValueName = "PhotoWidget";

    public static bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: false);
            if (key == null) return false;
            var value = key.GetValue(AppRegistryValueName) as string;
            return !string.IsNullOrEmpty(value);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn($"AutoStartManager: Failed to check autostart registry state: {ex.Message}");
            return false;
        }
    }

    public static bool SetAutoStart(bool enable)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true);
            if (key == null)
            {
                AppLogger.Instance.Error("AutoStartManager: Unable to open Run registry key for writing.");
                return false;
            }

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    key.SetValue(AppRegistryValueName, $"\"{exePath}\"");
                    AppLogger.Instance.Info($"AutoStartManager: Enabled Windows autostart pointing to: {exePath}");
                }
                else
                {
                    AppLogger.Instance.Warn("AutoStartManager: Failed to resolve current process executable path.");
                    return false;
                }
            }
            else
            {
                key.DeleteValue(AppRegistryValueName, throwOnMissingValue: false);
                AppLogger.Instance.Info("AutoStartManager: Disabled Windows autostart.");
            }

            var settings = SettingsService.Instance.Current;
            settings.StartWithWindows = enable;
            SettingsService.Instance.SaveImmediate();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("AutoStartManager: Error updating autostart registry setting", ex);
            return false;
        }
    }

    public static void SyncOnStartup()
    {
        try
        {
            var settings = SettingsService.Instance.Current;
            bool registryEnabled = IsAutoStartEnabled();

            // If user configured autostart or registry has it, ensure registry has the current exact path
            if (settings.StartWithWindows || registryEnabled)
            {
                SetAutoStart(true);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn($"AutoStartManager: Error syncing autostart on startup: {ex.Message}");
        }
    }
}
