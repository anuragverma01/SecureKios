using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SecureKiosk.Infrastructure.Windows;

namespace SecureKiosk.App.Security;

/// <summary>
/// Continuous background security watchdog that monitors and terminates unauthorized administrative
/// processes (Task Manager, Command Prompt, PowerShell, Windows Terminal, Registry Editor, etc.),
/// closes any File Explorer or Run dialogs, terminates any background applications,
/// and ensures the taskbar remains suppressed while SecureKiosk is active.
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private static readonly HashSet<string> BlockedProcessesSet = new(StringComparer.OrdinalIgnoreCase)
    {
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
    };

    private static readonly HashSet<string> SystemProcessWhitelist = new(StringComparer.OrdinalIgnoreCase)
    {
        // System and OS Kernel
        "system",
        "idle",
        "registry",
        "smss",
        "csrss",
        "wininit",
        "services",
        "lsass",
        "winlogon",

        // Windows Shell and Desktop Infrastructure
        "explorer",
        "dwm",
        "sihost",
        "ctfmon",
        "taskhostw",
        "RuntimeBroker",
        "TextInputHost",
        "fontdrvhost",
        "ShellExperienceHost",
        "ApplicationFrameHost",
        "SystemSettings",
        "audiodg",
        "spoolsv",
        "SecurityHealthSystray",
        "SecurityHealthService",
        "smartscreen",

        // Hardware and Driver Helpers
        "nvcontainer",
        "NVDisplay.Container",
        "igfxEM",
        "igfxHK",
        "igfxTray",
        "RtkNGUI64",
        "RAVCpl64",
        "SynTPEnh",
        "SynTPHelper",
        "ETDCtrl",
        "ETDService",
        "ETDControl",
        "AsusTPCenter",
        "HControl"
    };

    public static void RegisterKioskWindow(IntPtr hwnd)
    {
        _kioskHwnd = hwnd;
    }

    /// <summary>
    /// Closes and terminates all background user applications running in the interactive session,
    /// ensuring the kiosk runs in a completely clean, dedicated environment.
    /// </summary>
    public static void CloseAllBackgroundApps()
    {
        if (!OperatingSystem.IsWindows()) return;

        int currentPid = Environment.ProcessId;
        int currentSession = -1;
        try
        {
            using var currentProc = Process.GetCurrentProcess();
            currentSession = currentProc.SessionId;
        }
        catch { }

        // 1. Send WM_CLOSE to top-level application windows belonging to other processes
        try
        {
            var pidCache = new Dictionary<uint, bool>();
            EnumWindows((hwnd, _) =>
            {
                try
                {
                    GetWindowThreadProcessId(hwnd, out uint pid);
                    if (pid != 0 && pid != (uint)currentPid)
                    {
                        if (!pidCache.TryGetValue(pid, out bool isWhitelisted))
                        {
                            using var p = Process.GetProcessById((int)pid);
                            isWhitelisted = SystemProcessWhitelist.Contains(p.ProcessName);
                            pidCache[pid] = isWhitelisted;
                        }

                        if (!isWhitelisted)
                        {
                            PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                        }
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);
        }
        catch { }

        // Short pause to give applications a moment to exit cleanly
        Thread.Sleep(100);

        // 2. Forcibly terminate all remaining non-whitelisted user processes in the session
        try
        {
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    if (p.Id == currentPid || p.Id <= 4)
                        continue;

                    if (currentSession != -1 && p.SessionId != currentSession)
                        continue;

                    string name = p.ProcessName;
                    if (SystemProcessWhitelist.Contains(name))
                        continue;

                    if (!p.HasExited)
                    {
                        p.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Access denied or already exited
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch { }
    }

    public static void Start()
    {
        lock (_lock)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            // Immediately sweep and close all background user apps on startup in background worker
            Task.Run(CloseAllBackgroundApps);

            Task.Run(async () =>
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(200));
                int tickCounter = 0;
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Lightweight O(1) checks run every 200ms
                        CloseFileExplorerWindows();
                        CloseRunDialogs();
                        KioskPolicyManager.HideTaskbar();
                        KioskPolicyManager.SetDesktopIconsVisibility(false);

                        // Process scans run every 3 ticks (~600ms) to reduce CPU by 90%
                        if (++tickCounter % 3 == 0)
                        {
                            KillBlockedProcesses();
                        }

                        // Full session app sweep runs every 15 ticks (~3 seconds)
                        if (tickCounter % 15 == 0)
                        {
                            CloseAllBackgroundApps();
                        }

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
                                    // Kill any unauthorized process that stole foreground focus
                                    GetWindowThreadProcessId(foreground, out uint fgPid);
                                    if (fgPid != 0 && fgPid != (uint)Environment.ProcessId)
                                    {
                                        try
                                        {
                                            using var fgProc = Process.GetProcessById((int)fgPid);
                                            if (!SystemProcessWhitelist.Contains(fgProc.ProcessName))
                                            {
                                                fgProc.Kill(entireProcessTree: true);
                                            }
                                        }
                                        catch { }
                                    }

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
        try
        {
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    if (BlockedProcessesSet.Contains(p.ProcessName) && !p.HasExited)
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
