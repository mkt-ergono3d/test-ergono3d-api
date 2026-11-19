using InsoleApi.Common;
using InsoleApi.Utils;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text;

namespace InsoleApi.Controllers
{
    [ApiController]
    [Route("/api/status")]
    public class StatusController : Controller
    {
        private static readonly TimeSpan RhinoHealthMaxAge = TimeSpan.FromMinutes(15);
        private const string RhinoConnectionRefusedMessage = "由于目标计算机积极拒绝，无法连接。 (localhost:6500)";
        private const string RhinoConnectionRefusedMessageEnglish = "No connection could be made because the target machine actively refused it. (localhost:6500)";

        private readonly StlGenerationHealthCheckStatus _stlGenerationHealthCheckStatus;
        private readonly GenerationUsageStatisticsService _generationUsageStatisticsService;
        private readonly RhinoComputeLifetime _rhinoCompute;

        public StatusController(StlGenerationHealthCheckStatus stlGenerationHealthCheckStatus, GenerationUsageStatisticsService generationUsageStatisticsService, RhinoComputeLifetime rhinoCompute)
        {
            _stlGenerationHealthCheckStatus = stlGenerationHealthCheckStatus;
            _generationUsageStatisticsService = generationUsageStatisticsService;
            _rhinoCompute = rhinoCompute;
        }

        [HttpGet("/health")]
        [Produces("text/plain")]
        public ContentResult Health()
        {
            return Content("OK", "text/plain; charset=utf-8");
        }

        [HttpGet("/health/rhino")]
        public IActionResult RhinoHealth()
        {
            if (_rhinoCompute.Enabled)
            {
                var state = _rhinoCompute.State;
                var health = new { status = state, mode = "on_demand" };
                return state == "unavailable"
                    ? StatusCode(StatusCodes.Status503ServiceUnavailable, health)
                    : Ok(health);
            }

            var snapshot = _stlGenerationHealthCheckStatus.GetSnapshot();
            var now = DateTime.UtcNow;
            var isRecentSuccess = snapshot.IsHealthy == true &&
                snapshot.LastFinishedAtUtc is DateTime lastFinishedAtUtc &&
                now - lastFinishedAtUtc <= RhinoHealthMaxAge;
            var isCurrentCheckWithinGracePeriod = snapshot.IsChecking &&
                snapshot.LastStartedAtUtc is DateTime lastStartedAtUtc &&
                now - lastStartedAtUtc <= RhinoHealthMaxAge;

            var payload = new
            {
                status = isRecentSuccess ? "ok" : isCurrentCheckWithinGracePeriod ? "checking" : "unhealthy",
                last_result = snapshot.LastResult,
                last_finished_at_utc = snapshot.LastFinishedAtUtc,
                last_http_status_code = snapshot.LastHttpStatusCode,
                message = snapshot.Message,
                error = snapshot.Error
            };

            return isRecentSuccess || isCurrentCheckWithinGracePeriod
                ? Ok(payload)
                : StatusCode(StatusCodes.Status503ServiceUnavailable, payload);
        }

        [HttpGet]
        [Produces("text/html")]
        public async Task<ContentResult> GetStatus()
        {
            var snapshot = _stlGenerationHealthCheckStatus.GetSnapshot();
            var generationUsage = await _generationUsageStatisticsService.GetSnapshotAsync(HttpContext.RequestAborted);
            return Content(BuildStatusPage(snapshot, generationUsage, _rhinoCompute.Enabled ? _rhinoCompute.State : null),
                "text/html; charset=utf-8");
        }

        [HttpGet("json")]
        public async Task<Result> GetStatusJson()
        {
            return Result.Success(new
            {
                compute = _stlGenerationHealthCheckStatus.GetSnapshot(),
                rhino_process_state = _rhinoCompute.Enabled ? _rhinoCompute.State : null,
                user_generation_counts = await _generationUsageStatisticsService.GetSnapshotAsync(HttpContext.RequestAborted)
            });
        }

