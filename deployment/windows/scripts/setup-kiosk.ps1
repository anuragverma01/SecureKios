param(
  [Parameter(Mandatory)][string]$KioskUser,
  [Parameter(Mandatory)][string]$ApplicationPath,
  [string]$ConfigurationPath = "$PSScriptRoot\..\shell-launcher\SecureKiosk.xml"
)
. "$PSScriptRoot\common.ps1"
Assert-Administrator; Assert-SupportedEdition | Out-Null
if (-not (Test-Path $ApplicationPath)) { throw "Application not found: $ApplicationPath" }
$sid = Get-UserSid $KioskUser
$xml = Get-Content -Raw -Path $ConfigurationPath
$shellPath = [System.Security.SecurityElement]::Escape(('"' + $ApplicationPath + '"'))
$xml = $xml.Replace('__SECUREKIOSK_PATH__', $shellPath).Replace('__KIOSK_USER_SID__', $sid)
$bridge = Get-AssignedAccessBridge
$bridge.ShellLauncher = [System.Net.WebUtility]::HtmlEncode($xml)
Set-CimInstance -CimInstance $bridge | Out-Null
Write-Host 'Shell Launcher configuration applied. Sign out or restart to activate the kiosk shell.'
Write-Host 'Keyboard Filter and application allowlisting must be configured and validated according to the administrator guide.'
