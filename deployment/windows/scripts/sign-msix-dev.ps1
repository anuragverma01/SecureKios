[CmdletBinding()]
param(
  [string]$MsixPath,
  [string]$ManifestPath = (Join-Path $PSScriptRoot '..\..\..\src\SecureKiosk.App\Package.appxmanifest'),
  [string]$CertificateOutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ManifestPublisher([string]$Path) {
  [xml]$manifest = Get-Content -LiteralPath $Path -Raw
  $identity = $manifest.SelectSingleNode('/*[local-name()="Package"]/*[local-name()="Identity"]')
  if ($null -eq $identity -or [string]::IsNullOrWhiteSpace($identity.Publisher)) { throw 'The package manifest Publisher is missing.' }
  return $identity.Publisher
}

function Get-CodeSigningCertificate([string]$Publisher) {
  $certificate = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -ceq $Publisher -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
    Where-Object { $_.EnhancedKeyUsageList.ObjectId.Value -contains '1.3.6.1.5.5.7.3.3' } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1
  if ($null -ne $certificate) { return $certificate }
  return New-SelfSignedCertificate -Type CodeSigningCert -Subject $Publisher -CertStoreLocation Cert:\CurrentUser\My -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyUsage DigitalSignature -NotAfter (Get-Date).AddYears(1) -FriendlyName 'SecureKiosk DEVELOPMENT MSIX signing'
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

function Get-SignTool {
  $signTool = Get-Command signtool.exe -ErrorAction SilentlyContinue
  if ($null -ne $signTool) { return $signTool }

  $kitsRoots = @(
    "${env:ProgramFiles(x86)}\Windows Kits\10\bin",
    "$env:ProgramFiles\Windows Kits\10\bin"
  )
  try {
    $reg = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -ErrorAction SilentlyContinue).KitsRoot10
    if ($reg) { $kitsRoots += (Join-Path $reg 'bin') }
  } catch {}

  foreach ($root in $kitsRoots) {
    if (Test-Path $root) {
      $found = Get-ChildItem -Path $root -Filter 'signtool.exe' -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]x64[\\/]signtool\.exe$' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
      if ($found) {
        return [pscustomobject]@{ Source = $found.FullName }
      }
    }
  }
  return $null
}

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This script must run on Windows.' }
if ([string]::IsNullOrWhiteSpace($MsixPath)) {
  $repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
  $packages = @(Get-ChildItem -LiteralPath (Join-Path $repositoryRoot 'artifacts') -File -Filter '*.msix' -Recurse -ErrorAction SilentlyContinue)
  if ($packages.Count -ne 1) { throw 'Specify -MsixPath because exactly one generated .msix was not found under artifacts.' }
  $MsixPath = $packages[0].FullName
}
if (-not (Test-Path -LiteralPath $MsixPath -PathType Leaf)) { throw "MSIX package was not found: $MsixPath" }
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) { throw "Package manifest was not found: $ManifestPath" }
$signTool = Get-SignTool
if ($null -eq $signTool) { throw 'signtool.exe was not found. Install the Windows SDK Signing Tools feature and retry.' }
foreach ($commandName in @('New-SelfSignedCertificate', 'Export-Certificate')) {
  if ($null -eq (Get-Command $commandName -ErrorAction SilentlyContinue)) { throw "$commandName is unavailable. Install the Windows PKI PowerShell tools and retry." }
}

$resolvedMsix = (Resolve-Path -LiteralPath $MsixPath).Path
$publisher = Get-ManifestPublisher $ManifestPath
$packagePublisher = Get-MsixManifestPublisher $resolvedMsix
if ($packagePublisher -cne $publisher) { throw 'The generated MSIX Publisher does not exactly match the source manifest Publisher.' }
$certificate = Get-CodeSigningCertificate $publisher
if ($certificate.Subject -cne $publisher) { throw 'The development certificate Subject does not exactly match the manifest Publisher.' }

& $signTool.Source sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $resolvedMsix
if ($LASTEXITCODE -ne 0) { throw "MSIX signing failed with exit code $LASTEXITCODE." }

if ([string]::IsNullOrWhiteSpace($CertificateOutputPath)) {
  $CertificateOutputPath = Join-Path (Split-Path -Parent $resolvedMsix) 'SecureKiosk-development.cer'
}
Export-Certificate -Cert $certificate -FilePath $CertificateOutputPath -Force | Out-Null
if (-not (Test-Path -LiteralPath $CertificateOutputPath -PathType Leaf)) { throw 'MSIX was signed but the public development certificate could not be exported.' }

Write-Host "MSIX signed with development certificate. Public certificate: $CertificateOutputPath"
Write-Host 'For local test installation, import only this .cer into LocalMachine\TrustedPeople using trust-dev-certificate.ps1.'
