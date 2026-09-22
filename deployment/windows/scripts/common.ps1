Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Administrator {
  $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
  if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from an elevated PowerShell session.' }
}

function Assert-SupportedEdition {
  $edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
  $supported = @('Enterprise','EnterpriseS','Education','IoTEnterprise','IoTEnterpriseS')
  if ($edition -notin $supported) {
    Write-Warning "Shell Launcher WMI bridge requires Windows Enterprise/Education/IoT Enterprise (Detected: $edition). Using per-user shell configuration."
  }
  $edition
}

function Get-AssignedAccessBridge {
  try { Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess' } catch { throw 'The Assigned Access MDM bridge is unavailable. Verify a supported Windows edition and run elevated.' }
}

function Get-UserSid([string]$UserName) {
  ([System.Security.Principal.NTAccount]$UserName).Translate([System.Security.Principal.SecurityIdentifier]).Value
}

function Set-RegistryKioskShell([string]$UserSid, [string]$ApplicationPath) {
  $key = "Registry::HKEY_USERS\$UserSid\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"
  if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
  Set-ItemProperty -Path $key -Name 'Shell' -Value "`"$ApplicationPath`"" -Force
  Write-Host "Configured per-user shell in registry: $key\Shell"
}

function Remove-RegistryKioskShell([string]$UserSid) {
  $key = "Registry::HKEY_USERS\$UserSid\Software\Microsoft\Windows NT\CurrentVersion\Winlogon"
  if (Test-Path $key) {
    Remove-ItemProperty -Path $key -Name 'Shell' -ErrorAction SilentlyContinue
    Write-Host "Removed per-user shell in registry: $key\Shell"
  }
}
