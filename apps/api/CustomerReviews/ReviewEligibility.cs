namespace Marketplace.Api.CustomerReviews;

public sealed record ReviewEligibilityContext(
    bool IsCustomer,
    bool IsOrganizationMember,
    bool CanRepresentProvider,
    bool EmailVerified,
    string BookingStatus,
    bool HasReview,
    string? ReviewStatus,
    bool HasReply);

// Pure display-eligibility rules. Submission endpoints still enforce authorization at write time.
public static class ReviewEligibility
{
    public static string ReviewReason(ReviewEligibilityContext context)
    {
        if (context.HasReview)
            return "This booking already has a review. Reviews cannot be edited or replaced in this release.";
        if (!context.IsCustomer)
            return "Only this booking's customer can write its review.";
        if (context.IsOrganizationMember)
            return "You cannot review a provider organization you belong to.";
        if (context.BookingStatus != "Completed")
            return "Reviews become available after the booking is completed.";
        if (!context.EmailVerified)
            return "Verify your email before submitting a review.";
        return "";
    }

    public static string ReplyReason(ReviewEligibilityContext context)
    {
        if (!context.CanRepresentProvider || context.IsCustomer)
            return "Only this provider's active Owners or Staff may reply; customers cannot reply to their own reviews.";
        if (!context.EmailVerified)
            return "Verify your email before replying.";
        if (!context.HasReview)
            return "A customer review is required before replying.";
        if (context.HasReply)
            return "This review already has a provider response. Replies cannot be edited or replaced.";
        if (context.ReviewStatus != "Published" || context.BookingStatus != "Completed")
            return "Only published reviews of completed bookings can receive replies.";
        return "";
    }
}
