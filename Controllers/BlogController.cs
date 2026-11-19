using InsoleApi.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Buffers;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Eventing.Reader;
using System.Text;
using System.Text.Json.Serialization;
using InsoleApi.Common;
using InsoleApi.Database;
using inso.Utils;

namespace InsoleApi.Controllers
{
    [ApiController]
    [Route("/api/v1/blog")]
    public class BlogController : Controller
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        private readonly PrimaryDbContext _primaryDbContext;

        private readonly EmailHelper _emailHelper;

        private readonly JwtHelper _jwtHelper;

        private readonly ILogger<UserController> _logger;

        private readonly IConfiguration _configuration;

        private readonly IWebHostEnvironment _webHostEnvironment;

        public BlogController(IHttpContextAccessor httpContextAccessor, PrimaryDbContext primaryDbContext, EmailHelper emailHelper, JwtHelper jwtHelper, ILogger<UserController> logger, IConfiguration configuration, IWebHostEnvironment webHostEnvironment)
        {
            _httpContextAccessor = httpContextAccessor;
            _primaryDbContext = primaryDbContext;
            _emailHelper = emailHelper;
            _jwtHelper = jwtHelper;
            _logger = logger;
            _configuration = configuration;
            _webHostEnvironment = webHostEnvironment;
        }

        [HttpGet("list/{type}/{pagecount}")]
        public async Task<Result> Login(string type, int pagecount)
            => Result.Success(await _primaryDbContext.Blogs.Where(b => type == "ALL" || b.Type == type.ToLower()).Skip(pagecount * 6).OrderByDescending(i => i.CreateTime)
                .Select(i => new
                {
                    i.Title,
                    i.Detail,
                    i.CreateTime,
                    i.RelativeUrl,
                    i.PreviewImgBase64
                }).Take(6).ToListAsync());

        [HttpGet("blog/{url}")]
        public async Task<Result> Blog(string url)
            => Result.Success(await _primaryDbContext.Blogs.Where(b => b.RelativeUrl == url).Select(i => new { i.Title, i.Content, i.Seo }).FirstOrDefaultAsync());

        [HttpGet("first-blog")]
        public async Task<Result> First() => Result.Success(await _primaryDbContext.Blogs.Where(i => i.IsFirst).Select(i => new
        {
            i.IsFirst,
            i.Type,
            i.CreateTime,
            i.Title,
            i.Content,
            i.PreviewImgBase64,
            i.RelativeUrl,
            i.Detail
        }).FirstOrDefaultAsync());

        [HttpGet("blog-top-3")]
        public async Task<Result> Top3()
          => Result.Success(await _primaryDbContext.Blogs.Where(i => i.IsIndex)
                .Select(i => new
                {
                    i.Title,
                    i.Detail,
                    i.RelativeUrl,
                    i.PreviewImgBase64
                }).Take(3).ToListAsync());
    }
}
