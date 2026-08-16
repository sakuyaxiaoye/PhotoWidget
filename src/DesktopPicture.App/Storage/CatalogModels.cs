using System;

namespace DesktopPicture.Storage;

public enum RootHealthState
{
    Healthy = 1,
    Untrusted = 2,
    Unavailable = 3
}

public enum ImageState
{
    Healthy = 1,
    TemporarilyFailed = 2,
    Deleted = 3
}

public enum ImageExtensionType
{
    Unknown = 0,
    Jpg = 1,
    Jpeg = 2,
    Png = 3,
    Webp = 4,
    Gif = 5,
    Avif = 6,
    Heic = 7,
    Heif = 8,
    Bmp = 9,
    Tiff = 10,
    Tif = 11,
    Jfif = 12
}

public sealed record RootRecord(
    long Id,
    string CanonicalPath,
    long ScanVersion,
    DateTime? LastFullScanUtc,
    RootHealthState Health);

public sealed record ImageRecord(
    long Id,
    long RootId,
    string RelativePath,
    ImageExtensionType Extension,
    long Length,
    long LastWriteUtcTicks,
    ImageState State,
    long? RetryAfterUtcTicks,
    long SeenScanVersion);
