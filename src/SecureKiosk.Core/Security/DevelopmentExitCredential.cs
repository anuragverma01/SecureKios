using System.Diagnostics.CodeAnalysis;

namespace SecureKiosk.Core.Security;

public static class DevelopmentExitCredential
{
    public const string EnvironmentVariableName = "SECUREKIOSK_DEV_EXIT_CODE";

#if DEBUG
    // DEVELOPMENT ONLY — this constant is NOT compiled into Release builds.
    // It provides a well-known test code so the Administrator Exit can be
    // exercised without provisioning DPAPI credentials on a dev machine.
    // DO NOT change this to an env-var lookup or move it into any UI class.
    private const string FallbackDevelopmentCode = "5013";
#endif

    /// <summary>
    /// Returns the effective development exit code for a debug build.
    /// Prefers the explicit env-var override; falls back to the hardcoded
    /// development constant when the env var is absent or invalid.
    /// Always returns <see langword="false"/> in Release builds.
    /// </summary>
    public static bool TryGetConfiguredCode(bool isDevelopmentBuild, string? configuredCode, [NotNullWhen(true)] out string? code)
    {
        code = null;
        if (!isDevelopmentBuild) return false;

#if DEBUG
        // Prefer the explicit env-var value; otherwise use the built-in dev fallback.
        var effective = ExitCodePolicy.IsValid(configuredCode) ? configuredCode : FallbackDevelopmentCode;
        if (!ExitCodePolicy.IsValid(effective)) return false;
        code = effective;
        return true;
#else
        if (!ExitCodePolicy.IsValid(configuredCode)) return false;
        code = configuredCode;
        return true;
#endif
    }
}
