namespace SecureKiosk.Core.Security;

public static class ExitCodePolicy
{
    public const int Length = 4;

    public static bool IsValid(string? code)
    {
        if (code is null || code.Length != Length) return false;
        foreach (var character in code)
        {
            if (character is < '0' or > '9') return false;
        }
        return true;
    }
}
