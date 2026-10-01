using System.Security.Claims;
using System.Security.Cryptography;
using Marketplace.Api.Data;
using Marketplace.Api.Verification;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.Api.Catalog;

public static class ServiceImageEndpoints
{
    internal static object Summary(ServiceImage image, ServiceListing listing, bool publicView) => new
    {
        image.Id,
        image.AltText,
        image.Width,
        image.Height,
        image.Size,
        image.CreatedAt,
        contentType = "image/png",
        url = publicView ? $"/api/v1/services/{listing.Id}/images/{image.Id}/content" : $"/api/v1/organizations/{listing.OrganizationId}/services/{listing.Id}/images/{image.Id}/content"
    };
    internal static void Retire(ServiceImage image, MarketplaceDbContext db)
    {
        image.DeletedAt = DateTime.UtcNow;
        db.ServiceImageDeletions.Add(new ServiceImageDeletion { ObjectKey = image.ObjectKey });
    }
    public static void Map(RouteGroupBuilder group, WebApplication app)
    {
        group.MapPost("/{serviceId:guid}/images", async (Guid organizationId, Guid serviceId, HttpContext http, MarketplaceDbContext db, IServiceImageStore store, IEvidenceScanner scanner, ServiceImageProcessor processor) =>
        {
            var listing = await db.ServiceListings.Include(s => s.Images).SingleOrDefaultAsync(s => s.Id == serviceId && s.OrganizationId == organizationId);
            if (listing == null) return Results.NotFound();
            var precondition = CatalogEndpoints.Precondition(listing, http); if (precondition != null) return precondition;
            if (listing.Status == "Archived") return Results.Problem(statusCode: 409, title: "Archived listings cannot accept images.");
            if (listing.Images.Count(i => i.DeletedAt == null) >= ServiceImagePolicy.MaxImages) return Results.Problem(statusCode: 409, title: "A listing can have at most five images.");
            if (http.Request.ContentLength > ServiceImagePolicy.MaxRequestBytes) return Results.Problem(statusCode: 413, title: "Image request exceeds 6 MiB.");
            var bodyLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>(); if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = ServiceImagePolicy.MaxRequestBytes;
            if (!http.Request.HasFormContentType) return Results.Problem(statusCode: 400, title: "Use multipart/form-data.");
            try
            {
                var form = await http.Request.ReadFormAsync(http.RequestAborted);
                var alt = form["altText"].ToString().Trim();
                if (form.Files.Count != 1 || form.Files[0].Name != "file" || form.Keys.Any(k => k is not ("altText" or "demoAcknowledged"))
                    || form["demoAcknowledged"].ToString() != "true" || form["altText"].Count != 1 || alt.Length < 1 || alt.Length > 150)
                    return Results.Problem(statusCode: 400, title: "Supply one file, altText (1–150 characters), and demoAcknowledged=true.");
                var file = form.Files[0];
                if (file.Length < 1 || file.Length > ServiceImagePolicy.MaxInputBytes) return Results.Problem(statusCode: 413, title: "Images must be at most 5 MiB.");
                if (file.ContentType is not ("image/png" or "image/jpeg")) return Results.Problem(statusCode: 400, title: "Only JPEG and PNG images are accepted.");
                using var input = file.OpenReadStream(); using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
                while ((read = await input.ReadAsync(buffer, http.RequestAborted)) > 0)
                {
                    if (output.Length + read > ServiceImagePolicy.MaxInputBytes) return Results.Problem(statusCode: 413, title: "Image exceeds 5 MiB.");
                    await output.WriteAsync(buffer.AsMemory(0, read), http.RequestAborted);
                }
                var bytes = output.ToArray();
                if (!await scanner.IsClean(bytes, http.RequestAborted)) return Results.Problem(statusCode: 400, title: "Image failed malware scanning.");
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted); deadline.CancelAfter(TimeSpan.FromSeconds(30));
                var processed = await processor.Process(bytes, file.ContentType, deadline.Token);
                if (!await scanner.IsClean(processed.Bytes, http.RequestAborted)) return Results.Problem(statusCode: 400, title: "Processed image failed malware scanning.");
                var image = new ServiceImage
                {
                    ServiceId = serviceId,
                    AltText = alt,
                    Width = processed.Width,
                    Height = processed.Height,
                    Size = processed.Bytes.Length,
                    ObjectKey = "processed/" + Guid.NewGuid().ToString("N") + ".png",
                    Sha256 = Convert.ToHexString(SHA256.HashData(processed.Bytes))
                };
                // Persist orphan reservation before storage write, including failures/timeouts.
                var reservation = new ServiceImageDeletion { ObjectKey = image.ObjectKey, DueAt = DateTime.UtcNow.AddHours(1) };
                db.ServiceImageDeletions.Add(reservation); await db.SaveChangesAsync(http.RequestAborted);
                await store.Put(image.ObjectKey, processed.Bytes, http.RequestAborted);
                if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == http.User.FindFirstValue(ClaimTypes.NameIdentifier) && m.Active && m.Role == "Owner"))
                    return Results.NotFound();
                db.ServiceImages.Add(image); listing.Images.Add(image); listing.Status = "Draft";
                db.ServiceImageDeletions.Remove(reservation);
                return await CatalogEndpoints.Save(listing, "service-image.uploaded", http, db, 200);
            }
            catch (InvalidServiceImageException) { return Results.Problem(statusCode: 400, title: "Invalid image, unsupported animation, or excessive dimensions."); }
            catch (ImageCapacityException) { return Results.Problem(statusCode: 503, title: "Image processor is busy. Try again later."); }
            catch (DbUpdateConcurrencyException) { return Results.Problem(statusCode: 412, title: "Listing changed during upload. Reload."); }
            catch (Exception e) when (e is InvalidDataException or BadHttpRequestException) { return Results.Problem(statusCode: 413, title: "Invalid or oversized multipart request."); }
            catch (Exception e) when (e is not OperationCanceledException || !http.RequestAborted.IsCancellationRequested)
            { return Results.Problem(statusCode: 503, title: "Image processing, scanner, or storage is unavailable."); }
        }).RequireRateLimiting("evidence");
        group.MapDelete("/{serviceId:guid}/images/{imageId:guid}", async (Guid organizationId, Guid serviceId, Guid imageId, HttpContext http, MarketplaceDbContext db) =>
        {
            var listing = await db.ServiceListings.Include(s => s.Images).SingleOrDefaultAsync(s => s.Id == serviceId && s.OrganizationId == organizationId);
            if (listing == null) return Results.NotFound();
            var precondition = CatalogEndpoints.Precondition(listing, http); if (precondition != null) return precondition;
            if (listing.Status == "Archived") return Results.Problem(statusCode: 409, title: "Archived listings cannot be edited.");
            var image = listing.Images.SingleOrDefault(i => i.Id == imageId && i.DeletedAt == null); if (image == null) return Results.NotFound();
            Retire(image, db); listing.Status = "Draft"; return await CatalogEndpoints.Save(listing, "service-image.removed", http, db);
        });
        group.MapGet("/{serviceId:guid}/images/{imageId:guid}/content", async (Guid organizationId, Guid serviceId, Guid imageId, HttpContext http, MarketplaceDbContext db, IServiceImageStore store) =>
        {
            var image = await db.ServiceImages.AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId && i.ServiceId == serviceId && i.DeletedAt == null
                && db.ServiceListings.Any(s => s.Id == serviceId && s.OrganizationId == organizationId && s.Status != "Archived"));
            if (image == null) return Results.NotFound();
            return await Content(image, http, db, store, null);
        });
        app.MapGet("/api/v1/services/{serviceId:guid}/images/{imageId:guid}/content", async (Guid serviceId, Guid imageId, HttpContext http, MarketplaceDbContext db, IServiceImageStore store, IOptions<EvidenceOptions> policy) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var eligible = CatalogEndpoints.PublicQuery(db, policy.Value, DateTime.UtcNow);
            var image = await db.ServiceImages.AsNoTracking().SingleOrDefaultAsync(i => i.Id == imageId && i.ServiceId == serviceId && i.DeletedAt == null && eligible.Any(s => s.Id == serviceId));
            if (image == null) return Results.NotFound();
            return await Content(image, http, db, store, policy.Value);
        });
    }
    private static async Task<IResult> Content(ServiceImage image, HttpContext http, MarketplaceDbContext db, IServiceImageStore store, EvidenceOptions? policy)
    {
        try
        {
            var bytes = await store.Get(image.ObjectKey, http.RequestAborted);
            if (bytes.Length != image.Size || bytes.Length > ServiceImagePolicy.MaxOutputBytes || Convert.ToHexString(SHA256.HashData(bytes)) != image.Sha256)
                return Results.Problem(statusCode: 503, title: "Image temporarily unavailable.");
            // Recheck deletion, membership and public eligibility after the object read.
            if (!await db.ServiceImages.AnyAsync(i => i.Id == image.Id && i.DeletedAt == null)) return Results.NotFound();
            if (policy != null && !await CatalogEndpoints.PublicQuery(db, policy, DateTime.UtcNow).AnyAsync(s => s.Id == image.ServiceId)) return Results.NotFound();
            if (policy == null)
            {
                var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!await db.ServiceListings.AnyAsync(s => s.Id == image.ServiceId && s.Status != "Archived"
                    && db.OrganizationMemberships.Any(m => m.OrganizationId == s.OrganizationId && m.UserId == actor && m.Active))) return Results.NotFound();
            }
            http.Response.Headers.CacheControl = "no-store"; http.Response.Headers["X-Content-Type-Options"] = "nosniff";
            http.Response.Headers["Content-Security-Policy"] = "default-src 'none'; sandbox"; http.Response.Headers["Referrer-Policy"] = "no-referrer";
            http.Response.Headers.ContentDisposition = "inline; filename=\"service-image.png\"";
            return Results.Bytes(bytes, "image/png");
        }
        catch (Exception) when (!http.RequestAborted.IsCancellationRequested) { return Results.Problem(statusCode: 503, title: "Image storage temporarily unavailable."); }
    }
}
