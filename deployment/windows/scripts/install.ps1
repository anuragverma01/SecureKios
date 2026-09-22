[CmdletBinding()]
param(
  [Parameter()][string]$MsixPath,
  [string]$CertificatePath,
  [string]$KioskUser = $env:USERNAME
)

. "$PSScriptRoot\common.ps1"
Assert-Administrator

# 1. Locate MSIX if not provided
if ([string]::IsNullOrWhiteSpace($MsixPath)) {
  $candidate = Get-ChildItem -Path (Join-Path $PSScriptRoot '..\..\..\artifacts') -Filter '*.msix' -File -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($candidate) {
    $MsixPath = $candidate.FullName
  } else {
    throw 'Specify -MsixPath with the path to the SecureKiosk MSIX package.'
  }
}

if (-not (Test-Path -LiteralPath $MsixPath -PathType Leaf)) {
  throw "MSIX package not found: $MsixPath"
}

# 2. Trust development certificate if provided or found
if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
  $cerCandidate = Join-Path (Split-Path -Parent $MsixPath) 'SecureKiosk-Dev.cer'
  if (Test-Path $cerCandidate) { $CertificatePath = $cerCandidate }
}

if (-not [string]::IsNullOrWhiteSpace($CertificatePath) -and (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) {
  Write-Host "Trusting development certificate: $CertificatePath"
  & "$PSScriptRoot\trust-dev-certificate.ps1" -CertificatePath $CertificatePath -MsixPath $MsixPath
}

# 3. Install the MSIX package
Write-Host "Installing SecureKiosk MSIX: $MsixPath"
Add-AppxPackage -Path $MsixPath -ForceUpdateFromAnyVersion

$pkg = Get-AppxPackage -Name 'SecureKiosk' -ErrorAction Stop
Write-Host "SecureKiosk successfully installed: $($pkg.PackageFullName)"

# 4. Configure kiosk mode if requested
if ($KioskUser) {
  & "$PSScriptRoot\setup-kiosk.ps1" -KioskUser $KioskUser
}
