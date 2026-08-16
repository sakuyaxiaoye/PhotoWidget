using System;
using System.Threading;
using System.Windows.Interop;
using DesktopPicture.Host;
using DesktopPicture.Interop;
using Xunit;
using Xunit.Abstractions;

namespace DesktopPicture.Tests;

public class DesktopHostDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public DesktopHostDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Diagnose_Explorer_Windows()
    {
        using var host = new ExplorerDesktopHost();
        _output.WriteLine($"ExplorerDesktopHost health: {host.Health}, Name: {host.Name}");
        Assert.Equal(DesktopHostHealth.Healthy, host.Health);

        var bounds = host.GetDesktopBounds();
        _output.WriteLine($"Desktop bounds: Left={bounds.Left}, Top={bounds.Top}, Width={bounds.Width}, Height={bounds.Height}");
        Assert.True(bounds.Width > 0);
        Assert.True(bounds.Height > 0);
    }

    [Fact]
    public void Test_Attach_And_Detach_Cycle()
    {
        using var host = new ExplorerDesktopHost();
        Assert.Equal(DesktopHostHealth.Healthy, host.Health);

        var hwnd = NativeMethods.CreateWindowEx(
            NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
            "STATIC",
            "TestWidgetWindow",
            NativeMethods.WS_POPUP,
            100, 100, 300, 200,
            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        Assert.NotEqual(IntPtr.Zero, hwnd);

        try
        {
            var attachResult = host.Attach(hwnd);
            _output.WriteLine($"Attach result: Success={attachResult.Success}, Host={attachResult.HostTypeName}");
            Assert.True(attachResult.Success);

            host.Detach(hwnd);
        }
        finally
        {
            NativeMethods.DestroyWindow(hwnd);
        }
    }
}
