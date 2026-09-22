using System.Text.Json;
using SecureKiosk.Core.Interfaces;

namespace SecureKiosk.Infrastructure.Logging;

public sealed class JsonLineAuditService(string path) : IAuditService
{
    private readonly SemaphoreSlim _mutex = new(1, 1);

    public async ValueTask WriteAsync(string eventName, CancellationToken cancellationToken = default)
    {
        var entry = JsonSerializer.Serialize(new { timestampUtc = DateTimeOffset.UtcNow, eventName });
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await _mutex.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await File.AppendAllTextAsync(path, entry + Environment.NewLine, cancellationToken).ConfigureAwait(false); }
        finally { _mutex.Release(); }
    }
}
