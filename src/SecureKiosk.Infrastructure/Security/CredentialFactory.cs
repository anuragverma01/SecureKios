using System.Security.Cryptography;
using System.Text;
using SecureKiosk.Core.Interfaces;

namespace SecureKiosk.Infrastructure.Security;

public static class CredentialFactory
{
    public static CredentialRecord Create(string code, int iterations = 600_000)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        var salt = RandomNumberGenerator.GetBytes(32);
        var codeBytes = Encoding.UTF8.GetBytes(code);
        try
        {
            var verifier = Rfc2898DeriveBytes.Pbkdf2(codeBytes, salt, iterations, HashAlgorithmName.SHA256, 32);
            return new CredentialRecord(salt, verifier, iterations);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(codeBytes);
        }
    }
}
