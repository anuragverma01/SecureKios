@echo off
setlocal
cd /d "%~dp0"
echo ===================================================
echo   Restoring Windows Normal Desktop Mode
echo ===================================================
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Disable-KioskMode.ps1"
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Operation failed with exit code %ERRORLEVEL%.
    pause
)
