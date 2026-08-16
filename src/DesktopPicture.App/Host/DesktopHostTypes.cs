namespace DesktopPicture.Host;

public enum DesktopHostHealth
{
    Healthy,
    Degraded,
    Unavailable
}

public sealed record AttachResult(bool Success, string HostTypeName, string? ErrorMessage = null)
{
    public static AttachResult Succeeded(string hostTypeName) => new(true, hostTypeName);
    public static AttachResult Failed(string hostTypeName, string errorMessage) => new(false, hostTypeName, errorMessage);
}
