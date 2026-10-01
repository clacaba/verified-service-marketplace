using System.Security.Claims;
using Marketplace.Api.Data;
using Marketplace.Api.Organizations;
using Marketplace.Api.Verification;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.Api.Providers;

public static class PublicProviderEndpoints
{
    public sealed record PublicationInput(bool Published);
    public sealed record Badge(string Type, string Label, DateTime ExpiresAt, bool Simulated);
    public sealed record Category(Guid Id, string Name);
    public sealed record PublicProfile(Guid Id, string DisplayName, string ProviderType, string Biography, string ServiceArea, Category[] Categories, Badge[] Badges, string Disclosure);

    // Eligibility is evaluated in SQL before counting/paging. Historical approvals never qualify.
    internal static IQueryable<ProviderProfile> Eligible(MarketplaceDbContext db, EvidenceOptions policy, DateTime now)
    {
        var query = db.ProviderProfiles.Where(p => p.Biography != "" && p.ServiceArea != "" && p.Categories.Any()
            && !p.Categories.Any(c => !db.ServiceCategories.Any(s => s.Id == c.CategoryId && s.Active))
            && db.OrganizationMemberships.Any(m => m.OrganizationId == p.OrganizationId && m.Active && m.Role == "Owner"
                && db.Users.Any(u => u.Id == m.UserId && u.EmailConfirmed)));
        var tradeIds = policy.TradeCategoryIds.Select(Guid.Parse).ToArray();
        foreach (var type in policy.IndividualRequired.Distinct())
            query = query.Where(p => p.ProviderType != "Individual" || db.VerificationChecks.Any(c => c.OrganizationId == p.OrganizationId && c.Type == type && c.RevokedAt == null && c.ExpiresAt > now
                && db.VerificationDecisions.Any(d => d.Id == c.DecisionId && d.Decision == "Approved" && db.VerificationApplications.Any(a => a.Id == d.ApplicationId && a.ProviderId == p.Id && a.IsActive && a.Status == "Approved"))));
        foreach (var type in policy.BusinessRequired.Distinct())
            query = query.Where(p => p.ProviderType != "Business" || db.VerificationChecks.Any(c => c.OrganizationId == p.OrganizationId && c.Type == type && c.RevokedAt == null && c.ExpiresAt > now
                && db.VerificationDecisions.Any(d => d.Id == c.DecisionId && d.Decision == "Approved" && db.VerificationApplications.Any(a => a.Id == d.ApplicationId && a.ProviderId == p.Id && a.IsActive && a.Status == "Approved"))));
        query = query.Where(p => !p.Categories.Any(c => tradeIds.Contains(c.CategoryId)) || db.VerificationChecks.Any(c => c.OrganizationId == p.OrganizationId && c.Type == policy.TradeEvidence && c.RevokedAt == null && c.ExpiresAt > now
            && db.VerificationDecisions.Any(d => d.Id == c.DecisionId && d.Decision == "Approved" && db.VerificationApplications.Any(a => a.Id == d.ApplicationId && a.ProviderId == p.Id && a.IsActive && a.Status == "Approved"))));
        return query.Where(p => db.VerificationApplications.Any(a => a.ProviderId == p.Id && a.IsActive && a.Status == "Approved"));
    }
    private static async Task<PublicProfile[]> Project(IQueryable<ProviderProfile> query, MarketplaceDbContext db, DateTime now)
    {
        var rows = await query.AsNoTracking().Include(p => p.Categories).ToListAsync();
        var orgIds = rows.Select(p => p.OrganizationId).ToArray();
        var checks = await (from c in db.VerificationChecks.AsNoTracking()
                            join d in db.VerificationDecisions on c.DecisionId equals d.Id
                            join a in db.VerificationApplications on d.ApplicationId equals a.Id
                            where orgIds.Contains(c.OrganizationId) && c.RevokedAt == null && c.ExpiresAt > now && d.Decision == "Approved" && a.IsActive && a.Status == "Approved"
                            select c).ToListAsync();
        var categories = await db.ServiceCategories.AsNoTracking().Where(c => c.Active).ToDictionaryAsync(c => c.Id);
        return rows.Select(p => new PublicProfile(p.Id, p.DisplayName, p.ProviderType, p.Biography, p.ServiceArea,
            p.Categories.Where(c => categories.ContainsKey(c.CategoryId)).OrderBy(c => categories[c.CategoryId].Name).Select(c => new Category(c.CategoryId, categories[c.CategoryId].Name)).ToArray(),
            checks.Where(c => c.OrganizationId == p.OrganizationId && EvidenceOptions.EvidenceTypes.Contains(c.Type)).OrderBy(c => c.Type)
                .Select(c => new Badge(c.Type, Label(c.Type, c.Simulated), c.ExpiresAt, c.Simulated)).ToArray(),
            "Portfolio demo: fictional evidence and simulated checks only. Not a guarantee of identity, licensing, safety, or service quality.")).ToArray();
    }
    private static string Label(string type, bool simulated) => (type switch
    {
        "Identity" => "Identity evidence reviewed",
        "BusinessRegistration" => "Business registration evidence reviewed",
        "TradeQualification" => "Trade qualification evidence reviewed",
        _ => "Evidence reviewed"
    }) + (simulated ? " (simulated)" : "");

