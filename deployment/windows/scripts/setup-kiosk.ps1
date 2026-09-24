[CmdletBinding()]
param(
  [Parameter()][string]$KioskUser = $env:USERNAME,
  [Parameter()][string]$ApplicationPath,
  [string]$ConfigurationPath = "$PSScriptRoot\..\shell-launcher\SecureKiosk.xml"
)

. "$PSScriptRoot\common.ps1"
Assert-Administrator

# Auto-detect application path from installed MSIX if not explicitly passed
if ([string]::IsNullOrWhiteSpace($ApplicationPath)) {
  $pkg = Get-AppxPackage -Name 'SecureKiosk' -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($pkg -and (Test-Path (Join-Path $pkg.InstallLocation 'SecureKiosk.App.exe'))) {
    $ApplicationPath = Join-Path $pkg.InstallLocation 'SecureKiosk.App.exe'
  } else {
    $alias = Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\SecureKiosk.exe'
    if (Test-Path $alias) {
      $ApplicationPath = $alias
    } else {
      throw 'SecureKiosk MSIX package is not installed. Install the SecureKiosk MSIX first, or specify -ApplicationPath.'
    }
  }
}

if (-not (Test-Path $ApplicationPath)) { throw "Application executable not found: $ApplicationPath" }

# Validate administrator credential in ProgramData
$credFile = Join-Path $env:ProgramData 'SecureKiosk\credential.bin'
if (-not (Test-Path $credFile)) {
  Write-Host "No administrator exit credential provisioned. Launching exit code setup..."
  & $ApplicationPath --provision-exit-code
  if (-not (Test-Path $credFile)) {
    throw 'Refusing to enable kiosk mode because no administrator exit credential was provisioned.'
  }
}

$sid = Get-UserSid $KioskUser
$edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
$supportedEnterprise = @('Enterprise','EnterpriseS','Education','IoTEnterprise','IoTEnterpriseS')

if ($edition -in $supportedEnterprise) {
  try {
    $xml = Get-Content -Raw -Path $ConfigurationPath
    $shellPath = [System.Security.SecurityElement]::Escape(('"' + $ApplicationPath + '"'))
    $xml = $xml.Replace('__SECUREKIOSK_PATH__', $shellPath).Replace('__KIOSK_USER_SID__', $sid)
    $bridge = Get-AssignedAccessBridge
    $bridge.ShellLauncher = [System.Net.WebUtility]::HtmlEncode($xml)
    Set-CimInstance -CimInstance $bridge | Out-Null
    Write-Host "Shell Launcher v2 configuration applied for '$KioskUser'."
  } catch {
    Write-Warning "Shell Launcher WMI bridge failed: $($_.Exception.Message). Falling back to per-user Winlogon shell configuration."
    Set-RegistryKioskShell -UserSid $sid -ApplicationPath $ApplicationPath
  }
} else {
  Write-Host "Windows edition '$edition' uses per-user Winlogon shell configuration."
  Set-RegistryKioskShell -UserSid $sid -ApplicationPath $ApplicationPath
}

# Disable Task Manager for the kiosk user and system in host registry
$policyKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System"
if (-not (Test-Path $policyKey)) { New-Item -Path $policyKey -Force | Out-Null }
Set-ItemProperty -Path $policyKey -Name 'DisableTaskMgr' -Value 1 -Type DWord -Force

$hklmPolicy = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
if (-not (Test-Path $hklmPolicy)) { New-Item -Path $hklmPolicy -Force | Out-Null }
Set-ItemProperty -Path $hklmPolicy -Name 'DisableTaskMgr' -Value 1 -Type DWord -Force
Write-Host "Task Manager disabled for '$KioskUser' and system."

Write-Host "============================================================"
Write-Host "SUCCESS: SecureKiosk is now configured as the dedicated shell for '$KioskUser'."
Write-Host "Sign out or restart the machine to activate kiosk mode."
Write-Host "============================================================"
