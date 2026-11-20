[CmdletBinding()]
param([Parameter(Mandatory)][string]$Root)

$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Clear-TestEnvironment.ps1')
$log = Join-Path $Root 'logs\supervisor.log'
[IO.File]::AppendAllText($log, "Supervisor started $([DateTimeOffset]::Now.ToString('o'))`r`n")
$script = Join-Path $PSScriptRoot 'site.mjs'
$process = Start-Process -FilePath node.exe -ArgumentList "`"$script`" `"$Root`"" -WindowStyle Hidden -Wait -PassThru `
    -RedirectStandardOutput (Join-Path $Root 'logs\supervisor.output.log') `
    -RedirectStandardError (Join-Path $Root 'logs\supervisor.error.log')
exit $process.ExitCode
