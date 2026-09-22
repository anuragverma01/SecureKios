namespace SecureKiosk.Core.Models;

public enum ExitAuthorizationStatus
{
    Authorized,
    Invalid,
    RateLimited,
    Cancelled
}

public sealed record ExitAuthorizationResult(
    ExitAuthorizationStatus Status,
    TimeSpan? RetryAfter = null)
{
    public static ExitAuthorizationResult AuthorizedResult { get; } = new(ExitAuthorizationStatus.Authorized);
}
