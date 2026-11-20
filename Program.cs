using inso.Utils;
using InsoleApi.Controllers;
using InsoleApi.Database;
using InsoleApi.Utils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Stripe;
using System.ComponentModel;
using System.Text;

namespace InsoleApi
{
    public class Program
    {
        public static void Main(string[] args)
        {
            const string initializeDatabaseArgument = "--initialize-test-database";
            var initializeTestDatabase = args.Contains(initializeDatabaseArgument, StringComparer.Ordinal);
            var builder = WebApplication.CreateBuilder(args.Where(arg => arg != initializeDatabaseArgument).ToArray());
            var logFilePath = GetLogFilePath(builder.Configuration["Deployment:LogDirectory"]);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ApplicationPath", GetApplicationPath())
                .WriteTo.Console()
                .WriteTo.File(
                    logFilePath,
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: 50 * 1024 * 1024,
                    retainedFileCountLimit: 30,
                    shared: true,
                    buffered: false,
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
            builder.Host.UseSerilog();
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            Log.Information("Starting InsoleApi. Log file: {LogFilePath}", logFilePath);
            var beijingTime = TimeZoneInfo.ConvertTime(
                DateTimeOffset.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"));
            Log.Debug("InsoleApi startup time (Beijing): {BeijingTime:yyyy-MM-dd HH:mm:ss zzz}", beijingTime);

            PayStripe2Controller.PriceDict = builder.Configuration.GetSection("Price").Get<Dictionary<string, string>>()!;
            StripeConfiguration.ApiKey = builder.Configuration["Stripe-api-key"];
#if !DEBUG
            //BackupSQL();
#endif
            // Add services to the container.
            //StripeConfiguration.ApiKey = builder.Configuration["Stripe-api-key"];
            PrimaryDbContext.NPGSQL_URL = builder.Configuration["ConnectionStrings:Primary"]!;
            builder.Services.AddDbContext<PrimaryDbContext>();
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = builder.Configuration["Jwt:Issuer"],
                    ValidAudience = builder.Configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!))
                };
            });

            builder.Services.AddSingleton<ConfigurationManager>(options => builder.Configuration);
            builder.Services.AddSingleton<EmailHelper>();
            builder.Services.AddSingleton<JwtHelper>();
            builder.Services.AddScoped<GenerationUsageStatisticsService>();
            builder.Services.AddSingleton<StlGenerationHealthCheckStatus>();
            builder.Services.AddSingleton<OrderedGenerationGate>();
            builder.Services.AddSingleton<RhinoComputeLifetime>();
            var isIsolatedTest = builder.Environment.IsEnvironment("Test");
            var isTestEnv = isIsolatedTest || builder.Configuration["urls"] == "http://localhost:5033";
            var shouldRunStlGenerationHealthCheck = !isTestEnv &&
                string.IsNullOrWhiteSpace(builder.Configuration["RhinoCompute:LauncherPath"]);
#if DEBUG
            shouldRunStlGenerationHealthCheck = false;