        private static string BuildStatusPage(StlGenerationHealthCheckSnapshot snapshot, GenerationUsageSnapshot generationUsage, string? processState)
        {
            var history = snapshot.History;
            var generationRequests = snapshot.GenerationRequests;
            var successfulChecks = history.Count(i => i.IsHealthy == true);
            var uptime = history.Count == 0 ? "N/A" : $"{successfulChecks * 100.0 / history.Count:0.00}% uptime";
            var successfulRequests = generationRequests.Count(i => i.IsHealthy == true);
            var requestUptime = generationRequests.Count == 0 ? "N/A" : $"{successfulRequests * 100.0 / generationRequests.Count:0.00}% success";
            var lastRun = FormatUtc(snapshot.LastFinishedAtUtc ?? snapshot.LastStartedAtUtc);
            var (resultText, resultClass) = processState switch
            {
                "standby" => ("Standby", "unknown"),
                "starting" => ("Starting", "unknown"),
                "running" => ("Running", "ok"),
                "unavailable" => ("Unavailable", "down"),
                _ => (GetResultText(snapshot), snapshot.IsHealthy == true ? "ok" : snapshot.IsHealthy == false ? "down" : "unknown")
            };
            var message = WebUtility.HtmlEncode(processState is null
                ? snapshot.Error ?? snapshot.Message ?? "No status message."
                : "Rhino starts when an STL generation request arrives.");

            var builder = new StringBuilder();
            builder.AppendLine("""
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1" />
                  <meta http-equiv="refresh" content="60" />
                  <title>Ergono3D Status</title>
                  <style>
                    :root {
                      color-scheme: light;
                      --bg: #f7f7f8;
                      --panel: #ffffff;
                      --line: #dedee3;
                      --text: #1f1f29;
                      --muted: #777985;
                      --green: #16c75d;
                      --red: #ff3657;
                      --gray: #d7d9df;
                    }

                    * { box-sizing: border-box; }

                    body {
                      margin: 0;
                      background: var(--bg);
                      color: var(--text);
                      font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
                    }

                    main {
                      width: min(1120px, calc(100% - 32px));
                      margin: 56px auto;
                    }

                    header {
                      display: flex;
                      align-items: end;
                      justify-content: space-between;
                      gap: 24px;
                      margin-bottom: 28px;
                    }

                    h1 {
                      margin: 0;
                      font-size: 38px;
                      line-height: 1.1;
                      letter-spacing: 0;
                    }

                    .subtle {
                      margin-top: 10px;
                      color: var(--muted);
                      font-size: 16px;
                    }

                    .badge {
                      display: inline-flex;
                      align-items: center;
                      gap: 10px;
                      border: 1px solid var(--line);
                      border-radius: 999px;
                      padding: 10px 14px;
                      background: var(--panel);
                      color: var(--muted);
                      white-space: nowrap;
                    }

                    .light {
                      width: 12px;
                      height: 12px;
                      border-radius: 50%;
                      background: var(--gray);
                    }

                    .badge.ok .light { background: var(--green); }
                    .badge.down .light { background: var(--red); }

                    .scale {
                      display: flex;
                      justify-content: space-between;
                      margin: 0 36px 14px;
                      color: #6f717d;
                      font-size: 18px;
                      font-weight: 600;
                    }

                    .timeline {
                      display: flex;
                      gap: 6px;
                      align-items: center;
                      margin: 0 32px 46px;
                    }

                    .timeline.requests {
                      gap: 3px;
                      margin-bottom: 18px;
                    }

                    .point {
                      flex: 1 1 0;
                      min-width: 8px;
                      height: 12px;
                      border-radius: 999px;
                      background: var(--gray);
                    }

                    .point.ok { background: var(--green); }
                    .point.down { background: var(--red); }

                    .card {
                      background: var(--panel);
                      border: 1px solid var(--line);
                      border-radius: 8px;
                      box-shadow: 0 1px 2px rgba(20, 20, 30, 0.08);
                      overflow: hidden;
                    }

                    .service-row {
                      display: flex;
                      align-items: center;
                      justify-content: space-between;
                      gap: 20px;
                      padding: 26px 32px;
                      border-bottom: 1px solid var(--line);
                    }

                    .service-name {
                      display: flex;
                      align-items: center;
                      gap: 16px;
                      min-width: 0;
                      font-size: 26px;
                      font-weight: 700;
                    }

                    .chevron {
                      color: #20212b;
                      font-size: 26px;
                      line-height: 1;
                    }

                    .uptime {
                      color: #8b8d98;
                      font-size: 26px;
                      white-space: nowrap;
                    }

                    .details {
                      display: grid;
                      grid-template-columns: repeat(4, minmax(0, 1fr));
                      gap: 1px;
                      background: var(--line);
                    }

                    .detail {
                      background: var(--panel);
                      padding: 18px 20px;
                      min-width: 0;
                    }

                    .label {
                      color: var(--muted);
                      font-size: 13px;
                      text-transform: uppercase;
                      letter-spacing: 0;
                      margin-bottom: 8px;
                    }

                    .value {
                      color: var(--text);
                      font-size: 16px;
                      font-weight: 650;
                      overflow-wrap: anywhere;
                    }

                    .message {
                      margin-top: 18px;
                      color: var(--muted);
                      font-size: 14px;
                      line-height: 1.6;
                    }

                    .section-head {
                      display: flex;
                      align-items: end;
                      justify-content: space-between;
                      gap: 20px;
                      margin: 48px 0 16px;
                    }

                    .section-head h2 {
                      margin: 0;
                      font-size: 24px;
                      line-height: 1.2;
                      letter-spacing: 0;
                    }

                    .section-note {
                      color: var(--muted);
                      font-size: 14px;
                      margin-top: 6px;
                    }

                    .table-wrap {
                      overflow-x: auto;
                    }

                    table {
                      width: 100%;
                      border-collapse: collapse;
                      min-width: 920px;
                    }

                    th, td {
                      padding: 14px 16px;
                      border-bottom: 1px solid var(--line);
                      text-align: left;
                      vertical-align: top;
                      font-size: 14px;
                    }

                    th {
                      color: var(--muted);
                      font-size: 12px;
                      text-transform: uppercase;
                      letter-spacing: 0;
                      background: #fbfbfc;
                    }

                    tr:last-child td { border-bottom: 0; }

                    .mono {
                      font-family: "Cascadia Mono", "SFMono-Regular", Consolas, monospace;
                      font-size: 13px;
                    }

                    .status-pill {
                      display: inline-flex;
                      align-items: center;
                      gap: 8px;
                      white-space: nowrap;
                      font-weight: 650;
                    }

                    .status-dot {
                      width: 9px;
                      height: 9px;
                      border-radius: 50%;
                      background: var(--gray);
                    }

                    .status-pill.ok .status-dot { background: var(--green); }
                    .status-pill.down .status-dot { background: var(--red); }

                    @media (max-width: 760px) {
                      main { margin: 32px auto; }
                      header, .service-row { align-items: start; flex-direction: column; }
                      h1 { font-size: 30px; }
                      .service-name, .uptime { font-size: 22px; }
                      .details { grid-template-columns: 1fr 1fr; }
                      .scale, .timeline { margin-left: 0; margin-right: 0; }
                      .section-head { align-items: start; flex-direction: column; }
                    }
                  </style>
                </head>
                <body>
                  <main>
                    <header>
                      <div>
                        <h1>Ergono3D Status</h1>
                        <div class="subtle">Rhino Compute STL generation health</div>
                      </div>
                """);
            builder.AppendLine($"      <div class=\"badge {resultClass}\"><span class=\"light\"></span><span>{WebUtility.HtmlEncode(resultText)}</span></div>");
            builder.AppendLine("""
                    </header>

                    <div class="scale">
                      <span>24 checks ago</span>
                      <span>Latest</span>
                    </div>
                """);
            builder.AppendLine("    <div class=\"timeline\">");
            builder.Append(BuildTimeline(history));
            builder.AppendLine("    </div>");
            builder.AppendLine("""
                    <section class="card">
                      <div class="service-row">
                        <div class="service-name"><span class="chevron">v</span><span>API - Rhino Compute STL</span></div>
                """);
            builder.AppendLine($"        <div class=\"uptime\">{uptime}</div>");
            builder.AppendLine("""
                      </div>
                      <div class="details">
                """);
            builder.AppendLine(BuildDetail("Last run", lastRun));
            builder.AppendLine(BuildDetail("Result", resultText));
            builder.AppendLine(BuildDetail("HTTP", snapshot.LastHttpStatusCode?.ToString() ?? "N/A"));
            builder.AppendLine(BuildDetail("Duration", snapshot.DurationMs.HasValue ? $"{snapshot.DurationMs.Value} ms" : "N/A"));
            builder.AppendLine(BuildDetail("Decoded STL", snapshot.DecodedBytes.HasValue ? $"{snapshot.DecodedBytes.Value} bytes" : "N/A"));
            builder.AppendLine(BuildDetail("Failures", snapshot.ConsecutiveFailures.ToString()));
            builder.AppendLine(BuildDetail("Checking", snapshot.IsChecking ? "Yes" : "No"));
            builder.AppendLine(BuildDetail("History", $"{history.Count}/24 checks"));
            builder.AppendLine(BuildDetail("User generations today (UTC)", generationUsage.DailyCount.ToString()));
            builder.AppendLine(BuildDetail("User generations this week (UTC)", generationUsage.WeeklyCount.ToString()));
            builder.AppendLine(BuildDetail("Post-failure checks today (UTC)", generationUsage.DailyFailedCheckRetryCount.ToString()));
            builder.AppendLine(BuildDetail("Post-failure checks this week (UTC)", generationUsage.WeeklyFailedCheckRetryCount.ToString()));
            builder.AppendLine(BuildDetail("Daily average, last 7 days (UTC)", generationUsage.TrailingSevenDayDailyAverage.ToString("0.00")));
            builder.AppendLine(BuildDetail("User generations this month (UTC)", generationUsage.MonthlyCount.ToString()));
            builder.AppendLine($"""
                      </div>
                    </section>
                    <div class="message">{message}</div>
                    <section class="section-head">
                      <div>
                        <h2>Recent STL requests</h2>
                        <div class="section-note">Every user-triggered Rhino communication, latest 100 in memory.</div>
                      </div>
                """);
            builder.AppendLine($"      <div class=\"uptime\">{requestUptime}</div>");
            builder.AppendLine("""
                    </section>
                    <div class="scale">
                      <span>100 requests ago</span>
                      <span>Latest</span>
                    </div>
                    <div class="timeline requests">
                """);
            builder.Append(BuildGenerationRequestTimeline(generationRequests));
            builder.AppendLine("""
                    </div>
                    <section class="card table-wrap">
                """);
            builder.Append(BuildGenerationRequestTable(generationRequests));
            builder.AppendLine("""
                    </section>
                  </main>
                </body>
                </html>
                """);

            return builder.ToString();
        }

