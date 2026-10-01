using System.Security.Claims;
using Marketplace.Api.Data;
using Marketplace.Api.Organizations;
using Marketplace.Api.Providers;
using Marketplace.Api.Verification;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.Api.Catalog;

public static class CatalogEndpoints
{
    public sealed record ListingInput(string Title, string Description, Guid CategoryId, long PriceMinor, string Currency, int DurationMinutes, string ServiceArea);
    public sealed record PublicationInput(bool Published);
    internal static IQueryable<ServiceListing> PublicQuery(MarketplaceDbContext db, EvidenceOptions policy, DateTime now)
    {
        var eligibleProviders = PublicProviderEndpoints.Eligible(db, policy, now);
        return db.ServiceListings.AsNoTracking().Where(s => s.Status == "Published" && db.ServiceCategories.Any(c => c.Id == s.CategoryId && c.Active)
            && eligibleProviders.Any(p => p.OrganizationId == s.OrganizationId && p.PublishedAt != null && p.Categories.Any(c => c.CategoryId == s.CategoryId)));
    }
    private static string Tag(Guid value) => "\"" + value.ToString("D") + "\"";
    private static object PrivateSummary(ServiceListing s) => new { s.Id, s.OrganizationId, s.Title, s.Description, s.CategoryId, s.PriceMinor, s.Currency, s.DurationMinutes, s.ServiceArea, s.Status, s.Version, s.CreatedAt, s.UpdatedAt, images = s.Images.Where(i => i.DeletedAt == null).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).Select(i => ServiceImageEndpoints.Summary(i, s, false)) };
    private static object PublicSummary(ServiceListing s, ProviderProfile p, ServiceCategory category) => new
    {
        s.Id,
        s.Title,
        s.Description,
        s.PriceMinor,
        s.Currency,
        s.DurationMinutes,
        s.ServiceArea,
        images = s.Images.Where(i => i.DeletedAt == null).OrderBy(i => i.CreatedAt).ThenBy(i => i.Id).Select(i => ServiceImageEndpoints.Summary(i, s, true)),
        category = new { category.Id, category.Name },
        provider = new { p.Id, p.DisplayName },
        disclosure = "Portfolio demo: fictional services and simulated provider checks. Booking is not available yet."
    };
    private static async Task<Dictionary<string, string[]>> Validate(ListingInput input, Guid org, MarketplaceDbContext db)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(input.Title) || input.Title.Trim().Length > 150) errors["title"] = ["Enter a title of 1–150 characters."];
        if (string.IsNullOrWhiteSpace(input.Description) || input.Description.Trim().Length > 4000) errors["description"] = ["Enter a description of 1–4,000 characters."];
        if (string.IsNullOrWhiteSpace(input.ServiceArea) || input.ServiceArea.Trim().Length > 250) errors["serviceArea"] = ["Enter a service area of 1–250 characters."];
        if (input.PriceMinor < 1 || input.PriceMinor > 100000000) errors["priceMinor"] = ["Enter an integer price between 1 and 100,000,000 centavos."];
        if (input.Currency != "PHP") errors["currency"] = ["This MVP supports PHP only."];
        if (input.DurationMinutes < 15 || input.DurationMinutes > 1440 || input.DurationMinutes % 15 != 0) errors["durationMinutes"] = ["Choose 15–1,440 minutes in 15-minute increments."];
        if (!await db.ServiceCategories.AnyAsync(c => c.Id == input.CategoryId && c.Active)
            || !await db.ProviderProfiles.AnyAsync(p => p.OrganizationId == org && p.Categories.Any(c => c.CategoryId == input.CategoryId)))
            errors["categoryId"] = ["Choose an active category already included in the provider profile."];
        return errors;
    }
    private static void Assign(ServiceListing s, ListingInput input)
    {
        s.Title = input.Title.Trim(); s.Description = input.Description.Trim(); s.CategoryId = input.CategoryId;
        s.PriceMinor = input.PriceMinor; s.Currency = input.Currency; s.DurationMinutes = input.DurationMinutes; s.ServiceArea = input.ServiceArea.Trim();
    }
    internal static IResult? Precondition(ServiceListing s, HttpContext http)
    {
        var value = http.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(value)) return Results.Problem(statusCode: 428, title: "If-Match is required.");
        return value != Tag(s.Version) ? Results.Problem(statusCode: 412, title: "This listing changed. Reload before continuing.") : null;
    }
    internal static async Task<IResult> Save(ServiceListing s, string action, HttpContext http, MarketplaceDbContext db, int status = 200)
    {
        s.Version = Guid.NewGuid(); s.UpdatedAt = DateTime.UtcNow;
        db.OrganizationAuditEvents.Add(new OrganizationAuditEvent { OrganizationId = s.OrganizationId, ActorId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!, Action = action, ResourceId = s.Id.ToString() });
        try { await db.SaveChangesAsync(); } catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: 412, title: "This listing changed. Reload before continuing."); }
        http.Response.Headers.ETag = Tag(s.Version);
        return status == 201 ? Results.Created($"/api/v1/organizations/{s.OrganizationId}/services/{s.Id}", PrivateSummary(s)) : Results.Ok(PrivateSummary(s));
    }
    public static void MapMarketplaceCatalog(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/services").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext; http.Response.Headers.CacheControl = "no-store";
            if (!HttpMethods.IsGet(http.Request.Method))
            {
                try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 400, title: "Invalid CSRF token."); }
            }
            var db = http.RequestServices.GetRequiredService<MarketplaceDbContext>();
            var org = Guid.Parse(http.Request.RouteValues["organizationId"]!.ToString()!);
            var member = await db.OrganizationMemberships.SingleOrDefaultAsync(m => m.OrganizationId == org && m.UserId == http.User.FindFirstValue(ClaimTypes.NameIdentifier) && m.Active);
            if (member == null) return Results.NotFound();
            if (!HttpMethods.IsGet(http.Request.Method) && member.Role != "Owner") return Results.Forbid();
            return await next(context);
        });
        group.MapGet("", async (Guid organizationId, int? page, int? pageSize, string? status, MarketplaceDbContext db) =>
        {
            var current = page ?? 1; var size = pageSize ?? 20;
            if (current < 1 || current > 100000 || size < 1 || size > 100 || (status != null && status is not ("Draft" or "Published" or "Archived"))) return Results.Problem(statusCode: 400, title: "Invalid pagination or status.");
            var query = db.ServiceListings.AsNoTracking().Include(s => s.Images).Where(s => s.OrganizationId == organizationId);
            if (status != null) query = query.Where(s => s.Status == status);
            var rows = await query.OrderBy(s => s.Title).ThenBy(s => s.Id).Skip((current - 1) * size).Take(size).ToListAsync();
            return Results.Ok(new { items = rows.Select(PrivateSummary), page = current, pageSize = size, totalCount = await query.CountAsync() });
        });
        group.MapGet("/{serviceId:guid}", async (Guid organizationId, Guid serviceId, HttpContext http, MarketplaceDbContext db) =>
        {
            var s = await db.ServiceListings.AsNoTracking().Include(s => s.Images).SingleOrDefaultAsync(s => s.Id == serviceId && s.OrganizationId == organizationId);
            if (s == null) return Results.NotFound(); http.Response.Headers.ETag = Tag(s.Version); return Results.Ok(PrivateSummary(s));
        });
        group.MapPost("", async (Guid organizationId, ListingInput input, HttpContext http, MarketplaceDbContext db) =>
        {
            var errors = await Validate(input, organizationId, db); if (errors.Count > 0) return Results.ValidationProblem(errors);
            var s = new ServiceListing { OrganizationId = organizationId }; Assign(s, input); db.ServiceListings.Add(s);
            return await Save(s, "service.created", http, db, 201);
        });
        group.MapPut("/{serviceId:guid}", async (Guid organizationId, Guid serviceId, ListingInput input, HttpContext http, MarketplaceDbContext db) =>
        {
            var s = await db.ServiceListings.Include(s => s.Images).SingleOrDefaultAsync(s => s.Id == serviceId && s.OrganizationId == organizationId);
            if (s == null) return Results.NotFound(); var error = Precondition(s, http); if (error != null) return error;
            if (s.Status == "Archived") return Results.Problem(statusCode: 409, title: "Archived listings cannot be edited.");
            var errors = await Validate(input, organizationId, db); if (errors.Count > 0) return Results.ValidationProblem(errors);
            Assign(s, input); s.Status = "Draft"; // Edits never silently alter an already-public offer.
            return await Save(s, "service.updated", http, db);
        });
        group.MapPost("/{serviceId:guid}/publication", async (Guid organizationId, Guid serviceId, PublicationInput input, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            var s = await db.ServiceListings.Include(s => s.Images).SingleOrDefaultAsync(s => s.Id == serviceId && s.OrganizationId == organizationId);
            if (s == null) return Results.NotFound(); var error = Precondition(s, http); if (error != null) return error;
            if (s.Status == "Archived") return Results.Problem(statusCode: 409, title: "Archived listings cannot be published.");
            if (input.Published && (!await db.ServiceCategories.AnyAsync(c => c.Id == s.CategoryId && c.Active)
                || !await PublicProviderEndpoints.Eligible(db, policy.Value, DateTime.UtcNow).AnyAsync(p => p.OrganizationId == organizationId && p.PublishedAt != null && p.Categories.Any(c => c.CategoryId == s.CategoryId))))
                return Results.Problem(statusCode: 409, title: "Publish an eligible verified provider profile with this active category first.");
            s.Status = input.Published ? "Published" : "Draft";
            return await Save(s, input.Published ? "service.published" : "service.unpublished", http, db);
        });
        group.MapPost("/{serviceId:guid}/archive", async (Guid organizationId, Guid serviceId, HttpContext http, MarketplaceDbContext db) =>
        {
            var s = await db.ServiceListings.Include(s => s.Images).SingleOrDefaultAsync(s => s.Id == serviceId && s.OrganizationId == organizationId);
            if (s == null) return Results.NotFound(); var error = Precondition(s, http); if (error != null) return error;
            if (s.Status == "Archived") return Results.Problem(statusCode: 409, title: "This listing is already archived.");
            foreach (var image in s.Images.Where(i => i.DeletedAt == null)) ServiceImageEndpoints.Retire(image, db);
            s.Status = "Archived"; return await Save(s, "service.archived", http, db);
        });
        ServiceImageEndpoints.Map(group, app);
        app.MapGet("/api/v1/services", async (int? page, int? pageSize, string? q, Guid? categoryId, Guid? providerId, string? area, long? minPriceMinor, long? maxPriceMinor, string? sort, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            http.Response.Headers.CacheControl = "no-store"; var current = page ?? 1; var size = pageSize ?? 20; sort ??= "title";
            if (current < 1 || current > 100000 || size < 1 || size > 100 || (q?.Length ?? 0) > 150 || (area?.Length ?? 0) > 250
                || minPriceMinor < 0 || maxPriceMinor < 0 || minPriceMinor > 100000000 || maxPriceMinor > 100000000 || minPriceMinor > maxPriceMinor
                || sort is not ("title" or "priceAsc" or "priceDesc"))
                return Results.Problem(statusCode: 400, title: "Invalid search filters, sort, or pagination.");
            var query = PublicQuery(db, policy.Value, DateTime.UtcNow).Include(s => s.Images).AsQueryable();
            if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim().ToLowerInvariant(); query = query.Where(s => s.Title.ToLower().Contains(term) || s.Description.ToLower().Contains(term)); }
            if (!string.IsNullOrWhiteSpace(area)) { var term = area.Trim().ToLowerInvariant(); query = query.Where(s => s.ServiceArea.ToLower().Contains(term)); }
            if (categoryId != null) query = query.Where(s => s.CategoryId == categoryId);
            if (providerId != null) query = query.Where(s => db.ProviderProfiles.Any(p => p.Id == providerId && p.OrganizationId == s.OrganizationId));
            if (minPriceMinor != null) query = query.Where(s => s.PriceMinor >= minPriceMinor);
            if (maxPriceMinor != null) query = query.Where(s => s.PriceMinor <= maxPriceMinor);
            var count = await query.CountAsync();
            var ordered = sort switch { "priceAsc" => query.OrderBy(s => s.PriceMinor).ThenBy(s => s.Id), "priceDesc" => query.OrderByDescending(s => s.PriceMinor).ThenBy(s => s.Id), _ => query.OrderBy(s => s.Title).ThenBy(s => s.Id) };
            var rows = await ordered.Skip((current - 1) * size).Take(size).ToListAsync();
            var ids = rows.Select(s => s.Id).ToArray();
            var stillEligible = await PublicQuery(db, policy.Value, DateTime.UtcNow).Where(s => ids.Contains(s.Id)).ToListAsync();
            var orgIds = stillEligible.Select(s => s.OrganizationId).ToArray();
            var providers = await db.ProviderProfiles.AsNoTracking().Where(p => orgIds.Contains(p.OrganizationId)).ToDictionaryAsync(p => p.OrganizationId);
            var categories = await db.ServiceCategories.AsNoTracking().ToDictionaryAsync(c => c.Id);
            return Results.Ok(new { items = rows.Where(s => stillEligible.Any(e => e.Id == s.Id && e.Version == s.Version)).Select(s => PublicSummary(s, providers[s.OrganizationId], categories[s.CategoryId])), page = current, pageSize = size, totalCount = count });
        });
        app.MapGet("/api/v1/services/{serviceId:guid}", async (Guid serviceId, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var s = await PublicQuery(db, policy.Value, DateTime.UtcNow).Include(s => s.Images).SingleOrDefaultAsync(s => s.Id == serviceId);
            if (s == null) return Results.NotFound();
            var provider = await db.ProviderProfiles.AsNoTracking().SingleAsync(p => p.OrganizationId == s.OrganizationId);
            var category = await db.ServiceCategories.AsNoTracking().SingleAsync(c => c.Id == s.CategoryId);
            if (!await PublicQuery(db, policy.Value, DateTime.UtcNow).AnyAsync(x => x.Id == serviceId && x.Version == s.Version)) return Results.NotFound();
            return Results.Ok(PublicSummary(s, provider, category));
        });
    }
}
