using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPicture.Gif;
using Xunit;

namespace DesktopPicture.Tests;

public class GifDecoderAndPlayerTests : IDisposable
{
    private readonly string _testGifPath;

    public GifDecoderAndPlayerTests()
    {
        _testGifPath = Path.Combine(Path.GetTempPath(), $"test_anim_{Guid.NewGuid():N}.gif");
        CreateSampleAnimatedGif(_testGifPath);
    }

    private static void CreateSampleAnimatedGif(string filePath)
    {
        var encoder = new GifBitmapEncoder();

        for (int i = 0; i < 3; i++)
        {
            var color = i switch
            {
                0 => Colors.Red,
                1 => Colors.Green,
                _ => Colors.Blue
            };

            int width = 200;
            int height = 150;
            int stride = width * 4;
            var pixels = new byte[height * stride];

            for (int p = 0; p < pixels.Length; p += 4)
            {
                pixels[p] = color.B;
                pixels[p + 1] = color.G;
                pixels[p + 2] = color.R;
                pixels[p + 3] = 255;
            }

            var frameSource = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            encoder.Frames.Add(BitmapFrame.Create(frameSource));
        }

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
        encoder.Save(fs);
    }

    [Fact]
    public void Test_SkiaGifDecoder_FrameCount_And_Rendering()
    {
        using var decoder = new SkiaGifDecoder(_testGifPath);

        Assert.True(decoder.FrameCount >= 2, $"Expected at least 2 frames, got {decoder.FrameCount}");
        Assert.True(decoder.IsAnimated);

        int duration = decoder.GetFrameDurationMs(0);
        Assert.True(duration >= 10);

        var renderedFrame = decoder.RenderFrame(0, 480, 270);
        Assert.NotNull(renderedFrame);
        Assert.Equal(480, renderedFrame.PixelWidth);
        Assert.Equal(270, renderedFrame.PixelHeight);
        Assert.True(renderedFrame.IsFrozen);
    }

    [Fact]
    public async Task Test_GifAnimationPlayer_Playback_And_Stop()
    {
        var decoder = new SkiaGifDecoder(_testGifPath);
        int frameReceivedCount = 0;

        using var player = new GifAnimationPlayer(
            decoder,
            320,
            180,
            frame =>
            {
                Interlocked.Increment(ref frameReceivedCount);
                Assert.NotNull(frame);
                Assert.Equal(320, frame.PixelWidth);
                Assert.Equal(180, frame.PixelHeight);
            });

        player.Start();
        Assert.True(player.IsPlaying);

        // Wait ~200ms to receive at least 1-2 frames
        await Task.Delay(250);

        Assert.True(frameReceivedCount > 0, "No frames received during playback");

        player.Stop();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testGifPath)) File.Delete(_testGifPath);
        }
        catch { }
    }
}
