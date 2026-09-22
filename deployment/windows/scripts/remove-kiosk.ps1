. "$PSScriptRoot\common.ps1"
Assert-Administrator
$bridge = Get-AssignedAccessBridge
$bridge.ShellLauncher = $null
Set-CimInstance -CimInstance $bridge | Out-Null
Write-Host 'Shell Launcher configuration removed. Restart or sign out before uninstalling the application.'
