@echo off
setlocal
cd /d "%~dp0"
echo ===================================================
echo   Installing SecureKiosk
echo ===================================================
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Installation failed with exit code %ERRORLEVEL%.
    pause
)
