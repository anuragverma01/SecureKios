using System.Security.Cryptography;
using System.Text;
using SecureKiosk.Core.Interfaces;
using SecureKiosk.Core.Models;

namespace SecureKiosk.Core.Security;

public sealed class ExitAuthorizationService(
    ISecureCredentialStore credentialStore,
    IAuditService audit,
    int maxAttempts = 5,
    TimeSpan? attemptWindow = null,
    TimeSpan? lockoutDuration = null,
    TimeProvider? timeProvider = null) : IExitAuthorizationService
{
    private readonly int _maxAttempts = ValidateMaxAttempts(maxAttempts);
    private readonly TimeSpan _attemptWindow = ValidateDuration(attemptWindow ?? TimeSpan.FromMinutes(5), nameof(attemptWindow));
    private readonly TimeSpan _lockoutDuration = ValidateDuration(lockoutDuration ?? TimeSpan.FromMinutes(15), nameof(lockoutDuration));
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private readonly Queue<DateTimeOffset> _failedAttempts = new();
    private DateTimeOffset? _lockedUntil;

    public async Task<ExitAuthorizationResult> AuthorizeAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(code)) return await InvalidAsync(cancellationToken);

        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (_lockedUntil is { } lockedUntil && lockedUntil > now)
                return new(ExitAuthorizationStatus.RateLimited, lockedUntil - now);
            while (_failedAttempts.Count > 0 && now - _failedAttempts.Peek() > _attemptWindow) _failedAttempts.Dequeue();
        }

        var record = await credentialStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        var valid = record is not null && Verify(code, record);
        if (valid)
        {
            await audit.WriteAsync("exit_authorized", cancellationToken).ConfigureAwait(false);
            return ExitAuthorizationResult.AuthorizedResult;
        }
        return await InvalidAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ExitAuthorizationResult> InvalidAsync(CancellationToken cancellationToken)
    {
        TimeSpan? retryAfter = null;
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            _failedAttempts.Enqueue(now);
            while (_failedAttempts.Count > 0 && now - _failedAttempts.Peek() > _attemptWindow) _failedAttempts.Dequeue();
            if (_failedAttempts.Count >= _maxAttempts)
            {
                _lockedUntil = now + _lockoutDuration;
                retryAfter = _lockoutDuration;
                _failedAttempts.Clear();
            }
        }
        await audit.WriteAsync("exit_denied", cancellationToken).ConfigureAwait(false);
        return retryAfter is null ? new(ExitAuthorizationStatus.Invalid) : new(ExitAuthorizationStatus.RateLimited, retryAfter);
    }

    private static int ValidateMaxAttempts(int value) => value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(maxAttempts));
    private static TimeSpan ValidateDuration(TimeSpan value, string name) => value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(name);

    public static bool Verify(string code, CredentialRecord record)
    {
        var candidate = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), record.Salt, record.Iterations, HashAlgorithmName.SHA256, record.Verifier.Length);
        return CryptographicOperations.FixedTimeEquals(candidate, record.Verifier);
    }
}
