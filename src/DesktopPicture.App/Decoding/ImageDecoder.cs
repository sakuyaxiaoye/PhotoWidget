using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPicture.Logging;
using SkiaSharp;

namespace DesktopPicture.Decoding;

public sealed class ImageDecoder
{
    public const long MaxFileSize = 1024L * 1024L * 1024L; // 1 GiB
    public const long MaxSourcePixels = 200_000_000L; // 200 MP
    public const int MaxOutputPixels = 16_777_216; // Support up to 4K UHD+ (4096x4096)

    public static BitmapSource? DecodeAndCropToCover(
        string filePath,
        int targetWidthPx,
        int targetHeightPx)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            AppLogger.Instance.Warn($"ImageDecoder: File does not exist: '{filePath}'");
            return null;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                AppLogger.Instance.Warn($"ImageDecoder: File '{filePath}' is empty (0 bytes). Skipping.");
                return null;
            }

            if (fileInfo.Length > MaxFileSize)
            {
                AppLogger.Instance.Warn($"ImageDecoder: File '{filePath}' exceeds size limit ({fileInfo.Length} > {MaxFileSize}). Skipping.");
                return null;
            }

            // Adjust target size if it exceeds pixel budget
            int safeTargetWidth = Math.Max(16, targetWidthPx);
            int safeTargetHeight = Math.Max(16, targetHeightPx);
            long requestedPixels = (long)safeTargetWidth * safeTargetHeight;
            if (requestedPixels > MaxOutputPixels)
            {
                double downscale = Math.Sqrt((double)MaxOutputPixels / requestedPixels);
                safeTargetWidth = (int)(safeTargetWidth * downscale);
                safeTargetHeight = (int)(safeTargetHeight * downscale);
            }

            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var codec = SKCodec.Create(fileStream);
            if (codec == null)
            {
                AppLogger.Instance.Warn($"ImageDecoder: Could not create Skia codec for '{filePath}'.");
                return null;
            }

            var origin = codec.EncodedOrigin;
            var info = codec.Info;
            long srcPixels = (long)info.Width * info.Height;
            if (srcPixels > MaxSourcePixels)
            {
                AppLogger.Instance.Warn($"ImageDecoder: Image '{filePath}' exceeds pixel limit ({srcPixels} > {MaxSourcePixels}). Skipping.");
                return null;
            }

            // Decode source image (frame 0 for GIF/WebP)
            var srcBitmap = SKBitmap.Decode(codec);
            if (srcBitmap == null)
            {
                AppLogger.Instance.Warn($"ImageDecoder: Failed to decode pixels for '{filePath}'.");
                return null;
            }

            // Apply EXIF orientation rotation if necessary
            srcBitmap = ApplyExifOrientation(srcBitmap, origin);

            using (srcBitmap)
            {
                // Calculate Cover aspect ratio crop
                int srcWidth = srcBitmap.Width;
                int srcHeight = srcBitmap.Height;

                double scaleX = (double)safeTargetWidth / srcWidth;
                double scaleY = (double)safeTargetHeight / srcHeight;
                double scale = Math.Max(scaleX, scaleY);

                float cropWidth = (float)(safeTargetWidth / scale);
                float cropHeight = (float)(safeTargetHeight / scale);
                float cropX = (srcWidth - cropWidth) / 2.0f;
                float cropY = (srcHeight - cropHeight) / 2.0f;

                var srcRect = new SKRect(cropX, cropY, cropX + cropWidth, cropY + cropHeight);
                var dstRect = new SKRect(0, 0, safeTargetWidth, safeTargetHeight);

                // Render cropped image into target Bgra8888 premultiplied bitmap
                var targetInfo = new SKImageInfo(safeTargetWidth, safeTargetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var targetBitmap = new SKBitmap(targetInfo);
                using (var canvas = new SKCanvas(targetBitmap))
                {
                    canvas.Clear(SKColors.Transparent);
                    using var paint = new SKPaint
                    {
                        IsAntialias = true
                    };
                    using var image = SKImage.FromBitmap(srcBitmap);
                    var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
                    canvas.DrawImage(image, srcRect, dstRect, sampling, paint);
                    canvas.Flush();
                }

                // Copy pixels to WPF BitmapSource
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

                bitmapSource.Freeze(); // Freeze allows cross-thread usage and optimal performance in WPF
                return bitmapSource;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Instance.Error($"ImageDecoder: Error decoding '{filePath}'", ex);
            return null;
        }
    }

    private static SKBitmap ApplyExifOrientation(SKBitmap bitmap, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft) return bitmap;

        try
        {
            SKBitmap rotated;
            switch (origin)
            {
                case SKEncodedOrigin.BottomRight: // 180 degrees
                    rotated = new SKBitmap(bitmap.Width, bitmap.Height);
                    using (var canvas = new SKCanvas(rotated))
                    {
                        canvas.RotateDegrees(180, bitmap.Width / 2f, bitmap.Height / 2f);
                        canvas.DrawBitmap(bitmap, 0, 0);
                    }
                    bitmap.Dispose();
                    return rotated;

                case SKEncodedOrigin.RightTop: // 90 CW
                    rotated = new SKBitmap(bitmap.Height, bitmap.Width);
                    using (var canvas = new SKCanvas(rotated))
                    {
                        canvas.Translate(bitmap.Height, 0);
                        canvas.RotateDegrees(90);
                        canvas.DrawBitmap(bitmap, 0, 0);
                    }
                    bitmap.Dispose();
                    return rotated;

                case SKEncodedOrigin.LeftBottom: // 270 CW
                    rotated = new SKBitmap(bitmap.Height, bitmap.Width);
                    using (var canvas = new SKCanvas(rotated))
                    {
                        canvas.Translate(0, bitmap.Width);
                        canvas.RotateDegrees(270);
                        canvas.DrawBitmap(bitmap, 0, 0);
                    }
                    bitmap.Dispose();
                    return rotated;

                default:
                    return bitmap;
            }
        }
        catch
        {
            return bitmap;
        }
    }
}
