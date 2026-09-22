using SecureKiosk.Core.Interfaces;

namespace SecureKiosk.Infrastructure.Windows;

public sealed class WindowsKioskService : IWindowsKioskService
{
    public Task RequestCleanExitAsync(int exitCode, CancellationToken cancellationToken = default)
    {
        // Shell Launcher owns the shell lifecycle. Returning from the WinUI process is intentional;
        // the configured ReturnCodeAction decides whether the shell is restarted or handed to recovery.
        Environment.ExitCode = exitCode;
        return Task.CompletedTask;
    }
}
