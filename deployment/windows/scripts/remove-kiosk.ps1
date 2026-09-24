[CmdletBinding()]
param(
  [string]$KioskUser
)

. "$PSScriptRoot\common.ps1"
Assert-Administrator

# 1. Reset Shell Launcher if present
try {
  $bridge = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess' -ErrorAction SilentlyContinue
  if ($bridge -and $bridge.ShellLauncher) {
    $bridge.ShellLauncher = $null
    Set-CimInstance -CimInstance $bridge -ErrorAction Stop
    Write-Host 'Shell Launcher configuration cleared.'
  }
} catch {
  Write-Verbose "Shell Launcher reset skipped: $($_.Exception.Message)"
}

# 2. Reset Registry per-user shell and Task Manager
if ($KioskUser) {
  $sid = Get-UserSid $KioskUser
  Remove-RegistryKioskShell -UserSid $sid
  $policyKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System"
  if (Test-Path $policyKey) { Remove-ItemProperty -Path $policyKey -Name 'DisableTaskMgr' -ErrorAction SilentlyContinue }
} else {
  # Clean across all user hives
  Get-ChildItem 'Registry::HKEY_USERS' | Where-Object { $_.PSChildName -like 'S-1-5-21-*' } | ForEach-Object {
    Remove-RegistryKioskShell -UserSid $_.PSChildName
    $policyKey = "Registry::HKEY_USERS\$($_.PSChildName)\Software\Microsoft\Windows\CurrentVersion\Policies\System"
    if (Test-Path $policyKey) { Remove-ItemProperty -Path $policyKey -Name 'DisableTaskMgr' -ErrorAction SilentlyContinue }
  }
}

# 3. Clean HKLM, scheduled tasks, and Run key
$hklmPolicy = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
if (Test-Path $hklmPolicy) { Remove-ItemProperty -Path $hklmPolicy -Name 'DisableTaskMgr' -ErrorAction SilentlyContinue }
schtasks.exe /delete /tn "SecureKioskInstantLaunch" /f 2>$null | Out-Null
schtasks.exe /delete /tn "SecureKioskDisarm" /f 2>$null | Out-Null

Write-Host "============================================================"
Write-Host "SUCCESS: Kiosk mode removed. Default Windows desktop (explorer.exe) restored."
Write-Host "Restart or sign out to return to normal desktop mode."
Write-Host "============================================================"
