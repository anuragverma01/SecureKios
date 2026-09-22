using System.Text.Json;
using SecureKiosk.Core.Interfaces;
using SecureKiosk.Core.Models;

namespace SecureKiosk.Infrastructure.Configuration;

public sealed class JsonConfigurationService(string path) : IConfigurationService
{
    public async Task<KioskConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var configuration = await JsonSerializer.DeserializeAsync<KioskConfiguration>(stream, cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("Kiosk configuration is empty.");
        configuration.Validate();
        return configuration;
    }
}
