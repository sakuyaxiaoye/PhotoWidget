using System;
using System.Runtime.InteropServices;
using System.Text;
using DesktopPicture.Interop;
using Xunit;
using Xunit.Abstractions;

namespace DesktopPicture.Tests;

public class ShellWindowTests
{
    private readonly ITestOutputHelper _output;

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    public ShellWindowTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test_ShellWindow_And_InputDesktop()
    {
        var hShell = GetShellWindow();
        var hDesktop = GetDesktopWindow();
        _output.WriteLine($"GetShellWindow: 0x{hShell:X8}, GetDesktopWindow: 0x{hDesktop:X8}");

        var hInputDesktop = OpenInputDesktop(0, false, 0x01FF);
        _output.WriteLine($"OpenInputDesktop: 0x{hInputDesktop:X8}, LastError: {Marshal.GetLastWin32Error()}");

        if (hInputDesktop != IntPtr.Zero)
        {
            bool switched = SetThreadDesktop(hInputDesktop);
            _output.WriteLine($"SetThreadDesktop result: {switched}");

            var progman = NativeMethods.FindWindow("Progman", null);
            _output.WriteLine($"After switch Progman: 0x{progman:X8}");

            CloseDesktop(hInputDesktop);
        }
    }
}
