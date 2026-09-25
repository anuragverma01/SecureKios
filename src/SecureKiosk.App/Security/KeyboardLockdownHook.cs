using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SecureKiosk.App.Security;

/// <summary>
/// Low-level keyboard hook (WH_KEYBOARD_LL) that suppresses keys capable of escaping kiosk mode:
/// - Windows Key (Left &amp; Right) -> blocks Start Menu
/// - Alt + Tab -> blocks Task Switcher / window switching
/// - Alt + Esc -> blocks window cycling
/// - Ctrl + Esc &amp; Ctrl + Shift + Esc -> blocks Start Menu and direct Task Manager shortcut
/// - Alt + F4 -> blocks application closure
/// - Alt + Space -> blocks system window menu
/// - Win + [Key] -> blocks all Windows key shortcuts (Win+R, Win+E, Win+S, Win+D, Win+X, Win+Tab, etc.)
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
    private const int VK_SHIFT = 0x10;

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
    private static volatile bool _isWinDownState = false;

    public static void Install()
    {
        lock (_syncLock)
        {
            if (_hookId == IntPtr.Zero)
            {
                IntPtr hMod = IntPtr.Zero;
                try
                {
                    using var curProcess = Process.GetCurrentProcess();
                    using var curModule = curProcess.MainModule;
                    if (curModule != null)
                    {
                        hMod = GetModuleHandle(curModule.ModuleName);
                    }
                }
                catch
                {
                    // Fallback
                }

                if (hMod == IntPtr.Zero)
                {
                    hMod = GetModuleHandle(null);
                }

                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, hMod, 0);
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
                _isWinDownState = false;
            }
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var kbd = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint vk = kbd.vkCode;

            // Track Win key state reliably across all down/up messages
            if (vk == VK_LWIN || vk == VK_RWIN)
            {
                if (wParam == (IntPtr)0x0100 || wParam == (IntPtr)0x0104) // WM_KEYDOWN, WM_SYSKEYDOWN
                {
                    _isWinDownState = true;
                }
                else if (wParam == (IntPtr)0x0101 || wParam == (IntPtr)0x0105) // WM_KEYUP, WM_SYSKEYUP
                {
                    _isWinDownState = false;
                }
                return (IntPtr)1; // Suppress Start Menu trigger
            }

            bool isWinDown = _isWinDownState || (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;
            bool isAltDown = (kbd.flags & LLKHF_ALTDOWN) != 0 || (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
            bool isCtrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;

            // 1. Suppress all Windows Key shortcuts (Win+R, Win+E, Win+S, Win+D, Win+X, Win+Tab, etc.)
            if (isWinDown)
            {
                return (IntPtr)1;
            }

            // 2. Suppress Alt+Tab (Task Switcher / window switching)
            if (vk == VK_TAB && isAltDown)
            {
                return (IntPtr)1;
            }

            // 3. Suppress Alt+Esc (Window switching)
            if (vk == VK_ESCAPE && isAltDown)
            {
                return (IntPtr)1;
            }

            // 4. Suppress Ctrl+Esc and Ctrl+Shift+Esc (Start Menu / Task Manager)
            if (vk == VK_ESCAPE && isCtrlDown)
            {
                return (IntPtr)1;
            }

            // 5. Suppress Alt+F4 (Closing application)
            if (vk == VK_F4 && isAltDown)
            {
                return (IntPtr)1;
            }

            // 6. Suppress Alt+Space (Window system menu)
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

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
