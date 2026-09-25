using System;
using System.Runtime.InteropServices;

namespace SecureKiosk.App.Security;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) that suppresses keys capable of escaping kiosk mode:
/// - Windows Key (Left &amp; Right) -> blocks Start Menu
/// - Alt + Tab -> blocks Task Switcher / window switching
/// - Alt + Esc -> blocks window cycling
/// - Ctrl + Esc -> blocks Start Menu
/// - Alt + F4 -> blocks application closure
/// - Alt + Space -> blocks system window menu
/// - Win + [Key] -> blocks all Windows key shortcuts (Win+D, Win+E, Win+R, Win+X, Win+Tab, etc.)
/// </summary>
public static class KeyboardLockdownHook
{
    private const int WH_KEYBOARD_LL = 13;

    private const int VK_TAB = 0x09;
    private const int VK_ESCAPE = 0x1B;
    private const int VK_SPACE = 0x20;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_F4 = 0x73;

    private const uint LLKHF_ALTDOWN = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    // Keep delegate reference alive in a static field to prevent garbage collection
    private static readonly LowLevelKeyboardProc _proc = HookCallback;
    private static IntPtr _hookId = IntPtr.Zero;
    private static readonly object _syncLock = new();

    public static void Install()
    {
        lock (_syncLock)
        {
            if (_hookId == IntPtr.Zero)
            {
                // For WH_KEYBOARD_LL, hMod must be IntPtr.Zero and dwThreadId must be 0
                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);
            }
        }
    }

    public static void Uninstall()
    {
        lock (_syncLock)
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = kbd.vkCode;
            bool isAltDown = (kbd.flags & LLKHF_ALTDOWN) != 0 || (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            bool isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
            bool isWinDown = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

            // 1. Suppress Windows Keys (Start Menu)
            if (vk == VK_LWIN || vk == VK_RWIN)
            {
                return (IntPtr)1;
            }

            // 2. Suppress any key pressed while Windows Key is held (Win+Tab, Win+D, Win+R, Win+X, etc.)
            if (isWinDown)
            {
                return (IntPtr)1;
            }

            // 3. Suppress Alt+Tab (Task Switcher / window switching)
            if (vk == VK_TAB && isAltDown)
            {
                return (IntPtr)1;
            }

            // 4. Suppress Alt+Esc (Window switching)
            if (vk == VK_ESCAPE && isAltDown)
            {
                return (IntPtr)1;
            }

            // 5. Suppress Ctrl+Esc (Start Menu)
            if (vk == VK_ESCAPE && isCtrlDown)
            {
                return (IntPtr)1;
            }

            // 6. Suppress Alt+F4 (Closing application)
            if (vk == VK_F4 && isAltDown)
            {
                return (IntPtr)1;
            }

            // 7. Suppress Alt+Space (Window system menu)
            if (vk == VK_SPACE && isAltDown)
            {
                return (IntPtr)1;
            }
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
