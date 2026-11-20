[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExpectedInstanceId,
    [Parameter(Mandatory)][string]$PortableDotnet
)

$ErrorActionPreference = 'Stop'
$identity = 'http://169.254.169.254/latest'
$token = Invoke-RestMethod -Uri "$identity/api/token" -Method Put -NoProxy -TimeoutSec 4 -Headers @{'X-aws-ec2-metadata-token-ttl-seconds'='60'}
$actual = Invoke-RestMethod -Uri "$identity/meta-data/instance-id" -NoProxy -TimeoutSec 4 -Headers @{'X-aws-ec2-metadata-token'=$token}
if ($actual -cne $ExpectedInstanceId) { throw 'This is a different EC2 instance.' }

$root = 'C:\ProgramData\Ergono3D\DeploymentTest'
$userName = 'ergono3d-test'
$siteTask = 'Ergono3D Deployment Test - Host'
$syncTask = 'Ergono3D Deployment Test - Sync'
if ((Test-Path -LiteralPath $root) -or (Get-LocalUser -Name $userName -ErrorAction SilentlyContinue) -or
    (Get-ScheduledTask -TaskName $siteTask,$syncTask -ErrorAction SilentlyContinue)) {
    throw 'One of the dedicated test resources already exists.'
}

$postgres = 'C:\Program Files\PostgreSQL\17\bin'
$sdk = (Resolve-Path -LiteralPath $PortableDotnet).Path
if (-not (Test-Path -LiteralPath (Join-Path $sdk 'dotnet.exe'))) { throw 'Portable .NET SDK is unavailable.' }
if (-not (Test-Path -LiteralPath (Join-Path $postgres 'initdb.exe'))) { throw 'PostgreSQL 17 binaries are unavailable.' }
foreach ($tool in 'node.exe','npm.cmd','git.exe','ssh.exe','ssh-keygen.exe','tar.exe') {
    [void](Get-Command $tool -ErrorAction Stop)
}

$accountPassword = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N') + '!aA7'
$local = New-LocalUser -Name $userName -Description 'Isolated Ergono3D deployment test' `
    -Password (ConvertTo-SecureString $accountPassword -AsPlainText -Force) -PasswordNeverExpires -UserMayNotChangePassword
Add-LocalGroupMember -SID ([System.Security.Principal.SecurityIdentifier]'S-1-5-32-545') -Member $local
& (Join-Path $PSScriptRoot 'Grant-TestBatchLogon.ps1') -Sid $local.SID
$sid = "*$($local.SID.Value)"
$account = "$env:COMPUTERNAME\$userName"

function Set-Access([string[]]$Arguments) {
    $null = & icacls.exe @Arguments
    if ($LASTEXITCODE -ne 0) { throw 'Unable to set permissions for the isolated test deployment.' }
}

$null = New-Item -ItemType Directory -Path $root
Set-Access @($root, '/inheritance:r', '/grant:r', '*S-1-5-32-544:(OI)(CI)(F)', '*S-1-5-18:(OI)(CI)(F)', "${sid}:(OI)(CI)(RX)")
foreach ($directory in 'build','cache','database','logs','pending','releases','state','tools','controller','private') {
    $location = Join-Path $root $directory
    $null = New-Item -ItemType Directory -Path $location
    if ($directory -notin 'tools','controller','private') { Set-Access @($location, '/grant:r', "${sid}:(OI)(CI)(M)") }
}

$private = Join-Path $root 'private'
$controller = Join-Path $root 'controller'
Copy-Item -LiteralPath $sdk -Destination (Join-Path $root 'tools\dotnet') -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'site.mjs'), (Join-Path $PSScriptRoot 'Sync-TestReleases.ps1'), `
    (Join-Path $PSScriptRoot 'Start-TestSite.ps1'), (Join-Path $PSScriptRoot 'Start-TestSync.ps1') -Destination $controller

