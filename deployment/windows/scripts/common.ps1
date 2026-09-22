Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Administrator {
  $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
  if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from an elevated PowerShell session.' }
}

function Assert-SupportedEdition {
  $edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
  $supported = @('Enterprise','EnterpriseS','Education','IoTEnterprise','IoTEnterpriseS')
  if ($edition -notin $supported) { throw "SecureKiosk Shell Launcher provisioning requires Windows Enterprise/Education/IoT Enterprise. Detected: $edition" }
  $edition
}

function Get-AssignedAccessBridge {
  try { Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess' } catch { throw 'The Assigned Access MDM bridge is unavailable. Verify a supported Windows edition and run elevated.' }
}

function Get-UserSid([string]$UserName) {
  ([System.Security.Principal.NTAccount]$UserName).Translate([System.Security.Principal.SecurityIdentifier]).Value
}
