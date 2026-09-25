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

    private const uint WM_CLOSE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

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
        "SearchApp"
    ];

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
