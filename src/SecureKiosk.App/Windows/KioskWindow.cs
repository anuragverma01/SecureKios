using System;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SecureKiosk.App.Pages;
using SecureKiosk.App.Security;
using WinRT.Interop;

namespace SecureKiosk.App.Windows;

public sealed class KioskWindow : Window
{
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public KioskWindow()
    {
        Content = new MainPage();
        var windowHandle = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(windowHandle));
        appWindow.Closing += (_, e) => e.Cancel = true;
        appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);

        // Make window topmost to prevent any background windows or taskbars popping in front
        SetWindowPos(windowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        // Install low-level keyboard hook to block Alt+Tab, Windows Key, Ctrl+Esc, etc.
        KeyboardLockdownHook.Install();

        // If the window loses activation, immediately reassert topmost and foreground
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                SetWindowPos(windowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetForegroundWindow(windowHandle);
            }
        };

        Closed += (_, _) =>
        {
            KioskSecurityWatchdog.Stop();
            KeyboardLockdownHook.Uninstall();
        };
    }
}
