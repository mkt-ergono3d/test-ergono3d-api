namespace InsoleApi.Utils
{
    public class StlGenerationHealthCheckStatus
    {
        private readonly object _lock = new();
        private readonly List<StlGenerationHealthCheckHistoryItem> _history = new();
        private readonly List<StlGenerationRequestHistoryItem> _generationRequests = new();
        private StlGenerationHealthCheckSnapshot _snapshot = new()
        {
            Enabled = false,
            IsChecking = false,
            LastResult = "not_started",
            IsHealthy = null,
            Message = "STL generation health check has not started."
        };

        public StlGenerationHealthCheckSnapshot GetSnapshot()
        {
            lock (_lock)
            {
                return _snapshot with
                {
                    History = _history.ToArray(),
                    GenerationRequests = _generationRequests.ToArray()
                };
            }
        }

        public void SetEnabled(bool enabled)
        {
            lock (_lock)
            {
                _snapshot = _snapshot with { Enabled = enabled };
            }
        }

        public void MarkStarted(string endpoint)
        {
            lock (_lock)
            {
                _snapshot = _snapshot with
                {
                    Enabled = true,
                    IsChecking = true,
                    LastStartedAtUtc = DateTime.UtcNow,
                    Endpoint = endpoint,
                    LastResult = "checking",
                    Message = "STL generation health check is running."
                };
            }
        }

        public void MarkSucceeded(string endpoint, int? httpStatusCode, int responseLength, int decodedBytes, TimeSpan duration)
        {
            lock (_lock)
            {
                _snapshot = _snapshot with
                {
                    Enabled = true,
                    IsChecking = false,
                    LastStartedAtUtc = _snapshot.LastStartedAtUtc,
                    LastFinishedAtUtc = DateTime.UtcNow,
                    Endpoint = endpoint,
                    LastHttpStatusCode = httpStatusCode,
                    LastResult = "ok",
                    IsHealthy = true,
                    IsEmptyStl = false,
                    ResponseLength = responseLength,
                    DecodedBytes = decodedBytes,
                    DurationMs = (long)duration.TotalMilliseconds,
                    ConsecutiveFailures = 0,
                    Message = "STL generation returned a non-empty STL.",
                    Error = null
                };
                AddHistory(_snapshot);
            }
        }

        public void MarkEmptyStl(string endpoint, int? httpStatusCode, int responseLength, int decodedBytes, TimeSpan duration, string reason)
        {
            lock (_lock)
            {
                _snapshot = _snapshot with
                {
                    Enabled = true,
                    IsChecking = false,
                    LastFinishedAtUtc = DateTime.UtcNow,
                    Endpoint = endpoint,
                    LastHttpStatusCode = httpStatusCode,
                    LastResult = "empty_stl",
                    IsHealthy = false,
                    IsEmptyStl = true,
                    ResponseLength = responseLength,
                    DecodedBytes = decodedBytes,
                    DurationMs = (long)duration.TotalMilliseconds,
                    ConsecutiveFailures = _snapshot.ConsecutiveFailures + 1,
                    Message = reason,
                    Error = null
                };
                AddHistory(_snapshot);
            }
        }

        public void MarkFailed(string endpoint, int? httpStatusCode, TimeSpan duration, Exception exception)
        {
            lock (_lock)
            {
                _snapshot = _snapshot with
                {
                    Enabled = true,
                    IsChecking = false,
                    LastFinishedAtUtc = DateTime.UtcNow,
                    Endpoint = endpoint,
                    LastHttpStatusCode = httpStatusCode,
                    LastResult = "error",
                    IsHealthy = false,
                    IsEmptyStl = null,
                    DurationMs = (long)duration.TotalMilliseconds,
                    ConsecutiveFailures = _snapshot.ConsecutiveFailures + 1,
                    Message = "STL generation health check failed.",
                    Error = exception.Message
                };
                AddHistory(_snapshot);
            }
        }

        public void RecordGenerationSucceeded(string source, Guid uuid, string? userId, string? traceId, int httpStatusCode, int responseLength, int decodedBytes, TimeSpan duration)
        {
            lock (_lock)
            {
                AddGenerationRequest(new StlGenerationRequestHistoryItem
                {
                    StartedAtUtc = DateTime.UtcNow - duration,
                    FinishedAtUtc = DateTime.UtcNow,
                    Source = source,
                    Uuid = uuid,
                    UserId = userId,
                    TraceId = traceId,
                    LastHttpStatusCode = httpStatusCode,
                    Result = "ok",
                    IsHealthy = true,
                    IsEmptyStl = false,
                    ResponseLength = responseLength,
                    DecodedBytes = decodedBytes,
                    DurationMs = (long)duration.TotalMilliseconds,
                    Message = "STL generation returned a non-empty STL."
                });
            }
        }

        public void RecordGenerationEmptyStl(string source, Guid uuid, string? userId, string? traceId, int httpStatusCode, int responseLength, int decodedBytes, TimeSpan duration, string reason)
        {
            lock (_lock)
            {
                AddGenerationRequest(new StlGenerationRequestHistoryItem
                {
                    StartedAtUtc = DateTime.UtcNow - duration,
                    FinishedAtUtc = DateTime.UtcNow,
                    Source = source,
                    Uuid = uuid,
                    UserId = userId,
                    TraceId = traceId,
                    LastHttpStatusCode = httpStatusCode,
                    Result = "empty_stl",
                    IsHealthy = false,
                    IsEmptyStl = true,
                    ResponseLength = responseLength,
                    DecodedBytes = decodedBytes,
                    DurationMs = (long)duration.TotalMilliseconds,
                    Message = reason
                });
            }
        }

        public void RecordGenerationFailed(string source, Guid uuid, string? userId, string? traceId, int? httpStatusCode, TimeSpan duration, Exception exception)
        {
            lock (_lock)
            {
                AddGenerationRequest(new StlGenerationRequestHistoryItem
                {
                    StartedAtUtc = DateTime.UtcNow - duration,
                    FinishedAtUtc = DateTime.UtcNow,
                    Source = source,
                    Uuid = uuid,
                    UserId = userId,
                    TraceId = traceId,
                    LastHttpStatusCode = httpStatusCode,
                    Result = "error",
                    IsHealthy = false,
                    IsEmptyStl = null,
                    DurationMs = (long)duration.TotalMilliseconds,
                    Message = "STL generation request failed.",
                    Error = exception.Message
                });
            }
        }

        private void AddHistory(StlGenerationHealthCheckSnapshot snapshot)
        {
            _history.Add(new StlGenerationHealthCheckHistoryItem
            {
                StartedAtUtc = snapshot.LastStartedAtUtc,
                FinishedAtUtc = snapshot.LastFinishedAtUtc ?? DateTime.UtcNow,
                LastHttpStatusCode = snapshot.LastHttpStatusCode,
                Result = snapshot.LastResult,
                IsHealthy = snapshot.IsHealthy,
                IsEmptyStl = snapshot.IsEmptyStl,
                ResponseLength = snapshot.ResponseLength,
                DecodedBytes = snapshot.DecodedBytes,
                DurationMs = snapshot.DurationMs,
                Message = snapshot.Message,
                Error = snapshot.Error
            });

            if (_history.Count > 24)
                _history.RemoveRange(0, _history.Count - 24);
        }

        private void AddGenerationRequest(StlGenerationRequestHistoryItem item)
        {
            _generationRequests.Add(item);

            if (_generationRequests.Count > 100)
                _generationRequests.RemoveRange(0, _generationRequests.Count - 100);
        }
    }

    public record StlGenerationHealthCheckSnapshot
    {
        public bool Enabled { get; init; }
        public bool IsChecking { get; init; }
        public DateTime? LastStartedAtUtc { get; init; }
        public DateTime? LastFinishedAtUtc { get; init; }
        public string? Endpoint { get; init; }
        public int? LastHttpStatusCode { get; init; }
        public string LastResult { get; init; } = "not_started";
        public bool? IsHealthy { get; init; }
        public bool? IsEmptyStl { get; init; }
        public int? ResponseLength { get; init; }
        public int? DecodedBytes { get; init; }
        public long? DurationMs { get; init; }
        public int ConsecutiveFailures { get; init; }
        public string? Message { get; init; }
        public string? Error { get; init; }
        public IReadOnlyList<StlGenerationHealthCheckHistoryItem> History { get; init; } = [];
        public IReadOnlyList<StlGenerationRequestHistoryItem> GenerationRequests { get; init; } = [];
    }

    public record StlGenerationHealthCheckHistoryItem
    {
        public DateTime? StartedAtUtc { get; init; }
        public DateTime FinishedAtUtc { get; init; }
        public int? LastHttpStatusCode { get; init; }
        public string Result { get; init; } = "not_started";
        public bool? IsHealthy { get; init; }
        public bool? IsEmptyStl { get; init; }
        public int? ResponseLength { get; init; }
        public int? DecodedBytes { get; init; }
        public long? DurationMs { get; init; }
        public string? Message { get; init; }
        public string? Error { get; init; }
    }

    public record StlGenerationRequestHistoryItem
    {
        public DateTime? StartedAtUtc { get; init; }
        public DateTime FinishedAtUtc { get; init; }
        public string Source { get; init; } = "";
        public Guid Uuid { get; init; }
        public string? UserId { get; init; }
        public string? TraceId { get; init; }
        public int? LastHttpStatusCode { get; init; }
        public string Result { get; init; } = "not_started";
        public bool? IsHealthy { get; init; }
        public bool? IsEmptyStl { get; init; }
        public int? ResponseLength { get; init; }
        public int? DecodedBytes { get; init; }
        public long? DurationMs { get; init; }
        public string? Message { get; init; }
        public string? Error { get; init; }
    }
}
