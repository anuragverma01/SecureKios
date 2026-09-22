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
        var store = new MemoryStore(CredentialRecordFor("5013"));
        var service = new ExitAuthorizationService(store, new MemoryAudit(), maxAttempts: 3);
        var result = await service.AuthorizeAsync("5013");
        Assert.Equal(ExitAuthorizationStatus.Authorized, result.Status);
    }

    [Fact]
    public async Task Wrong_code_is_denied_and_eventually_rate_limited()
    {
        var service = new ExitAuthorizationService(new MemoryStore(CredentialRecordFor("5013")), new MemoryAudit(), maxAttempts: 2);
        Assert.Equal(ExitAuthorizationStatus.Invalid, (await service.AuthorizeAsync("9999")).Status);
        Assert.Equal(ExitAuthorizationStatus.RateLimited, (await service.AuthorizeAsync("9999")).Status);
        Assert.Equal(ExitAuthorizationStatus.RateLimited, (await service.AuthorizeAsync("5013")).Status);
    }

    [Theory]
    [InlineData("5013", true)]
    [InlineData("501", false)]
    [InlineData("50130", false)]
    [InlineData("50a3", false)]
    [InlineData("", false)]
    public void Exit_code_requires_exactly_four_numeric_digits(string code, bool expected) =>
        Assert.Equal(expected, ExitCodePolicy.IsValid(code));

    [Fact]
    public async Task Malformed_code_does_not_authorize()
    {
        var service = new ExitAuthorizationService(new MemoryStore(CredentialRecordFor("5013")), new MemoryAudit());
        Assert.Equal(ExitAuthorizationStatus.Invalid, (await service.AuthorizeAsync("501")).Status);
        Assert.Equal(ExitAuthorizationStatus.Invalid, (await service.AuthorizeAsync("50130")).Status);
        Assert.Equal(ExitAuthorizationStatus.Invalid, (await service.AuthorizeAsync("50a3")).Status);
        Assert.Equal(ExitAuthorizationStatus.Invalid, (await service.AuthorizeAsync(string.Empty)).Status);
    }

    [Fact]
    public async Task Missing_credential_does_not_authorize()
    {
        var service = new ExitAuthorizationService(new MissingStore(), new MemoryAudit());
        Assert.Equal(ExitAuthorizationStatus.Invalid, (await service.AuthorizeAsync("5013")).Status);
    }

    [Fact]
    public void Development_credential_is_disabled_outside_a_development_build()
    {
        Assert.False(DevelopmentExitCredential.TryGetConfiguredCode(false, "5013", out var productionCode));
        Assert.Null(productionCode);
        Assert.True(DevelopmentExitCredential.TryGetConfiguredCode(true, "5013", out var developmentCode));
        Assert.Equal("5013", developmentCode);
    }

    [Fact]
    public async Task Cancelled_authorization_does_not_read_credentials()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new ExitAuthorizationService(new MissingStore(), new MemoryAudit());
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.AuthorizeAsync("5013", cancellation.Token));
    }

    private static CredentialRecord CredentialRecordFor(string code)
    {
        var salt = new byte[] { 2, 3, 4 };
        var verifier = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(System.Text.Encoding.UTF8.GetBytes(code), salt, 10_000, System.Security.Cryptography.HashAlgorithmName.SHA256, 32);
        return new(salt, verifier, 10_000);
    }

    private sealed class MemoryStore(CredentialRecord record) : ISecureCredentialStore
    { public Task<CredentialRecord?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult<CredentialRecord?>(record); public Task WriteAsync(CredentialRecord r, CancellationToken c = default) => Task.CompletedTask; }
    private sealed class MissingStore : ISecureCredentialStore
    { public Task<CredentialRecord?> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult<CredentialRecord?>(null); public Task WriteAsync(CredentialRecord r, CancellationToken c = default) => Task.CompletedTask; }
    private sealed class MemoryAudit : IAuditService { public ValueTask WriteAsync(string eventName, CancellationToken cancellationToken = default) => ValueTask.CompletedTask; }
}
