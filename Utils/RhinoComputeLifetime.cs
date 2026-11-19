using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace InsoleApi.Utils;

public sealed class RhinoComputeLifetime : IAsyncDisposable
{
    public const string BaseUrl = "http://localhost:6500";
    private const int Port = 6500;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly Regex MissingStl = new(
        @"FileNotFoundException.{0,1024}?Could not find file[^\r\n]*\.stl\b",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

    private readonly string? _launcherPath;
    private readonly string? _runtimeLogPath;
    private readonly ILogger<RhinoComputeLifetime> _logger;
    private readonly CancellationToken _applicationStopping;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly Timer _idleTimer;
    private Process? _process;
    private RhinoProcessJob? _job;
    private volatile string _state = "standby";
    private long _lastJobFinished;
    private bool _disposed;

    public RhinoComputeLifetime(
        IConfiguration configuration,
        IHostApplicationLifetime applicationLifetime,
        ILogger<RhinoComputeLifetime> logger)
    {
        var launcher = configuration["RhinoCompute:LauncherPath"];
        if (!string.IsNullOrWhiteSpace(launcher))
        {
            _launcherPath = Path.GetFullPath(launcher);
            _runtimeLogPath = Path.Combine(Path.GetDirectoryName(_launcherPath)!, "rhino-runtime.log");
        }

        _applicationStopping = applicationLifetime.ApplicationStopping;
        _logger = logger;
        _idleTimer = new Timer(state => _ = StopIfIdleAsync());
    }

    public bool Enabled => _launcherPath is not null;

    public string State
    {
        get
        {
            if (!_gate.Wait(0))
                return _state;

            try
            {
                return _state == "running" && _process?.HasExited == true ? "unavailable" : _state;
            }
            finally
            {
                _gate.Release();
            }
        }
    }

    public async Task<HttpResponseMessage> SendAsync(HttpClient client, string endpoint)
    {
        if (!Enabled)
            return await client.GetAsync(endpoint);

        await _gate.WaitAsync(_applicationStopping);
        try
        {
            _applicationStopping.ThrowIfCancellationRequested();
            _idleTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            for (var attempt = 0; ; attempt++)
            {
                Process process;
                try
                {
                    process = await EnsureRunningAsync(client);
                }
                catch (RhinoStartupException ex) when (attempt == 0 && !_applicationStopping.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "Rhino.Compute did not finish starting. Retrying the STL job once.");
                    continue;
                }

                using var worker = _job!.FindGeometry();
                var logOffset = GetRuntimeLogLength();
                HttpResponseMessage response;
                try
                {
                    response = await SendWhileRunningAsync(client, endpoint, process, worker, _applicationStopping);
                }
                catch (Exception ex) when (
                    !_applicationStopping.IsCancellationRequested &&
                    ex is HttpRequestException or OperationCanceledException)
                {
                    if (!await IsComputeUnavailableAsync(client, process, worker) && !HasMissingStl(logOffset))
                        throw;

                    await StopForRecoveryAsync("Rhino lost the connection during an STL job");
                    if (attempt > 0)
                        throw;

                    continue;
                }

                try
                {
                    var missingStl = HasMissingStl(logOffset);
                    var workerUnavailable = (int)response.StatusCode >= 500 &&
                        await IsComputeUnavailableAsync(client, process, worker);
                    if (!missingStl && !workerUnavailable && !process.HasExited && worker?.HasExited != true)
                        return response;

                    await StopForRecoveryAsync(missingStl ? "Rhino reported a missing STL" : "Rhino became unavailable");
                    if (attempt > 0)
                        return response;
                }
                catch
                {
                    response.Dispose();
                    throw;
                }

                response.Dispose();
            }
        }
        finally
        {
            if (_process is not null && !_applicationStopping.IsCancellationRequested)
            {
                _lastJobFinished = Stopwatch.GetTimestamp();
                _idleTimer.Change(IdleTimeout, Timeout.InfiniteTimeSpan);
            }

            _gate.Release();
        }
    }

    private static async Task<HttpResponseMessage> SendWhileRunningAsync(
        HttpClient client, string endpoint, Process process, Process? worker, CancellationToken cancellationToken)
    {
        using var execution = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var requestFinished = new CancellationTokenSource();
        var watchExit = CancelRequestOnExitAsync(process);
        if (worker is not null)
            watchExit = Task.WhenAll(watchExit, CancelRequestOnExitAsync(worker));

        try
        {
            return await client.GetAsync(endpoint, execution.Token);
        }
        finally
        {
            requestFinished.Cancel();
            await watchExit;
        }

        async Task CancelRequestOnExitAsync(Process watched)
        {
            try
            {
                await watched.WaitForExitAsync(requestFinished.Token);
                execution.Cancel();
            }
            catch (OperationCanceledException) when (requestFinished.IsCancellationRequested)
            {
            }
        }
    }

