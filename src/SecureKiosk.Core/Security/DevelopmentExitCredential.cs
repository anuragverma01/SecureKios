using System.Diagnostics.CodeAnalysis;

namespace SecureKiosk.Core.Security;

public static class DevelopmentExitCredential
{
    public const string EnvironmentVariableName = "SECUREKIOSK_DEV_EXIT_CODE";

    public static bool TryGetConfiguredCode(bool isDevelopmentBuild, string? configuredCode, [NotNullWhen(true)] out string? code)
    {
        code = null;
        if (!isDevelopmentBuild || !ExitCodePolicy.IsValid(configuredCode)) return false;
        code = configuredCode;
        return true;
    }
}
