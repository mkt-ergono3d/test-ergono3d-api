[CmdletBinding()]
param([Parameter(Mandatory)][string]$Root)

$ErrorActionPreference = 'Stop'
$log = Join-Path $Root 'logs\sync.log'
& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File (Join-Path $PSScriptRoot 'Sync-TestReleases.ps1') -Root $Root *> $log
exit $LASTEXITCODE