    private async Task<Process> EnsureRunningAsync(HttpClient client)
    {
        if (_job is not null && _process is { HasExited: false } running)
            return running;

        await StopProcessAsync();
        _state = "starting";
        try
        {
            if (IsPortListening())
                throw new InvalidOperationException($"Port {Port} is already in use. Stop the previous Rhino launcher and its process before enabling on-demand Rhino.");

            if (!OperatingSystem.IsWindows() || !File.Exists(_launcherPath))
                throw new InvalidOperationException("RhinoCompute:LauncherPath must point to the Rhino launcher BAT on Windows.");

            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = "/d /s /v:off /c \"\"%INSOLE_RHINO_LAUNCHER%\" --run\"",
                WorkingDirectory = Path.GetDirectoryName(_launcherPath)!,
                UseShellExecute = false,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };
            start.Environment["INSOLE_RHINO_LAUNCHER"] = _launcherPath;
            _job = new RhinoProcessJob();
            _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Rhino launcher.");
            using (var input = _process.StandardInput)
            {
                _job.Add(_process);
                await input.WriteLineAsync("start");
            }
            _logger.LogInformation("Starting Rhino.Compute for an STL job. Launcher PID {Pid}", _process.Id);

            await WaitForGeometryAsync(client, _process);
            _state = "running";
            return _process;
        }
        catch
        {
            _state = "unavailable";
            await StopProcessAsync();
            throw;
        }
    }

    private async Task WaitForGeometryAsync(HttpClient client, Process process)
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(_applicationStopping);
        startup.CancelAfter(StartupTimeout);
        Process? worker = null;
        try
        {
            while (true)
            {
                startup.Token.ThrowIfCancellationRequested();
                worker ??= _job!.FindGeometry();
                var ready = worker is not null && !worker.HasExited && !process.HasExited &&
                    await ProbeGeometryAsync(client, process, worker, startup.Token);

                if (process.HasExited)
                    throw new RhinoStartupException($"The Rhino.Compute launcher exited during startup. See {_runtimeLogPath}.");

                if (worker?.HasExited == true)
                    throw new RhinoStartupException($"The compute.geometry worker exited during startup. See {_runtimeLogPath}.");

                if (ready)
                    return;

                await Task.Delay(TimeSpan.FromSeconds(1), startup.Token);
            }
        }
        catch (OperationCanceledException ex) when (startup.IsCancellationRequested && !_applicationStopping.IsCancellationRequested)
        {
            throw new RhinoStartupException($"Rhino.Compute did not become ready within {StartupTimeout.TotalSeconds} seconds. See {_runtimeLogPath}.", ex);
        }
        finally
        {
            worker?.Dispose();
        }
    }

    private static async Task<bool> ProbeGeometryAsync(
        HttpClient client, Process process, Process worker, CancellationToken cancellationToken)
    {
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probe.CancelAfter(ProbeTimeout);
        try
        {
            // The proxy forwards /version to compute.geometry. It returns version metadata without doing a solve.
            using var response = await SendWhileRunningAsync(client, BaseUrl + "/version", process, worker, probe.Token);
            if (!response.IsSuccessStatusCode)
                return false;

            await using var body = await response.Content.ReadAsStreamAsync(probe.Token);
            using var version = await JsonDocument.ParseAsync(body, cancellationToken: probe.Token);
            var root = version.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("rhino", out var rhino) && rhino.ValueKind == JsonValueKind.String &&
                root.TryGetProperty("compute", out var compute) && compute.ValueKind == JsonValueKind.String;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
            ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return false;
        }
    }

    private sealed class RhinoStartupException(string message, Exception? innerException = null)
        : Exception(message, innerException);

    private async Task<bool> IsComputeUnavailableAsync(HttpClient client, Process process, Process? worker)
    {
        if (process.HasExited || worker?.HasExited == true || !await ProbeAsync(client))
            return true;

        using var currentWorker = _job!.FindGeometry();
        return currentWorker is null || process.HasExited || worker?.HasExited == true;
    }

    private async Task<bool> ProbeAsync(HttpClient client)
    {
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(_applicationStopping);
        probe.CancelAfter(ProbeTimeout);
        try
        {
            using var response = await client.GetAsync(BaseUrl + "/healthcheck", probe.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (
            !_applicationStopping.IsCancellationRequested &&
            ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task StopForRecoveryAsync(string reason)
    {
        _logger.LogWarning("{Reason}. Resetting Rhino.Compute.", reason);
        _state = "unavailable";
        await StopProcessAsync();
    }

    private async Task StopIfIdleAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_disposed || _applicationStopping.IsCancellationRequested || _process is null)
                return;

            var remaining = IdleTimeout - Stopwatch.GetElapsedTime(_lastJobFinished);
            if (remaining > TimeSpan.Zero)
            {
                _idleTimer.Change(remaining, Timeout.InfiniteTimeSpan);
                return;
            }

            await StopProcessAsync();
            _state = "standby";
            _logger.LogInformation("Stopped Rhino.Compute after {IdleMinutes} minutes without an STL job.", IdleTimeout.TotalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not stop idle Rhino.Compute.");
            _idleTimer.Change(TimeSpan.FromSeconds(10), Timeout.InfiniteTimeSpan);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopProcessAsync()
    {
        var hadJob = _job is not null;
        _job?.Dispose();
        _job = null;

        if (_process is not { } process)
            return;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeout.Token);
        if (hadJob)
        {
            while (IsPortListening() && !timeout.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
        _process = null;
        process.Dispose();
    }

    private static bool IsPortListening() =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(listener => listener.Port == Port);

    private long GetRuntimeLogLength()
    {
        try
        {
            var log = new FileInfo(_runtimeLogPath!);
            return log.Exists ? log.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private bool HasMissingStl(long offset)
    {
        try
        {
            using var log = new FileStream(_runtimeLogPath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            log.Position = Math.Max(offset, log.Length - 128 * 1024);
            using var reader = new StreamReader(log);
            return MissingStl.IsMatch(reader.ReadToEnd());
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _disposed = true;
            _idleTimer.Dispose();
            await StopProcessAsync();
        }
        finally
        {
            _gate.Release();
        }
    }
}
