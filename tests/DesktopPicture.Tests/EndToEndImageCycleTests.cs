using System;
using System.IO;
using DesktopPicture.Scanning;
using SkiaSharp;
using Xunit;

namespace DesktopPicture.Tests;

public class EndToEndImageCycleTests : IDisposable
{
    private readonly string _testImagesDir;

    public EndToEndImageCycleTests()
    {
        _testImagesDir = Path.Combine(Path.GetTempPath(), $"DesktopPic_E2E_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testImagesDir);

        // Generate 3 test images with distinct colors
        GenerateTestImage(Path.Combine(_testImagesDir, "Red.png"), SKColors.Crimson, "RED");
        GenerateTestImage(Path.Combine(_testImagesDir, "Green.jpg"), SKColors.SeaGreen, "GREEN");
        GenerateTestImage(Path.Combine(_testImagesDir, "Blue.webp"), SKColors.RoyalBlue, "BLUE");
    }

    private static void GenerateTestImage(string filePath, SKColor color, string label)
    {
        using var bitmap = new SKBitmap(800, 600, SKColorType.Bgra8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(color);
        }

        var format = filePath.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? SKEncodedImageFormat.Jpeg :
                     filePath.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? SKEncodedImageFormat.Webp :
                     SKEncodedImageFormat.Png;

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        using var fs = File.OpenWrite(filePath);
        data.SaveTo(fs);
    }

    [Fact]
    public async Task Test_DirectoryScanner_And_Decoder_Integration()
    {
        var scanner = new DirectoryScanner();
        var files = await scanner.ScanAsync(_testImagesDir);
        Assert.Equal(3, files.Count);

        foreach (var file in files)
        {
            var bitmap = DesktopPicture.Decoding.ImageDecoder.DecodeAndCropToCover(file, 480, 270);
            Assert.NotNull(bitmap);
            Assert.Equal(480, bitmap.PixelWidth);
            Assert.Equal(270, bitmap.PixelHeight);
            Assert.True(bitmap.IsFrozen);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_testImagesDir, recursive: true); } catch { }
    }
}
