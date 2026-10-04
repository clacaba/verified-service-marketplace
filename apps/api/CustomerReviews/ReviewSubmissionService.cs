using Marketplace.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.CustomerReviews;

// Request-scoped use case: re-check authorization at write time, never trust display eligibility.
public sealed class ReviewSubmissionService(MarketplaceDbContext db)
{
    public async Task<SubmissionResult<BookingReviewResponse>> SubmitAsync(
        Guid bookingId, string userId, ReviewInput input,
        CancellationToken cancellationToken = default)
    {
        var booking = await db.Bookings.SingleOrDefaultAsync(b => b.Id == bookingId && b.CustomerId == userId, cancellationToken);
        if (booking == null)
            return SubmissionResult<BookingReviewResponse>.NotFound();
        if (!await db.Users.AnyAsync(u => u.Id == userId && u.EmailConfirmed, cancellationToken))
            return SubmissionResult<BookingReviewResponse>.Forbidden("Verify your email before submitting a review.");
        if (await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == booking.OrganizationId && m.UserId == userId && m.Active, cancellationToken))
            return SubmissionResult<BookingReviewResponse>.Forbidden("You cannot review your own provider organization.");
        if (booking.Status != "Completed")
            return SubmissionResult<BookingReviewResponse>.Conflict("Only completed bookings can be reviewed.");
        if (await db.BookingReviews.AnyAsync(r => r.BookingId == bookingId, cancellationToken))
            return SubmissionResult<BookingReviewResponse>.Conflict("This booking already has a review.");
        var errors = ReviewSubmissionValidator.Validate(input);
        if (errors.Count != 0)
            return SubmissionResult<BookingReviewResponse>.Invalid(errors);
        var review = new BookingReview { BookingId = booking.Id, Rating = input.Rating, Comment = input.Comment!.Trim() };
        db.BookingReviews.Add(review);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The database key, not the pre-check, protects two simultaneous submissions.
            if (await db.BookingReviews.AsNoTracking().AnyAsync(r => r.BookingId == bookingId, cancellationToken))
                return SubmissionResult<BookingReviewResponse>.Conflict("This booking already has a review. Reload to see it.");
            throw;
        }
        return SubmissionResult<BookingReviewResponse>.Created(new(review.Rating, review.Comment, review.Status, review.CreatedAt, null));
    }
}
