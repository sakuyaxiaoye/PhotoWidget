using System;
using System.IO;
using System.Windows.Media.Imaging;
using DesktopPicture.Logging;

namespace DesktopPicture.Decoding;

public sealed class StartupPreviewCache
{
    private static readonly Lazy<StartupPreviewCache> _instance = new(() => new StartupPreviewCache());
    public static StartupPreviewCache Instance => _instance.Value;

    private readonly string _cacheDir;

    public StartupPreviewCache(string? customDir = null)
    {
        _cacheDir = customDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopPicture",
            "cache",
            "startup");

        Directory.CreateDirectory(_cacheDir);
    }

    public string GetPreviewPath(string widgetId)
    {
        return Path.Combine(_cacheDir, $"{widgetId}.png");
    }

    public BitmapSource? LoadPreview(string widgetId)
    {
        var path = GetPreviewPath(widgetId);
        if (!File.Exists(path)) return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];
            frame.Freeze();
            return frame;
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn($"Failed to load startup preview for widget {widgetId}: {ex.Message}");
            return null;
        }
    }

    public void SavePreview(string widgetId, BitmapSource bitmap)
    {
        var path = GetPreviewPath(widgetId);
        try
        {
            var tempPath = Path.Combine(_cacheDir, $"{widgetId}_{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(stream);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn($"Failed to save startup preview for widget {widgetId}: {ex.Message}");
        }
    }

    public void DeletePreview(string widgetId)
    {
        var path = GetPreviewPath(widgetId);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch { }
    }
}
