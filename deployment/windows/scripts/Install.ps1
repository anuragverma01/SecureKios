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

# 3. Optimize Windows Startup Delay to 0 ms so apps open instantly
$currentUser = $env:USERNAME
try {
    $sid = ([System.Security.Principal.NTAccount]$currentUser).Translate([System.Security.Principal.SecurityIdentifier]).Value
} catch {
    $sid = $null
}

$serializeKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize'
if (-not (Test-Path $serializeKey)) { New-Item -Path $serializeKey -Force | Out-Null }
Set-ItemProperty -Path $serializeKey -Name 'StartupDelayInMSec' -Value 0 -Type DWord -Force
Set-ItemProperty -Path $serializeKey -Name 'WaitForIdleState' -Value 0 -Type DWord -Force

if ($sid) {
    $userSerializeKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize"
    if (-not (Test-Path $userSerializeKey)) { New-Item -Path $userSerializeKey -Force | Out-Null }
    Set-ItemProperty -Path $userSerializeKey -Name 'StartupDelayInMSec' -Value 0 -Type DWord -Force
    Set-ItemProperty -Path $userSerializeKey -Name 'WaitForIdleState' -Value 0 -Type DWord -Force
}

# 4. Instant Launch Task Scheduler (< 1s at logon) & Run Key
$appId = "$($pkg.PackageFamilyName)!App"
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runKey -Name 'SecureKioskFastLaunch' -Value "explorer.exe shell:AppsFolder\$appId" -Force

# Create Scheduled Task to trigger the exact second the user signs in (eliminates Explorer 30s idle delay)
$taskAction = "explorer.exe shell:AppsFolder\$appId"
schtasks.exe /create /tn "SecureKioskInstantLaunch" /tr "$taskAction" /sc onlogon /f /rl highest | Out-Null

# 5. Disable Task Manager in Host Registry (Machine and User hives)
$hklmPolicyKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
if (-not (Test-Path $hklmPolicyKey)) { New-Item -Path $hklmPolicyKey -Force | Out-Null }
Set-ItemProperty -Path $hklmPolicyKey -Name 'DisableTaskMgr' -Value 1 -Type DWord -Force

$hkcuPolicyKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System'
if (-not (Test-Path $hkcuPolicyKey)) { New-Item -Path $hkcuPolicyKey -Force | Out-Null }
Set-ItemProperty -Path $hkcuPolicyKey -Name 'DisableTaskMgr' -Value 1 -Type DWord -Force

if ($sid) {
    $userPolicyKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System"
    if (-not (Test-Path $userPolicyKey)) { New-Item -Path $userPolicyKey -Force | Out-Null }
    Set-ItemProperty -Path $userPolicyKey -Name 'DisableTaskMgr' -Value 1 -Type DWord -Force
}

# 6. Create Elevated Disarm Task (triggered upon authorized '5013' exit or recovery)
$disarmScript = "Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -Name 'DisableTaskMgr' -ErrorAction SilentlyContinue; Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System' -Name 'DisableTaskMgr' -ErrorAction SilentlyContinue; if ('$sid') { Remove-ItemProperty -Path 'Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System' -Name 'DisableTaskMgr' -ErrorAction SilentlyContinue }; schtasks.exe /delete /tn 'SecureKioskInstantLaunch' /f 2>`$null; Start-Process explorer.exe -ErrorAction SilentlyContinue"
$disarmTaskAction = "powershell.exe -NoProfile -ExecutionPolicy Bypass -Command `"$disarmScript`""
schtasks.exe /create /tn "SecureKioskDisarm" /tr "$disarmTaskAction" /sc once /st 00:00 /f /rl highest | Out-Null

# 7. Launch SecureKiosk immediately
Write-Host "Launching SecureKiosk..."
Start-Process "explorer.exe" "shell:AppsFolder\$appId"
Write-Host "SecureKiosk launched successfully."
