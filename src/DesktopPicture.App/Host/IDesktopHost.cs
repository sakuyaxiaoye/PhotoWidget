using System;
using DesktopPicture.Interop;

namespace DesktopPicture.Host;

public interface IDesktopHost : IDisposable
{
    string Name { get; }
    DesktopHostHealth Health { get; }

    AttachResult Attach(IntPtr widgetHwnd);
    void Detach(IntPtr widgetHwnd);
    void ReattachAll(string reason);
    NativeMethods.RECT GetDesktopBounds();
}
