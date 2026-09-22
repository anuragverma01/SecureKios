. "$PSScriptRoot\common.ps1"
Assert-Administrator; $edition = Assert-SupportedEdition
$bridge = Get-AssignedAccessBridge
[pscustomobject]@{ Edition = $edition; ShellLauncherConfigured = [bool]$bridge.ShellLauncher; ConsoleSession = [Environment]::UserInteractive }
Write-Host 'Validation is informational. Test startup, escape paths, keyboard policy, crash recovery, and recovery on the target Windows image.'
