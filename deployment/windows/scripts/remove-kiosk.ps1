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

# 2. Reset Registry per-user shell
if ($KioskUser) {
  $sid = Get-UserSid $KioskUser
  Remove-RegistryKioskShell -UserSid $sid
} else {
  # Clean across all user hives
  Get-ChildItem 'Registry::HKEY_USERS' | Where-Object { $_.PSChildName -like 'S-1-5-21-*' } | ForEach-Object {
    Remove-RegistryKioskShell -UserSid $_.PSChildName
  }
}

Write-Host "============================================================"
Write-Host "SUCCESS: Kiosk mode removed. Default Windows desktop (explorer.exe) restored."
Write-Host "Restart or sign out to return to normal desktop mode."
Write-Host "============================================================"
