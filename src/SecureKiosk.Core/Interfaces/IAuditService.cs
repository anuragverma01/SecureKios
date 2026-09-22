namespace SecureKiosk.Core.Interfaces;

public interface IAuditService
{
    ValueTask WriteAsync(string eventName, CancellationToken cancellationToken = default);
}
