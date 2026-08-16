using System;
using System.Threading;
using System.Windows.Media.Imaging;

namespace DesktopPicture.Decoding;

public sealed record DecodeRequest(
    string WidgetId,
    long Generation,
    string FilePath,
    int TargetWidthPx,
    int TargetHeightPx,
    CancellationToken CancellationToken,
    bool IsHistoryNavigation = false);

public sealed record DecodeResult(
    string WidgetId,
    long Generation,
    string FilePath,
    BitmapSource? Bitmap,
    bool Success,
    string? ErrorMessage = null,
    bool IsHistoryNavigation = false);
