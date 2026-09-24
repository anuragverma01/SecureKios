@echo off
setlocal
cd /d "%~dp0"
echo ===================================================
echo   Activating SecureKiosk Dedicated Kiosk Mode
echo ===================================================
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Enable-KioskMode.ps1"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Kiosk activation failed with exit code %ERRORLEVEL%.
    pause
)
