using SecureKiosk.Core.Models;

namespace SecureKiosk.Core.Interfaces;

public interface IConfigurationService
{
    Task<KioskConfiguration> LoadAsync(CancellationToken cancellationToken = default);
}
