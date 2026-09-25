@echo off
setlocal
set "DIR=%~dp0"

if exist "%DIR%SecureKiosk.cer" (
    certutil.exe -addstore -f "TrustedPeople" "%DIR%SecureKiosk.cer" >nul 2>&1
    certutil.exe -addstore -f "Root" "%DIR%SecureKiosk.cer" >nul 2>&1
) else if exist "%DIR%SecureKiosk-Dev.cer" (
    certutil.exe -addstore -f "TrustedPeople" "%DIR%SecureKiosk-Dev.cer" >nul 2>&1
    certutil.exe -addstore -f "Root" "%DIR%SecureKiosk-Dev.cer" >nul 2>&1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference = 'SilentlyContinue'; Stop-Process -Name SecureKiosk.App -Force -ErrorAction SilentlyContinue; Add-AppxPackage -Path '%DIR%SecureKiosk.App_1.0.0.0_x64.msix' -ForceUpdateFromAnyVersion; $p = Get-AppxPackage -Name SecureKiosk; if ($p) { Start-Process 'explorer.exe' ('shell:AppsFolder\' + $p.PackageFamilyName + '!App') }"
exit /b 0
