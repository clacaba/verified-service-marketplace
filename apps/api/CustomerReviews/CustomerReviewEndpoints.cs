using System.Security.Claims;
using Marketplace.Api.Data;
using Marketplace.Api.Providers;
using Marketplace.Api.Verification;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.Api.CustomerReviews;

public static class CustomerReviewEndpoints
{
    public sealed record ReviewInput(int Rating, string? Comment, bool PublishAcknowledged);
    private static IResult Problem(int status, string title) => Results.Problem(statusCode: status, title: title);
    private static object Summary(BookingReview review) => new { review.Rating, review.Comment, review.Status, review.CreatedAt };

    internal static Dictionary<string, string[]> Validate(ReviewInput input)
    {
        var errors = new Dictionary<string, string[]>();
        if (input.Rating is < 1 or > 5) errors["rating"] = ["Choose a whole-number rating from 1 to 5."];
        if (input.Comment == null || input.Comment.Trim().Length is < 10 or > 2000 || input.Comment.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            errors["comment"] = ["Write 10–2000 characters; only line breaks and tabs are allowed as control characters."];
        if (!input.PublishAcknowledged) errors["publishAcknowledged"] = ["Confirm that your rating and feedback will be public."];
        return errors;
    }

    public static void MapCustomerReviews(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/bookings/{bookingId:guid}/review").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (!HttpMethods.IsGet(http.Request.Method))
            {
                try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException) { return Problem(400, "Invalid CSRF token."); }
            }
            return await next(context);
        });
        group.MapGet("", async (Guid bookingId, HttpContext http, MarketplaceDbContext db) =>
        {
            var user = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId && (b.CustomerId == user || db.OrganizationMemberships.Any(m => m.OrganizationId == b.OrganizationId && m.UserId == user && m.Active)));
            if (booking == null) return Results.NotFound();
            var review = await db.BookingReviews.AsNoTracking().SingleOrDefaultAsync(r => r.BookingId == bookingId);
            var member = await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == booking.OrganizationId && m.UserId == user && m.Active);
            var verified = await db.Users.AnyAsync(u => u.Id == user && u.EmailConfirmed);
            var reason = review != null ? "This booking already has a review. Reviews cannot be edited or replaced in this release."
                : booking.CustomerId != user ? "Only this booking's customer can write its review."
                : member ? "You cannot review a provider organization you belong to."
                : booking.Status != "Completed" ? "Reviews become available after the booking is completed."
                : !verified ? "Verify your email before submitting a review." : "";
            return Results.Ok(new { canReview = reason == "", reason, review = review == null ? null : Summary(review) });
        });
        group.MapPost("", async (Guid bookingId, ReviewInput input, HttpContext http, MarketplaceDbContext db) =>
        {
            var user = http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            // Ownership comes from the saved booking; no customer/provider IDs are accepted in the request.
            var booking = await db.Bookings.SingleOrDefaultAsync(b => b.Id == bookingId && b.CustomerId == user);
            if (booking == null) return Results.NotFound();
            if (!await db.Users.AnyAsync(u => u.Id == user && u.EmailConfirmed)) return Problem(403, "Verify your email before submitting a review.");
            if (await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == booking.OrganizationId && m.UserId == user && m.Active)) return Problem(403, "You cannot review your own provider organization.");
            if (booking.Status != "Completed") return Problem(409, "Only completed bookings can be reviewed.");
            if (await db.BookingReviews.AnyAsync(r => r.BookingId == bookingId)) return Problem(409, "This booking already has a review.");
            var errors = Validate(input);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var review = new BookingReview { BookingId = booking.Id, Rating = input.Rating, Comment = input.Comment!.Trim() };
            db.BookingReviews.Add(review);
            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // The database key, not the pre-check, protects two simultaneous submissions.
                if (await db.BookingReviews.AsNoTracking().AnyAsync(r => r.BookingId == bookingId)) return Problem(409, "This booking already has a review. Reload to see it.");
                throw;
            }
            return Results.Created($"/api/v1/bookings/{bookingId}/review", Summary(review));
        });
        app.MapGet("/api/v1/providers/{providerId:guid}/reviews", async (Guid providerId, int? page, int? pageSize, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";
            var current = page ?? 1; var size = pageSize ?? 10;
            if (current is < 1 or > 100000 || size is < 1 or > 50) return Problem(400, "Invalid review pagination.");
            var profile = await PublicProviderEndpoints.Eligible(db, policy.Value, DateTime.UtcNow).AsNoTracking().SingleOrDefaultAsync(p => p.Id == providerId && p.PublishedAt != null);
            if (profile == null) return Results.NotFound();
            var query = db.BookingReviews.AsNoTracking().Where(r => r.Booking.OrganizationId == profile.OrganizationId && r.Booking.Status == "Completed" && r.Status == "Published");
            var total = await query.CountAsync();
            var average = await query.Select(r => (double?)r.Rating).AverageAsync();
            var items = await query.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.BookingId).Skip((current - 1) * size).Take(size)
                .Select(r => new { r.Rating, r.Comment, r.CreatedAt, authorLabel = "Customer with a completed booking" }).ToListAsync();
            if (!await PublicProviderEndpoints.Eligible(db, policy.Value, DateTime.UtcNow).AnyAsync(p => p.Id == providerId && p.PublishedAt != null)) return Results.NotFound();
            return Results.Ok(new { items, totalCount = total, averageRating = average == null ? (double?)null : Math.Round(average.Value, 2), page = current, pageSize = size,
                disclosure = "Portfolio demo: ratings reflect completed fictional marketplace bookings, not a guarantee of service quality." });
        });
    }
}
