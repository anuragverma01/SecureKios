param([Parameter(Mandatory)][string]$ApplicationPath, [Parameter(Mandatory)][string]$KioskUser)
. "$PSScriptRoot\setup-kiosk.ps1" -ApplicationPath $ApplicationPath -KioskUser $KioskUser
Write-Host 'Shell configuration reapplied. Restart is required.'
