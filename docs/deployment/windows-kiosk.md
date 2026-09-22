# Windows Kiosk Deployment Guide

## Supported Windows Editions & Architecture

SecureKiosk is a native C# / .NET 10 / WinUI 3 desktop application packaged as a self-contained MSIX.

In Windows 10 and 11, WinUI 3 desktop applications (`runFullTrust`) do not run under classic UWP single-app Assigned Access (`KioskModeApp`) because classic single-app kiosk is restricted to UWP/Edge AppContainer packages.

Instead, SecureKiosk uses:
1. **Shell Launcher v2 (MDM WMI Bridge)** on Windows 10/11 Enterprise, Education, and IoT Enterprise.
2. **Per-user Winlogon Shell Replacement** on Windows 10/11 Pro and all editions.

Under both mechanisms, `explorer.exe` is replaced as the user shell. This completely prevents access to:
- The desktop
- The taskbar
- The Start Menu
- Windows key hotkeys
- Task View (Win+Tab) and Alt+Tab application switching

## Application CLI Management

After installing the MSIX package, SecureKiosk can be configured and managed directly from an elevated console:

```powershell
# Check current configuration and credential status
SecureKiosk.exe --status

# Provision or change the 4-digit administrator exit code
SecureKiosk.exe --provision-exit-code

# Configure SecureKiosk as the dedicated kiosk shell for a user
SecureKiosk.exe --configure-kiosk --user KioskUser

# Remove kiosk mode and restore the default Windows Explorer desktop
SecureKiosk.exe --remove-kiosk --user KioskUser

# Run in development test mode (non-enforcing window)
SecureKiosk.exe --dev-test
```

## PowerShell Deployment Scripts

Administrators may also use the automated deployment scripts in `deployment/windows/scripts/`:

```powershell
# 1. Install MSIX and configure kiosk mode
.\deployment\windows\scripts\install.ps1 -MsixPath .\SecureKiosk.App_1.0.0.0_x64.msix -KioskUser KioskUser

# 2. Validate configuration
.\deployment\windows\scripts\validate-kiosk.ps1

# 3. Remove kiosk configuration
.\deployment\windows\scripts\remove-kiosk.ps1 -KioskUser KioskUser
```

## Administrator Exit and Crash Recovery

- **Crash Recovery**: If SecureKiosk crashes or is terminated unexpectedly, Windows Shell Launcher automatically restarts the application (`DefaultAction Action="RestartShell"`).
- **Administrator Exit**: When the administrator enters the verified 4-digit code in the Administrator Exit dialog, the application exits with return code `1`. Shell Launcher maps ReturnCode `1` to `Action="Logoff"`, and `WindowsKioskService` calls `ExitWindowsEx`, signing out the kiosk session and returning the system to the Windows sign-in screen where the administrator can log in.
