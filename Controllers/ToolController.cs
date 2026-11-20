using inso.Utils;
using InsoleApi.Common;
using InsoleApi.Database;
using InsoleApi.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using Stripe;
using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace InsoleApi.Controllers
{
    [ApiController]
    [Route("/api/v1/tool")]
    public class ToolController : Controller
    {

        private static readonly string[] StlGenerationAlertRecipients =
        [
            "team@ergono3d.com",
            "xurui@comfyplus.org"
        ];

        static private readonly MemoryCache Cache = new MemoryCache(new MemoryCacheOptions());

        private readonly IHttpContextAccessor _httpContextAccessor;

        private readonly PrimaryDbContext _primaryDbContext;

        private readonly EmailHelper _emailHelper;

        private readonly JwtHelper _jwtHelper;

        private readonly ILogger<ToolController> _logger;

        private readonly IConfiguration _configuration;

        private readonly IWebHostEnvironment _webHostEnvironment;

        private readonly OrderedGenerationGate _orderedGenerationGate;

        private readonly StlGenerationHealthCheckStatus _stlGenerationHealthCheckStatus;

        private readonly GenerationUsageStatisticsService _generationUsageStatisticsService;
        private readonly RhinoComputeLifetime _rhinoCompute;

        public ToolController(IHttpContextAccessor httpContextAccessor, PrimaryDbContext primaryDbContext, EmailHelper emailHelper, JwtHelper jwtHelper, ILogger<ToolController> logger, IConfiguration configuration, IWebHostEnvironment webHostEnvironment, OrderedGenerationGate orderedGenerationGate, StlGenerationHealthCheckStatus stlGenerationHealthCheckStatus, GenerationUsageStatisticsService generationUsageStatisticsService, RhinoComputeLifetime rhinoCompute)
        {
            _httpContextAccessor = httpContextAccessor;
            _primaryDbContext = primaryDbContext;
            _emailHelper = emailHelper;
            _jwtHelper = jwtHelper;
            _logger = logger;
            _configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
            _orderedGenerationGate = orderedGenerationGate;
            _stlGenerationHealthCheckStatus = stlGenerationHealthCheckStatus;
            _generationUsageStatisticsService = generationUsageStatisticsService;
            _rhinoCompute = rhinoCompute;
        }


        [HttpPost("get-param")]
        [Authorize]
        public async Task<Result> GetParam(Pre pre)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");
            string result = await AI.GetInsoleRecommendation(pre.Step1, pre.Step2);
            Guid guid = Guid.NewGuid();
            Cache.Set(guid, pre);
            return Result.Success(new
            {
                bs64 = ParamValidator.Normalize(result.Replace("```json\n", "").Replace("\n```", "")),
                uuid = guid,
            });
        }

        [HttpPost("get-param3")]
        [Authorize]
        public async Task<Result> GetParam(Pre2 pre)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");
            string result = await AI.GetInsoleRecommendation(pre.Step1, pre.Step2);
            Guid guid = Guid.NewGuid();
            Cache.Set(guid, pre);
            return Result.Success(new
            {
                bs64 = ParamValidator.Normalize(result.Replace("```json\n", "").Replace("\n```", "")),
                uuid = guid,
            });
        }

        [HttpPost("get-param-ai")]
        [Authorize]
        public async Task<Result> GetParamAi(PreAi pre)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");

            string result = await AI.GetInsoleRecommendationFromImage(pre.Step1, pre.ImageBase64, pre.MediaType);

            Guid guid = Guid.NewGuid();
            _primaryDbContext.Params.Add(new ParamsEntity
            {
                Id = guid,
                Step1 = JsonSerializer.Serialize(pre.Step1),
                Step2 = "{}",
                Step3 = ""
            });
            await _primaryDbContext.SaveChangesAsync();

            return Result.Success(new
            {
                bs64 = ParamValidator.Normalize(result.Replace("```json\n", "").Replace("\n```", "")),
                uuid = guid,
            });
        }

        [HttpGet("get-param2/{id}")]
        [Authorize]
        public async Task<Result> GetParam2(string id)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");
            Guid iid = new Guid(id);
            if (!await _primaryDbContext.STLs.Where(stl => stl.Id == iid && stl.UserId == new Guid(userId)).AnyAsync()) return Result.Failed(-1);
            return Result.Success(await _primaryDbContext.Params.Where(p => p.Id == iid).FirstOrDefaultAsync());
        }

        public class GetStlBody
        {
            public required string Bs64 { get; set; }

            public required Guid Uuid { get; set; }
        }

        private sealed class InvalidStlGenerationException : Exception
        {
            public InvalidStlGenerationException(string reason) : base(reason) { }
        }

        private async Task<string> RequestStlFromRhinoAsync(string source, Guid uuid, string userId, string stlParams)
        {
            _primaryDbContext.Records.Add(new RecordEntity
            {
                UserId = Guid.Parse(userId),
                Time = DateTime.UtcNow
            });
            await _primaryDbContext.SaveChangesAsync();

            await _generationUsageStatisticsService.RecordUserGenerationAsync(
                _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);

            var endpoint = GetStlGenerationEndpoint(stlParams);
            var traceId = HttpContext.TraceIdentifier;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                _rhinoCompute.ConfigureAuthentication(client);
                client.Timeout = TimeSpan.FromSeconds(600);

                using var response = await _rhinoCompute.SendAsync(client, endpoint);
                response.EnsureSuccessStatusCode();

                var stlBs64 = await response.Content.ReadAsStringAsync();
                var emptyReason = StlResponseInspector.GetEmptyStlReason(stlBs64, out var decodedBytes);
                if (emptyReason is null)
                {
                    _stlGenerationHealthCheckStatus.RecordGenerationSucceeded(
                        source,
                        uuid,
                        userId,
                        traceId,
                        (int)response.StatusCode,
                        stlBs64.Length,
                        decodedBytes,
                        stopwatch.Elapsed);
                }
                else
                {
                    _stlGenerationHealthCheckStatus.RecordGenerationEmptyStl(
                        source,
                        uuid,
                        userId,
                        traceId,
                        (int)response.StatusCode,
                        stlBs64.Length,
                        decodedBytes,
                        stopwatch.Elapsed,
                        emptyReason);
                    await SendGenerationFailureAlertAsync(source, uuid, userId, traceId, (int)response.StatusCode, emptyReason);
                    throw new InvalidStlGenerationException(emptyReason);
                }

                return stlBs64;
            }
            catch (InvalidStlGenerationException)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                _stlGenerationHealthCheckStatus.RecordGenerationFailed(
                    source,
                    uuid,
                    userId,
                    traceId,
                    null,
                    stopwatch.Elapsed,
                    ex);
                await SendGenerationFailureAlertAsync(source, uuid, userId, traceId, null, ex.Message);
                throw;
            }
            catch (HttpRequestException ex)
            {
                int? statusCode = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : null;
                _stlGenerationHealthCheckStatus.RecordGenerationFailed(
                    source,
                    uuid,
                    userId,
                    traceId,
                    statusCode,
                    stopwatch.Elapsed,
                    ex);
                await SendGenerationFailureAlertAsync(source, uuid, userId, traceId, statusCode, ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                _stlGenerationHealthCheckStatus.RecordGenerationFailed(
                    source,
                    uuid,
                    userId,
                    traceId,
                    null,
                    stopwatch.Elapsed,
                    ex);
                await SendGenerationFailureAlertAsync(source, uuid, userId, traceId, null, ex.Message);
                throw;
            }
        }

        private async Task SendGenerationFailureAlertAsync(string source, Guid uuid, string userId, string traceId, int? httpStatusCode, string reason)
        {
            var body = JsonSerializer.Serialize(new
            {
                message = "STL generation request failed.",
                source,
                uuid,
                user_id = userId,
                trace_id = traceId,
                http_status_code = httpStatusCode,
                reason,
                occurred_at_utc = DateTime.UtcNow
            });

            foreach (var recipient in StlGenerationAlertRecipients)
            {
                await _emailHelper.Send(
                    "no_reply@comfyplus.org",
                    recipient,
                    "Ergono3D STL generation request alert",
                    body);
            }
        }

        private string GetStlGenerationEndpoint(string stlParams)
        {
            var data = Uri.EscapeDataString(Convert.ToBase64String(Encoding.UTF8.GetBytes(stlParams)));
#if DEBUG
            if (!_rhinoCompute.Enabled)
                return $"http://13.55.129.243/StarDesigner/insoles?data={data}";
#endif
            return $"{RhinoComputeLifetime.BaseUrl}/StarDesigner/insoles?data={data}";
        }

        [HttpPost("get-stl")]
        [Authorize]
        public async Task<Result> GetStl(GetStlBody body)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");

            body.Bs64 = ParamValidator.Normalize(body.Bs64);

            var lease = await _orderedGenerationGate.WaitForLockAsync(_httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
            try
            {
                _logger.LogInformation("[STL_GATE_DEPLOY_CHECK] get-stl acquired OrderedGenerationGate. TraceId={TraceId}", HttpContext.TraceIdentifier);
                UserEntity user = await _primaryDbContext.Users.Where(u => u.Id == new Guid(userId)).FirstAsync();
                if (user.ExpiredTime is null) return Result.Failed(-1, null);
                if (user.ExpiredTime < DateTime.UtcNow)
                {
                    user.ExpiredTime = null;
                    user.Times = 0;
                    await _primaryDbContext.SaveChangesAsync();
                    return Result.Failed(-1, null);
                }
                CostEntity? costEntity = await _primaryDbContext.Costs.Where(i => i.Id == body.Uuid).FirstOrDefaultAsync();
                bool consumesCredit = costEntity is null || costEntity.Remain == 0;
                if (consumesCredit && user.Times <= 0) return Result.Failed(-1, null);

                string stlBs64;
                try
                {
                    stlBs64 = await RequestStlFromRhinoAsync("get-stl", body.Uuid, userId, body.Bs64);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "STL generation failed before quota was consumed. TraceId={TraceId}", HttpContext.TraceIdentifier);
                    return Result.Failed(-3, "Generation failed. Please try again later.");
                }

                if (consumesCredit)
                {
                    user.Times--;
                    if (costEntity is null) _primaryDbContext.Costs.Add(costEntity = new CostEntity { Id = body.Uuid, Remain = 6 });
                    else costEntity.Remain = 5;
                }
                costEntity!.Remain--;
                ParamsEntity? p;
                if (Cache.TryGetValue(body.Uuid, out Pre? pre))
                {
                    _primaryDbContext.Params.Add(new ParamsEntity
                    {
                        Id = body.Uuid,
                        Step1 = JsonSerializer.Serialize(pre!.Step1),
                        Step2 = JsonSerializer.Serialize(pre!.Step2),
                        Step3 = body.Bs64
                    });
                    Cache.Remove(body.Uuid);
                }
                else if ((p = await _primaryDbContext.Params.Where(u => u.Id == body.Uuid).FirstOrDefaultAsync()) is not null)
                {
                    p.Step3 = body.Bs64;
                }
                if (user.Times == 0)
                {
                    await _primaryDbContext.SaveChangesAsync();
                    string? param = await _primaryDbContext.Params.Where(p => p.Id == body.Uuid).Select(i => i.Step1).FirstOrDefaultAsync();
                    if (param is null) return Result.Failed(-1);
                    using JsonDocument doc = JsonDocument.Parse(param);
                    STLEntity? stl = _primaryDbContext.STLs.Where(i => i.Id == body.Uuid).FirstOrDefault();
                    if (stl is null)
                        _primaryDbContext.STLs.Add(new STLEntity
                        {
                            Id = body.Uuid,
                            UserId = Guid.Parse(userId),
                            Params = param,
                            CreateTime = DateTime.UtcNow,
                            Name = doc.RootElement.GetProperty("Name_").GetString()!
                        });
                    else
                    {
                        stl.Id = body.Uuid;
                        stl.UserId = Guid.Parse(userId);
                        stl.Params = param;
                        stl.CreateTime = DateTime.UtcNow;
                        stl.Name = doc.RootElement.GetProperty("Name_").GetString()!;
                    }
                    await STLManager.SaveAsync(stlBs64, body.Uuid.ToString());
                }
                await _primaryDbContext.SaveChangesAsync();
                return Result.Success(stlBs64);
            }
            finally
            {
                _orderedGenerationGate.ReleaseLock(lease);
            }
        }


        [HttpPost("get-stl3")]
        [Authorize]
        public async Task<Result> GetStl3(GetStlBody body)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");

            body.Bs64 = ParamValidator.Normalize(body.Bs64);

            var lease = await _orderedGenerationGate.WaitForLockAsync(_httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
            try
            {
                _logger.LogInformation("[STL_GATE_DEPLOY_CHECK] get-stl3 acquired OrderedGenerationGate. TraceId={TraceId}", HttpContext.TraceIdentifier);
                UserEntity user = await _primaryDbContext.Users.Where(u => u.Id == new Guid(userId)).FirstAsync();
                if (user.ExpiredTime is null) return Result.Failed(-1, null);
                if (user.ExpiredTime < DateTime.UtcNow)
                {
                    user.ExpiredTime = null;
                    user.Times = 0;
                    await _primaryDbContext.SaveChangesAsync();
                    return Result.Failed(-1, null);
                }
                CostEntity? costEntity = await _primaryDbContext.Costs.Where(i => i.Id == body.Uuid).FirstOrDefaultAsync();
                bool consumesCredit = costEntity is null || costEntity.Remain == 0;
                if (consumesCredit && user.Times <= 0) return Result.Failed(-1, null);

                string stlBs64;
                try
                {
                    stlBs64 = await RequestStlFromRhinoAsync("get-stl3", body.Uuid, userId, body.Bs64);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "STL generation failed before quota was consumed. TraceId={TraceId}", HttpContext.TraceIdentifier);
                    return Result.Failed(-3, "Generation failed. Please try again later.");
                }

                if (consumesCredit)
                {
                    user.Times--;
                    if (costEntity is null) _primaryDbContext.Costs.Add(costEntity = new CostEntity { Id = body.Uuid, Remain = 6 });
                    else costEntity.Remain = 5;
                }
                costEntity!.Remain--;
                ParamsEntity? p;
                if (Cache.TryGetValue(body.Uuid, out Pre2? pre))
                {
                    _primaryDbContext.Params.Add(new ParamsEntity
                    {
                        Id = body.Uuid,
                        Step1 = JsonSerializer.Serialize(pre!.Step1),
                        Step2 = JsonSerializer.Serialize(pre!.Step2),
                        Step3 = body.Bs64
                    });
                    Cache.Remove(body.Uuid);
                }
                else if ((p = await _primaryDbContext.Params.Where(u => u.Id == body.Uuid).FirstOrDefaultAsync()) is not null)
                {
                    p.Step3 = body.Bs64;
                }
                if (user.Times == 0)
                {
                    await _primaryDbContext.SaveChangesAsync();
                    string? param = await _primaryDbContext.Params.Where(p => p.Id == body.Uuid).Select(i => i.Step1).FirstOrDefaultAsync();
                    if (param is null) return Result.Failed(-1);
                    using JsonDocument doc = JsonDocument.Parse(param);
                    STLEntity? stl = _primaryDbContext.STLs.Where(i => i.Id == body.Uuid).FirstOrDefault();
                    if (stl is null)
                        _primaryDbContext.STLs.Add(new STLEntity
                        {
                            Id = body.Uuid,
                            UserId = Guid.Parse(userId),
                            Params = param,
                            CreateTime = DateTime.UtcNow,
                            Name = doc.RootElement.GetProperty("Name_").GetString()!
                        });
                    else
                    {
                        stl.Id = body.Uuid;
                        stl.UserId = Guid.Parse(userId);
                        stl.Params = param;
                        stl.CreateTime = DateTime.UtcNow;
                        stl.Name = doc.RootElement.GetProperty("Name_").GetString()!;
                    }
                    await STLManager.SaveAsync(stlBs64, body.Uuid.ToString());
                }
                await _primaryDbContext.SaveChangesAsync();
                return Result.Success(stlBs64);
            }
            finally
            {
                _orderedGenerationGate.ReleaseLock(lease);
            }
        }

        [HttpPost("get-stl2")]
        [Authorize]
        public async Task<Result> GetStl2(GetStlBody body)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");

            body.Bs64 = ParamValidator.Normalize(body.Bs64);

            var lease = await _orderedGenerationGate.WaitForLockAsync(_httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
            try
            {
                UserEntity user = await _primaryDbContext.Users.Where(u => u.Id == new Guid(userId)).FirstAsync();
                if (user.ExpiredTime is null) return Result.Failed(-1, null);
                if (user.ExpiredTime < DateTime.UtcNow)
                {
                    user.ExpiredTime = null;
                    user.Times = 0;
                    await _primaryDbContext.SaveChangesAsync();
                    return Result.Failed(-1, null);
                }
                CostEntity? costEntity = await _primaryDbContext.Costs.Where(i => i.Id == body.Uuid).FirstOrDefaultAsync();
                bool consumesCredit = costEntity is null || costEntity.Remain == 0;
                if (consumesCredit && user.Times <= 0) return Result.Failed(-1, null);

                string stlBs64;
                try
                {
                    stlBs64 = await RequestStlFromRhinoAsync("get-stl2", body.Uuid, userId, body.Bs64);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "STL generation failed before quota was consumed. TraceId={TraceId}", HttpContext.TraceIdentifier);
                    return Result.Failed(-3, "Generation failed. Please try again later.");
                }

                if (consumesCredit)
                {
                    user.Times--;
                    if (costEntity is null) _primaryDbContext.Costs.Add(costEntity = new CostEntity { Id = body.Uuid, Remain = 6 });
                    else costEntity.Remain = 5;
                }
                costEntity!.Remain--;
                ParamsEntity? p;
                if (Cache.TryGetValue(body.Uuid, out Pre? pre))
                {
                    _primaryDbContext.Params.Add(new ParamsEntity
                    {
                        Id = body.Uuid,
                        Step1 = JsonSerializer.Serialize(pre!.Step1),
                        Step2 = JsonSerializer.Serialize(pre!.Step2),
                        Step3 = body.Bs64
                    });
                    Cache.Remove(body.Uuid);
                }
                else if ((p = await _primaryDbContext.Params.Where(u => u.Id == body.Uuid).FirstOrDefaultAsync()) is not null)
                {
                    p.Step3 = body.Bs64;
                }
                string? param = await _primaryDbContext.Params.Where(p => p.Id == body.Uuid).Select(i => i.Step1).FirstOrDefaultAsync();
                if (param is null) return Result.Failed(-1);
                using JsonDocument doc = JsonDocument.Parse(param);
                STLEntity? stl = _primaryDbContext.STLs.Where(i => i.Id == body.Uuid).FirstOrDefault();
                if (stl is null)
                    _primaryDbContext.STLs.Add(new STLEntity
                    {
                        Id = body.Uuid,
                        UserId = Guid.Parse(userId),
                        Params = param,
                        CreateTime = DateTime.UtcNow,
                        Name = doc.RootElement.GetProperty("Name_").GetString()!
                    });
                else
                {
                    stl.Id = body.Uuid;
                    stl.UserId = Guid.Parse(userId);
                    stl.Params = param;
                    stl.CreateTime = DateTime.UtcNow;
                    stl.Name = doc.RootElement.GetProperty("Name_").GetString()!;
                }
                await STLManager.SaveAsync(stlBs64, body.Uuid.ToString());

                await _primaryDbContext.SaveChangesAsync();
                return Result.Success(stlBs64);
            }
            finally
            {
                _orderedGenerationGate.ReleaseLock(lease);
            }
        }

        [HttpPost("send-stl")]
        [Authorize]
        public async Task<Result> SendStl(SendSTLBody body)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");

            await _emailHelper.SendMailWithBase64Attachment(body.Email, body.Bs64);
            return Result.Success(null);
        }

        [HttpGet("record/{page}/{search}")]
        [Authorize]
        public async Task<Result> LoadRecord(int page, string search)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");
            search = search[1..];
            return Result.Success(await _primaryDbContext.STLs.Where(i => i.UserId == Guid.Parse(userId) && (search == "" || i.Name.Contains(search))).OrderByDescending(i => i.CreateTime).Skip(page * 10).Take(10).Select(i => new { i.Id, i.Params, i.CreateTime }).ToListAsync());
            //return Result.Success(await _primaryDbContext.)
        }

        [HttpGet("record/page-count/{search}")]
        [Authorize]
        public async Task<Result> GetPageCount(string search)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");
            search = search[1..];
            return Result.Success(Math.Ceiling((double)(await _primaryDbContext.STLs.Where(i => i.UserId == Guid.Parse(userId) && (search == "" || i.Name.Contains(search))).CountAsync()) / 10));
        }

        [HttpPost("save-record")]
        [Authorize]
        public async Task<Result> SaveRecord(SaveSTL stl)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");
            if (await _primaryDbContext.Users.Where(u => u.Id == Guid.Parse(userId)).Select(i => i.IsPreview).FirstAsync()) return Result.Failed(-2);

            if (!await _primaryDbContext.STLs.Where(i => i.Id == stl.Id && i.UserId == Guid.Parse(userId)).AnyAsync())
            {
                string? param = await _primaryDbContext.Params.Where(p => p.Id == stl.Id).Select(i => i.Step1).FirstOrDefaultAsync();
                if (param is null) return Result.Failed(-1);
                using JsonDocument doc = JsonDocument.Parse(param);
                _primaryDbContext.STLs.Add(new STLEntity
                {
                    Id = stl.Id,
                    UserId = Guid.Parse(userId),
                    Params = param,
                    CreateTime = DateTime.UtcNow,
                    Name = doc.RootElement.GetProperty("Name_").GetString()!
                });
                await _primaryDbContext.SaveChangesAsync();
            }
            await STLManager.SaveAsync(stl.Bs64, stl.Id.ToString());
            return Result.Success(null);
        }

        [HttpGet("download-stl/{id}")]
        [Authorize]
        public async Task<Result> DownloadStl(string id)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");
            return Result.Success(await STLManager.ReadAsync(id));
        }

        [HttpGet("try-download-stl/{id}")]
        [Authorize]
        public async Task<Result> TryDownloadStl(string id)
        {
            static string ReplaceName(string a, string b)
            {
                var node = JsonNode.Parse(a) as JsonObject;
                if (node == null) return a;

                node["Name_"] = b;

                return node.ToJsonString();
            }

            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");

            Guid iid = new Guid(id);
            if (!await _primaryDbContext.STLs.Where(stl => stl.Id == iid && stl.UserId == new Guid(userId)).AnyAsync()) return Result.Failed(-1);
            ParamsEntity? param = await _primaryDbContext.Params.Where(p => p.Id == iid).FirstOrDefaultAsync();
            STLEntity? stlEntity = await _primaryDbContext.STLs.Where(i => i.Id == iid).FirstOrDefaultAsync();

            if (param is null || stlEntity is null) return Result.Failed(-2);

            ParamsEntity newParams = new ParamsEntity
            {
                Id = Guid.NewGuid(),
                Step1 = param.Step1,
                Step2 = param.Step2,
                Step3 = param.Step3
            };

            string name = $"{stlEntity.Name}_{DateTime.UtcNow.ToString("yyMMdd")}";
            STLEntity newStl = new STLEntity
            {
                Id = newParams.Id,
                Name = name,
                Params = ReplaceName(stlEntity.Params, name),
                CreateTime = DateTime.UtcNow,
                UserId = stlEntity.UserId
            };
            _primaryDbContext.Params.Add(newParams);
            _primaryDbContext.STLs.Add(newStl);
            await _primaryDbContext.SaveChangesAsync();
            string? stlData = await STLManager.ReadAsync(id);
            if (stlData is null)
            {
                var lease = await _orderedGenerationGate.WaitForLockAsync(_httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None);
                try
                {
                    _logger.LogInformation("[STL_GATE_DEPLOY_CHECK] try-download-stl acquired OrderedGenerationGate after cache miss. TraceId={TraceId}", HttpContext.TraceIdentifier);
                    stlData = await RequestStlFromRhinoAsync("try-download-stl", newParams.Id, userId, param.Step3);
                }
                finally
                {
                    _orderedGenerationGate.ReleaseLock(lease);
                }
            }
            await STLManager.SaveAsync(stlData!, newParams.Id.ToString());
            return Result.Success(new { Id = newParams.Id, Param = newParams, Stl = stlData });
        }


        [HttpPost("auto-update")]
        [Authorize]
        public async Task<Result> AutoUpdateParams([FromBody] JsonElement body)
        {
            string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
            if (userId == null) return Result.Failed(-2, "Please login again.");
            string newParams = await AI.AutoUpdateParam(body.GetProperty("params").GetString()!, body.GetProperty("advice").GetString()!);
            return Result.Success(ParamValidator.Normalize(newParams.Replace("```json\n", "").Replace("\n```", "")));
        }

        #region

        public class Step3
        {
            public required string Daily_routine { get; set; }

            public required string Sub_category { get; set; }

            public required string Foot_arch_assessment { get; set; }

            public required string Loading_wear_pattern { get; set; }

            public required string[] Concern_area { get; set; }

            public required string[] Usage_scenario { get; set; }

            public required string Prescription_goal { get; set; }

            public required string Shoe_type { get; set; }

            public required string Personal_description { get; set; }

            public required string Previous_insoles { get; set; }

        }

        public class Step2
        {
            public required string Daily_routine { get; set; }

            public required string Sub_category { get; set; }

            public required string Arch_type { get; set; }

            public required string Wear_pattern { get; set; }

            public required string[] Discomfort_area { get; set; }

            public required string[] Usage_scenario { get; set; }

            public required string Priority { get; set; }

            public required string Shoe_type { get; set; }

            public required string Personal_description { get; set; }

            public required string Previous_insoles { get; set; }

        }

        public class Step1
        {

            public required string Name_ { get; set; }

            public required string Usertype_ { get; set; }

            public required string Size_ { get; set; }

            public string? Age_ { get; set; }

            public string? Height_ { get; set; }

            public string? Weight_ { get; set; }

        }

        public class Pre
        {
            public required Step1 Step1 { get; set; }

            public required Step2 Step2 { get; set; }

        }

        public class Pre2
        {
            public required Step1 Step1 { get; set; }

            public required Step3 Step2 { get; set; }

        }

        public class PreAi
        {
            public required Step1 Step1 { get; set; }

            public required string ImageBase64 { get; set; }

            public required string MediaType { get; set; }
        }


        public class SendSTLBody
        {
            public required string Bs64 { get; set; }

            public required string Email { get; set; }
        }

        public class SaveSTL
        {
            public required string Bs64 { get; set; }

            public required Guid Id { get; set; }
        }


        #endregion

    }
}
