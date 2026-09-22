param(
  [Parameter(Mandatory)][string]$PublishRoot,
  [Parameter(Mandatory)][string]$KioskUser,
  [string]$InstallRoot = "$env:ProgramFiles\SecureKiosk"
)
. "$PSScriptRoot\common.ps1"
Assert-Administrator; Assert-SupportedEdition | Out-Null
if (-not (Test-Path (Join-Path $PublishRoot 'SecureKiosk.App.exe'))) { throw "Signed publish output was not found at $PublishRoot" }
New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
Copy-Item -Path (Join-Path $PublishRoot '*') -Destination $InstallRoot -Recurse -Force
& "$PSScriptRoot\setup-kiosk.ps1" -KioskUser $KioskUser -ApplicationPath (Join-Path $InstallRoot 'SecureKiosk.App.exe')
Write-Host "SecureKiosk installed and Shell Launcher configured at $InstallRoot. Restart/sign out to activate the kiosk shell."
Write-Host 'This script intentionally does not create a Startup-folder shortcut; Shell Launcher owns startup after sign-in.'
