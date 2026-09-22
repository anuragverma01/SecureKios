using SecureKiosk.Core.Interfaces;
using SecureKiosk.Core.Models;
using SecureKiosk.Core.Security;
using Xunit;

namespace SecureKiosk.Core.Tests;

public sealed class ExitAuthorizationServiceTests
{
    [Fact]
    public void Kiosk_configuration_rejects_missing_application_path()
    {
        var configuration = new KioskConfiguration { KioskUserName = "kiosk", ApplicationPath = "" };
        Assert.Throws<ArgumentException>(() => configuration.Validate());
    }

    [Fact]
    public async Task Correct_code_authorizes_without_exposing_secret()
    {
        var store = new MemoryStore(CredentialRecordFor("correct-code"));
        var service = new ExitAuthorizationService(store, new MemoryAudit(), maxAttempts: 3);
        var result = await service.AuthorizeAsync("correct-code");
        Assert.Equal(ExitAuthorizationStatus.Authorized, result.Status);
    }

    [Fact]
    public async Task Wrong_code_is_denied_and_eventually_rate_limited()
    {
        var service = new ExitAuthorizationService(new MemoryStore(CredentialRecordFor("correct-code")), new MemoryAudit(), maxAttempts: 2);
        Assert.Equal(ExitAuthorizationStatus.Invalid, (await service.AuthorizeAsync("wrong")).Status);
        Assert.Equal(ExitAuthorizationStatus.RateLimited, (await service.AuthorizeAsync("wrong")).Status);
        Assert.Equal(ExitAuthorizationStatus.RateLimited, (await service.AuthorizeAsync("correct-code")).Status);
    }

    private static CredentialRecord CredentialRecordFor(string code)
    {
        var salt = new byte[] { 2, 3, 4 };
        var verifier = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(code), salt, 10_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        return new(salt, verifier, 10_000);
    }

    private sealed class MemoryStore(CredentialRecord record) : ISecureCredentialStore
    { public Task<CredentialRecord?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult<CredentialRecord?>(record); public Task WriteAsync(CredentialRecord r, CancellationToken c = default) => Task.CompletedTask; }
    private sealed class MemoryAudit : IAuditService { public ValueTask WriteAsync(string eventName, CancellationToken cancellationToken = default) => ValueTask.CompletedTask; }
}
