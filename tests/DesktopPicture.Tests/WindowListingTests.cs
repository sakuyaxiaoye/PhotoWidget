using System;
using System.Text;
using DesktopPicture.Interop;
using Xunit;
using Xunit.Abstractions;

namespace DesktopPicture.Tests;

public class WindowListingTests
{
    private readonly ITestOutputHelper _output;

    public WindowListingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void List_All_TopLevel_Windows()
    {
        int count = 0;
        NativeMethods.EnumWindows((hWnd, lParam) =>
        {
            count++;
            var sbClass = new StringBuilder(256);
            NativeMethods.GetClassName(hWnd, sbClass, sbClass.Capacity);
            var sbTitle = new StringBuilder(256);
            NativeMethods.SendMessage(hWnd, 0x000D /* WM_GETTEXT */, (IntPtr)256, (IntPtr)0);
            if (count <= 25)
            {
                _output.WriteLine($"HWND: 0x{hWnd:X8}, Class: '{sbClass}'");
            }
            return true;
        }, IntPtr.Zero);

        _output.WriteLine($"Total windows enumerated: {count}");
    }
}
