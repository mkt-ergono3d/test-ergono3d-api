[CmdletBinding()]
param([Parameter(Mandatory)][string]$Root)

$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path -LiteralPath $Root).Path
$work = Join-Path $rootPath 'build'
$cache = Join-Path $rootPath 'cache'
$private = Join-Path $rootPath 'private'
$null = New-Item -ItemType Directory -Path $work, $cache -Force
$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'Never'
$env:DOTNET_CLI_HOME = Join-Path $cache 'dotnet'
$env:DOTNET_NOLOGO = 'true'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:NUGET_PACKAGES = Join-Path $cache 'nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $cache 'nuget-http'
$env:npm_config_cache = Join-Path $cache 'npm'
$env:npm_config_userconfig = Join-Path $private 'npmrc'
$sdk = Join-Path $rootPath 'tools\dotnet\dotnet.exe'

function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    $ErrorActionPreference = 'Continue'
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program exited with status $LASTEXITCODE" }
}

function Write-Pending([string]$Type, [string]$Commit) {
    $directory = Join-Path $rootPath 'pending'
    $null = New-Item -ItemType Directory -Path $directory -Force
    $file = Join-Path $directory "$Type.json"
    $temporary = "$file.$PID.tmp"
    [IO.File]::WriteAllText($temporary, (@{ commit = $Commit } | ConvertTo-Json -Compress))
    if (Test-Path -LiteralPath $file) { [IO.File]::Replace($temporary, $file, $null) }
    else { [IO.File]::Move($temporary, $file) }
}

function Build-Release([string]$Type, [string]$Source, [string]$Destination, [string]$Scratch) {
    Push-Location -LiteralPath $Source
    try {
        switch ($Type) {
            api {
                Invoke-Checked $sdk @('publish', 'InsoleApi.csproj', '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'true', '--configfile', (Join-Path $private 'NuGet.Config'), '--output', $Destination)
                & (Join-Path $Source 'deploy\Test-WindowsPublish.ps1') -PublishDirectory $Destination -ScratchDirectory (Join-Path $Scratch 'smoke')
            }
            app {
                $buildOutput = [IO.Path]::GetFullPath((Join-Path $Source '..\dist\app'))
                if ($buildOutput -ne (Join-Path $Scratch 'dist\app')) { throw 'Unexpected Vite build output' }
                Remove-Item Env:VITE_API_BASE, Env:VITE_GOOGLE_CLIENT_ID -ErrorAction SilentlyContinue
                Invoke-Checked 'npm.cmd' @('ci', '--no-audit', '--no-fund')
                Invoke-Checked 'npm.cmd' @('run', 'build', '--', '--mode', 'deployment-test')
                [IO.Directory]::Move($buildOutput, $Destination)
            }
            landing {
                Invoke-Checked 'npm.cmd' @('ci', '--no-audit', '--no-fund')
                foreach ($project in '3d-foot-fit', 'scripts/model-scale-and-cut', 'scripts/heel-inset') {
                    Invoke-Checked 'npm.cmd' @('--prefix', $project, 'ci', '--no-audit', '--no-fund')
                    Invoke-Checked 'npm.cmd' @('--prefix', $project, 'run', 'build')
                }
                Invoke-Checked 'npm.cmd' @('run', 'test:heel')
                $null = New-Item -ItemType Directory -Path $Destination
                Invoke-Checked 'node.exe' @('scripts/stage-test-site.mjs', (Join-Path $Destination 'public'))
                Copy-Item -LiteralPath 'package.json', 'package-lock.json', 'web-server.js', 'server' -Destination $Destination -Recurse
                Invoke-Checked 'npm.cmd' @('--prefix', $Destination, 'ci', '--omit=dev', '--no-audit', '--no-fund')
            }
        }
    } finally { Pop-Location }
}

$lock = $null
try { $lock = [IO.File]::Open((Join-Path $work 'sync.lock'), 'OpenOrCreate', 'ReadWrite', 'None') }
catch [IO.IOException] { return }

$failed = $false
try {
    foreach ($type in 'api', 'landing', 'app') {
        $scratch = $null
        try {
            $name = "mkt-ergono3d/test-ergono3d-$type"
            $store = Join-Path $cache "$type.git"
            $releaseRoot = Join-Path $rootPath "releases\$type"
            $null = New-Item -ItemType Directory -Path $releaseRoot -Force
            if (-not (Test-Path -LiteralPath $store)) { Invoke-Checked 'git.exe' @('init', '--bare', $store) }
            $gitOptions = @('-c', 'http.sslBackend=openssl', '-c', 'credential.helper=', '-C', $store)
            if ($type -eq 'api') {
                $remote = "https://github.com/$name.git"
            } else {
                $remote = "ssh://git@ssh.github.com:443/$name.git"
                $key = (Join-Path $private "$type.deploy").Replace('\', '/')
                $hosts = (Join-Path $private 'known_hosts').Replace('\', '/')
                $ssh = "$env:SystemRoot/System32/OpenSSH/ssh.exe".Replace('\', '/')
                $gitOptions += @('-c', "core.sshCommand='$ssh' -i '$key' -o IdentitiesOnly=yes -o BatchMode=yes -o StrictHostKeyChecking=yes -o UserKnownHostsFile='$hosts'")
            }
            Invoke-Checked 'git.exe' ($gitOptions + @('fetch', '--quiet', '--no-tags', '--depth=1', $remote, 'refs/heads/main'))
            $commit = (Invoke-Checked 'git.exe' ($gitOptions + @('rev-parse', 'FETCH_HEAD'))).Trim()
            if ($commit -cnotmatch '^[0-9a-f]{40}$') { throw 'Unexpected commit identifier' }
            $last = Join-Path $cache "$type.last-submitted"
            if ((Test-Path -LiteralPath $last) -and ([IO.File]::ReadAllText($last).Trim() -ceq $commit)) { continue }
            $installed = Join-Path $releaseRoot $commit
            if (-not (Test-Path -LiteralPath $installed)) {
                $scratch = Join-Path $work "$type-$([Guid]::NewGuid().ToString('N'))"
                $source = Join-Path $scratch 'src'
                $zip = Join-Path $scratch 'source.zip'
                $destination = Join-Path $scratch 'artifact'
                $null = New-Item -ItemType Directory -Path $source
                Invoke-Checked 'git.exe' ($gitOptions + @('archive', '--format=zip', "--output=$zip", $commit))
                Invoke-Checked 'tar.exe' @('-xf', $zip, '-C', $source)
                Build-Release $type $source $destination $scratch
                [IO.Directory]::Move($destination, $installed)
            }
            Write-Pending $type $commit
            [IO.File]::WriteAllText($last, $commit)
            Write-Output "Submitted $type $commit for activation"
        } catch {
            $failed = $true
            Write-Warning "$type sync failed: $($_.Exception.Message)"
        } finally {
            if ($scratch -and (Test-Path -LiteralPath $scratch)) {
                $actual = (Resolve-Path -LiteralPath $scratch).Path
                if ([IO.Path]::GetDirectoryName($actual) -eq (Resolve-Path -LiteralPath $work).Path) {
                    Remove-Item -LiteralPath $actual -Recurse -Force
                }
            }
        }
    }
} finally { $lock.Dispose() }
if ($failed) { exit 1 }
