[CmdletBinding()]
param(
  [Parameter(Mandatory)][string]$MsixPath,
  [Parameter(Mandatory)][string]$CertificatePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-MsixManifestPublisher([string]$Path) {
  Add-Type -AssemblyName System.IO.Compression.FileSystem
  $archive = [System.IO.Compression.ZipFile]::OpenRead($Path)
  try {
    $entry = $archive.GetEntry('AppxManifest.xml')
    if ($null -eq $entry) { throw 'The MSIX does not contain AppxManifest.xml.' }
    if ($null -eq $archive.GetEntry('AppxSignature.p7x')) { throw 'The MSIX does not contain an AppxSignature.p7x package signature.' }
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
function Test-CodeSigningEku($Cert) {
  try {
    if ($Cert.PSObject.Properties['EnhancedKeyUsageList'] -and $Cert.EnhancedKeyUsageList) {
      return ($Cert.EnhancedKeyUsageList.ObjectId.Value -contains '1.3.6.1.5.5.7.3.3')
    }
  } catch {}

  try {
    foreach ($ext in $Cert.Extensions) {
      if ($ext.Oid -and $ext.Oid.Value -eq '2.5.29.37') {
        $formatted = $ext.Format($false)
        if ($formatted -match '1\.3\.6\.1\.5\.5\.7\.3\.3|Code Signing') {
          return $true
        }
        try {
          $typedEku = [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($ext, $ext.Critical)
          foreach ($u in $typedEku.EnhancedKeyUsages) {
            if ($u.Value -eq '1.3.6.1.5.5.7.3.3') { return $true }
          }
        } catch {}
      }
    }
  } catch {}
  return $false
}

if (-not (Test-Path -LiteralPath $MsixPath -PathType Leaf)) { throw "MSIX package was not found: $MsixPath" }
if (-not (Test-Path -LiteralPath $CertificatePath -PathType Leaf)) { throw "Public certificate was not found: $CertificatePath" }
$signTool = Get-SignTool
if ($null -eq $signTool) { throw 'signtool.exe was not found. Install the Windows SDK Signing Tools feature and retry.' }

$resolvedMsix = (Resolve-Path -LiteralPath $MsixPath).Path
$manifestPublisher = Get-MsixManifestPublisher $resolvedMsix
$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path -LiteralPath $CertificatePath).Path)
if ($certificate.Subject -cne $manifestPublisher) { throw 'Certificate Subject does not exactly match the MSIX manifest Publisher.' }
if ($certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)) { throw 'The development certificate is not currently valid.' }
if (-not (Test-CodeSigningEku $certificate)) { throw 'The certificate is not a code-signing certificate.' }

& $signTool.Source verify /pa /v $resolvedMsix
if ($LASTEXITCODE -ne 0) { throw "MSIX signature verification failed with exit code $LASTEXITCODE. Trust the intended development certificate before verifying local test packages." }

Write-Host "Signature is valid. Manifest Publisher: $manifestPublisher"
Write-Host "Certificate Subject: $($certificate.Subject)"
Write-Host "Certificate validity: $($certificate.NotBefore:u) to $($certificate.NotAfter:u)"