$databasePassword = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$jwtKey = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
$runtime = @{
    webPort = 18080
    postgresBin = $postgres
    api = @{
        ConnectionStrings__Primary = "Host=127.0.0.1;Port=55432;Database=ergono_test;Username=ergono_test;Password=$databasePassword;Timeout=3"
        Jwt__Key = $jwtKey
        Jwt__Issuer = 'Ergono3D.DeploymentTest'
        Jwt__Audience = 'Ergono3D.DeploymentTest'
        INSOLE_SMTP_PASSWORD = ''
        INSOLE_AI_API_KEY = ''
        'Stripe-api-key' = ''
        'Stripe-webhook-key' = ''
        'GoogleLogin-ClientId' = ''
        RhinoCompute__LauncherPath = ''
        ApplicationUrl = 'http://127.0.0.1:18080'
        'Web-UI Application URL' = 'http://127.0.0.1:18080/app/'
    }
}
[IO.File]::WriteAllText((Join-Path $private 'runtime.json'), ($runtime | ConvertTo-Json -Depth 4))
[IO.File]::WriteAllText((Join-Path $private 'database-bootstrap'), "$databasePassword`n")
[IO.File]::WriteAllText((Join-Path $private 'NuGet.Config'), '<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
[IO.File]::WriteAllText((Join-Path $private 'npmrc'), '')

$env:GIT_TERMINAL_PROMPT = '0'
$env:GCM_INTERACTIVE = 'Never'
$registerKeys = @'
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const [privateDir, machine] = process.argv.slice(2);
const userAgent = 'Ergono3D-Windows-deployment-test';
(async () => {
  const meta = await fetch('https://api.github.com/meta', { headers: { 'User-Agent': userAgent } });
  if (!meta.ok) throw new Error('Unable to fetch GitHub SSH host keys');
  const hostKeys = (await meta.json()).ssh_keys;
  fs.writeFileSync(path.join(privateDir, 'known_hosts'), hostKeys.map(key => '[ssh.github.com]:443 ' + key).join('\n') + '\n');
  for (const type of ['app', 'landing']) {
    const repo = 'mkt-ergono3d/test-ergono3d-' + type;
    const file = path.join(privateDir, type + '.deploy');
    const sshKeygen = path.join(process.env.SystemRoot, 'System32', 'OpenSSH', 'ssh-keygen.exe');
    const key = spawnSync(sshKeygen, ['-q', '-t', 'ed25519', '-N', '', '-C', machine + '-' + type, '-f', file], { windowsHide: true });
    if (key.status !== 0) throw new Error('SSH deploy key generation failed for ' + type);
    const credential = spawnSync('git.exe', ['-c', 'http.sslBackend=openssl', '-c', 'credential.interactive=never', 'credential', 'fill'], {
      input: `protocol=https\nhost=github.com\npath=${repo}.git\n\n`, encoding: 'utf8', windowsHide: true
    });
    const authorization = /^password=(.+)$/m.exec(credential.stdout || '')?.[1]?.trim();
    if (credential.status !== 0 || !authorization) throw new Error('Authorized GitHub login unavailable');
    const response = await fetch('https://api.github.com/repos/' + repo + '/keys', {
      method: 'POST',
      headers: { 'User-Agent': userAgent, Accept: 'application/vnd.github+json', Authorization: 'Bearer ' + authorization, 'Content-Type': 'application/json' },
      body: JSON.stringify({ title: 'Ergono3D test ' + machine, key: fs.readFileSync(file + '.pub', 'utf8').trim(), read_only: true })
    });
    if (response.status !== 201) throw new Error('GitHub declined the read-only ' + type + ' deploy key: HTTP ' + response.status);
    console.log(type + ' read-only deploy key registered');
  }
})().catch(error => { console.error(error.message); process.exitCode = 1; });
'@
$registerKeys | & node.exe - $private $ExpectedInstanceId
if ($LASTEXITCODE -ne 0) { throw 'GitHub deploy key setup failed.' }
foreach ($type in 'app','landing') {
    $key = Join-Path $private "$type.deploy"
    Set-Access @($key, '/inheritance:r', '/grant:r', "${sid}:(R)", '*S-1-5-18:(F)', '*S-1-5-32-544:(F)')
    Set-Access @($key, '/setowner', $account)
    $creatorSid = [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    Set-Access @($key, '/remove:g', "*$creatorSid")
}

function Get-Action([string]$ScriptName) {
    $script = Join-Path $controller $ScriptName
    New-ScheduledTaskAction -Execute "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" `
        -Argument "-NoProfile -NonInteractive -ExecutionPolicy RemoteSigned -File `"$script`" -Root `"$root`"" -WorkingDirectory $root
}

$boot = New-ScheduledTaskTrigger -AtStartup
$repeat = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1)
$siteSettings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$syncSettings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 45) `
    -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
$null = Register-ScheduledTask -TaskName $siteTask -Action (Get-Action 'Start-TestSite.ps1') -Trigger $boot `
    -Settings $siteSettings -User $account -Password $accountPassword -RunLevel Limited
$null = Register-ScheduledTask -TaskName $syncTask -Action (Get-Action 'Start-TestSync.ps1') -Trigger @($boot, $repeat) `
    -Settings $syncSettings -User $account -Password $accountPassword -RunLevel Limited
Start-ScheduledTask -TaskName $siteTask
Start-ScheduledTask -TaskName $syncTask
Write-Output 'Installed isolated test tasks and a loopback-only test gateway.'
