[CmdletBinding()]
param([Parameter(Mandatory)][string]$Root)

$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'Sync-TestReleases.ps1'
$arguments = "-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File `"$script`" -Root `"$Root`""
$process = Start-Process -FilePath powershell.exe -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru `
    -RedirectStandardOutput (Join-Path $Root 'logs\sync.log') `
    -RedirectStandardError (Join-Path $Root 'logs\sync.error.log')
exit $process.ExitCode
