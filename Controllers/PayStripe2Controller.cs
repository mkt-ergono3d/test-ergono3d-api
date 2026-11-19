using InsoleApi.Common;
using InsoleApi.Database;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Stripe;
using Stripe.Checkout;

namespace InsoleApi.Controllers;

[Route("api/v1/pay/stripe2")]
[ApiController]
public class PayStripe2Controller : ControllerBase
{
    private const string DefaultPublicWebsite = "https://ergono3d.com";
    private const string StagePublicWebsite = "https://stage.ergono3d.com";

    private static readonly HashSet<string> AllowedCheckoutReturnOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "https://ergono3d.com",
        "https://stage.ergono3d.com",
    };

    public static Dictionary<string, string> PriceDict = new();

    private static readonly Dictionary<string, string> StripeCheckoutLocales = new(StringComparer.OrdinalIgnoreCase)
    {
        ["auto"] = "auto", ["bg"] = "bg", ["cs"] = "cs", ["da"] = "da", ["de"] = "de",
        ["el"] = "el", ["en"] = "en", ["en-GB"] = "en-GB", ["es"] = "es", ["es-419"] = "es-419",
        ["et"] = "et", ["fi"] = "fi", ["fil"] = "fil", ["fr"] = "fr", ["fr-CA"] = "fr-CA",
        ["hr"] = "hr", ["hu"] = "hu", ["id"] = "id", ["it"] = "it", ["ja"] = "ja",
        ["ko"] = "ko", ["lt"] = "lt", ["lv"] = "lv", ["ms"] = "ms", ["mt"] = "mt",
        ["nb"] = "nb", ["nl"] = "nl", ["pl"] = "pl", ["pt"] = "pt", ["pt-BR"] = "pt-BR",
        ["ro"] = "ro", ["ru"] = "ru", ["sk"] = "sk", ["sl"] = "sl", ["sv"] = "sv",
        ["th"] = "th", ["tr"] = "tr", ["vi"] = "vi", ["zh"] = "zh", ["zh-HK"] = "zh-HK",
        ["zh-TW"] = "zh-TW",
    };

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly PrimaryDbContext _primaryDbContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PayStripe2Controller> _logger;
    private readonly Uri _publicWebsite;

    public PayStripe2Controller(IHttpContextAccessor httpContextAccessor, PrimaryDbContext primaryDbContext, IConfiguration configuration, ILogger<PayStripe2Controller> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _primaryDbContext = primaryDbContext;
        _configuration = configuration;
        _logger = logger;
        _publicWebsite = ResolvePublicWebsite(configuration["PublicWebsite"]);
    }

    [HttpPost("create-checkout-session/{type}")]
    [Authorize]
    public async Task<Result> CreateCheckoutSession(string type, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateCheckoutSessionBody? body)
    {
        if (!TryGetAuthenticatedUserId(out Guid userId)) return Result.Failed(-2, "Please login again.");
        if (!PriceDict.TryGetValue(type, out string? priceId))
        {
            _logger.LogWarning("Stripe checkout rejected because no price is configured. ProductType: {ProductType}; PaymentAttemptId: {PaymentAttemptId}", type, body?.PaymentAttemptId);
            return Result.Failed(-1, "Unsupported payment product.");
        }

        var request = body ?? new CreateCheckoutSessionBody();
        Guid attemptId = request.PaymentAttemptId.GetValueOrDefault();
        if (attemptId == Guid.Empty) attemptId = Guid.NewGuid();

        PaymentAttemptEntity? attempt = await _primaryDbContext.PaymentAttempts
            .SingleOrDefaultAsync(item => item.Id == attemptId && item.UserId == userId);

        if (attempt is not null)
        {
            if (!string.Equals(attempt.ProductType, type, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Stripe checkout rejected because a payment attempt belongs to a different product. ProductType: {ProductType}; PaymentAttemptId: {PaymentAttemptId}", type, attempt.Id);
                return Result.Failed(-1, "Payment attempt does not match this product.");
            }

            if (!string.IsNullOrWhiteSpace(attempt.StripeCheckoutSessionId) && attempt.Status == PaymentAttemptStatus.CheckoutCreated)
            {
                var existingSession = await new SessionService().GetAsync(attempt.StripeCheckoutSessionId);
                if (!string.IsNullOrWhiteSpace(existingSession.Url))
                    return Result.Success(new CheckoutSessionResponse(existingSession.Url, attempt.Id, existingSession.Id));
            }

            _logger.LogWarning("Stripe checkout rejected because the payment attempt cannot be resumed. ProductType: {ProductType}; PaymentAttemptId: {PaymentAttemptId}; Status: {Status}", type, attempt.Id, attempt.Status);
            return Result.Failed(-1, "This payment attempt can no longer be resumed. Please start again.", new { paymentAttemptId = attempt.Id });
        }

        Uri returnWebsite = ResolveCheckoutReturnWebsite();
        string returnPath = NormalizeReturnPath(request.ReturnPath, returnWebsite);
        DateTime now = DateTime.UtcNow;
        attempt = new PaymentAttemptEntity
        {
            Id = attemptId,
            UserId = userId,
            ProductType = type,
            SourcePage = NormalizeContext(request.SourcePage, "/app/dashboard", 512),
            CtaPlacement = NormalizeContext(request.CtaPlacement, "unknown", 128),
            Language = NormalizeContext(request.Language, "en", 32),
            DesignType = NormalizeOptionalContext(request.DesignType, 128),
            ReturnPath = returnPath,
            Status = PaymentAttemptStatus.Pending,
            CreatedTime = now,
            UpdatedTime = now,
        };
        _primaryDbContext.PaymentAttempts.Add(attempt);
        await _primaryDbContext.SaveChangesAsync();

        try
        {
            var options = new SessionCreateOptions
            {
                ClientReferenceId = userId.ToString(),
                LineItems = [new SessionLineItemOptions { Price = priceId, Quantity = 1 }],
                Locale = ResolveStripeLocale(request.Locale),
                SuccessUrl = BuildWebsiteUrl(returnWebsite, "/app/payment/success", attempt.Id, "completed", includeStripeSessionPlaceholder: true),
                CancelUrl = BuildWebsiteUrl(returnWebsite, returnPath, attempt.Id, "canceled"),
                Mode = "payment",
                AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true },
                // Keep production sessions at Stripe's default expiry. Stage sessions expire
                // after 30 minutes so the checkout.session.expired webhook can be accepted
                // without changing production checkout behavior.
                ExpiresAt = IsStageCheckoutReturnWebsite(returnWebsite) ? DateTime.UtcNow.AddMinutes(30) : null,
                Metadata = CreateMetadata(userId, type, attempt.Id),
                PaymentIntentData = new SessionPaymentIntentDataOptions { Metadata = CreateMetadata(userId, type, attempt.Id) },
            };

            Session session = await new SessionService().CreateAsync(options, new RequestOptions { IdempotencyKey = attempt.Id.ToString("N") });
            attempt.StripeCheckoutSessionId = session.Id;
            attempt.Status = PaymentAttemptStatus.CheckoutCreated;
            attempt.UpdatedTime = DateTime.UtcNow;
            await _primaryDbContext.SaveChangesAsync();

            return Result.Success(new CheckoutSessionResponse(session.Url, attempt.Id, session.Id));
        }
        catch (StripeException exception)
        {
            _logger.LogWarning(exception, "Stripe Checkout session creation failed. ProductType: {ProductType}; PaymentAttemptId: {PaymentAttemptId}; StripeErrorCode: {StripeErrorCode}; StripeErrorType: {StripeErrorType}; HttpStatusCode: {HttpStatusCode}", type, attempt.Id, exception.StripeError?.Code, exception.StripeError?.Type, exception.HttpStatusCode);
            attempt.Status = PaymentAttemptStatus.Failed;
            attempt.FailureCode = string.IsNullOrWhiteSpace(exception.StripeError?.Code)
                ? "checkout_session_create_failed"
                : $"stripe_{exception.StripeError.Code}"[..Math.Min(128, $"stripe_{exception.StripeError.Code}".Length)];
            attempt.UpdatedTime = DateTime.UtcNow;
            await _primaryDbContext.SaveChangesAsync();
            return Result.Failed(-1, "Unable to start checkout.", new { paymentAttemptId = attempt.Id });
        }
    }

    [HttpGet("payment-attempt/{attemptId:guid}")]
    [Authorize]
    public async Task<Result> GetPaymentAttempt(Guid attemptId)
    {
        if (!TryGetAuthenticatedUserId(out Guid userId)) return Result.Failed(-2, "Please login again.");

        PaymentAttemptEntity? attempt = await _primaryDbContext.PaymentAttempts
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == attemptId && item.UserId == userId);
        if (attempt is null) return Result.Failed(-1, "Payment attempt not found.");

        return Result.Success(new PaymentAttemptStatusResponse(
            attempt.Id, attempt.ProductType, attempt.Status, attempt.StripeCheckoutSessionId,
            attempt.StripePaymentIntentId, attempt.Amount, attempt.Currency, attempt.FailureCode,
            attempt.SourcePage, attempt.CtaPlacement, attempt.Language, attempt.DesignType));
    }

    [HttpPost("payment-attempt/{attemptId:guid}/cancel")]
    [Authorize]
    public async Task<Result> CancelPaymentAttempt(Guid attemptId)
    {
        if (!TryGetAuthenticatedUserId(out Guid userId)) return Result.Failed(-2, "Please login again.");

        PaymentAttemptEntity? attempt = await _primaryDbContext.PaymentAttempts
            .SingleOrDefaultAsync(item => item.Id == attemptId && item.UserId == userId);
        if (attempt is null) return Result.Failed(-1, "Payment attempt not found.");

        if (attempt.Status == PaymentAttemptStatus.CheckoutCreated || attempt.Status == PaymentAttemptStatus.Pending)
        {
            attempt.Status = PaymentAttemptStatus.Canceled;
            attempt.UpdatedTime = DateTime.UtcNow;
            await _primaryDbContext.SaveChangesAsync();
        }

        return Result.Success(new PaymentAttemptStatusResponse(
            attempt.Id, attempt.ProductType, attempt.Status, attempt.StripeCheckoutSessionId,
            attempt.StripePaymentIntentId, attempt.Amount, attempt.Currency, attempt.FailureCode,
            attempt.SourcePage, attempt.CtaPlacement, attempt.Language, attempt.DesignType));
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> WebHook()
    {
        string json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"], _configuration["Stripe-webhook-key"]);
        }
        catch (StripeException)
        {
            return BadRequest();
        }

        await using var transaction = await _primaryDbContext.Database.BeginTransactionAsync();
        try
        {
            Guid? paymentAttemptId = GetPaymentAttemptId(stripeEvent.Data.Object);
            _primaryDbContext.StripeWebhookEvents.Add(new StripeWebhookEventEntity
            {
                StripeEventId = stripeEvent.Id,
                EventType = stripeEvent.Type,
                PaymentAttemptId = paymentAttemptId,
                ProcessedTime = DateTime.UtcNow,
            });
            await _primaryDbContext.SaveChangesAsync();

            switch (stripeEvent.Type)
            {
                case EventTypes.PaymentIntentSucceeded:
                    await HandlePaymentSucceeded((PaymentIntent)stripeEvent.Data.Object);
                    break;
                case EventTypes.PaymentIntentPaymentFailed:
                    await UpdatePaymentAttempt((PaymentIntent)stripeEvent.Data.Object, PaymentAttemptStatus.Failed, "payment_failed");
                    break;
                case EventTypes.CheckoutSessionExpired:
                    await UpdatePaymentAttempt((Session)stripeEvent.Data.Object, PaymentAttemptStatus.Expired, "checkout_expired");
                    break;
            }

            await transaction.CommitAsync();
            return Ok();
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync();
            return Ok();
        }
    }

    private async Task HandlePaymentSucceeded(PaymentIntent payment)
    {
        Guid? attemptId = GetPaymentAttemptId(payment);
        if (attemptId is null) return;

        // The row lock prevents two different Stripe events for the same attempt
        // from granting the entitlement twice before either marks it completed.
        PaymentAttemptEntity? attempt = await _primaryDbContext.PaymentAttempts
            .FromSqlInterpolated($"SELECT * FROM payment_attempt WHERE id = {attemptId.Value} FOR UPDATE")
            .SingleOrDefaultAsync();
        if (attempt is null || attempt.Status == PaymentAttemptStatus.Completed) return;

        string? userId = payment.Metadata.GetValueOrDefault("UserId");
        string? productType = payment.Metadata.GetValueOrDefault("ProductType");
        if (!Guid.TryParse(userId, out Guid parsedUserId) || string.IsNullOrWhiteSpace(productType) || parsedUserId != attempt.UserId)
        {
            attempt.Status = PaymentAttemptStatus.Failed;
            attempt.FailureCode = "invalid_payment_metadata";
            attempt.UpdatedTime = DateTime.UtcNow;
            await _primaryDbContext.SaveChangesAsync();
            return;
        }

        switch (productType)
        {
            case "download":
                await _primaryDbContext.Users.Where(user => user.Id == parsedUserId).ExecuteUpdateAsync(user => user
                    .SetProperty(item => item.IsPreview, false)
                    .SetProperty(item => item.Paid, true)
                    .SetProperty(item => item.ExpiredTime, DateTime.UtcNow.AddMonths(1)));
                break;
            case "lite":
                await _primaryDbContext.Users.Where(user => user.Id == parsedUserId).ExecuteUpdateAsync(user => user
                    .SetProperty(item => item.Times, item => item.Times + 10)
                    .SetProperty(item => item.IsPreview, false)
                    .SetProperty(item => item.Paid, true)
                    .SetProperty(item => item.ExpiredTime, DateTime.UtcNow.AddMonths(3)));
                break;
            case "pro":
                await _primaryDbContext.Users.Where(user => user.Id == parsedUserId).ExecuteUpdateAsync(user => user
                    .SetProperty(item => item.Times, item => item.Times + 50)
                    .SetProperty(item => item.IsPreview, false)
                    .SetProperty(item => item.Paid, true)
                    .SetProperty(item => item.ExpiredTime, DateTime.UtcNow.AddMonths(3)));
                break;
            default:
                attempt.Status = PaymentAttemptStatus.Failed;
                attempt.FailureCode = "unsupported_payment_product";
                attempt.UpdatedTime = DateTime.UtcNow;
                await _primaryDbContext.SaveChangesAsync();
                return;
        }

        attempt.Status = PaymentAttemptStatus.Completed;
        attempt.StripePaymentIntentId = payment.Id;
        attempt.Amount = payment.AmountReceived;
        attempt.Currency = payment.Currency?.ToUpperInvariant();
        attempt.FailureCode = null;
        attempt.UpdatedTime = DateTime.UtcNow;
        await _primaryDbContext.SaveChangesAsync();
    }

    private async Task UpdatePaymentAttempt(PaymentIntent payment, string status, string failureCode)
    {
        Guid? attemptId = GetPaymentAttemptId(payment);
        if (attemptId is null) return;

        PaymentAttemptEntity? attempt = await _primaryDbContext.PaymentAttempts.SingleOrDefaultAsync(item => item.Id == attemptId);
        if (attempt is null || attempt.Status == PaymentAttemptStatus.Completed) return;

        attempt.Status = status;
        attempt.StripePaymentIntentId = payment.Id;
        attempt.Amount = payment.Amount;
        attempt.Currency = payment.Currency?.ToUpperInvariant();
        attempt.FailureCode = failureCode;
        attempt.UpdatedTime = DateTime.UtcNow;
        await _primaryDbContext.SaveChangesAsync();
    }

    private async Task UpdatePaymentAttempt(Session session, string status, string failureCode)
    {
        Guid? attemptId = GetPaymentAttemptId(session);
        if (attemptId is null) return;

        PaymentAttemptEntity? attempt = await _primaryDbContext.PaymentAttempts.SingleOrDefaultAsync(item => item.Id == attemptId);
        if (attempt is null || attempt.Status == PaymentAttemptStatus.Completed) return;

        attempt.Status = status;
        attempt.StripeCheckoutSessionId = session.Id;
        attempt.FailureCode = failureCode;
        attempt.UpdatedTime = DateTime.UtcNow;
        await _primaryDbContext.SaveChangesAsync();
    }

    private bool TryGetAuthenticatedUserId(out Guid userId)
    {
        string? value = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(item => item.Type == "id")?.Value;
        return Guid.TryParse(value, out userId);
    }

    private static Dictionary<string, string> CreateMetadata(Guid userId, string productType, Guid attemptId) => new()
    {
        ["UserId"] = userId.ToString(),
        ["ProductType"] = productType,
        ["PaymentAttemptId"] = attemptId.ToString(),
    };

    private static Guid? GetPaymentAttemptId(object? stripeObject)
    {
        Dictionary<string, string>? metadata = stripeObject switch
        {
            PaymentIntent paymentIntent => paymentIntent.Metadata,
            Session session => session.Metadata,
            _ => null,
        };
        return metadata is not null && Guid.TryParse(metadata.GetValueOrDefault("PaymentAttemptId"), out Guid attemptId)
            ? attemptId
            : null;
    }

    private static string ResolveStripeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale)) return "auto";
        string normalized = locale.Trim().Replace('_', '-');
        if (StripeCheckoutLocales.TryGetValue(normalized, out string? stripeLocale)) return stripeLocale;
        string baseLocale = normalized.Split('-')[0];
        return StripeCheckoutLocales.TryGetValue(baseLocale, out stripeLocale) ? stripeLocale : "auto";
    }

    private static string NormalizeContext(string? value, string fallback, int maxLength)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeOptionalContext(string? value, int maxLength)
    {
        return string.IsNullOrWhiteSpace(value) ? null : NormalizeContext(value, string.Empty, maxLength);
    }

    private Uri ResolveCheckoutReturnWebsite()
    {
        string origin = Request.Headers.Origin.ToString();
        if (Uri.TryCreate(origin, UriKind.Absolute, out Uri? originUri)
            && string.IsNullOrEmpty(originUri.UserInfo)
            && (originUri.AbsolutePath == "/" || string.IsNullOrEmpty(originUri.AbsolutePath))
            && string.IsNullOrEmpty(originUri.Query)
            && string.IsNullOrEmpty(originUri.Fragment)
            && AllowedCheckoutReturnOrigins.Contains(originUri.GetLeftPart(UriPartial.Authority)))
        {
            return new Uri(originUri.GetLeftPart(UriPartial.Authority));
        }

        return _publicWebsite;
    }

    private static bool IsStageCheckoutReturnWebsite(Uri website)
    {
        return string.Equals(website.GetLeftPart(UriPartial.Authority), StagePublicWebsite, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeReturnPath(string? returnPath, Uri returnWebsite)
    {
        const string fallback = "/app/dashboard";
        if (string.IsNullOrWhiteSpace(returnPath)) return fallback;
        if (Uri.TryCreate(returnPath, UriKind.Absolute, out Uri? absoluteUri))
        {
            if (!string.Equals(absoluteUri.Scheme, returnWebsite.Scheme, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(absoluteUri.Host, returnWebsite.Host, StringComparison.OrdinalIgnoreCase)
                || absoluteUri.Port != returnWebsite.Port) return fallback;
            return absoluteUri.PathAndQuery;
        }

        return returnPath.StartsWith('/') && !returnPath.StartsWith("//", StringComparison.Ordinal)
            ? returnPath.Split('#')[0]
            : fallback;
    }

    private static string BuildWebsiteUrl(Uri returnWebsite, string path, Guid attemptId, string paymentStatus, bool includeStripeSessionPlaceholder = false)
    {
        string normalizedPath = NormalizeReturnPath(path, returnWebsite);
        string separator = normalizedPath.Contains('?') ? "&" : "?";
        string url = $"{returnWebsite.GetLeftPart(UriPartial.Authority)}{normalizedPath}{separator}payment_attempt_id={Uri.EscapeDataString(attemptId.ToString())}&payment_status={Uri.EscapeDataString(paymentStatus)}";
        return includeStripeSessionPlaceholder ? $"{url}&session_id={{CHECKOUT_SESSION_ID}}" : url;
    }

    private static Uri ResolvePublicWebsite(string? configuredValue)
    {
        if (Uri.TryCreate(configuredValue, UriKind.Absolute, out Uri? configuredUri)
            && (configuredUri.Scheme == Uri.UriSchemeHttp || configuredUri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(configuredUri.Query)
            && string.IsNullOrEmpty(configuredUri.Fragment))
        {
            return configuredUri;
        }

        return new Uri(DefaultPublicWebsite);
    }
}

public class CreateCheckoutSessionBody
{
    public string? Locale { get; set; }
    public Guid? PaymentAttemptId { get; set; }
    public string? SourcePage { get; set; }
    public string? CtaPlacement { get; set; }
    public string? Language { get; set; }
    public string? DesignType { get; set; }
    public string? ReturnPath { get; set; }
}

public record CheckoutSessionResponse(string? Url, Guid PaymentAttemptId, string? CheckoutSessionId);
public record PaymentAttemptStatusResponse(Guid PaymentAttemptId, string ProductType, string Status, string? CheckoutSessionId, string? PaymentIntentId, long? Amount, string? Currency, string? FailureCode, string SourcePage, string CtaPlacement, string Language, string? DesignType);
