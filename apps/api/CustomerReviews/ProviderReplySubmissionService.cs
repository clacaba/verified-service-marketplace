using Marketplace.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.CustomerReviews;

// Request-scoped use case: re-check authorization at write time, never trust display eligibility.
public sealed class ProviderReplySubmissionService(MarketplaceDbContext db)
{
    public async Task<SubmissionResult<ReviewReplyResponse>> SubmitAsync(
        Guid bookingId, string userId, ReplyInput input,
        CancellationToken cancellationToken = default)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId, cancellationToken);
        if (booking == null)
            return SubmissionResult<ReviewReplyResponse>.NotFound();
        if (!await db.OrganizationMemberships.AnyAsync(m =>
            m.OrganizationId == booking.OrganizationId && m.UserId == userId && m.Active &&
            (m.Role == "Owner" || m.Role == "Staff"), cancellationToken))
            return SubmissionResult<ReviewReplyResponse>.NotFound();
        if (booking.CustomerId == userId)
            return SubmissionResult<ReviewReplyResponse>.Forbidden("Customers cannot respond to their own reviews.");
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.EmailConfirmed, cancellationToken))
            return SubmissionResult<ReviewReplyResponse>.Forbidden("Verify your email before replying.");
        var review = await db.BookingReviews.AsNoTracking().SingleOrDefaultAsync(r => r.BookingId == bookingId, cancellationToken);
        if (review == null)
            return SubmissionResult<ReviewReplyResponse>.NotFound();
        if (review.Status != "Published" || booking.Status != "Completed")
            return SubmissionResult<ReviewReplyResponse>.Conflict("Only published reviews of completed bookings can receive replies.");
        if (await db.ProviderReviewReplies.AnyAsync(r => r.BookingId == bookingId, cancellationToken))
            return SubmissionResult<ReviewReplyResponse>.Conflict("This review already has a provider response.");
        var errors = ProviderReplySubmissionValidator.Validate(input);
        if (errors.Count != 0)
            return SubmissionResult<ReviewReplyResponse>.Invalid(errors);
        var reply = new ProviderReviewReply { BookingId = bookingId, ActorId = userId, Comment = input.Comment!.Trim() };
        db.ProviderReviewReplies.Add(reply);
        db.OrganizationAuditEvents.Add(new Marketplace.Api.Organizations.OrganizationAuditEvent
        {
            OrganizationId = booking.OrganizationId,
            ActorId = userId,
            Action = "review.replied",
            ResourceId = bookingId.ToString()
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            if (await db.ProviderReviewReplies.AsNoTracking().AnyAsync(r => r.BookingId == bookingId, cancellationToken))
                return SubmissionResult<ReviewReplyResponse>.Conflict("This review already has a provider response. Reload to see it.");
            throw;
        }
        return SubmissionResult<ReviewReplyResponse>.Created(new(reply.Comment, reply.CreatedAt, "Provider response"));
    }
}
