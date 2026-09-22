using System.Security.Cryptography;
using SecureKiosk.Core.Interfaces;

namespace SecureKiosk.Infrastructure.Security;

public sealed class DpapiCredentialStore(string path) : ISecureCredentialStore
{
    private const int CurrentVersion = 1;

    public async Task<CredentialRecord?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return null;
        var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var bytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
        await using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        if (reader.ReadInt32() != CurrentVersion) throw new InvalidDataException("Unsupported credential format.");
        var iterations = reader.ReadInt32();
        var saltLength = reader.ReadInt32();
        var salt = ReadBounded(reader, saltLength);
        var verifierLength = reader.ReadInt32();
        var verifier = ReadBounded(reader, verifierLength);
        if (iterations < 100_000 || verifier.Length < 16) throw new InvalidDataException("Credential parameters are invalid.");
        return new CredentialRecord(salt, verifier, iterations);
    }

    private static byte[] ReadBounded(BinaryReader reader, int length)
    {
        if (length is < 16 or > 1024) throw new InvalidDataException("Credential field length is invalid.");
        var value = reader.ReadBytes(length);
        if (value.Length != length) throw new InvalidDataException("Credential file is truncated.");
        return value;
    }

    public async Task WriteAsync(CredentialRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(CurrentVersion);
            writer.Write(record.Iterations);
            writer.Write(record.Salt.Length); writer.Write(record.Salt);
            writer.Write(record.Verifier.Length); writer.Write(record.Verifier);
        }
        var protectedBytes = ProtectedData.Protect(stream.ToArray(), null, DataProtectionScope.LocalMachine);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, protectedBytes, cancellationToken).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }
}
