using System;
using System.IO;
using DesktopPicture.Playback;
using DesktopPicture.Storage;
using DesktopPicture.Watcher;
using Xunit;

namespace DesktopPicture.Tests;

public class PlaybackHistoryAndModernFeaturesTests
{
    [Fact]
    public void PlaybackHistory_ShouldNavigateBackAndForwardCorrectly()
    {
        var history = new PlaybackHistory(maxCapacity: 5);

        Assert.False(history.CanGoBack);
        Assert.False(history.CanGoForward);

        history.Push("img1.jpg");
        history.Push("img2.jpg");
        history.Push("img3.jpg");

        Assert.Equal(3, history.Count);
        Assert.Equal("img3.jpg", history.Current);
        Assert.True(history.CanGoBack);
        Assert.False(history.CanGoForward);

        // Go back
        var prev1 = history.GoBack();
        Assert.Equal("img2.jpg", prev1);
        Assert.True(history.CanGoBack);
        Assert.True(history.CanGoForward);

        var prev2 = history.GoBack();
        Assert.Equal("img1.jpg", prev2);
        Assert.False(history.CanGoBack);
        Assert.True(history.CanGoForward);

        // Go forward
        var next1 = history.GoForward();
        Assert.Equal("img2.jpg", next1);
        Assert.True(history.CanGoBack);
        Assert.True(history.CanGoForward);
        Assert.Equal("img3.jpg", history.PeekForward());
        Assert.Equal("img1.jpg", history.PeekBack());

        // Push new branch
        history.Push("img4.jpg");
        Assert.Equal(3, history.Count); // img1, img2, img4
        Assert.Equal("img4.jpg", history.Current);
        Assert.False(history.CanGoForward);
    }

    [Fact]
    public void PlaybackHistory_ShouldEnforceCapacityLimits()
    {
        var history = new PlaybackHistory(maxCapacity: 10);
        for (int i = 1; i <= 15; i++)
        {
            history.Push($"photo_{i}.jpg");
        }

        Assert.Equal(10, history.Count);
        Assert.Equal("photo_15.jpg", history.Current);

        // Step back to the oldest preserved element (photo_6)
        string? oldest = null;
        while (history.CanGoBack)
        {
            oldest = history.GoBack();
        }
        Assert.Equal("photo_6.jpg", oldest);
    }

    [Fact]
    public void FastDirectoryEnumerator_ShouldRecognizeModernImageFormats()
    {
        Assert.Equal(ImageExtensionType.Jpg, FastDirectoryEnumerator.GetExtensionType("pic.jpg"));
        Assert.Equal(ImageExtensionType.Jpeg, FastDirectoryEnumerator.GetExtensionType("photo.JPEG"));
        Assert.Equal(ImageExtensionType.Png, FastDirectoryEnumerator.GetExtensionType("art.PNG"));
        Assert.Equal(ImageExtensionType.Webp, FastDirectoryEnumerator.GetExtensionType("vector.webp"));
        Assert.Equal(ImageExtensionType.Avif, FastDirectoryEnumerator.GetExtensionType("modern.avif"));
        Assert.Equal(ImageExtensionType.Heic, FastDirectoryEnumerator.GetExtensionType("iphone.HEIC"));
        Assert.Equal(ImageExtensionType.Heif, FastDirectoryEnumerator.GetExtensionType("live.heif"));
        Assert.Equal(ImageExtensionType.Bmp, FastDirectoryEnumerator.GetExtensionType("bitmap.bmp"));
        Assert.Equal(ImageExtensionType.Tiff, FastDirectoryEnumerator.GetExtensionType("scan.TIFF"));
        Assert.Equal(ImageExtensionType.Tiff, FastDirectoryEnumerator.GetExtensionType("archive.tif"));
        Assert.Equal(ImageExtensionType.Gif, FastDirectoryEnumerator.GetExtensionType("animation.gif"));
        Assert.Equal(ImageExtensionType.Jfif, FastDirectoryEnumerator.GetExtensionType("legacy.jfif"));
        Assert.Equal(ImageExtensionType.Unknown, FastDirectoryEnumerator.GetExtensionType("document.pdf"));
    }

