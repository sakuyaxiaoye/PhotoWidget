using System;
using System.Diagnostics;
using System.IO;
using DesktopPicture.Decoding;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace DesktopPicture.Tests;

public class MemoryBudgetSoakTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly string _testDir;
    private readonly string[] _testImages;

    public MemoryBudgetSoakTests(ITestOutputHelper output)
    {
        _output = output;
        _testDir = Path.Combine(Path.GetTempPath(), $"SoakTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        _testImages = new string[10];
        for (int i = 0; i < 10; i++)
        {
            _testImages[i] = Path.Combine(_testDir, $"soak_{i}.jpg");
            using var bitmap = new SKBitmap(1920, 1080, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.DarkSlateGray);
                using var paint = new SKPaint { Color = SKColors.Orange };
                canvas.DrawCircle(960, 540, 200, paint);
            }
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
            using var stream = File.OpenWrite(_testImages[i]);
            data.SaveTo(stream);
        }
    }

    [Fact]
    public void Test_1000_Decodes_MemoryBudget_Under_300MB()
    {
        var proc = Process.GetCurrentProcess();
        proc.Refresh();
        long initialMemory = proc.PrivateMemorySize64;

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < 1000; i++)
        {
            string img = _testImages[i % _testImages.Length];
            // Decode to 480x270
            var bmp = ImageDecoder.DecodeAndCropToCover(img, 480, 270);
            Assert.NotNull(bmp);
        }

        sw.Stop();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        proc.Refresh();
        long finalPrivateBytes = proc.PrivateMemorySize64;
        long maxAllowedBytes = 300L * 1024L * 1024L; // 300 MiB per SPEC 9.2

        _output.WriteLine($"1,000 Decodes in {sw.ElapsedMilliseconds} ms ({1000.0 * 1000 / sw.ElapsedMilliseconds:F0} fps)");
        _output.WriteLine($"Private Memory: Initial={initialMemory / (1024 * 1024)} MB, Final={finalPrivateBytes / (1024 * 1024)} MB (Limit={maxAllowedBytes / (1024 * 1024)} MB)");

        Assert.True(finalPrivateBytes <= maxAllowedBytes, $"Private memory {finalPrivateBytes / (1024 * 1024)} MB exceeded 300 MB budget");
    }

    public void Dispose()
    {
        try { Directory.Delete(_testDir, recursive: true); } catch { }
    }
}
