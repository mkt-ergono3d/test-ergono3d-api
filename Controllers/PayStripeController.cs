using inso.Utils;
using InsoleApi.Common;
using InsoleApi.Database;
using InsoleApi.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Stripe;
using Stripe.Checkout;
using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Text.Json.Serialization;

namespace InsoleApi.Controllers
{
    [ApiController]
    [Route("/api/v1/pay/stripe")]
    public class PayStripeController : Controller
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        private readonly PrimaryDbContext _primaryDbContext;

        private readonly EmailHelper _emailHelper;

        private readonly JwtHelper _jwtHelper;

        private readonly ILogger<UserController> _logger;

        private readonly IConfiguration _configuration;

        private readonly IWebHostEnvironment _webHostEnvironment;

        public PayStripeController(IHttpContextAccessor httpContextAccessor, PrimaryDbContext primaryDbContext, EmailHelper emailHelper, JwtHelper jwtHelper, ILogger<UserController> logger, IConfiguration configuration, IWebHostEnvironment webHostEnvironment)
        {
            _httpContextAccessor = httpContextAccessor;
            _primaryDbContext = primaryDbContext;
            _emailHelper = emailHelper;
            _jwtHelper = jwtHelper;
            _logger = logger;
            _configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
        }

        //public class StripeRequestBody
        //{
        //    public required string ProductId { get; set; }
        //}

        ///// <summary>
        ///// Stripe创建支付页面
        ///// </summary>
        ///// <param name="body"></param>
        //[HttpGet("create-checkout-session/{productId}")]
        //[Authorize]
        //public async Task<Result> CreateCheckoutSession(string productId)
        //{
        //    string? userId = _httpContextAccessor.HttpContext?.User?.Claims.FirstOrDefault(i => i.Type == "id")?.Value;
        //    if (userId == null) return Result.Failed(-2, "Please login again.");

        //    string userEmail = await _primaryDbContext.Users.Where(u => u.Id == Guid.Parse(userId)).Select(i => i.Email).FirstAsync();

        //    var options = new SessionCreateOptions
        //    {
        //        CustomerEmail = userEmail,
        //        //LineItems = new List<SessionLineItemOptions>
        //        //{
        //        //    new SessionLineItemOptions
        //        //    {
        //        //        Price = prod.DefaultPriceId,
        //        //        Quantity = (int)type % 2 == 0 ? 1 : body.Quantity,
        //        //    },
        //        //},
        //        Locale = "en",
        //        Mode =  "payment",
        //        AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true },
        //        ExpiresAt = DateTime.UtcNow.AddMinutes(30),
        //        PaymentIntentData = new SessionPaymentIntentDataOptions
        //        {
        //            Metadata = new Dictionary<string, string>
        //            {
        //                ["Prod-Id"] = productId,
        //                ["Email"] = userEmail,
        //                //["ProductType"] = body.ProductType.ToString(),
        //                //["Quantity"] = (int)type % 2 == 0 ? "1" : body.Quantity.ToString(),
        //            }
        //        },
        //    };
        //    var service = new SessionService();
        //    Session session = await service.CreateAsync(options);
        //    return Result.Success(session.Url);
        //}


        [Route("webhook")]
        public async Task<IActionResult> WebHook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            try
            {
                var stripeEvent = EventUtility.ConstructEvent(json, Request.Headers["Stripe-Signature"], _configuration["Stripe-webhook-key"]);

                switch (stripeEvent.Type)
                {
                    case EventTypes.CheckoutSessionCompleted: await Checkout((Session)stripeEvent.Data.Object); break;
                    default: break;
                }
                return Ok();
            }
            catch (StripeException) { return BadRequest(); }
        }

        private async Task Checkout(Session checkout)
        {
            string email = checkout.CustomerDetails.Email;
            string prod = checkout.Metadata["type"];
            switch (prod)
            {
                case "download":
                    if (checkout.ClientReferenceId is not null)
                        await _primaryDbContext.Users.Where(u => u.Id == Guid.Parse(checkout.ClientReferenceId)).ExecuteUpdateAsync(u => u.SetProperty(i => i.IsPreview, false).SetProperty(i => i.ExpiredTime, DateTime.UtcNow.AddMonths(3)));
                    else
                        await _primaryDbContext.Users.Where(u => u.Email == email).ExecuteUpdateAsync(u => u.SetProperty(i => i.IsPreview, false).SetProperty(i => i.ExpiredTime, DateTime.UtcNow.AddMonths(3)));
                    break;
                case "lite":
                    if (checkout.ClientReferenceId is not null)
                        await _primaryDbContext.Users.Where(u => u.Id == Guid.Parse(checkout.ClientReferenceId)).ExecuteUpdateAsync(u => u.SetProperty(i => i.Times, i => i.Times + 10).SetProperty(i => i.IsPreview, false).SetProperty(i => i.ExpiredTime, DateTime.UtcNow.AddMonths(3)));
                    else
                        await _primaryDbContext.Users.Where(u => u.Email == email).ExecuteUpdateAsync(u => u.SetProperty(i => i.Times, i => i.Times + 10).SetProperty(i => i.IsPreview, false).SetProperty(i => i.ExpiredTime, DateTime.UtcNow.AddMonths(3)));
                    break;
                case "pro":
                    if (checkout.ClientReferenceId is not null)
                        await _primaryDbContext.Users.Where(u => u.Id == Guid.Parse(checkout.ClientReferenceId)).ExecuteUpdateAsync(u => u.SetProperty(i => i.Times, i => i.Times + 50).SetProperty(i => i.IsPreview, false).SetProperty(i => i.ExpiredTime, DateTime.UtcNow.AddMonths(3)));
                    else
                        await _primaryDbContext.Users.Where(u => u.Email == email).ExecuteUpdateAsync(u => u.SetProperty(i => i.Times, i => i.Times + 50).SetProperty(i => i.IsPreview, false).SetProperty(i => i.ExpiredTime, DateTime.UtcNow.AddMonths(3)));
                    break;
            }
        }


    }
}
