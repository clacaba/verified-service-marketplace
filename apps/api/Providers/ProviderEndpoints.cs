using System.Security.Claims;
using Marketplace.Api.Data;
using Marketplace.Api.Organizations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Providers;

public static class ProviderEndpoints
{
    public sealed record ProfileInput(string DisplayName, string ProviderType, string? Biography, Guid[]? CategoryIds, string? ServiceArea, Guid? ImageId);
    public static void MapMarketplaceProviders(this WebApplication app)
    {
        app.MapGet("/api/v1/categories", async (int? page, int? pageSize, MarketplaceDbContext db) =>
        {
            var current = page ?? 1; var size = pageSize ?? 20;
            if (current < 1 || current > 100000 || size < 1 || size > 100) return Results.Problem(statusCode: 400, title: "Invalid pagination.");
            var query = db.ServiceCategories.Where(x => x.Active);
            var items = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((current - 1) * size).Take(size).Select(x => new { x.Id, x.Name }).ToListAsync();
            return Results.Ok(new { items, page = current, pageSize = size, totalCount = await query.CountAsync() });
        });
        var group = app.MapGroup("/api/v1/organizations/{organizationId:guid}/provider-profile").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
            {
                try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
                catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 400, title: "Refresh the page and try again.", detail: "Invalid CSRF token."); }
            }
            return await next(context);
        });
        group.MapGet("", async (Guid organizationId, HttpContext context, MarketplaceDbContext db) =>
        {
            var member = await Member(db, organizationId, context);
            if (member is null) return Results.NotFound();
            var profile = await db.ProviderProfiles.Include(x => x.Categories).SingleOrDefaultAsync(x => x.OrganizationId == organizationId);
            if (profile is null) return Results.NotFound();
            context.Response.Headers.ETag = Tag(profile.Version);
            return Results.Ok(Summary(profile));
        });
        group.MapPut("", async (Guid organizationId, ProfileInput input, HttpContext context, MarketplaceDbContext db) =>
        {
            var member = await Member(db, organizationId, context);
            if (member is null) return Results.NotFound();
            if (member.Role != "Owner") return Results.Forbid();
            var profile = await db.ProviderProfiles.Include(x => x.Categories).SingleOrDefaultAsync(x => x.OrganizationId == organizationId);
            if (profile is null) return Results.NotFound();
            var expected = context.Request.Headers.IfMatch.ToString();
            if (string.IsNullOrWhiteSpace(expected)) return Results.Problem(statusCode: 428, title: "Reload the profile before saving.", detail: "If-Match is required.");
            if (expected != Tag(profile.Version)) return Results.Problem(statusCode: 412, title: "This profile has changed. Reload before saving.");
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(input.DisplayName) || input.DisplayName.Trim().Length > 150) errors["displayName"] = ["Enter a display name of 1–150 characters."];
            if (input.ProviderType is not ("Individual" or "Business")) errors["providerType"] = ["Choose Individual or Business."];
            if ((input.Biography?.Length ?? 0) > 4000) errors["biography"] = ["Biography must not exceed 4,000 characters."];
            if ((input.ServiceArea?.Trim().Length ?? 0) > 250) errors["serviceArea"] = ["Service area must not exceed 250 characters."];
            if (input.ImageId is not null) errors["imageId"] = ["Image upload is not available yet. Do not supply an external image identifier."];
            var categoryIds = input.CategoryIds ?? [];
            if (categoryIds.Length > 20 || categoryIds.Distinct().Count() != categoryIds.Length) errors["categoryIds"] = ["Choose up to 20 distinct active categories."];
            else if (await db.ServiceCategories.CountAsync(x => x.Active && categoryIds.Contains(x.Id)) != categoryIds.Length) errors["categoryIds"] = ["Choose only active marketplace categories."];
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var materiallyChanged = profile.DisplayName != input.DisplayName.Trim() || profile.ProviderType != input.ProviderType
                || profile.Biography != (input.Biography?.Trim() ?? "") || profile.ServiceArea != (input.ServiceArea?.Trim() ?? "")
                || !profile.Categories.Select(x => x.CategoryId).Order().SequenceEqual(categoryIds.Order());
            if (materiallyChanged)
            {
                var checks = await db.VerificationChecks.Where(x => x.OrganizationId == organizationId && x.RevokedAt == null).ToListAsync();
                foreach (var check in checks) check.RevokedAt = DateTime.UtcNow;
            }
            profile.DisplayName = input.DisplayName.Trim();
            profile.ProviderType = input.ProviderType;
            profile.Biography = input.Biography?.Trim() ?? "";
            profile.ServiceArea = input.ServiceArea?.Trim() ?? "";
            var removed = profile.Categories.Where(x => !categoryIds.Contains(x.CategoryId)).ToList();
            db.ProviderCategories.RemoveRange(removed);
            foreach (var item in removed) profile.Categories.Remove(item);
            foreach (var id in categoryIds.Where(id => !profile.Categories.Any(x => x.CategoryId == id)))
                profile.Categories.Add(new ProviderCategory { ProfileId = profile.Id, CategoryId = id });
            profile.Version = Guid.NewGuid();
            profile.UpdatedAt = DateTime.UtcNow;
            var organization = await db.Organizations.SingleAsync(x => x.Id == organizationId);
            organization.ProviderType = input.ProviderType;
            db.OrganizationAuditEvents.Add(new OrganizationAuditEvent { OrganizationId = organizationId, ActorId = member.UserId, Action = "provider-profile.updated", ResourceId = profile.Id.ToString() });
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: 412, title: "This profile has changed. Reload before saving."); }
            context.Response.Headers.ETag = Tag(profile.Version);
            return Results.Ok(Summary(profile));
        });
    }
    private static Task<OrganizationMembership?> Member(MarketplaceDbContext db, Guid id, HttpContext context) =>
        db.OrganizationMemberships.SingleOrDefaultAsync(x => x.OrganizationId == id && x.UserId == context.User.FindFirstValue(ClaimTypes.NameIdentifier) && x.Active);
    private static string Tag(Guid version) => "\"" + version.ToString("D") + "\"";
    private static object Summary(ProviderProfile profile) => new
    {
        profile.Id,
        profile.OrganizationId,
        profile.DisplayName,
        profile.ProviderType,
        profile.Biography,
        categoryIds = profile.Categories.Select(x => x.CategoryId).OrderBy(x => x).ToArray(),
        profile.ServiceArea,
        imageId = (Guid?)null,
        profile.Version,
        profile.UpdatedAt,
        status = "Draft",
        readyForVerification = !string.IsNullOrWhiteSpace(profile.Biography) && !string.IsNullOrWhiteSpace(profile.ServiceArea) && profile.Categories.Count > 0
    };
}
