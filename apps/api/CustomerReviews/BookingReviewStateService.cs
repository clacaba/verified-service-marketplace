using Marketplace.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.CustomerReviews;

public sealed record ReviewReplyResponse(string Comment, DateTime CreatedAt, string AuthorLabel);
public sealed record BookingReviewResponse(
    int Rating, string Comment, string Status, DateTime CreatedAt, ReviewReplyResponse? Reply);
public sealed record BookingReviewStateResponse(
    bool CanReview, string Reason, bool CanReply, string ReplyReason, BookingReviewResponse? Review);

// Request-scoped query service: no HTTP concerns, writes, or entity exposure.
public sealed class BookingReviewStateService(MarketplaceDbContext db)
{
    public async Task<BookingReviewStateResponse?> GetAsync(
        Guid bookingId, string userId, CancellationToken cancellationToken = default)
    {
        // Keep ownership filtering in SQL so unrelated bookings remain indistinguishable from missing ones.
        var booking = await db.Bookings.AsNoTracking()
            .Where(b => b.Id == bookingId &&
                (b.CustomerId == userId || db.OrganizationMemberships.Any(m =>
                    m.OrganizationId == b.OrganizationId && m.UserId == userId && m.Active)))
            .Select(b => new
            {
                IsCustomer = b.CustomerId == userId,
                b.Status,
                IsMember = db.OrganizationMemberships.Any(m =>
                    m.OrganizationId == b.OrganizationId && m.UserId == userId && m.Active),
                CanRepresentProvider = db.OrganizationMemberships.Any(m =>
                    m.OrganizationId == b.OrganizationId && m.UserId == userId && m.Active &&
                    (m.Role == "Owner" || m.Role == "Staff")),
                EmailVerified = db.Users.Any(u => u.Id == userId && u.EmailConfirmed)
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (booking is null) return null;

        var review = await db.BookingReviews.AsNoTracking()
            .Where(r => r.BookingId == bookingId)
            .Select(r => new BookingReviewResponse(r.Rating, r.Comment, r.Status, r.CreatedAt,
                r.Reply == null ? null :
                    new ReviewReplyResponse(r.Reply.Comment, r.Reply.CreatedAt, "Provider response")))
            .SingleOrDefaultAsync(cancellationToken);

        var context = new ReviewEligibilityContext(
            booking.IsCustomer, booking.IsMember, booking.CanRepresentProvider,
            booking.EmailVerified, booking.Status, review is not null,
            review?.Status, review?.Reply is not null);
        var reason = ReviewEligibility.ReviewReason(context);
        var replyReason = ReviewEligibility.ReplyReason(context);

        return new(reason.Length == 0, reason, replyReason.Length == 0, replyReason, review);
    }
}
