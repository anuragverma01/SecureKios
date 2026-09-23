using SecureKiosk.Core.Interfaces;

namespace SecureKiosk.Infrastructure.Security;

/// <summary>
/// In-memory credential store seeded from a plaintext code.
/// Used ONLY during development/testing when no DPAPI credential file
/// has been provisioned.  The runtime gate is the SECUREKIOSK_DEV_EXIT_CODE
/// environment variable (checked in App.xaml.cs / DevelopmentExitCredential).
/// </summary>
public sealed class DevelopmentCredentialStore(string code) : ISecureCredentialStore
{
    private readonly CredentialRecord _record = CredentialFactory.Create(code);

    public Task<CredentialRecord?> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<CredentialRecord?>(_record);
    }

    public Task WriteAsync(CredentialRecord record, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("Development credentials cannot be persisted.");
}
