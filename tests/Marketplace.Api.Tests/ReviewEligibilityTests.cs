using Marketplace.Api.CustomerReviews;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed class ReviewEligibilityTests
{
    private static readonly ReviewEligibilityContext Customer =
        new(true, false, false, true, "Completed", false, null, false);
    private static readonly ReviewEligibilityContext Provider =
        new(false, true, true, true, "Completed", true, "Published", false);

    [Fact]
    public void CompletedBookingCustomerMayReviewButNotReply()
    {
        Assert.Empty(ReviewEligibility.ReviewReason(Customer));
        Assert.NotEmpty(ReviewEligibility.ReplyReason(Customer));
    }

    [Fact]
    public void ProviderMayReplyButNotReview()
    {
        Assert.Empty(ReviewEligibility.ReplyReason(Provider));
        Assert.NotEmpty(ReviewEligibility.ReviewReason(Provider));
    }

    [Fact]
    public void ExistingReviewTakesPrecedenceOverOtherReviewRestrictions()
    {
        var context = Customer with { HasReview = true, IsOrganizationMember = true, EmailVerified = false };
        Assert.Equal("This booking already has a review. Reviews cannot be edited or replaced in this release.",
            ReviewEligibility.ReviewReason(context));
    }

    [Fact]
    public void CustomerCannotReplyEvenWhenTheyAreProviderStaff()
    {
        Assert.Equal(
            "Only this provider's active Owners or Staff may reply; customers cannot reply to their own reviews.",
            ReviewEligibility.ReplyReason(Provider with { IsCustomer = true }));
    }

    [Fact]
    public void ReviewRequiresIndependentVerifiedCustomerAndCompletedBooking()
    {
        Assert.NotEmpty(ReviewEligibility.ReviewReason(Customer with { IsCustomer = false }));
        Assert.NotEmpty(ReviewEligibility.ReviewReason(Customer with { IsOrganizationMember = true }));
        Assert.NotEmpty(ReviewEligibility.ReviewReason(Customer with { BookingStatus = "Confirmed" }));
        Assert.NotEmpty(ReviewEligibility.ReviewReason(Customer with { EmailVerified = false }));
    }

    [Fact]
    public void ReplyRequiresVerifiedProviderAndPublishedCompletedReviewWithoutExistingReply()
    {
        Assert.NotEmpty(ReviewEligibility.ReplyReason(Provider with { CanRepresentProvider = false }));
        Assert.NotEmpty(ReviewEligibility.ReplyReason(Provider with { EmailVerified = false }));
        Assert.NotEmpty(ReviewEligibility.ReplyReason(Provider with { HasReview = false }));
        Assert.NotEmpty(ReviewEligibility.ReplyReason(Provider with { HasReply = true }));
        Assert.NotEmpty(ReviewEligibility.ReplyReason(Provider with { ReviewStatus = "Hidden" }));
        Assert.NotEmpty(ReviewEligibility.ReplyReason(Provider with { BookingStatus = "Cancelled" }));
    }

    [Fact]
    public void EmailVerificationPrecedesMissingReviewAndDuplicateReply()
    {
        Assert.Equal("Verify your email before replying.",
            ReviewEligibility.ReplyReason(Provider with { EmailVerified = false, HasReview = false, HasReply = true }));
    }
}
