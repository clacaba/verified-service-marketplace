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
var app = builder.Build();
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Database:ApplyMigrations")) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>().Database.MigrateAsync(); }
app.UseExceptionHandler(); app.UseStatusCodePages(); app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false }); app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });
app.MapGet("/api/v1/platform", () => new { name = "Verified Service Marketplace", stage = "Authentication", featuresImplemented = true }); app.MapMarketplaceAuth();
app.MapMarketplaceOrganizations(); app.Run();
public partial class Program { }
public sealed class DatabaseHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) { using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>(); try { return await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Database unavailable."); } catch { return HealthCheckResult.Unhealthy("Database unavailable."); } }
}
