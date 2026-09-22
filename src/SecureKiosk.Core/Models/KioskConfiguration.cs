namespace SecureKiosk.Core.Models;

public sealed record KioskConfiguration
{
    public required string KioskUserName { get; init; }
    public required string ApplicationPath { get; init; }
    public int MaxExitAttempts { get; init; } = 5;
    public TimeSpan AttemptWindow { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromMinutes(15);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(KioskUserName)) throw new ArgumentException("Kiosk user is required.");
        if (string.IsNullOrWhiteSpace(ApplicationPath)) throw new ArgumentException("Application path is required.");
        if (MaxExitAttempts is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(MaxExitAttempts));
        if (AttemptWindow <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(AttemptWindow));
        if (LockoutDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(LockoutDuration));
    }
}
