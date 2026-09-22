[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$CertificatePath,
  [Parameter(Mandatory)][string]$MsixPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Administrator {
  $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
  if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run this script from an elevated PowerShell session.' }
}

function Get-MsixManifestPublisher([string]$Path) {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
  try {
    $entry = $archive.GetEntry('AppxManifest.xml')
    if ($null -eq $entry) { throw 'The MSIX does not contain AppxManifest.xml.' }
    $reader = [System.IO.StreamReader]::new($entry.Open())
    try {
      [xml]$manifest = $reader.ReadToEnd()
      $identity = $manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Identity"]')
      if ($null -eq $identity -or [string]::IsNullOrWhiteSpace($identity.Publisher)) { throw 'The MSIX manifest Publisher is missing.' }
      return $identity.Publisher
    } finally { $reader.Dispose() }
  } finally { $archive.Dispose() }
}

function Test-CodeSigningEku([System.Security.Cryptography.X509Certificates.X509Certificate2]$Cert) {
  if ($Cert.PSObject.Properties['EnhancedKeyUsageList']) {
    return ($Cert.EnhancedKeyUsageList.ObjectId.Value -contains '1.3.6.1.5.5.7.3.3')
  }
  $ext = $Cert.Extensions['2.5.29.37']
  if ($null -ne $ext) {
    $eku = [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]$ext
    foreach ($usage in $eku.EnhancedKeyUsages) {
      if ($usage.Value -eq '1.3.6.1.5.5.7.3.3') { return $true }
    }
  }
  return $false
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This script must run on Windows.' }
Assert-Administrator
if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) { throw "Public certificate was not found: $CertificatePath" }
if (-not (Test-Path -LiteralPath $MsixPath -PathType Leaf)) { throw "MSIX package was not found: $MsixPath" }
if ($null -eq (Get-Command Import-Certificate -ErrorAction SilentlyContinue)) { throw 'Import-Certificate is unavailable. Install the Windows PKI PowerShell tools and retry.' }

$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $CertificatePath).Path)
$publisher = Get-MsixManifestPublisher ((Resolve-Path -LiteralPath $MsixPath).Path)
if ($certificate.Subject -cne $publisher) { throw 'Refusing to trust this certificate because its Subject does not exactly match the MSIX manifest Publisher.' }
if ($certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) { throw 'Refusing to trust an expired or not-yet-valid certificate.' }
if (-not (Test-CodeSigningEku $certificate)) { throw 'Refusing to trust a certificate that is not valid for code signing.' }

Import-Certificate -FilePath $CertificatePath -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Import-Certificate -FilePath $CertificatePath -CertStoreLocation Cert:\LocalMachine\Root -ErrorAction SilentlyContinue | Out-Null

$installed = Get-Item "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)" -ErrorAction SilentlyContinue
if ($null -eq $installed) {
  $installed = Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Thumbprint -eq $certificate.Thumbprint } | Select-Object -First 1
}
if ($null -eq $installed -or $installed.Subject -cne $publisher) { throw 'The intended development certificate was not found in LocalMachine\TrustedPeople after import.' }
Write-Host "Development certificate trusted for local MSIX testing: $($installed.Thumbprint)"
