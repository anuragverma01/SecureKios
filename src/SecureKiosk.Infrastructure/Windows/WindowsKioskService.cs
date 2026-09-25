using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using SecureKiosk.Core.Interfaces;
using SecureKiosk.Core.Models;

namespace SecureKiosk.Infrastructure.Windows;

public sealed partial class WindowsKioskService : IWindowsKioskService
{
    private readonly ISecureCredentialStore _credentialStore;
    private readonly IAuditService _audit;

    public WindowsKioskService(ISecureCredentialStore credentialStore, IAuditService audit)
    {
        _credentialStore = credentialStore;
        _audit = audit;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    private const uint EWX_LOGOFF = 0x00000000;
    private const uint EWX_FORCEIFHUNG = 0x00000010;
    private const uint SHTDN_REASON_FLAG_PLANNED = 0x40000000;

    public async Task RequestCleanExitAsync(int exitCode, CancellationToken cancellationToken = default)
    {
        Environment.ExitCode = exitCode;
        await _audit.WriteAsync($"kiosk_clean_exit_requested:code={exitCode}", cancellationToken).ConfigureAwait(false);

        if (OperatingSystem.IsWindows() && exitCode == ExitCodes.AuthorizedExit)
        {
            // Disarm startup task so the app does NOT reopen on restart/power off
            // after an authorized administrator exit.
            try
            {
                var task = await global::Windows.ApplicationModel.StartupTask.GetAsync("SecureKioskStartupTask");
                task.Disable();

                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        var disarmInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = "/run /tn \"SecureKioskDisarm\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var proc = System.Diagnostics.Process.Start(disarmInfo);
                        proc?.WaitForExit(3000);
                    }
                    catch { }

                    try
                    {
                        var progData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                        var disarmScript = System.IO.Path.Combine(progData, "SecureKiosk", "disarm.cmd");
                        if (System.IO.File.Exists(disarmScript))
                        {
                            var cmdInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/c \"{disarmScript}\"",
                                UseShellExecute = false,
                                CreateNoWindow = true
                            };
                            using var cmdProc = System.Diagnostics.Process.Start(cmdInfo);
                            cmdProc?.WaitForExit(3000);
                        }
                    }
                    catch { }

                    try
                    {
                        var regInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "reg.exe",
                            Arguments = "delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System\" /v \"DisableTaskMgr\" /f",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var regProc = System.Diagnostics.Process.Start(regInfo);
                        regProc?.WaitForExit(2000);
                    }
                    catch { }

                    try
                    {
                        var delInfo = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = "/delete /tn \"SecureKioskInstantLaunch\" /f",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var delProc = System.Diagnostics.Process.Start(delInfo);
                        delProc?.WaitForExit(3000);
                    }
                    catch { }

                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", writable: true);
                    key?.DeleteValue("StartupDelayInMSec", throwOnMissingValue: false);
                    key?.DeleteValue("WaitForIdleState", throwOnMissingValue: false);

                    using var runKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                    runKey?.DeleteValue("SecureKioskFastLaunch", throwOnMissingValue: false);

                    using var policyKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System", writable: true);
                    policyKey?.DeleteValue("DisableTaskMgr", throwOnMissingValue: false);
                }
            }
            catch
            {
                // Fallback for unpackaged or unsupported environments
            }

