using System;
using System.IO;
using System.Linq;
using System.Text;

namespace DesktopPicture.Logging;

public sealed class AppLogger
{
    private static readonly Lazy<AppLogger> _instance = new(() => new AppLogger());
    public static AppLogger Instance => _instance.Value;

    private readonly object _lock = new();
    private readonly string _logDir;
    private readonly string _currentLogFile;
    private const long MaxFileSize = 2 * 1024 * 1024; // 2 MB
    private const int MaxLogFiles = 5;

    public AppLogger()
    {
        _logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoWidget",
            "logs");

        Directory.CreateDirectory(_logDir);
        _currentLogFile = Path.Combine(_logDir, "app.log");
    }

    public void Info(string message) => Log("INFO", message);
    public void Warn(string message) => Log("WARN", message);
    public void Error(string message, Exception? ex = null)
    {
        var msg = ex != null ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}" : message;
        Log("ERROR", msg);
    }

    private void Log(string level, string message)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLine = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";

        lock (_lock)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(_currentLogFile, logLine, Encoding.UTF8);
            }
            catch
            {
                // Silently avoid crashing on log write failures
            }
        }
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_currentLogFile)) return;

        var fileInfo = new FileInfo(_currentLogFile);
        if (fileInfo.Length < MaxFileSize) return;

        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var archivePath = Path.Combine(_logDir, $"app_{timestamp}.log");
            File.Move(_currentLogFile, archivePath, overwrite: true);

            var oldFiles = Directory.GetFiles(_logDir, "app_*.log")
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.CreationTimeUtc)
                .Skip(MaxLogFiles - 1)
                .ToList();

            foreach (var old in oldFiles)
            {
                try { old.Delete(); } catch { }
            }
        }
        catch
        {
            // Ignore rotation errors
        }
    }
}
