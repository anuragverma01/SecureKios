using SecureKiosk.Core.Models;

namespace SecureKiosk.Core.Interfaces;

public interface IWindowsKioskService
{
    Task RequestCleanExitAsync(int exitCode, CancellationToken cancellationToken = default);
    Task<KioskStatusResult> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<KioskOperationResult> ConfigureKioskAsync(string kioskUser, string? applicationPath = null, CancellationToken cancellationToken = default);
    Task<KioskOperationResult> RemoveKioskAsync(string? kioskUser = null, CancellationToken cancellationToken = default);
}
