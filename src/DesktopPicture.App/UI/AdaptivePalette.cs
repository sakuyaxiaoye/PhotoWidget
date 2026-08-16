using System;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopPicture.UI;

public sealed record AdaptivePalette(
    Brush BackgroundBrush,
    Brush BorderBrush,
    Brush TitleForeground,
    Brush SubtitleForeground,
    Brush IconForeground,
    Brush IconHoverBackground,
    bool IsLightBackground)
{
    public static readonly AdaptivePalette Default = new(
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D90F172A")),
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#33FFFFFF")),
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFFFF")),
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8")),
        new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0")),
        new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
        false);

    public static AdaptivePalette FromBitmap(BitmapSource bitmap)
    {
        try
        {
            int width = bitmap.PixelWidth;
            int height = bitmap.PixelHeight;
            if (width <= 0 || height <= 0) return Default;

            // Sample the bottom 25% of the image where the info bar sits
            int sampleStartY = (int)(height * 0.72);
            int sampleHeight = Math.Max(1, height - sampleStartY);
            int sampleStepX = Math.Max(1, width / 40);
            int sampleStepY = Math.Max(1, sampleHeight / 20);

            var formatted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            int stride = width * 4;
            byte[] pixels = new byte[stride * sampleHeight];
            var rect = new System.Windows.Int32Rect(0, sampleStartY, width, sampleHeight);
            formatted.CopyPixels(rect, pixels, stride, 0);

            long totalR = 0, totalG = 0, totalB = 0;
            int count = 0;

            for (int y = 0; y < sampleHeight; y += sampleStepY)
            {
                int rowOffset = y * stride;
                for (int x = 0; x < width; x += sampleStepX)
                {
                    int offset = rowOffset + x * 4;
                    byte b = pixels[offset];
                    byte g = pixels[offset + 1];
                    byte r = pixels[offset + 2];
                    byte a = pixels[offset + 3];

                    if (a > 80)
                    {
                        totalR += r;
                        totalG += g;
                        totalB += b;
                        count++;
                    }
                }
            }

            if (count == 0) return Default;

            byte avgR = (byte)(totalR / count);
            byte avgG = (byte)(totalG / count);
            byte avgB = (byte)(totalB / count);

            // Perceived luminance formula (ITU-R BT.709)
            double luminance = (0.299 * avgR + 0.587 * avgG + 0.114 * avgB);
            bool isLight = luminance > 125.0;

            if (isLight)
            {
                // Soft warm/light translucent acrylic matching the image (like in screenshot 1 & 2)
                var bg = Color.FromArgb(220, avgR, avgG, avgB);
                var border = Color.FromArgb(40, (byte)Math.Max(0, avgR - 50), (byte)Math.Max(0, avgG - 50), (byte)Math.Max(0, avgB - 50));
                var title = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181E28"));
                var subtitle = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#475569"));
                var icon = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B"));
                var iconHover = new SolidColorBrush(Color.FromArgb(45, 0, 0, 0));

                return new AdaptivePalette(
                    new SolidColorBrush(bg),
                    new SolidColorBrush(border),
                    title,
                    subtitle,
                    icon,
                    iconHover,
                    true);
            }
            else
            {
                // Dark translucent acrylic matching the image tone
                byte darkR = (byte)Math.Clamp((int)avgR, 10, 45);
                byte darkG = (byte)Math.Clamp((int)avgG, 14, 50);
                byte darkB = (byte)Math.Clamp((int)avgB, 18, 60);
                var bg = Color.FromArgb(225, darkR, darkG, darkB);
                var border = Color.FromArgb(50, 255, 255, 255);
                var title = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8FAFC"));
                var subtitle = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#94A3B8"));
                var icon = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E2E8F0"));
                var iconHover = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));

                return new AdaptivePalette(
                    new SolidColorBrush(bg),
                    new SolidColorBrush(border),
                    title,
                    subtitle,
                    icon,
                    iconHover,
                    false);
            }
        }
        catch
        {
            return Default;
        }
    }
}