            // Only initiate a Windows session logoff when Shell Launcher is actually active.
            // In development / non-kiosk mode, ExitWindowsEx must NOT be called — it would
            // sign out the entire Windows user session. Process termination is handled by
            // Environment.Exit in the caller (ExitCodeDialog).
            var shellStatus = QueryShellLauncherStatus();
            if (shellStatus.IsConfigured)
            {
                try
                {
                    ExitWindowsEx(EWX_LOGOFF | EWX_FORCEIFHUNG, SHTDN_REASON_FLAG_PLANNED);
                }
                catch
                {
                    // Fallback: Shell Launcher exit code handling will terminate the session.
                }
            }
        }
    }

    public async Task<KioskStatusResult> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new KioskStatusResult(false, null, null, null, "Non-Windows", false, "Not running on Windows.");
        }

        string edition = GetWindowsEdition();
        var credentialRecord = await _credentialStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        bool hasAdminCredential = credentialRecord is not null;

        // Check Shell Launcher via PowerShell CIM query
        var shellLauncherInfo = QueryShellLauncherStatus();
        if (shellLauncherInfo.IsConfigured)
        {
            return new KioskStatusResult(
                true,
                shellLauncherInfo.User,
                "ShellLauncher",
                shellLauncherInfo.ShellPath,
                edition,
                hasAdminCredential,
                "Shell Launcher is actively configured.");
        }

        // Check user registry shell override
        var regShellInfo = QueryRegistryKioskStatus();
        if (regShellInfo.IsConfigured)
        {
            return new KioskStatusResult(
                true,
                regShellInfo.User,
                "WinlogonShell",
                regShellInfo.ShellPath,
                edition,
                hasAdminCredential,
                "Per-user Winlogon shell is configured.");
        }

        return new KioskStatusResult(
            false,
            null,
            null,
            null,
            edition,
            hasAdminCredential,
            "Kiosk mode is currently not configured.");
    }

    public async Task<KioskOperationResult> ConfigureKioskAsync(string kioskUser, string? applicationPath = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new KioskOperationResult(false, "Kiosk configuration requires Windows.");
        }

        if (!IsAdministrator())
        {
            return new KioskOperationResult(false, "Administrator privileges are required to configure kiosk mode. Run from an elevated session.");
        }

        var credentialRecord = await _credentialStore.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (credentialRecord is null)
        {
            return new KioskOperationResult(false, "Refusing to configure kiosk mode because no administrator exit credential has been provisioned. Provision an exit credential first.");
        }

        string resolvedAppPath = applicationPath ?? ResolveApplicationPath();
        if (string.IsNullOrWhiteSpace(resolvedAppPath))
        {
            return new KioskOperationResult(false, "Could not determine application executable path for kiosk shell.");
        }

        string sid;
        try
        {
            var account = new NTAccount(kioskUser);
            sid = ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
        }
        catch (Exception ex)
        {
            return new KioskOperationResult(false, $"Failed to resolve SID for kiosk user '{kioskUser}': {ex.Message}");
        }

        string edition = GetWindowsEdition();
        bool isEnterprise = edition is "Enterprise" or "EnterpriseS" or "Education" or "IoTEnterprise" or "IoTEnterpriseS";

        if (isEnterprise)
        {
            // Configure Shell Launcher v2 via MDM WMI bridge
            var shellResult = ConfigureShellLauncher(kioskUser, sid, resolvedAppPath);
            if (shellResult.Success)
            {
                await _audit.WriteAsync($"kiosk_configured:user={kioskUser},method=ShellLauncher,path={resolvedAppPath}", cancellationToken).ConfigureAwait(false);
                return shellResult;
            }
        }

        // Fallback or Windows Pro: Configure per-user Winlogon shell in registry
        var regResult = ConfigureRegistryShell(sid, kioskUser, resolvedAppPath);
        if (regResult.Success)
        {
            await _audit.WriteAsync($"kiosk_configured:user={kioskUser},method=WinlogonShell,path={resolvedAppPath}", cancellationToken).ConfigureAwait(false);
        }
        return regResult;
    }

    public async Task<KioskOperationResult> RemoveKioskAsync(string? kioskUser = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new KioskOperationResult(false, "Operation requires Windows.");
        }

        if (!IsAdministrator())
        {
            return new KioskOperationResult(false, "Administrator privileges are required to remove kiosk mode.");
        }

        // Remove Shell Launcher if active
        RemoveShellLauncher();

        // Remove per-user shell registry overrides
        RemoveRegistryShell(kioskUser);

        await _audit.WriteAsync("kiosk_removed", cancellationToken).ConfigureAwait(false);
        return new KioskOperationResult(true, "Kiosk mode has been removed. Explorer is restored as the default desktop shell.");
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static string GetWindowsEdition()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("EditionID")?.ToString() ?? "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string ResolveApplicationPath()
    {
        // 1. Try Process main module
        try
        {
            using var current = Process.GetCurrentProcess();
            var path = current.MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                return path;
            }
        }
        catch { }

        // 2. Fall back to Execution Alias in WindowsApps
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var aliasPath = Path.Combine(localAppData, @"Microsoft\WindowsApps\SecureKiosk.exe");
        if (File.Exists(aliasPath)) return aliasPath;

        return "SecureKiosk.App.exe";
    }

    private static KioskOperationResult ConfigureShellLauncher(string user, string sid, string appPath)
    {
        try
        {
            string escapedPath = appPath.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
            string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<ShellLauncherConfiguration xmlns=""http://schemas.microsoft.com/ShellLauncher/2018/Configuration"" xmlns:V2=""http://schemas.microsoft.com/ShellLauncher/2019/Configuration"">
  <Profiles>
    <DefaultProfile><Shell Shell=""%SystemRoot%\explorer.exe"" /></DefaultProfile>
    <Profile Id=""{{6C1660BB-91DF-4A5A-8E4F-2F2AD2E5A8D7}}"">
      <Shell Shell=""{escapedPath}"" V2:AppType=""Desktop"" V2:AllAppsFullScreen=""true"">
        <ReturnCodeActions>
          <ReturnCodeAction ReturnCode=""1"" Action=""Logoff"" />
          <ReturnCodeAction ReturnCode=""0"" Action=""RestartShell"" />
          <ReturnCodeAction ReturnCode=""-1"" Action=""RestartDevice"" />
        </ReturnCodeActions>
        <DefaultAction Action=""RestartShell"" />
      </Shell>
    </Profile>
  </Profiles>
  <Configs><Config><Account Sid=""{sid}"" /><Profile Id=""{{6C1660BB-91DF-4A5A-8E4F-2F2AD2E5A8D7}}"" /></Config></Configs>
</ShellLauncherConfiguration>";

            string encodedXml = System.Net.WebUtility.HtmlEncode(xml);
            string script = $@"
$bridge = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess' -ErrorAction Stop
$bridge.ShellLauncher = [System.Net.WebUtility]::HtmlDecode('{encodedXml}')
Set-CimInstance -CimInstance $bridge -ErrorAction Stop
";
            var result = RunPowerShell(script);
            if (result.ExitCode == 0)
            {
                return new KioskOperationResult(true, $"Shell Launcher configured for '{user}'. The kiosk shell will launch on sign-in.");
            }
            return new KioskOperationResult(false, $"Shell Launcher WMI configuration failed: {result.Error}");
        }
        catch (Exception ex)
        {
            return new KioskOperationResult(false, $"Failed to apply Shell Launcher: {ex.Message}");
        }
    }

    private static KioskOperationResult RemoveShellLauncher()
    {
        try
        {
            string script = @"
try {
    $bridge = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess' -ErrorAction SilentlyContinue
    if ($bridge -and $bridge.ShellLauncher) {
        $bridge.ShellLauncher = $null
        Set-CimInstance -CimInstance $bridge -ErrorAction Stop
    }
} catch {}
";
            RunPowerShell(script);
            return new KioskOperationResult(true, "Shell Launcher reset.");
        }
        catch (Exception ex)
        {
            return new KioskOperationResult(false, ex.Message);
        }
    }

    private static KioskOperationResult ConfigureRegistryShell(string sid, string user, string appPath)
    {
        try
        {
            // Configure per-user Winlogon shell override:
            // HKU\<SID>\Software\Microsoft\Windows NT\CurrentVersion\Winlogon -> Shell = "<appPath>"
            using var userRoot = Registry.Users.OpenSubKey(sid, writable: true);
            if (userRoot is not null)
            {
                using var winlogonKey = userRoot.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Winlogon");
                winlogonKey.SetValue("Shell", $"\"{appPath}\"", RegistryValueKind.String);
                return new KioskOperationResult(true, $"Per-user shell configured for '{user}'. Upon login, SecureKiosk will launch instead of Explorer.");
            }

            // If user hive is not currently mounted, configure via PowerShell / reg.exe
            string script = $@"
$key = 'Registry::HKEY_USERS\{sid}\Software\Microsoft\Windows NT\CurrentVersion\Winlogon'
if (-not (Test-Path $key)) {{ New-Item -Path $key -Force | Out-Null }}
Set-ItemProperty -Path $key -Name 'Shell' -Value '""{appPath}""' -Force
";
            var result = RunPowerShell(script);
            if (result.ExitCode == 0)
            {
                return new KioskOperationResult(true, $"Per-user shell configured for '{user}'.");
            }
            return new KioskOperationResult(false, $"Registry shell configuration failed: {result.Error}");
        }
        catch (Exception ex)
        {
            return new KioskOperationResult(false, $"Failed to configure registry shell: {ex.Message}");
        }
    }

    private static KioskOperationResult RemoveRegistryShell(string? kioskUser)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(kioskUser))
            {
                try
                {
                    var account = new NTAccount(kioskUser);
                    string targetSid = ((SecurityIdentifier)account.Translate(typeof(SecurityIdentifier))).Value;
                    using var userWinlogon = Registry.Users.OpenSubKey($@"{targetSid}\Software\Microsoft\Windows NT\CurrentVersion\Winlogon", writable: true);
                    if (userWinlogon?.GetValue("Shell") is not null)
                    {
                        userWinlogon.DeleteValue("Shell", throwOnMissingValue: false);
                    }
                }
                catch { }
            }

            // Remove from current user if run as kiosk user, or all loaded users
            foreach (var subKeyName in Registry.Users.GetSubKeyNames())
            {
                if (subKeyName.StartsWith("S-1-5-21-", StringComparison.Ordinal))
                {
                    try
                    {
                        using var winlogon = Registry.Users.OpenSubKey($@"{subKeyName}\Software\Microsoft\Windows NT\CurrentVersion\Winlogon", writable: true);
                        if (winlogon?.GetValue("Shell") is not null)
                        {
                            winlogon.DeleteValue("Shell", throwOnMissingValue: false);
                        }
                    }
                    catch { }
                }
            }
            return new KioskOperationResult(true, "Registry shell overrides removed.");
        }
        catch (Exception ex)
        {
            return new KioskOperationResult(false, ex.Message);
        }
    }

    private static (bool IsConfigured, string? User, string? ShellPath) QueryShellLauncherStatus()
    {
        try
        {
            string script = @"
$bridge = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess' -ErrorAction SilentlyContinue
if ($bridge -and $bridge.ShellLauncher) {
    Write-Output $bridge.ShellLauncher
}
";
            var res = RunPowerShell(script);
            if (!string.IsNullOrWhiteSpace(res.Output))
            {
                return (true, "AssignedAccessUser", res.Output.Contains("SecureKiosk") ? "SecureKiosk" : "CustomShell");
            }
        }
        catch { }
        return (false, null, null);
    }

    private static (bool IsConfigured, string? User, string? ShellPath) QueryRegistryKioskStatus()
    {
        try
        {
            foreach (var subKeyName in Registry.Users.GetSubKeyNames())
            {
                if (subKeyName.StartsWith("S-1-5-21-") && !subKeyName.EndsWith("_Classes"))
                {
                    using var winlogon = Registry.Users.OpenSubKey($@"{subKeyName}\Software\Microsoft\Windows NT\CurrentVersion\Winlogon");
                    var shellVal = winlogon?.GetValue("Shell")?.ToString();
                    if (!string.IsNullOrWhiteSpace(shellVal) && shellVal.Contains("SecureKiosk", StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, subKeyName, shellVal);
                    }
                }
            }
        }
        catch { }
        return (false, null, null);
    }

    private static (int ExitCode, string Output, string Error) RunPowerShell(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{script.Replace("\"", "\\\"")}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi);
        if (proc is null) return (-1, string.Empty, "Failed to start PowerShell process.");

        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();
        return (proc.ExitCode, stdout.Trim(), stderr.Trim());
    }
}
