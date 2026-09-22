#if DEBUG
using SecureKiosk.Core.Interfaces;

namespace SecureKiosk.Infrastructure.Security;

// DEVELOPMENT ONLY: this type is not compiled into Release builds.
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
#endif
