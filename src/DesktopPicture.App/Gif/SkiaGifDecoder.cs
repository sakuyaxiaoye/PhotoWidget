using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPicture.Decoding;
using DesktopPicture.Logging;
using SkiaSharp;

namespace DesktopPicture.Gif;

public sealed class SkiaGifDecoder : IDisposable
{
    private readonly string _filePath;
    private FileStream? _fileStream;
    private SKCodec? _codec;
    private SKCodecFrameInfo[] _frameInfos = Array.Empty<SKCodecFrameInfo>();
    private readonly object _lock = new();

    public int FrameCount => _frameInfos.Length;
    public bool IsAnimated => FrameCount > 1;
    public int RepetitionCount { get; private set; } = 0; // 0 = infinite loop

    public SkiaGifDecoder(string filePath)
    {
        _filePath = filePath;
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            if (!File.Exists(_filePath)) return;

            var fileInfo = new FileInfo(_filePath);
            if (fileInfo.Length > ImageDecoder.MaxFileSize) return;

            _fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _codec = SKCodec.Create(_fileStream);

            if (_codec != null)
            {
                _frameInfos = _codec.FrameInfo;
                RepetitionCount = _codec.RepetitionCount;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Warn($"SkiaGifDecoder: Failed to initialize for '{_filePath}': {ex.Message}");
            Dispose();
        }
    }

    public int GetFrameDurationMs(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= _frameInfos.Length) return 100;

        int duration = _frameInfos[frameIndex].Duration;
        // Standard Web standard: 0ms or <=10ms durations default to 100ms
        return duration <= 10 ? 100 : duration;
    }

    public BitmapSource? RenderFrame(int frameIndex, int targetWidthPx, int targetHeightPx)
    {
        lock (_lock)
        {
            if (_codec == null || frameIndex < 0 || frameIndex >= FrameCount)
            {
                return null;
            }

            try
            {
                int safeTargetWidth = Math.Max(16, targetWidthPx);
                int safeTargetHeight = Math.Max(16, targetHeightPx);

                long requestedPixels = (long)safeTargetWidth * safeTargetHeight;
                if (requestedPixels > ImageDecoder.MaxOutputPixels)
                {
                    double downscale = Math.Sqrt((double)ImageDecoder.MaxOutputPixels / requestedPixels);
                    safeTargetWidth = (int)(safeTargetWidth * downscale);
                    safeTargetHeight = (int)(safeTargetHeight * downscale);
                }

                var info = _codec.Info;
                int srcWidth = info.Width;
                int srcHeight = info.Height;

                if ((long)srcWidth * srcHeight > ImageDecoder.MaxSourcePixels)
                {
                    return null;
                }

                // Decode raw GIF frame into source bitmap
                var srcInfo = new SKImageInfo(srcWidth, srcHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var srcBitmap = new SKBitmap(srcInfo);

                var options = new SKCodecOptions(frameIndex);
                var result = _codec.GetPixels(srcInfo, srcBitmap.GetPixels(), options);
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                {
                    return null;
                }

                // Calculate Cover aspect ratio crop
                double scaleX = (double)safeTargetWidth / srcWidth;
                double scaleY = (double)safeTargetHeight / srcHeight;
                double scale = Math.Max(scaleX, scaleY);

                float cropWidth = (float)(safeTargetWidth / scale);
                float cropHeight = (float)(safeTargetHeight / scale);
                float cropX = (srcWidth - cropWidth) / 2.0f;
                float cropY = (srcHeight - cropHeight) / 2.0f;

                var srcRect = new SKRect(cropX, cropY, cropX + cropWidth, cropY + cropHeight);
                var dstRect = new SKRect(0, 0, safeTargetWidth, safeTargetHeight);

                // Render cropped frame into target Bgra8888 bitmap
                var targetInfo = new SKImageInfo(safeTargetWidth, safeTargetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var targetBitmap = new SKBitmap(targetInfo);
                using (var canvas = new SKCanvas(targetBitmap))
                {
                    canvas.Clear(SKColors.Transparent);
                    using var paint = new SKPaint { IsAntialias = true };
                    using var image = SKImage.FromBitmap(srcBitmap);
                    var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    canvas.DrawImage(image, srcRect, dstRect, sampling, paint);
                    canvas.Flush();
                }

                // Copy to frozen WPF BitmapSource
                int stride = safeTargetWidth * 4;
                int totalBytes = stride * safeTargetHeight;
                var pixelBytes = new byte[totalBytes];

                var pixelsPtr = targetBitmap.GetPixels();
                System.Runtime.InteropServices.Marshal.Copy(pixelsPtr, pixelBytes, 0, totalBytes);

                var bitmapSource = BitmapSource.Create(
                    safeTargetWidth,
                    safeTargetHeight,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    pixelBytes,
                    stride);

                bitmapSource.Freeze();
                return bitmapSource;
            }
            catch (Exception ex)
            {
                AppLogger.Instance.Warn($"SkiaGifDecoder: Error rendering frame {frameIndex} of '{_filePath}': {ex.Message}");
                return null;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _codec?.Dispose();
            _codec = null;
            _fileStream?.Dispose();
            _fileStream = null;
        }
    }
}
