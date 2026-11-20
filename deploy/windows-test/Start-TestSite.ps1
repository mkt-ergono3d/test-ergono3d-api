[CmdletBinding()]
param([Parameter(Mandatory)][string]$Root)

$ErrorActionPreference = 'Continue'
$log = Join-Path $Root 'logs\supervisor.log'
[IO.File]::AppendAllText($log, "Supervisor started $([DateTimeOffset]::Now.ToString('o'))`r`n")
& node.exe (Join-Path $PSScriptRoot 'site.mjs') $Root *>> $log
exit $LASTEXITCODE
