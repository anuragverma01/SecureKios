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

# 2. Re-enable Task Manager, Ctrl+Alt+Del options, and Command Prompt
$policiesToRemove = @(
    'DisableTaskMgr', 'DisableLockWorkstation', 'DisableChangePassword', 'HideFastUserSwitching'
)
$explorerPoliciesToRemove = @(
    'NoLogoff', 'NoRun'
)

foreach ($name in $policiesToRemove) {
    Remove-ItemProperty -Path "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System" -Name $name -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System' -Name $name -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name $name -ErrorAction SilentlyContinue
}

foreach ($name in $explorerPoliciesToRemove) {
    Remove-ItemProperty -Path "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" -Name $name -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer' -Name $name -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer' -Name $name -ErrorAction SilentlyContinue
}

Remove-ItemProperty -Path "Registry::HKEY_USERS\$sid\Software\Policies\Microsoft\Windows\System" -Name 'DisableCMD' -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKCU:\Software\Policies\Microsoft\Windows\System' -Name 'DisableCMD' -ErrorAction SilentlyContinue
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System' -Name 'DisableCMD' -ErrorAction SilentlyContinue

# 3. Clean up Scheduled Tasks and Fast Launch Run key
schtasks.exe /delete /tn "SecureKioskInstantLaunch" /f 2>$null | Out-Null
schtasks.exe /delete /tn "SecureKioskDisarm" /f 2>$null | Out-Null
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'SecureKioskFastLaunch' -ErrorAction SilentlyContinue

# 4. Ensure Explorer desktop process is running
$exp = Get-Process -Name 'explorer' -ErrorAction SilentlyContinue
if (-not $exp) {
    Start-Process 'explorer.exe'
}

Write-Host ""
Write-Host "============================================================"
Write-Host "SUCCESS: Windows default desktop (explorer.exe) is RESTORED."
Write-Host "Task Manager is RE-ENABLED."
Write-Host "Sign out or restart to return to the normal desktop."
Write-Host "============================================================"
pause