#endif
            if (shouldRunStlGenerationHealthCheck)
            {
                builder.Services.AddHostedService<StlGenerationHealthCheckService>();
            }
            builder.Services.AddControllers();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowDevelopment",
                    builder =>
                    {
                        builder.WithOrigins("http://localhost:5173").AllowAnyMethod().AllowAnyHeader();
                    });
            });

            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowProd",
                    builder =>
                    {
                        builder.WithOrigins("https://stage.ergono3d.com").AllowAnyMethod().AllowAnyHeader();
                        builder.WithOrigins("https://demo.ergono3d.com").AllowAnyMethod().AllowAnyHeader();
                        builder.WithOrigins("https://ergono3d.com").AllowAnyMethod().AllowAnyHeader();
                    });
            });

            var app = builder.Build();

            if (initializeTestDatabase)
            {
                var database = new Npgsql.NpgsqlConnectionStringBuilder(PrimaryDbContext.NPGSQL_URL);
                if (!isIsolatedTest || database.Host != "127.0.0.1" || database.Port != 55432 || database.Database != "ergono_test")
                {
                    throw new InvalidOperationException("Schema initialization requires the local isolated test database.");
                }
                using var scope = app.Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<PrimaryDbContext>().Database.EnsureCreated();
                return;
            }

            if (app.Environment.IsDevelopment())
            {
                app.UseCors("AllowDevelopment");
            }

            if (!isIsolatedTest)
            {
                app.UseCors("AllowProd");
            }

            // Configure the HTTP request pipeline.

            //app.UseHttpsRedirection();

            app.UseSerilogRequestLogging();

            app.UseAuthorization();

            app.MapControllers();
            if (isIsolatedTest)
            {
                app.MapGet("/health/ready", async (PrimaryDbContext db, CancellationToken cancellationToken) =>
                {
                    var canConnect = await db.Database.CanConnectAsync(cancellationToken);
                    return canConnect
                        ? Results.Ok(new { release = builder.Configuration["Deployment:Release"] })
                        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
                });
            }
            //GenerateData();

            // appsettings.json 中 "urls": "http://localhost:5033" 取消注释时为测试环境，跳过邮件提醒避免重复发送
            if (!isTestEnv)
            {
                StartExpireNotifyLoop();
            }

            app.Lifetime.ApplicationStopped.Register(Log.CloseAndFlush);
            app.Run();
        }

        private static string GetLogFilePath(string? configuredDirectory)
        {
            var logDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
                ? @"C:\Ergono3DLogs"
                : Path.GetFullPath(configuredDirectory);
            Directory.CreateDirectory(logDirectory);

            var applicationPath = GetApplicationPath();
            var fullApplicationPath = Path.GetFullPath(applicationPath);
            var extension = Path.GetExtension(fullApplicationPath);
            var pathWithoutExtension = string.IsNullOrEmpty(extension)
                ? fullApplicationPath
                : fullApplicationPath[..^extension.Length];
            var logName = pathWithoutExtension
                .Replace(':', '-')
                .Replace('\\', '-')
                .Replace('/', '-');

            foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
                logName = logName.Replace(invalidCharacter, '-');

            while (logName.Contains("--", StringComparison.Ordinal))
                logName = logName.Replace("--", "-", StringComparison.Ordinal);

            logName = logName.Trim('-').ToLowerInvariant();
            return Path.Combine(logDirectory, $"{logName}.log");
        }

        private static string GetApplicationPath()
        {
            var applicationAssemblyPath = typeof(Program).Assembly.Location;
            var processPath = Environment.ProcessPath;

            if (!string.IsNullOrWhiteSpace(processPath) &&
                string.Equals(
                    Path.GetFileNameWithoutExtension(processPath),
                    Path.GetFileNameWithoutExtension(applicationAssemblyPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                return processPath;
            }

            var applicationExecutablePath = Path.ChangeExtension(applicationAssemblyPath, ".exe");
            return System.IO.File.Exists(applicationExecutablePath) ? applicationExecutablePath : applicationAssemblyPath;
        }

        static public void StartExpireNotifyLoop()
        {
            _ = Task.Run(async () =>
            {
                // 每天北京时间 16:30 执行一次
                var now = DateTime.Now;
                var nextRun = now.Date.AddHours(16).AddMinutes(30);
                if (nextRun <= now)
                    nextRun = nextRun.AddDays(1);

                while (true)
                {
                    var delay = nextRun - DateTime.Now;
                    if (delay.TotalMilliseconds > 0)
                        await Task.Delay(delay);

                    try
                    {
                        await NotifyExpiringUsersAsync();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                    }

                    nextRun = nextRun.AddDays(1);
                }
            });
        }


        static public async Task NotifyExpiringUsersAsync()
        {
            var now = DateTime.UtcNow;

            // 窗口与北京时间16:00对齐：查找6-7天后到期的用户（UTC精确时刻，不取零点）
            var start = now.AddDays(6);
            var end = now.AddDays(7);

            using var db = new PrimaryDbContext(new Microsoft.EntityFrameworkCore.DbContextOptions<PrimaryDbContext>());


            var query = db.Users
                .Where(u => u.ExpiredTime != null
                            && u.ExpiredTime >= start
                            && u.ExpiredTime < end)
                .Select(u => u.Email);

            // TODO: 测试完成后移除此行，开放全部用户
            query = query.Where(email => email == "xurui@comfyplus.org" || email == "wangfrank113@gmail.com" || email == "team@ergono3d.com");

            var emails = await query.ToListAsync();
            EmailHelper emailHelper = new EmailHelper();
            foreach (var email in emails)
            {
                await Notify(email, emailHelper);
            }
        }


        static public async Task Notify(string email, EmailHelper emailHelper)
        {
            await emailHelper.Send("no_reply@comfyplus.org", email!, "Ergono3D", """
                                <table width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#f5f7fa; padding:24px 0; font-family:Arial, Helvetica, sans-serif; color:#111827;">
                  <tr>
                    <td align="center">
                      <table width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:640px; background-color:#ffffff; border-radius:12px; overflow:hidden;">
                        <!-- Header / Logo -->
                        <tr>
                          <td style="padding:30px 32px 18px 32px;">
                            <a href="https://ergono3d.com" target="_blank" style="text-decoration:none;">
                              <img
                                src="https://ergono3d.com/assets/img/logo.png"
                                alt="Ergono3D"
                                width="160"
                                style="display:block; width:160px; max-width:160px; height:auto;"
                              />
                            </a>
                            <div style="font-size:14px; color:#6b7280; margin-top:10px;">
                              Parametric 3D-printable insole design
                            </div>
                          </td>
                        </tr>
                        <!-- Hero Image -->
                        <tr>
                          <td style="padding:0 32px 24px 32px;">
                            <img
                              src="https://ergono3d.com/assets/img/custom-3d-insole-design.png"
                              alt="Custom 3D insole design generated by Ergono3D"
                              width="576"
                              style="display:block; width:100%; max-width:576px; height:auto; border-radius:12px; border:1px solid #e5e7eb;"
                            />
                          </td>
                        </tr>
                        <!-- Main Content -->
                        <tr>
                          <td style="padding:0 32px 32px 32px;">
                            <h1 style="font-size:28px; line-height:1.3; margin:0 0 20px 0; color:#111827;">
                              Your Ergono3D credits expire in 7 days
                            </h1>
                            <p style="font-size:16px; line-height:1.7; margin:0 0 16px 0;">
                              Hi there,
                            </p>
                            <p style="font-size:16px; line-height:1.7; margin:0 0 16px 0;">
                              This is a reminder that your Ergono3D <strong>credits</strong> will expire in <strong>7 days</strong>.
                            </p>
                            <p style="font-size:16px; line-height:1.7; margin:0 0 16px 0;">
                              You can use your credits to create, preview, and refine a 3D-printable insole model based on your foot size, shoe type, activity, and support needs.
                            </p>
                            <p style="font-size:16px; line-height:1.7; margin:0 0 24px 0;">
                              If you have not started yet, we recommend creating your first insole with the default design settings, then adjusting the model based on comfort, pressure, stability, or fit.
                            </p>
                            <!-- CTA Button -->
                            <table cellpadding="0" cellspacing="0" border="0" style="margin:0 0 28px 0;">
                              <tr>
                                <td align="center" bgcolor="#111827" style="border-radius:8px;">
                                  <a
                                    href="https://ergono3d.com?utm_source=credit_expiry_email&utm_medium=email&utm_campaign=credits_expire_7_days"
                                    target="_blank"
                                    style="display:inline-block; padding:14px 24px; font-size:16px; font-weight:700; color:#ffffff; text-decoration:none; border-radius:8px;"
                                  >
                                    Use Your Credits
                                  </a>
                                </td>
                              </tr>
                            </table>
                            <h2 style="font-size:20px; line-height:1.4; margin:0 0 12px 0; color:#111827;">
                              Need help before they expire?
                            </h2>
                            <p style="font-size:16px; line-height:1.7; margin:0 0 24px 0;">
                              Reply to
                              <a href="mailto:team@ergono3d.com" style="color:#2563eb; text-decoration:underline;">
                                team@ergono3d.com
                              </a>
                              with your shoe type, activity, foot size, and what you want to improve. We’ll help you choose a reasonable starting point.
                            </p>
                            <p style="font-size:16px; line-height:1.7; margin:0 0 24px 0;">
                              You can also read our DIY guide here:
                              <a href="https://ergono3d.com/blog/diy-3d-printed-insoles?utm_source=credit_expiry_email&utm_medium=email&utm_campaign=credits_expire_7_days" target="_blank" style="color:#2563eb; text-decoration:underline;">
                                How to Make Custom 3D Printed TPU Insoles with Ergono3D
                              </a>.
                            </p>
                            <!-- Disclaimer -->
                            <table width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:#fff7ed; border:1px solid #fed7aa; border-radius:10px; margin:0 0 28px 0;">
                              <tr>
                                <td style="padding:18px;">
                                  <p style="font-size:13px; line-height:1.6; margin:0; color:#7c2d12;">
                                    <strong>Important note:</strong> Ergono3D is a 3D insole design and printing workflow tool. If you have severe pain, injury, numbness, diabetes-related foot issues, or post-surgery conditions, please consult a qualified healthcare professional.
                                  </p>
                                </td>
                              </tr>
                            </table>
                            <p style="font-size:16px; line-height:1.7; margin:0;">
                              Thanks,<br />
                              The Ergono3D Team
                            </p>

                          </td>
                        </tr>
                        <!-- Footer -->
                        <tr>
                          <td style="padding:24px 32px; background-color:#f9fafb; border-top:1px solid #e5e7eb;">
                            <p style="font-size:12px; line-height:1.6; color:#6b7280; margin:0 0 8px 0;">
                              You are receiving this email because you have Ergono3D credits associated with your account.
                            </p>
                            <p style="font-size:12px; line-height:1.6; color:#6b7280; margin:0 0 8px 0;">
                              Website:
                              <a href="https://ergono3d.com" target="_blank" style="color:#2563eb; text-decoration:underline;">
                                ergono3d.com
                              </a>
                            </p>
                            <p style="font-size:12px; line-height:1.6; color:#6b7280; margin:0;">
                              © 2026 Ergono3D. All rights reserved.
                            </p>
                          </td>
                        </tr>
                      </table>
                    </td>
                  </tr>
                </table>
                """, "html");

        }

        static private async void GenerateData()
        {
            using var p = new PrimaryDbContext(new Microsoft.EntityFrameworkCore.DbContextOptions<PrimaryDbContext>());
            for (int i = 0; i < 50; i++)
            {
                p.STLs.Add(new STLEntity
                {
                    Id = Guid.NewGuid(),
                    CreateTime = DateTime.UtcNow.AddSeconds(i),
                    UserId = Guid.Parse("0adc6d37-4148-485a-8411-f3deef9ec1ee"),
                    Params = $"{{\"Name_\":\"Test{i}\",\"Usertype_\":\"Woman\",\"Size_\":\"37\",\"Age_\":\"21\",\"Height_\":\"\",\"Weight_\":\"\"}}",
                    Name = i.ToString()
                });
            }
            await p.SaveChangesAsync();
        }

        static private async void BackupSQL()
        {
            while (true)
            {
                PostgresBackup.BackupDatabase();
                await Task.Delay(TimeSpan.FromHours(24));
            }
        }

    }
}
