# Elevate if not already administrator
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Elevating permissions to configure Kiosk Shell..."
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$pkg = Get-AppxPackage -Name 'SecureKiosk' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $pkg) {
    Write-Error "SecureKiosk is not installed. Install the MSIX package first."
    pause
    exit 1
}

$appPath = Join-Path $pkg.InstallLocation 'SecureKiosk.App.exe'
if (-not (Test-Path $appPath)) {
    Write-Error "SecureKiosk executable not found at: $appPath"
    pause
    exit 1
}

$currentUser = $env:USERNAME
$sid = ([System.Security.Principal.NTAccount]$currentUser).Translate([System.Security.Principal.SecurityIdentifier]).Value

Write-Host "Configuring SecureKiosk as the dedicated shell for '$currentUser'..."

# 1. Set Per-User Winlogon Shell override
$winlogonKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"
if (-not (Test-Path $winlogonKey)) { New-Item -Path $winlogonKey -Force | Out-Null }
Set-ItemProperty -Path $winlogonKey -Name 'Shell' -Value "`"$appPath`"" -Force

# 2. Disable Task Manager for Kiosk User in Host Registry
$policyKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System"
if (-not (Test-Path $policyKey)) { New-Item -Path $policyKey -Force | Out-Null }
Set-ItemProperty -Path $policyKey -Name 'DisableTaskMgr' -Value 1 -Type DWord -Force

Write-Host ""
Write-Host "============================================================"
Write-Host "SUCCESS: SecureKiosk is now the DEDICATED SHELL for '$currentUser'."
Write-Host ""
Write-Host "What this does:"
Write-Host "  - Windows Explorer (desktop, taskbar, start menu) is REPLACED."
Write-Host "  - Upon sign-in, SecureKiosk starts in 0.5 seconds."
Write-Host "  - Task Manager is DISABLED."
Write-Host "  - Users cannot Alt+Tab or escape to the desktop."
Write-Host ""
Write-Host "Sign out or restart your PC now to enter Kiosk Mode."
Write-Host "============================================================"
pause
