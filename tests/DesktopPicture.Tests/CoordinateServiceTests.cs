using DesktopPicture.Display;
using DesktopPicture.Interop;
using Xunit;

namespace DesktopPicture.Tests;

public class CoordinateServiceTests
{
    [Fact]
    public void Test_EnsureVisibleOnScreen_Clamps_If_Outside()
    {
        var service = DisplayCoordinateService.Instance;

        // An absurd off-screen coordinate like (-999999, -999999)
        var (safeLeft, safeTop) = service.EnsureVisibleOnScreen(-999999, -999999, 480, 270);

        // Should return a valid coordinate within screen bounds
        Assert.True(safeLeft > -10000);
        Assert.True(safeTop > -10000);
    }

    [Fact]
    public void Test_VirtualDesktopBounds_Valid()
    {
        var service = DisplayCoordinateService.Instance;
        var bounds = service.GetVirtualDesktopBounds();

        Assert.True(bounds.Width > 0);
        Assert.True(bounds.Height > 0);
    }

    [Fact]
    public void Test_MonitorInfo_Calculations()
    {
        var info = new MonitorInfo(
            IntPtr.Zero,
            "\\\\.\\DISPLAY1",
            new NativeMethods.RECT(0, 0, 1920, 1080),
            new NativeMethods.RECT(0, 0, 1920, 1040),
            true,
            144, // 150% scaling
            144
        );

        Assert.Equal(1.5, info.ScaleX);
        Assert.Equal(1.5, info.ScaleY);
        Assert.Equal(1920, info.MonitorRect.Width);
        Assert.Equal(1080, info.MonitorRect.Height);
    }
}
