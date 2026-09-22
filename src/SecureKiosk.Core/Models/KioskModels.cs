namespace SecureKiosk.Core.Models;

public sealed record KioskStatusResult(
    bool IsConfigured,
    string? ConfiguredUser,
    string? ShellType,
    string? ShellPath,
    string WindowsEdition,
    bool IsAdministratorProvisioned,
    string Message);

public sealed record KioskOperationResult(
    bool Success,
    string Message);
