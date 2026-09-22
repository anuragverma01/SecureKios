namespace SecureKiosk.Core.Interfaces;

public sealed record CredentialRecord(byte[] Salt, byte[] Verifier, int Iterations);

public interface ISecureCredentialStore
{
    Task<CredentialRecord?> ReadAsync(CancellationToken cancellationToken = default);
    Task WriteAsync(CredentialRecord record, CancellationToken cancellationToken = default);
}