    [Fact]
    public void SqliteCatalogDatabase_OptimizeDatabase_ShouldExecuteWithoutError()
    {
        string tempDb = Path.Combine(Path.GetTempPath(), $"opt_test_{Guid.NewGuid():N}.db");
        try
        {
            using (var db = new SqliteCatalogDatabase(tempDb))
            {
                var root = db.GetOrCreateRoot("C:\\MockPath");
                db.OptimizeDatabase();
            }
        }
        finally
        {
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }

    [Fact]
    public void GenerateTransparentIcon_ShouldProduceValidMultiResolutionIco()
    {
        string srcPath = @"C:\Users\chen\.gemini\antigravity\brain\5bacc052-84ea-4834-b70b-a4668fb279ab\app_icon_design_1786885266600.jpg";
        if (!File.Exists(srcPath)) return;

        string resDir = @"d:\Antigravity\desktop_picture\src\DesktopPicture.App\Resources";
        Directory.CreateDirectory(resDir);

        using var srcBitmap = SkiaSharp.SKBitmap.Decode(srcPath);
        Assert.NotNull(srcBitmap);

        // Find boundary
        int left = 0, right = srcBitmap.Width - 1, top = 0, bottom = srcBitmap.Height - 1;
        for (int x = 0; x < 512; x++)
        {
            var c = srcBitmap.GetPixel(x, 512);
            if (c.Blue > 80 || c.Red > 50 || c.Green > 60) { left = x; break; }
        }
        for (int x = srcBitmap.Width - 1; x > 512; x--)
        {
            var c = srcBitmap.GetPixel(x, 512);
            if (c.Blue > 80 || c.Red > 50 || c.Green > 60) { right = x; break; }
        }
        for (int y = 0; y < 512; y++)
        {
            var c = srcBitmap.GetPixel(512, y);
            if (c.Blue > 80 || c.Red > 50 || c.Green > 60) { top = y; break; }
        }
        for (int y = srcBitmap.Height - 1; y > 512; y--)
        {
            var c = srcBitmap.GetPixel(512, y);
            if (c.Blue > 80 || c.Red > 50 || c.Green > 60) { bottom = y; break; }
        }

        // Generate 512x512 transparent canvas
        using var transparentBitmap = new SkiaSharp.SKBitmap(512, 512, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        using (var canvas = new SkiaSharp.SKCanvas(transparentBitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.Transparent);

            float margin = 16f;
            var dstRect = new SkiaSharp.SKRect(margin, margin, 512f - margin, 512f - margin);
            float cornerRadius = dstRect.Width * 0.225f; // iOS/Fluent squircle radius
            using var clipPath = new SkiaSharp.SKPath();
            clipPath.AddRoundRect(dstRect, cornerRadius, cornerRadius);

            canvas.ClipPath(clipPath, SkiaSharp.SKClipOperation.Intersect, antialias: true);

            var srcRect = new SkiaSharp.SKRect(left, top, right + 1, bottom + 1);
            using var paint = new SkiaSharp.SKPaint { IsAntialias = true, FilterQuality = SkiaSharp.SKFilterQuality.High };
            canvas.DrawBitmap(srcBitmap, srcRect, dstRect, paint);
        }

        // Save 512x512 PNG with 100% Alpha Transparency
        string pngPath = Path.Combine(resDir, "app.png");
        using (var fs = File.Create(pngPath))
        {
            transparentBitmap.Encode(fs, SkiaSharp.SKEncodedImageFormat.Png, 100);
        }

        // Encode multi-size Windows .ico with PNG frames (256, 128, 64, 48, 32, 16)
        int[] sizes = new[] { 256, 128, 64, 48, 32, 16 };
        byte[][] pngFrames = new byte[sizes.Length][];

        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            using var resized = transparentBitmap.Resize(new SkiaSharp.SKImageInfo(s, s, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul), SkiaSharp.SKSamplingOptions.Default);
            using var data = resized.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            pngFrames[i] = data.ToArray();
        }

        string icoPath = Path.Combine(resDir, "app.ico");
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write((ushort)0);
            bw.Write((ushort)1);
            bw.Write((ushort)sizes.Length);

            int offset = 6 + (16 * sizes.Length);

            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0);
                bw.Write((byte)0);
                bw.Write((ushort)1);
                bw.Write((ushort)32);
                bw.Write((uint)pngFrames[i].Length);
                bw.Write((uint)offset);

                offset += pngFrames[i].Length;
            }

            for (int i = 0; i < sizes.Length; i++)
            {
                bw.Write(pngFrames[i]);
            }

            File.WriteAllBytes(icoPath, ms.ToArray());
        }

        Assert.True(File.Exists(pngPath));
        Assert.True(File.Exists(icoPath));
        Assert.True(new FileInfo(icoPath).Length > 1000);
    }
}
