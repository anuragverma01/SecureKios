namespace SecureKiosk.Core.Interfaces;

public interface IWindowsKioskService
{
    Task RequestCleanExitAsync(int exitCode, CancellationToken cancellationToken = default);
}