    public static void MapPublicProviders(this WebApplication app)
    {
        app.MapGet("/api/v1/providers", async (int? page, int? pageSize, Guid? categoryId, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var current = page ?? 1; var size = pageSize ?? 20;
            if (current < 1 || current > 100000 || size < 1 || size > 100) return Results.Problem(statusCode: 400, title: "Invalid pagination.");
            var now = DateTime.UtcNow;
            var query = Eligible(db, policy.Value, now).Where(p => p.PublishedAt != null);
            if (categoryId != null) query = query.Where(p => p.Categories.Any(c => c.CategoryId == categoryId));
            var count = await query.CountAsync();
            var items = await Project(query.OrderBy(p => p.DisplayName).ThenBy(p => p.Id).Skip((current - 1) * size).Take(size), db, now);
            // Fail closed if a check was revoked between the eligibility query and projection.
            var eligibleIds = await Eligible(db, policy.Value, DateTime.UtcNow).Where(p => p.PublishedAt != null && items.Select(i => i.Id).Contains(p.Id)).Select(p => p.Id).ToListAsync();
            return Results.Ok(new { items = items.Where(i => eligibleIds.Contains(i.Id)), page = current, pageSize = size, totalCount = count });
        });
        app.MapGet("/api/v1/providers/{providerId:guid}", async (Guid providerId, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            http.Response.Headers.CacheControl = "no-store"; var now = DateTime.UtcNow;
            var query = Eligible(db, policy.Value, now).Where(p => p.Id == providerId && p.PublishedAt != null);
            var items = await Project(query, db, now);
            if (items.Length != 1 || !await Eligible(db, policy.Value, DateTime.UtcNow).AnyAsync(p => p.Id == providerId && p.PublishedAt != null)) return Results.NotFound();
            return Results.Ok(items[0]);
        });
        app.MapPost("/api/v1/organizations/{organizationId:guid}/provider-profile/publication", async (Guid organizationId, PublicationInput input, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
            catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 400, title: "Invalid CSRF token."); }
            var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var member = await db.OrganizationMemberships.SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == actor && m.Active);
            if (member == null) return Results.NotFound();
            if (member.Role != "Owner") return Results.Forbid();
            var profile = await db.ProviderProfiles.SingleAsync(p => p.OrganizationId == organizationId);
            var expected = http.Request.Headers.IfMatch.ToString();
            if (string.IsNullOrWhiteSpace(expected)) return Results.Problem(statusCode: 428, title: "If-Match is required.");
            if (expected != "\"" + profile.Version.ToString("D") + "\"") return Results.Problem(statusCode: 412, title: "Reload the profile before changing publication.");
            if (input.Published && !await Eligible(db, policy.Value, DateTime.UtcNow).AnyAsync(p => p.Id == profile.Id))
                return Results.Problem(statusCode: 409, title: "Complete the profile and obtain every required current approval before publishing.");
            profile.PublishedAt = input.Published ? DateTime.UtcNow : null;
            profile.Version = Guid.NewGuid(); profile.UpdatedAt = DateTime.UtcNow;
            db.OrganizationAuditEvents.Add(new OrganizationAuditEvent { OrganizationId = organizationId, ActorId = actor!, Action = input.Published ? "provider-profile.published" : "provider-profile.unpublished", ResourceId = profile.Id.ToString() });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: 412, title: "Reload the profile before changing publication."); }
            http.Response.Headers.ETag = "\"" + profile.Version.ToString("D") + "\"";
            return Results.Ok(new { profile.Id, profile.PublishedAt });
        }).RequireAuthorization();
    }
}
