using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace SecureKiosk.App.Security;

/// <summary>
/// Continuous background security watchdog that monitors and terminates unauthorized administrative
/// processes (Task Manager, Command Prompt, PowerShell, Windows Terminal, Registry Editor, etc.)
/// while SecureKiosk is active.
/// </summary>
public static class KioskSecurityWatchdog
{
    private static CancellationTokenSource? _cts;
    private static readonly object _lock = new();

    private static readonly string[] BlockedProcesses =
    [
        "taskmgr",
        "cmd",
        "powershell",
        "pwsh",
        "wt",
        "regedit",
        "mmc",
        "taskkill"
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
}
