@echo off
setlocal
set "DIR=%~dp0"
certutil.exe -addstore -f "TrustedPeople" "%DIR%SecureKiosk-Dev.cer" >nul 2>&1
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Add-AppxPackage -Path '%DIR%SecureKiosk.App_1.0.0.0_x64.msix'; $p = Get-AppxPackage -Name SecureKiosk; if ($p) { Start-Process 'explorer.exe' ('shell:AppsFolder\' + $p.PackageFamilyName + '!App') }"
exit /b 0
