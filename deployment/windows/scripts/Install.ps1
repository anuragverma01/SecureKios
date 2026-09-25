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

# 5. Lock down Ctrl+Alt+Del, Command Prompt, and Task Manager across Machine and User hives
$policiesToSet = @(
    @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'; Name = 'DisableTaskMgr'; Value = 1 },
    @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'; Name = 'DisableLockWorkstation'; Value = 1 },
    @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'; Name = 'DisableChangePassword'; Value = 1 },
    @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'; Name = 'HideFastUserSwitching'; Value = 1 },
    @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'; Name = 'NoLogoff'; Value = 1 },
    @{ Path = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'; Name = 'NoRun'; Value = 1 },
    @{ Path = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'; Name = 'DisableCMD'; Value = 2 },

    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System'; Name = 'DisableTaskMgr'; Value = 1 },
    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System'; Name = 'DisableLockWorkstation'; Value = 1 },
    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System'; Name = 'DisableChangePassword'; Value = 1 },
    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer'; Name = 'NoLogoff'; Value = 1 },
    @{ Path = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer'; Name = 'NoRun'; Value = 1 },
    @{ Path = 'HKCU:\Software\Policies\Microsoft\Windows\System'; Name = 'DisableCMD'; Value = 2 }
)

foreach ($p in $policiesToSet) {
    if (-not (Test-Path $p.Path)) { New-Item -Path $p.Path -Force | Out-Null }
    Set-ItemProperty -Path $p.Path -Name $p.Name -Value $p.Value -Type DWord -Force
}

if ($sid) {
    $userSystemPath = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System"
    if (-not (Test-Path $userSystemPath)) { New-Item -Path $userSystemPath -Force | Out-Null }
    Set-ItemProperty -Path $userSystemPath -Name 'DisableTaskMgr' -Value 1 -Type DWord -Force
    Set-ItemProperty -Path $userSystemPath -Name 'DisableLockWorkstation' -Value 1 -Type DWord -Force
    Set-ItemProperty -Path $userSystemPath -Name 'DisableChangePassword' -Value 1 -Type DWord -Force

    $userExplorerPath = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"
    if (-not (Test-Path $userExplorerPath)) { New-Item -Path $userExplorerPath -Force | Out-Null }
    Set-ItemProperty -Path $userExplorerPath -Name 'NoLogoff' -Value 1 -Type DWord -Force
    Set-ItemProperty -Path $userExplorerPath -Name 'NoRun' -Value 1 -Type DWord -Force

    $userCmdPath = "Registry::HKEY_USERS\$sid\Software\Policies\Microsoft\Windows\System"
    if (-not (Test-Path $userCmdPath)) { New-Item -Path $userCmdPath -Force | Out-Null }
    Set-ItemProperty -Path $userCmdPath -Name 'DisableCMD' -Value 2 -Type DWord -Force
}

# 6. Create Dedicated Elevated Disarm Script and Task (triggered upon authorized '5013' exit)
$kioskDir = Join-Path $env:ProgramData 'SecureKiosk'
if (-not (Test-Path $kioskDir)) { New-Item -ItemType Directory -Path $kioskDir -Force | Out-Null }

$disarmCmdPath = Join-Path $kioskDir 'disarm.cmd'
$sidLines = if ($sid) {
@"
reg delete "HKU\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v "DisableTaskMgr" /f >nul 2>&1
reg delete "HKU\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v "DisableLockWorkstation" /f >nul 2>&1
reg delete "HKU\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v "DisableChangePassword" /f >nul 2>&1
reg delete "HKU\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v "NoLogoff" /f >nul 2>&1
reg delete "HKU\$sid\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v "NoRun" /f >nul 2>&1
reg delete "HKU\$sid\Software\Policies\Microsoft\Windows\System" /v "DisableCMD" /f >nul 2>&1
"@
} else { "" }

$disarmContent = @"
@echo off
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v "DisableTaskMgr" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v "DisableLockWorkstation" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v "DisableChangePassword" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v "HideFastUserSwitching" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v "NoLogoff" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v "NoRun" /f >nul 2>&1
reg delete "HKLM\SOFTWARE\Policies\Microsoft\Windows\System" /v "DisableCMD" /f >nul 2>&1

reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v "DisableTaskMgr" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v "DisableLockWorkstation" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v "DisableChangePassword" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v "NoLogoff" /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v "NoRun" /f >nul 2>&1
reg delete "HKCU\Software\Policies\Microsoft\Windows\System" /v "DisableCMD" /f >nul 2>&1

$sidLines

schtasks /delete /tn "SecureKioskInstantLaunch" /f >nul 2>&1
exit /b 0
"@
Set-Content -Path $disarmCmdPath -Value $disarmContent -Encoding Ascii -Force

schtasks.exe /create /tn "SecureKioskDisarm" /tr "`"$disarmCmdPath`"" /sc once /st 00:00 /f /rl highest | Out-Null

# 7. Launch SecureKiosk immediately
Write-Host "Launching SecureKiosk..."
Start-Process "explorer.exe" "shell:AppsFolder\$appId"
Write-Host "SecureKiosk launched successfully."
