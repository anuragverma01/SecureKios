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

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    private const uint WM_SYSCOMMAND = 0x0112;
    private const int SC_MINIMIZE = 0xF020;
    private const int SC_CLOSE = 0xF060;
    private const int SC_KEYMENU = 0xF100;
    private const int SC_MOVE = 0xF010;
    private const int SC_SIZE = 0xF000;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    private readonly IntPtr _hwnd;
    private readonly SubclassProc _subclassProc;
    private int _maxHotkeyId = 0;

    public KioskWindow()
    {
        Content = new MainPage();
        _hwnd = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        appWindow.Closing += (_, e) => e.Cancel = true;
        appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);

        // Register window with watchdog for continuous focus and iconic restoration
        KioskSecurityWatchdog.RegisterKioskWindow(_hwnd);

        // Subclass window procedure to block SC_MINIMIZE (touchpad swipe minimizes), SC_CLOSE, and system menus
        _subclassProc = WindowSubclassCallback;
        SetWindowSubclass(_hwnd, _subclassProc, (UIntPtr)101, IntPtr.Zero);

        // Make window topmost to prevent any background windows or taskbars popping in front
        SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);

        // Install low-level keyboard hook to block Alt+Tab, Windows Key, Ctrl+Esc, etc.
        KeyboardLockdownHook.Install();

        // Register system hotkeys to claim them at kernel window manager level so Explorer cannot process them
        RegisterSystemHotkeys();

        // If the window loses activation, immediately reassert topmost and foreground
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                SetForegroundWindow(_hwnd);
            }
        };

        Closed += (_, _) =>
        {
            RemoveWindowSubclass(_hwnd, _subclassProc, (UIntPtr)101);
            UnregisterSystemHotkeys();
            KioskSecurityWatchdog.Stop();
            KeyboardLockdownHook.Uninstall();
        };
    }

    private IntPtr WindowSubclassCallback(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_SYSCOMMAND)
        {
            long cmd = wParam.ToInt64() & 0xFFF0;
            if (cmd == SC_MINIMIZE || cmd == SC_CLOSE || cmd == SC_KEYMENU || cmd == SC_MOVE || cmd == SC_SIZE)
            {
                // Disallow minimizing, closing, moving, sizing, or invoking system menu via gestures or OS
                return IntPtr.Zero;
            }
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void RegisterSystemHotkeys()
    {
        int id = 1;
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'R'); // Win+R (Run)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'E'); // Win+E (Explorer)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'S'); // Win+S (Search)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'Q'); // Win+Q (Search)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'X'); // Win+X (Quick Link)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'D'); // Win+D (Desktop)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'M'); // Win+M (Minimize all)
        RegisterHotKey(_hwnd, id++, MOD_WIN | MOD_SHIFT, (uint)'M'); // Win+Shift+M (Undo minimize)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'A'); // Win+A (Action Center)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'I'); // Win+I (Settings)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'B'); // Win+B (Notification Area / Tray)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'T'); // Win+T (Taskbar items)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'L'); // Win+L (Lock)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'C'); // Win+C (Copilot / Teams)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'W'); // Win+W (Widgets)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'N'); // Win+N (Notifications)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'Z'); // Win+Z (Snap Layouts)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'K'); // Win+K (Cast)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'P'); // Win+P (Project)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'U'); // Win+U (Accessibility)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'V'); // Win+V (Clipboard)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)'G'); // Win+G (Game Bar)
        RegisterHotKey(_hwnd, id++, MOD_WIN, 0x20);      // Win+Space (Switch input)
        RegisterHotKey(_hwnd, id++, MOD_WIN, 0x24);      // Win+Home (Minimize others)
        RegisterHotKey(_hwnd, id++, MOD_WIN, (uint)0x09); // Win+Tab (Task View)
        RegisterHotKey(_hwnd, id++, MOD_WIN, 0x25);      // Win+Left (Snap)
        RegisterHotKey(_hwnd, id++, MOD_WIN, 0x26);      // Win+Up (Maximize / Snap)
        RegisterHotKey(_hwnd, id++, MOD_WIN, 0x27);      // Win+Right (Snap)
        RegisterHotKey(_hwnd, id++, MOD_WIN, 0x28);      // Win+Down (Minimize / Restore)
        RegisterHotKey(_hwnd, id++, MOD_WIN | MOD_CONTROL, 0x25); // Win+Ctrl+Left (Previous Desktop)
        RegisterHotKey(_hwnd, id++, MOD_WIN | MOD_CONTROL, 0x27); // Win+Ctrl+Right (Next Desktop)
        RegisterHotKey(_hwnd, id++, MOD_WIN | MOD_CONTROL, (uint)'D');  // Win+Ctrl+D (New Desktop)
        RegisterHotKey(_hwnd, id++, MOD_WIN | MOD_CONTROL, 0x73); // Win+Ctrl+F4 (Close Desktop)
        RegisterHotKey(_hwnd, id++, MOD_ALT, (uint)0x09); // Alt+Tab
        RegisterHotKey(_hwnd, id++, MOD_ALT, 0x73);      // Alt+F4
        RegisterHotKey(_hwnd, id++, MOD_ALT, 0x1B);      // Alt+Esc
        RegisterHotKey(_hwnd, id++, MOD_ALT, 0x20);      // Alt+Space (Window menu)
        RegisterHotKey(_hwnd, id++, MOD_CONTROL, 0x1B);  // Ctrl+Esc
        RegisterHotKey(_hwnd, id++, MOD_CONTROL | MOD_SHIFT, 0x1B); // Ctrl+Shift+Esc
        _maxHotkeyId = id;
    }

    private void UnregisterSystemHotkeys()
    {
        for (int i = 1; i <= _maxHotkeyId; i++)
        {
            try
            {
                UnregisterHotKey(_hwnd, i);
            }
            catch { }
        }
    }
}
