using inso.Utils;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace InsoleApi.Utils
{
    public class StlGenerationHealthCheckService : BackgroundService
    {
        private static readonly TimeSpan HealthyCheckInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan FailedCheckRetryInterval = TimeSpan.FromMinutes(1);

        private static readonly string[] AlertRecipients =
        [
            "team@ergono3d.com",
            "xurui@comfyplus.org"
        ];

        private const string HealthCheckPayload = """
            {
              "patient_id": "stl-health-check",
              "insole": {
                "length": 42,
                "width": 0,
                "thickness": 3,
                "midsole": 0.15,
                "left": {
                  "arch_height": 4,
                  "arch_length": 0.25,
                  "lateral_heel_skive": 0,
                  "medial_heel_skive": 0,
                  "rearfoot_varus_post": 0,
                  "rearfoot_valgus_post": 0,
                  "forefoot_varus_post": 0,
                  "forefoot_valgus_post": 0,
                  "heel_cup_depth": 4,
                  "medial_flange_height": 0,
                  "lateral_flange_height": 0
                },
                "right": {
                  "arch_height": 4,
                  "arch_length": 0.25,
                  "lateral_heel_skive": 0,
                  "medial_heel_skive": 0,
                  "rearfoot_varus_post": 0,
                  "rearfoot_valgus_post": 0,
                  "forefoot_varus_post": 0,
                  "forefoot_valgus_post": 0,
                  "heel_cup_depth": 4,
                  "medial_flange_height": 0,
                  "lateral_flange_height": 0
                }
              }
            }
            """;

        private readonly EmailHelper _emailHelper;
        private readonly ILogger<StlGenerationHealthCheckService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly StlGenerationHealthCheckStatus _status;
        private readonly RhinoComputeLifetime _rhinoCompute;
        private bool _hasUnresolvedFailure;

        public StlGenerationHealthCheckService(
            EmailHelper emailHelper,
            ILogger<StlGenerationHealthCheckService> logger,
            IServiceScopeFactory serviceScopeFactory,
            StlGenerationHealthCheckStatus status,
            RhinoComputeLifetime rhinoCompute)
        {
            _emailHelper = emailHelper;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _status = status;
            _rhinoCompute = rhinoCompute;
            _status.SetEnabled(true);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (_hasUnresolvedFailure)
                    await RecordFailedCheckRetryAsync(stoppingToken);

                var isHealthy = await CheckStlGenerationAsync(stoppingToken);
                var delay = isHealthy ? HealthyCheckInterval : FailedCheckRetryInterval;
                await Task.Delay(delay, stoppingToken);
            }
        }

        private async Task<bool> CheckStlGenerationAsync(CancellationToken cancellationToken)
        {
            var endpoint = GetStlGenerationEndpoint();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _status.MarkStarted(endpoint);

                var result = await GenerateStlAsync(endpoint, cancellationToken);
                var emptyReason = StlResponseInspector.GetEmptyStlReason(result.StlBase64, out var decodedBytes);
                if (emptyReason is null)
                {
                    _status.MarkSucceeded(endpoint, result.HttpStatusCode, result.StlBase64.Length, decodedBytes, stopwatch.Elapsed);
                    _logger.LogInformation("STL generation health check succeeded.");
                    await NotifyRecoveryIfNeededAsync(endpoint);
                    return true;
                }

                _status.MarkEmptyStl(endpoint, result.HttpStatusCode, result.StlBase64.Length, decodedBytes, stopwatch.Elapsed, emptyReason);
                _logger.LogWarning("STL generation health check returned an empty STL: {Reason}", emptyReason);
                await NotifyFailureIfNeededAsync(emptyReason);
                return false;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                int? statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : null;
                _status.MarkFailed(endpoint, statusCode, stopwatch.Elapsed, ex);
                _logger.LogError(ex, "STL generation health check failed.");
                await NotifyFailureIfNeededAsync(ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _status.MarkFailed(endpoint, null, stopwatch.Elapsed, ex);
                _logger.LogError(ex, "STL generation health check failed.");
                await NotifyFailureIfNeededAsync(ex.Message);
                return false;
            }
        }

        private async Task<StlGenerationResult> GenerateStlAsync(string endpoint, CancellationToken cancellationToken)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            _rhinoCompute.ConfigureAuthentication(client);
            client.Timeout = TimeSpan.FromSeconds(600);

            using var response = await client.GetAsync(endpoint, cancellationToken);
            var stlBs64 = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            return new StlGenerationResult((int)response.StatusCode, stlBs64);
        }

        private async Task RecordFailedCheckRetryAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var generationUsageStatisticsService = scope.ServiceProvider.GetRequiredService<GenerationUsageStatisticsService>();
                await generationUsageStatisticsService.RecordFailedCheckRetryAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record the post-failure health-check retry.");
            }
        }

        private string GetStlGenerationEndpoint()
        {
            var data = Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(HealthCheckPayload)));
#if DEBUG
            return $"http://13.55.129.243/StarDesigner/insoles?data={data}";
#else
            return $"{_rhinoCompute.BaseUrl}/StarDesigner/insoles?data={data}";
#endif
        }

        private async Task NotifyFailureIfNeededAsync(string reason)
        {
            if (_hasUnresolvedFailure)
                return;

            _hasUnresolvedFailure = true;
            await SendAlertAsync(reason);
        }

        private async Task NotifyRecoveryIfNeededAsync(string endpoint)
        {
            if (!_hasUnresolvedFailure)
                return;

            _hasUnresolvedFailure = false;
            var body = JsonSerializer.Serialize(new
            {
                message = "STL generation health check recovered.",
                endpoint,
                recovered_at_utc = DateTime.UtcNow
            });

            await SendNotificationAsync("Ergono3D STL generation health check recovered", body);
        }

        private async Task SendAlertAsync(string reason)
        {
            var body = JsonSerializer.Serialize(new
            {
                message = "STL generation health check failed.",
                reason,
                checked_at_utc = DateTime.UtcNow
            });

            await SendNotificationAsync("Ergono3D STL generation health check alert", body);
        }

        private async Task SendNotificationAsync(string subject, string body)
        {
            foreach (var recipient in AlertRecipients)
            {
                try
                {
                    await _emailHelper.Send("no_reply@comfyplus.org", recipient, subject, body);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send STL generation health check notification to {Recipient}.", recipient);
                }
            }
        }

        private record StlGenerationResult(int HttpStatusCode, string StlBase64);
    }
}
