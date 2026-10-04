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
    private static IResult Problem(int status, string title) => Results.Problem(statusCode: status, title: title);
    private static async Task<IResult> GetReviewStateAsync(
        Guid bookingId,
        HttpContext http,
        BookingReviewStateService service,
        CancellationToken cancellationToken)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();

        var state = await service.GetAsync(bookingId, userId, cancellationToken);
        return state is null ? Results.NotFound() : Results.Ok(state);
    }

    private static async Task<IResult> SubmitReviewAsync(
        Guid bookingId, ReviewInput input, HttpContext http,
        ReviewSubmissionService service, CancellationToken cancellationToken)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
        return ToHttpResult(bookingId, await service.SubmitAsync(bookingId, userId, input, cancellationToken));
    }

    private static async Task<IResult> SubmitReplyAsync(
        Guid bookingId, ReplyInput input, HttpContext http,
        ProviderReplySubmissionService service, CancellationToken cancellationToken)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Results.Unauthorized();
        return ToHttpResult(bookingId, await service.SubmitAsync(bookingId, userId, input, cancellationToken));
    }

    private static IResult ToHttpResult<T>(Guid bookingId, SubmissionResult<T> result) =>
        result.Outcome switch
        {
            SubmissionOutcome.Created => Results.Created($"/api/v1/bookings/{bookingId}/review", result.Value),
            SubmissionOutcome.NotFound => Results.NotFound(),
            SubmissionOutcome.Forbidden => Problem(403, result.Reason!),
            SubmissionOutcome.Conflict => Problem(409, result.Reason!),
            SubmissionOutcome.Invalid => Results.ValidationProblem(result.Errors!),
            _ => throw new InvalidOperationException("Unknown submission outcome.")
        };

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
        group.MapGet("", GetReviewStateAsync);
        group.MapPost("", SubmitReviewAsync);
        group.MapPost("/reply", SubmitReplyAsync);
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
                .Select(r => new { r.Rating, r.Comment, r.CreatedAt, authorLabel = "Customer with a completed booking", reply = r.Reply == null ? null : new { r.Reply.Comment, r.Reply.CreatedAt, authorLabel = "Provider response" } }).ToListAsync();
            if (!await PublicProviderEndpoints.Eligible(db, policy.Value, DateTime.UtcNow).AnyAsync(p => p.Id == providerId && p.PublishedAt != null)) return Results.NotFound();
            return Results.Ok(new { items, totalCount = total, averageRating = average == null ? (double?)null : Math.Round(average.Value, 2), page = current, pageSize = size,
                disclosure = "Portfolio demo: ratings reflect completed fictional marketplace bookings, not a guarantee of service quality." });
        });
    }
}
