using System.Globalization;
using System.Security.Claims;
using Marketplace.Api.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Properties;

public static class PropertyEndpoints
{
    public sealed record PropertyInput(string Label, string AddressLine1, string AddressLine2, string City, string Region, string PostalCode, string CountryCode);
    private static readonly HashSet<string> Countries = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(c => new RegionInfo(c.Name).TwoLetterISORegionName).ToHashSet(StringComparer.Ordinal);
    private static string UserId(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static string Tag(Guid version) => $"\"{version:D}\"";
    private static object Summary(CustomerProperty p) => new { p.Id, p.Label, p.AddressLine1, p.AddressLine2, p.City, p.Region, p.PostalCode, p.CountryCode, p.Version, p.CreatedAt, p.UpdatedAt, p.ArchivedAt };
    private static Dictionary<string, string[]> Validate(PropertyInput input)
    {
        var errors = new Dictionary<string, string[]>();
        void Field(string key, string? value, int max, bool required = true)
        {
            if (value == null || (required && string.IsNullOrWhiteSpace(value)) || value.Trim().Length > max || value.Any(char.IsControl))
                errors[key] = [$"Enter {(required ? "1" : "0")}–{max} characters without control characters."];
        }
        Field("label", input.Label, 80); Field("addressLine1", input.AddressLine1, 200); Field("addressLine2", input.AddressLine2, 200, false);
        Field("city", input.City, 100); Field("region", input.Region, 100); Field("postalCode", input.PostalCode, 20);
        if (input.CountryCode == null || !Countries.Contains(input.CountryCode)) errors["countryCode"] = ["Choose an uppercase ISO two-letter country code, for example PH."];
        return errors;
    }
    private static void Assign(CustomerProperty p, PropertyInput input)
    {
        p.Label = input.Label.Trim(); p.AddressLine1 = input.AddressLine1.Trim(); p.AddressLine2 = input.AddressLine2.Trim();
        p.City = input.City.Trim(); p.Region = input.Region.Trim(); p.PostalCode = input.PostalCode.Trim(); p.CountryCode = input.CountryCode;
    }
    private static IResult? Precondition(CustomerProperty p, HttpContext http)
    {
        if (string.IsNullOrWhiteSpace(http.Request.Headers.IfMatch)) return Results.Problem(statusCode: 428, title: "If-Match is required.");
        return http.Request.Headers.IfMatch.ToString() != Tag(p.Version) ? Results.Problem(statusCode: 412, title: "This property changed. Reload before continuing.") : null;
    }
    private static async Task<IResult> Save(CustomerProperty p, HttpContext http, MarketplaceDbContext db, bool created = false)
    {
        p.Version = Guid.NewGuid(); p.UpdatedAt = DateTime.UtcNow;
        try { await db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: 412, title: "This property changed. Reload before continuing."); }
        http.Response.Headers.ETag = Tag(p.Version);
        return created ? Results.Created($"/api/v1/properties/{p.Id}", Summary(p)) : Results.Ok(Summary(p));
    }
    public static void MapMarketplaceProperties(this WebApplication app)
    {
        // Provider accounts may also act as customers; membership never grants access to someone else's properties.
        var group = app.MapGroup("/api/v1/properties").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext; http.Response.Headers.CacheControl = "no-store"; http.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (!HttpMethods.IsGet(http.Request.Method))
            {
                try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 400, title: "Invalid CSRF token."); }
            }
            return await next(context);
        });
        group.MapGet("", async (int? page, int? pageSize, HttpContext http, MarketplaceDbContext db) =>
        {
            var current = page ?? 1; var size = pageSize ?? 20;
            if (current < 1 || current > 100000 || size < 1 || size > 100) return Results.Problem(statusCode: 400, title: "Invalid pagination.");
            var user = UserId(http); var query = db.CustomerProperties.AsNoTracking().Where(p => p.CustomerId == user && p.ArchivedAt == null);
            var rows = await query.OrderBy(p => p.Label).ThenBy(p => p.Id).Skip((current - 1) * size).Take(size).ToListAsync();
            return Results.Ok(new { items = rows.Select(Summary), page = current, pageSize = size, totalCount = await query.CountAsync() });
        });
        group.MapGet("/{propertyId:guid}", async (Guid propertyId, HttpContext http, MarketplaceDbContext db) =>
        {
            var user = UserId(http); var p = await db.CustomerProperties.AsNoTracking().SingleOrDefaultAsync(p => p.Id == propertyId && p.CustomerId == user);
            if (p == null) return Results.NotFound(); http.Response.Headers.ETag = Tag(p.Version); return Results.Ok(Summary(p));
        });
        group.MapPost("", async (PropertyInput input, HttpContext http, MarketplaceDbContext db) =>
        {
            var errors = Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
            var p = new CustomerProperty { CustomerId = UserId(http) }; Assign(p, input); db.CustomerProperties.Add(p); return await Save(p, http, db, true);
        });
        group.MapPut("/{propertyId:guid}", async (Guid propertyId, PropertyInput input, HttpContext http, MarketplaceDbContext db) =>
        {
            var user = UserId(http); var p = await db.CustomerProperties.SingleOrDefaultAsync(p => p.Id == propertyId && p.CustomerId == user);
            if (p == null) return Results.NotFound(); var conflict = Precondition(p, http); if (conflict != null) return conflict;
            if (p.ArchivedAt != null) return Results.Problem(statusCode: 409, title: "Archived properties cannot be edited.");
            var errors = Validate(input); if (errors.Count > 0) return Results.ValidationProblem(errors);
            Assign(p, input); return await Save(p, http, db);
        });
        group.MapPost("/{propertyId:guid}/archive", async (Guid propertyId, HttpContext http, MarketplaceDbContext db) =>
        {
            var user = UserId(http); var p = await db.CustomerProperties.SingleOrDefaultAsync(p => p.Id == propertyId && p.CustomerId == user);
            if (p == null) return Results.NotFound(); var conflict = Precondition(p, http); if (conflict != null) return conflict;
            if (p.ArchivedAt != null) return Results.Problem(statusCode: 409, title: "This property is already archived.");
            p.ArchivedAt = DateTime.UtcNow; return await Save(p, http, db);
        });
    }
}