        private static string BuildTimeline(IReadOnlyList<StlGenerationHealthCheckHistoryItem> history)
        {
            var builder = new StringBuilder();
            var placeholders = Math.Max(0, 24 - history.Count);
            for (var i = 0; i < placeholders; i++)
                builder.AppendLine("<span class=\"point\" title=\"No data\"></span>");

            foreach (var item in history)
            {
                var cssClass = item.IsHealthy == true ? "ok" : "down";
                var title = WebUtility.HtmlEncode($"{FormatUtc(item.FinishedAtUtc)} - {GetResultText(item.Result, item.IsHealthy, item.IsEmptyStl)}");
                builder.AppendLine($"<span class=\"point {cssClass}\" title=\"{title}\"></span>");
            }

            return builder.ToString();
        }

        private static string BuildGenerationRequestTimeline(IReadOnlyList<StlGenerationRequestHistoryItem> requests)
        {
            var builder = new StringBuilder();
            var placeholders = Math.Max(0, 100 - requests.Count);
            for (var i = 0; i < placeholders; i++)
                builder.AppendLine("<span class=\"point\" title=\"No data\"></span>");

            foreach (var item in requests)
            {
                var cssClass = item.IsHealthy == true ? "ok" : "down";
                var title = WebUtility.HtmlEncode($"{FormatUtc(item.FinishedAtUtc)} - {item.Source} - {GetResultText(item.Result, item.IsHealthy, item.IsEmptyStl)}");
                builder.AppendLine($"<span class=\"point {cssClass}\" title=\"{title}\"></span>");
            }

            return builder.ToString();
        }

