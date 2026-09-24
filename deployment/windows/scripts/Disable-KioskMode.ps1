# Elevate if not already administrator
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Elevating permissions to restore normal desktop..."
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$currentUser = $env:USERNAME
$sid = ([System.Security.Principal.NTAccount]$currentUser).Translate([System.Security.Principal.SecurityIdentifier]).Value

Write-Host "Restoring default Windows desktop (explorer.exe) for '$currentUser'..."

# 1. Remove Per-User Winlogon Shell override
$winlogonKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"
if (Test-Path $winlogonKey) {
    Remove-ItemProperty -Path $winlogonKey -Name 'Shell' -ErrorAction SilentlyContinue
}

# 2. Re-enable Task Manager in Host Registry
$policyKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System"
if (Test-Path $policyKey) {
    Remove-ItemProperty -Path $policyKey -Name 'DisableTaskMgr' -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "============================================================"
Write-Host "SUCCESS: Windows default desktop (explorer.exe) is RESTORED."
Write-Host "Task Manager is RE-ENABLED."
Write-Host "Sign out or restart to return to the normal desktop."
Write-Host "============================================================"
pause
