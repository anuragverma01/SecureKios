. "$PSScriptRoot\common.ps1"
Assert-Administrator

$edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
$supportedEnterprise = @('Enterprise','EnterpriseS','Education','IoTEnterprise','IoTEnterpriseS')

$pkg = Get-AppxPackage -Name 'SecureKiosk' -ErrorAction SilentlyContinue | Select-Object -First 1
$credFile = Join-Path $env:ProgramData 'SecureKiosk\credential.bin'
$hasCred = Test-Path $credFile

$bridge = $null
$hasShellLauncher = $false
try {
  $bridge = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess' -ErrorAction SilentlyContinue
  if ($bridge -and $bridge.ShellLauncher) { $hasShellLauncher = $true }
} catch {}

$regShells = @()
Get-ChildItem 'Registry::HKEY_USERS' | Where-Object { $_.PSChildName -like 'S-1-5-21-*' } | ForEach-Object {
  $key = Join-Path $_.PSPath 'Software\Microsoft\Windows NT\CurrentVersion\Winlogon'
  if (Test-Path $key) {
    $sh = (Get-ItemProperty -Path $key -Name 'Shell' -ErrorAction SilentlyContinue).Shell
    if ($sh) { $regShells += [pscustomobject]@{ UserSid = $_.PSChildName; Shell = $sh } }
  }
}

Write-Host "=== SecureKiosk Environment Validation ==="
Write-Host "Windows Edition            : $edition"
Write-Host "Shell Launcher Supported   : $($edition -in $supportedEnterprise)"
Write-Host "SecureKiosk MSIX Installed : $([bool]$pkg)"
if ($pkg) {
  Write-Host "Package Full Name          : $($pkg.PackageFullName)"
  Write-Host "Install Location           : $($pkg.InstallLocation)"
}
Write-Host "Admin Credential Provisioned: $hasCred"
Write-Host "Shell Launcher Configured  : $hasShellLauncher"
Write-Host "Registry Shells Configured : $($regShells.Count)"
if ($regShells.Count -gt 0) {
  $regShells | Format-Table -AutoSize | Out-String | Write-Host
}
Write-Host "=========================================="
