using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SecureKiosk.Infrastructure.Windows;

namespace SecureKiosk.App.Security;

/// <summary>
/// Continuous background security watchdog that monitors and terminates unauthorized administrative
/// processes (Task Manager, Command Prompt, PowerShell, Windows Terminal, Registry Editor, etc.),
/// closes any File Explorer or Run dialogs, and ensures the taskbar remains suppressed while SecureKiosk is active.
/// </summary>
public static class KioskSecurityWatchdog
{
    private static CancellationTokenSource? _cts;
    private static readonly object _lock = new();
    private static IntPtr _kioskHwnd = IntPtr.Zero;

    private const uint WM_CLOSE = 0x0010;
    private const int SW_RESTORE = 9;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint GA_ROOT = 2;
    private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void SwitchToThisWindow(IntPtr hWnd, bool fUnknown);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

    private static readonly string[] BlockedProcesses =
    [
        "taskmgr",
        "cmd",
        "powershell",
        "pwsh",
        "wt",
        "regedit",
        "mmc",
        "taskkill",
        "SearchHost",
        "SearchApp",
        "StartMenuExperienceHost"
    ];

    public static void RegisterKioskWindow(IntPtr hwnd)
    {
        _kioskHwnd = hwnd;
    }

    public static void Start()
    {
        lock (_lock)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        KillBlockedProcesses();
                        CloseFileExplorerWindows();
                        CloseRunDialogs();
                        KioskPolicyManager.HideTaskbar();
                        KioskPolicyManager.SetDesktopIconsVisibility(false);

                        if (_kioskHwnd != IntPtr.Zero)
                        {
                            if (IsIconic(_kioskHwnd))
                            {
                                ShowWindow(_kioskHwnd, SW_RESTORE);
                                SetWindowPos(_kioskHwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                                SetForegroundWindow(_kioskHwnd);
                                SwitchToThisWindow(_kioskHwnd, true);
                            }
                            else
                            {
                                var foreground = GetForegroundWindow();
                                if (foreground != IntPtr.Zero && foreground != _kioskHwnd && GetAncestor(foreground, GA_ROOT) != _kioskHwnd)
                                {
                                    SetWindowPos(_kioskHwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW);
                                    SetForegroundWindow(_kioskHwnd);
                                    SwitchToThisWindow(_kioskHwnd, true);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore watchdog scan errors
                    }

                    try
                    {
                        await timer.WaitForNextTickAsync(token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }
    }

    public static void Stop()
    {
        lock (_lock)
        {
            _kioskHwnd = IntPtr.Zero;
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }
    }

    private static void KillBlockedProcesses()
    {
        foreach (var processName in BlockedProcesses)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);
                foreach (var p in processes)
                {
                    try
                    {
                        if (!p.HasExited)
                        {
                            p.Kill(entireProcessTree: true);
                        }
                    }
                    catch
                    {
                        // Process may have already exited or access denied
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }
            catch
            {
                // Ignore process enumeration errors
            }
        }
    }

    /// <summary>
    /// Closes any File Explorer windows (CabinetWClass or ExploreWClass) that attempt to open.
    /// </summary>
    private static void CloseFileExplorerWindows()
    {
        try
        {
            IntPtr hwnd = IntPtr.Zero;
            while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, "CabinetWClass", null)) != IntPtr.Zero)
            {
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }

            hwnd = IntPtr.Zero;
            while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, "ExploreWClass", null)) != IntPtr.Zero)
            {
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch { }
    }

    /// <summary>
    /// Closes any Run dialogs (#32770 with title "Run") that attempt to open.
    /// </summary>
    private static void CloseRunDialogs()
    {
        try
        {
            IntPtr hwnd = IntPtr.Zero;
            while ((hwnd = FindWindowEx(IntPtr.Zero, hwnd, "#32770", "Run")) != IntPtr.Zero)
            {
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
        }
        catch { }
    }
}
