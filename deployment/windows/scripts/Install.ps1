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

# 0. Unblock all extracted files to prevent Windows SmartScreen blocking
Get-ChildItem -Path $scriptDir -Recurse -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue

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

# Pre-authorize app in Windows Defender Firewall so it never prompts
try {
    $installLoc = $pkg.InstallLocation
    if ($installLoc) {
        $exe = Join-Path $installLoc "SecureKiosk.App.exe"
        if (Test-Path $exe) {
            Remove-NetFirewallRule -DisplayName "SecureKiosk*" -ErrorAction SilentlyContinue | Out-Null
            New-NetFirewallRule -DisplayName "SecureKiosk Inbound" -Program $exe -Direction Inbound -Action Allow -Profile Any -ErrorAction SilentlyContinue | Out-Null
            New-NetFirewallRule -DisplayName "SecureKiosk Outbound" -Program $exe -Direction Outbound -Action Allow -Profile Any -ErrorAction SilentlyContinue | Out-Null
        }
    }
} catch { }

# 3. Clean up any existing HKLM lockdown policies and elevated scheduled tasks
# Policies must ONLY reside in HKCU/HKU so the user app can cleanly remove them on exit!
$hklmSysPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
if (Test-Path $hklmSysPath) {
    Remove-ItemProperty -Path $hklmSysPath -Name 'DisableTaskMgr' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $hklmSysPath -Name 'DisableLockWorkstation' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $hklmSysPath -Name 'DisableChangePassword' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $hklmSysPath -Name 'HideFastUserSwitching' -ErrorAction SilentlyContinue
}
$hklmExpPath = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer'
if (Test-Path $hklmExpPath) {
    Remove-ItemProperty -Path $hklmExpPath -Name 'NoLogoff' -ErrorAction SilentlyContinue
    Remove-ItemProperty -Path $hklmExpPath -Name 'NoRun' -ErrorAction SilentlyContinue
}
$hklmCmdPath = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\System'
if (Test-Path $hklmCmdPath) {
    Remove-ItemProperty -Path $hklmCmdPath -Name 'DisableCMD' -ErrorAction SilentlyContinue
}
schtasks.exe /delete /tn "SecureKioskInstantLaunch" /f 2>$null | Out-Null
schtasks.exe /delete /tn "SecureKioskDisarm" /f 2>$null | Out-Null

$credFile = Join-Path $env:ProgramData 'SecureKiosk\credential.bin'
if (Test-Path $credFile) {
    Remove-Item -Path $credFile -Force -ErrorAction SilentlyContinue
}

# 4. Optimize Windows Startup Delay to 0 ms so apps open instantly
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

# 5. Fast Launch via HKCU Run Key (cleanly manageable and deletable by the app on exit)
$appId = "$($pkg.PackageFamilyName)!App"
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runKey -Name 'SecureKioskFastLaunch' -Value "explorer.exe shell:AppsFolder\$appId" -Force

if ($sid) {
    $userRunKey = "Registry::HKEY_USERS\$sid\Software\Microsoft\Windows\CurrentVersion\Run"
    if (-not (Test-Path $userRunKey)) { New-Item -Path $userRunKey -Force | Out-Null }
    Set-ItemProperty -Path $userRunKey -Name 'SecureKioskFastLaunch' -Value "explorer.exe shell:AppsFolder\$appId" -Force
}

# 6. Lock down Ctrl+Alt+Del, Command Prompt, and Task Manager strictly in User Hive (HKCU / HKU)
$policiesToSet = @(
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

# 7. Create Dedicated Disarm Script for emergency recovery
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
reg delete "HKU\$sid\Software\Microsoft\Windows\CurrentVersion\Run" /v "SecureKioskFastLaunch" /f >nul 2>&1
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
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Run" /v "SecureKioskFastLaunch" /f >nul 2>&1

$sidLines

schtasks /delete /tn "SecureKioskInstantLaunch" /f >nul 2>&1
exit /b 0
"@
Set-Content -Path $disarmCmdPath -Value $disarmContent -Encoding Ascii -Force

schtasks.exe /create /tn "SecureKioskDisarm" /tr "`"$disarmCmdPath`"" /sc once /st 00:00 /ru "SYSTEM" /rl highest /f | Out-Null

# Grant Authenticated Users permission to trigger SecureKioskDisarm without UAC prompt
try {
    $scheduler = New-Object -ComObject "Schedule.Service"
    $scheduler.Connect()
    $task = $scheduler.GetFolder("\").GetTask("SecureKioskDisarm")
    $sd = $task.GetSecurityDescriptor(0xF)
    if ($sd -notmatch 'AU') {
        $sd = $sd + "(A;;GRGX;;;AU)"
        $task.SetSecurityDescriptor($sd, 0)
    }
} catch { }

# 7. Launch SecureKiosk immediately
Write-Host "Launching SecureKiosk..."
Start-Process "explorer.exe" "shell:AppsFolder\$appId"
Write-Host "SecureKiosk launched successfully."
