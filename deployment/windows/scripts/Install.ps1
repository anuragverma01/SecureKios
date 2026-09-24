# SecureKiosk Single-Click Installer & Launcher
# Elevate if not already administrator
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Elevating permissions to install certificate and package..."
    Start-Process powershell.exe -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# 1. Trust Certificate
$certFile = Get-ChildItem -Path $scriptDir -Filter "*.cer" -File -ErrorAction SilentlyContinue | Select-Object -First 1
if ($certFile) {
    Write-Host "Trusting certificate: $($certFile.Name)..."
    Import-Certificate -FilePath $certFile.FullName -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' -ErrorAction SilentlyContinue | Out-Null
    Import-Certificate -FilePath $certFile.FullName -CertStoreLocation 'Cert:\LocalMachine\Root' -ErrorAction SilentlyContinue | Out-Null
}

# 2. Install MSIX Package
$msixFile = Get-ChildItem -Path $scriptDir -Filter "*.msix" -File -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $msixFile) {
    Write-Error "No .msix package found in $scriptDir"
    exit 1
}

Write-Host "Installing package: $($msixFile.Name)..."
Add-AppxPackage -Path $msixFile.FullName -ForceUpdateFromAnyVersion

$pkg = Get-AppxPackage -Name 'SecureKiosk' -ErrorAction Stop
Write-Host "SecureKiosk installed successfully: $($pkg.PackageFullName)"

# 3. Optimize Windows Startup Delay to 0 ms so SecureKiosk opens instantly on reboot
$serializeKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize'
if (-not (Test-Path $serializeKey)) { New-Item -Path $serializeKey -Force | Out-Null }
Set-ItemProperty -Path $serializeKey -Name 'StartupDelayInMSec' -Value 0 -Type DWord -Force
Set-ItemProperty -Path $serializeKey -Name 'WaitForIdleState' -Value 0 -Type DWord -Force

# 4. Fast launch via Run key to execute immediately upon logon
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$appId = "$($pkg.PackageFamilyName)!App"
Set-ItemProperty -Path $runKey -Name 'SecureKioskFastLaunch' -Value "explorer.exe shell:AppsFolder\$appId" -Force

# 5. Disable Task Manager while kiosk mode is active
$policyKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System'
if (-not (Test-Path $policyKey)) { New-Item -Path $policyKey -Force | Out-Null }
Set-ItemProperty -Path $policyKey -Name 'DisableTaskMgr' -Value 1 -Type DWord -Force

# 6. Launch SecureKiosk immediately
Write-Host "Launching SecureKiosk..."
Start-Process "explorer.exe" "shell:AppsFolder\$appId"
Write-Host "SecureKiosk launched successfully."