        private static string BuildGenerationRequestTable(IReadOnlyList<StlGenerationRequestHistoryItem> requests)
        {
            var builder = new StringBuilder();
            builder.AppendLine("""
                <table>
                  <thead>
                    <tr>
                      <th>Time</th>
                      <th>Result</th>
                      <th>Source</th>
                      <th>UUID</th>
                      <th>HTTP</th>
                      <th>Duration</th>
                      <th>Decoded STL</th>
                      <th>Type</th>
                      <th>Message</th>
                    </tr>
                  </thead>
                  <tbody>
                """);

            if (requests.Count == 0)
            {
                builder.AppendLine("<tr><td colspan=\"9\">No STL requests recorded yet.</td></tr>");
            }
            else
            {
                foreach (var item in requests.Reverse())
                {
                    var cssClass = item.IsHealthy == true ? "ok" : "down";
                    var resultText = WebUtility.HtmlEncode(GetResultText(item.Result, item.IsHealthy, item.IsEmptyStl));
                    var rawMessage = item.Error ?? item.Message ?? "";
                    var isRhinoConnectionRefused = rawMessage.Contains(RhinoConnectionRefusedMessage, StringComparison.Ordinal);
                    var type = item.IsHealthy == true
                        ? ""
                        : isRhinoConnectionRefused ? "Restart required" : "Other";
                    var message = WebUtility.HtmlEncode(isRhinoConnectionRefused ? RhinoConnectionRefusedMessageEnglish : rawMessage);
                    builder.AppendLine($"""
                        <tr>
                          <td>{FormatUtc(item.FinishedAtUtc)}</td>
                          <td><span class="status-pill {cssClass}"><span class="status-dot"></span>{resultText}</span></td>
                          <td>{WebUtility.HtmlEncode(item.Source)}</td>
                          <td class="mono">{WebUtility.HtmlEncode(item.Uuid.ToString())}</td>
                          <td>{WebUtility.HtmlEncode(item.LastHttpStatusCode?.ToString() ?? "N/A")}</td>
                          <td>{WebUtility.HtmlEncode(item.DurationMs.HasValue ? $"{item.DurationMs.Value} ms" : "N/A")}</td>
                          <td>{WebUtility.HtmlEncode(item.DecodedBytes.HasValue ? $"{item.DecodedBytes.Value} bytes" : "N/A")}</td>
                          <td>{type}</td>
                          <td>{message}</td>
                        </tr>
                        """);
                }
            }

            builder.AppendLine("""
                  </tbody>
                </table>
                """);

            return builder.ToString();
        }

        private static string BuildDetail(string label, string value)
        {
            return $"""
                <div class="detail">
                  <div class="label">{WebUtility.HtmlEncode(label)}</div>
                  <div class="value">{WebUtility.HtmlEncode(value)}</div>
                </div>
                """;
        }

        private static string GetResultText(StlGenerationHealthCheckSnapshot snapshot)
        {
            if (snapshot.IsChecking)
                return "Checking";

            return GetResultText(snapshot.LastResult, snapshot.IsHealthy, snapshot.IsEmptyStl);
        }

        private static string GetResultText(string result, bool? isHealthy, bool? isEmptyStl)
        {
            if (isHealthy == true)
                return "Operational (non-empty STL)";

            if (result == "empty_stl" || isEmptyStl == true)
                return "Down (empty STL)";

            if (result == "error")
                return "Down (compute error)";

            if (result == "checking")
                return "Checking";

            return "No data";
        }

        private static string FormatUtc(DateTime? value)
        {
            return value.HasValue ? value.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'") : "N/A";
        }
    }
}
