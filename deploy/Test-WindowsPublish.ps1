[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)][string]$ScratchDirectory
)

$ErrorActionPreference = 'Stop'
if (-not ('System.Net.Http.HttpClient' -as [type])) {
    Add-Type -AssemblyName System.Net.Http
}
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$scratch = [IO.Path]::GetFullPath($ScratchDirectory)
[void][IO.Directory]::CreateDirectory($scratch)

$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
try {
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
} finally {
    $listener.Stop()
}

$settings = @{
    ASPNETCORE_ENVIRONMENT = 'Test'
    DOTNET_ENVIRONMENT = 'Test'
    ASPNETCORE_URLS = "http://127.0.0.1:$port"
    Deployment__LogDirectory = (Join-Path $scratch 'logs')
    ConnectionStrings__Primary = 'Host=127.0.0.1;Port=1;Database=smoke;Username=smoke;Timeout=1'
    Jwt__Key = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    INSOLE_SMTP_PASSWORD = $null
    INSOLE_AI_API_KEY = $null
}
$previous = @{}
$process = $null
$http = $null
try {
    foreach ($key in $settings.Keys) {
        $previous[$key] = [Environment]::GetEnvironmentVariable($key)
        [Environment]::SetEnvironmentVariable($key, $settings[$key])
    }
    $process = Start-Process -FilePath (Join-Path $publish 'InsoleApi.exe') -WorkingDirectory $publish `
        -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $scratch 'api.stdout.log') `
        -RedirectStandardError (Join-Path $scratch 'api.stderr.log')

    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseProxy = $false
    $http = [Net.Http.HttpClient]::new($handler)
    $http.Timeout = [TimeSpan]::FromSeconds(3)
    $origin = "http://127.0.0.1:$port"
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    $healthy = $false
    while (-not $process.HasExited -and [DateTime]::UtcNow -lt $deadline) {
        try {
            $response = $http.GetAsync("$origin/health").GetAwaiter().GetResult()
            try {
                $healthy = $response.StatusCode -eq 200 -and $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() -ceq 'OK'
            } finally {
                $response.Dispose()
            }
            if ($healthy) { break }
        } catch [Net.Http.HttpRequestException] {
            # Kestrel may still be starting.
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $healthy) { throw 'The published API failed its /health probe.' }

    $ready = $http.GetAsync("$origin/health/ready").GetAwaiter().GetResult()
    try {
        if ($ready.StatusCode -ne 503) { throw 'The API should report HTTP 503 when the test database is unreachable.' }
    } finally {
        $ready.Dispose()
    }
    Write-Host 'API smoke tests passed (/health 200 and /health/ready 503 with an unreachable database).'
} finally {
    if ($http) { $http.Dispose() }
    if ($process) {
        if (-not $process.HasExited) { Stop-Process -InputObject $process }
        $process.Dispose()
    }
    foreach ($key in $previous.Keys) { [Environment]::SetEnvironmentVariable($key, $previous[$key]) }
}
