using System;
using System.IO;
using System.Windows.Media.Imaging;
using DesktopPicture.Decoding;
using SkiaSharp;
using Xunit;

namespace DesktopPicture.Tests;

public class ImageDecoderTests : IDisposable
{
    private readonly string _tempFile;

    public ImageDecoderTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"test_image_{Guid.NewGuid():N}.png");

        // Create a 400x300 gradient PNG image using SkiaSharp
        using var bitmap = new SKBitmap(400, 300, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Blue);
            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawCircle(200, 150, 80, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(_tempFile);
        data.SaveTo(stream);
    }

    [Fact]
    public void Test_DecodeAndCropToCover_ValidImage()
    {
        int targetW = 480;
        int targetH = 270;

        var bitmapSource = ImageDecoder.DecodeAndCropToCover(_tempFile, targetW, targetH);

        Assert.NotNull(bitmapSource);
        Assert.Equal(targetW, bitmapSource.PixelWidth);
        Assert.Equal(targetH, bitmapSource.PixelHeight);
        Assert.True(bitmapSource.IsFrozen);
    }

    [Fact]
    public void Test_Decode_NonExistentFile_ReturnsNull()
    {
        var result = ImageDecoder.DecodeAndCropToCover("C:\\non_existent_image_12345.jpg", 480, 270);
        Assert.Null(result);
    }

    [Fact]
    public void Test_StartupPreviewCache_SaveAndLoad()
    {
        var tempCacheDir = Path.Combine(Path.GetTempPath(), $"PreviewCache_{Guid.NewGuid():N}");
        var cache = new StartupPreviewCache(tempCacheDir);

        var bitmapSource = ImageDecoder.DecodeAndCropToCover(_tempFile, 200, 150);
        Assert.NotNull(bitmapSource);

        string widgetId = Guid.NewGuid().ToString("D");
        cache.SavePreview(widgetId, bitmapSource);

        var loaded = cache.LoadPreview(widgetId);
        Assert.NotNull(loaded);
        Assert.Equal(200, loaded.PixelWidth);
        Assert.Equal(150, loaded.PixelHeight);

        cache.DeletePreview(widgetId);
        Assert.Null(cache.LoadPreview(widgetId));

        try { Directory.Delete(tempCacheDir, recursive: true); } catch { }
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_tempFile))
            {
                File.Delete(_tempFile);
            }
        }
        catch { }
    }
}
