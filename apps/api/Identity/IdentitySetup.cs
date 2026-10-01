using System.Security.Claims;
using System.Threading.RateLimiting;
using Marketplace.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace Marketplace.Api.Identity;

public static class IdentitySetup
{
    public const string Scheme = "MarketplaceSession";
    public static void AddMarketplaceIdentity(this WebApplicationBuilder builder)
    {
        builder.Services.AddIdentityCore<Account>(o => { o.User.RequireUniqueEmail = true; o.Password.RequiredLength = 12; o.Lockout.MaxFailedAccessAttempts = 5; o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15); }).AddEntityFrameworkStores<MarketplaceDbContext>().AddDefaultTokenProviders();
        builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
        var secure = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing") ? CookieSecurePolicy.None : CookieSecurePolicy.Always;
        builder.Services.AddAuthentication(Scheme).AddCookie(Scheme, o =>
        {
            o.Cookie.Name = "marketplace_session"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Lax; o.Cookie.SecurePolicy = secure; o.ExpireTimeSpan = TimeSpan.FromHours(8); o.SlidingExpiration = false;
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = 401; return Task.CompletedTask; }; o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = 403; return Task.CompletedTask; };
            o.Events.OnValidatePrincipal = async c => { var db = c.HttpContext.RequestServices.GetRequiredService<MarketplaceDbContext>(); var raw = c.Principal?.FindFirstValue("session_id"); var uid = c.Principal?.FindFirstValue(ClaimTypes.NameIdentifier); var session = Guid.TryParse(raw, out var id) ? await db.AccountSessions.SingleOrDefaultAsync(x => x.Id == id) : null; if (session is null || session.UserId != uid || session.RevokedAt != null || session.ExpiresAt <= DateTime.UtcNow) { c.RejectPrincipal(); await c.HttpContext.SignOutAsync(Scheme); } };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(o => { o.HeaderName = "X-CSRF-Token"; o.Cookie.Name = "marketplace_csrf"; o.Cookie.HttpOnly = true; o.Cookie.SameSite = SameSiteMode.Strict; o.Cookie.SecurePolicy = secure; });
        builder.Services.AddRateLimiter(o => { o.AddPolicy("auth", c => RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); o.OnRejected = async (c, _) => { c.HttpContext.Response.Headers.RetryAfter = "60"; await Results.Problem(statusCode: 429, title: "Too many requests. Try again shortly.").ExecuteAsync(c.HttpContext); }; });
        if (builder.Configuration.GetValue<bool>("Email:Enabled")) builder.Services.AddHostedService<VerificationEmailWorker>();
    }
}
