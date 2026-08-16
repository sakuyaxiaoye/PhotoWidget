using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DesktopPicture.Logging;

namespace DesktopPicture.Config;

public sealed class SettingsService : IDisposable
{
    private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
    public static SettingsService Instance => _instance.Value;

    private readonly string _settingsDir;
    private readonly string _settingsPath;
    private readonly string _backupPath;
    private readonly object _fileLock = new();
    private readonly JsonSerializerOptions _jsonOptions;

    private AppSettings _currentSettings;
    private Timer? _debounceTimer;
    private bool _hasPendingSave;

    public AppSettings Current => _currentSettings;
    public event Action<AppSettings>? SettingsChanged;

    public SettingsService(string? customDir = null)
    {
        _settingsDir = customDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoWidget");

        EnsureMigration();
        Directory.CreateDirectory(_settingsDir);
        _settingsPath = Path.Combine(_settingsDir, "settings.json");
        _backupPath = Path.Combine(_settingsDir, "settings.json.bak");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        _currentSettings = LoadInternal();
    }

    private static void EnsureMigration()
    {
        try
        {
            var oldDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPicture");
            var newDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoWidget");

            if (Directory.Exists(oldDir) && !Directory.Exists(newDir))
            {
                Directory.CreateDirectory(newDir);
                foreach (var file in Directory.GetFiles(oldDir))
                {
                    var dest = Path.Combine(newDir, Path.GetFileName(file));
                    if (!File.Exists(dest)) File.Copy(file, dest, overwrite: true);
                }
            }
        }
        catch { }
    }

    public AppSettings Load()
    {
        lock (_fileLock)
        {
            _currentSettings = LoadInternal();
            return _currentSettings;
        }
    }

    private AppSettings LoadInternal()
    {
        if (File.Exists(_settingsPath))
        {
            try
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (settings != null)
                {
                    return settings;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Warn($"Failed to load settings from {_settingsPath}: {ex.Message}. Trying backup.");
            }
        }

        if (File.Exists(_backupPath))
        {
            try
            {
                var json = File.ReadAllText(_backupPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (settings != null)
                {
                    AppLogger.Instance.Info("Successfully recovered settings from backup.");
                    return settings;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Error($"Failed to load settings from backup {_backupPath}", ex);
            }
        }

        if (File.Exists(_settingsPath))
        {
            try
            {
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var corruptPath = Path.Combine(_settingsDir, $"settings_corrupt_{timestamp}.json");
                File.Copy(_settingsPath, corruptPath, overwrite: true);
                AppLogger.Instance.Warn($"Preserved corrupt settings file to {corruptPath}");
            }
            catch { }
        }

        AppLogger.Instance.Info("Creating default application settings.");
        var defaultSettings = AppSettings.CreateDefault();
        SaveImmediateInternal(defaultSettings);
        return defaultSettings;
    }

    public void ScheduleSave(int delayMs = 250)
    {
        lock (_fileLock)
        {
            _hasPendingSave = true;
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ =>
            {
                SaveImmediate();
            }, null, delayMs, Timeout.Infinite);
        }
    }

    public void SaveImmediate()
    {
        lock (_fileLock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _hasPendingSave = false;
            SaveImmediateInternal(_currentSettings);
        }
        SettingsChanged?.Invoke(_currentSettings);
    }

    public void Update(Action<AppSettings> updateAction, bool immediate = false)
    {
        lock (_fileLock)
        {
            updateAction(_currentSettings);
        }

        if (immediate)
        {
            SaveImmediate();
        }
        else
        {
            ScheduleSave();
        }
    }

    private void SaveImmediateInternal(AppSettings settings)
    {
        var tempFile = Path.Combine(_settingsDir, $"settings_{Guid.NewGuid():N}.tmp");
        try
        {
            var json = JsonSerializer.Serialize(settings, _jsonOptions);

            using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(fs))
            {
                writer.Write(json);
                writer.Flush();
                fs.Flush(flushToDisk: true);
            }

            if (File.Exists(_settingsPath))
            {
                File.Copy(_settingsPath, _backupPath, overwrite: true);
            }

            File.Move(tempFile, _settingsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error("Failed to save settings atomically", ex);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    public void Dispose()
    {
        lock (_fileLock)
        {
            if (_hasPendingSave)
            {
                SaveImmediateInternal(_currentSettings);
                _hasPendingSave = false;
            }
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}
