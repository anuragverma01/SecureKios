using System.Diagnostics.CodeAnalysis;

namespace SecureKiosk.Core.Security;

/// <summary>
/// Provides the development/testing exit code.
/// The fallback constant allows the Administrator Exit to work on machines
/// where no DPAPI credential has been provisioned, without requiring the
/// operator to set an environment variable first.
///
/// PRODUCTION: Provisioned DPAPI credentials are always used; this class
/// is only activated when <see cref="TryGetConfiguredCode"/> is called
/// with isDevelopmentBuild = true (which is gated by the env var or by
/// the absence of a provisioned credential file).
/// </summary>
public static class DevelopmentExitCredential
{
    public const string EnvironmentVariableName = "SECUREKIOSK_DEV_EXIT_CODE";

    /// <summary>
    /// Well-known development exit code.  Used only when no env-var override
    /// is provided and no DPAPI credential file exists.
    /// DO NOT use this value in production deployments.
    /// </summary>
    internal const string FallbackDevelopmentCode = "5013";

    /// <summary>
    /// Returns the effective development exit code.
    /// Prefers an explicit env-var override; falls back to <see cref="FallbackDevelopmentCode"/>
    /// when no env var is set.
    /// Returns <see langword="false"/> when <paramref name="isDevelopmentBuild"/> is false.
    /// </summary>
    public static bool TryGetConfiguredCode(bool isDevelopmentBuild, string? configuredCode, [NotNullWhen(true)] out string? code)
    {
        code = null;
        if (!isDevelopmentBuild) return false;

        // Prefer the explicit env-var value; otherwise use the built-in dev fallback.
        var effective = ExitCodePolicy.IsValid(configuredCode) ? configuredCode : FallbackDevelopmentCode;
        if (!ExitCodePolicy.IsValid(effective)) return false;
        code = effective;
        return true;
    }
}
