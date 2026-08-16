using System;
using DesktopPicture.Interop;

namespace DesktopPicture;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        uint access = NativeMethods.DESKTOP_READOBJECTS | NativeMethods.DESKTOP_CREATEWINDOW |
                      NativeMethods.DESKTOP_ENUMERATE | NativeMethods.DESKTOP_WRITEOBJECTS |
                      NativeMethods.DESKTOP_SWITCHDESKTOP;

        var hInputDesktop = NativeMethods.OpenInputDesktop(0, false, access);
        if (hInputDesktop == IntPtr.Zero)
        {
            hInputDesktop = NativeMethods.OpenDesktop("Default", 0, false, access);
        }

        if (hInputDesktop != IntPtr.Zero)
        {
            NativeMethods.SetThreadDesktop(hInputDesktop);
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
