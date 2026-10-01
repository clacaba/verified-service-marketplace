using Marketplace.Api.Catalog;
using Marketplace.Api.Review;
using Marketplace.Api.Verification;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Marketplace.Api.Providers;
using Marketplace.Api.Data;
using Marketplace.Api.Organizations;
using Marketplace.Api.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json.Serialization;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = c => c.ProblemDetails.Extensions["requestId"] = c.HttpContext.TraceIdentifier);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
builder.Services.AddDbContext<MarketplaceDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Marketplace") ?? throw new InvalidOperationException("Configure ConnectionStrings__Marketplace.")));
if (!string.IsNullOrWhiteSpace(builder.Configuration["DataProtection:KeyPath"])) builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(builder.Configuration["DataProtection:KeyPath"]!));
builder.AddMarketplaceIdentity(); builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("postgresql", tags: ["ready"]);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => { o.MultipartBodyLengthLimit = EvidenceOptions.MaxBytes + 1024 * 1024; o.ValueLengthLimit = 4096; o.MultipartHeadersLengthLimit = 16384; });
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = EvidenceOptions.MaxBytes + 1024 * 1024);
builder.Services.AddOptions<EvidenceOptions>().Bind(builder.Configuration.GetSection("Evidence"))
    .Validate(o => o.AccessSeconds is >= 10 and <= 300 && o.DraftRetentionDays is >= 1 and <= 365 && o.SubmittedRetentionDays is >= 1 and <= 365
        && o.IndividualRequired.Length > 0 && o.BusinessRequired.Length > 0
        && o.IndividualRequired.Concat(o.BusinessRequired).Append(o.TradeEvidence).All(EvidenceOptions.EvidenceTypes.Contains)
        && o.TradeCategoryIds.All(id => Guid.TryParse(id, out _)), "Invalid demo evidence policy.").ValidateOnStart();
builder.Services.AddScoped<IEvidenceStore, S3EvidenceStore>();
builder.Services.AddScoped<IEvidenceScanner, ClamEvidenceScanner>();
if (builder.Configuration.GetValue<bool>("Evidence:RetentionEnabled")) builder.Services.AddHostedService<EvidenceRetentionWorker>();
builder.Services.AddRateLimiter(o => o.AddPolicy("evidence", c => RateLimitPartition.GetFixedWindowLimiter(c.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous", _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
var app = builder.Build();
if (args.Length == 2 && args[0] is "--grant-reviewer" or "--revoke-reviewer")
{
    Environment.ExitCode = await ReviewerAccess.Provision(app.Services, args[1], args[0] == "--grant-reviewer");
    return;
}
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:ApplyMigrations")) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>().Database.MigrateAsync(); }
app.UseExceptionHandler(); app.UseStatusCodePages(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }); app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapGet("/api/v1/platform", () => new { name = "Verified Service Marketplace", stage = "Service catalog", featuresImplemented = true }); app.MapMarketplaceAuth();
app.MapMarketplaceOrganizations(); app.MapMarketplaceProviders(); app.MapMarketplaceVerification(); app.MapMarketplaceReview(); app.MapMarketplaceCatalog(); app.Run();
public partial class Program { }
public sealed class DatabaseHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) { using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>(); try { return await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Database unavailable."); } catch { return HealthCheckResult.Unhealthy("Database unavailable."); } }
}
