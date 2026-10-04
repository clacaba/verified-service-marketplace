using Marketplace.Api.CustomerReviews;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed class SubmissionValidatorTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public void ReviewAcceptsRatingBoundariesAndTrimmedFeedback(int rating)
    {
        Assert.Empty(ReviewSubmissionValidator.Validate(new(rating, "  Valid fictional feedback.  ", true)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void ReviewRejectsInvalidRatings(int rating)
    {
        Assert.Contains("rating", ReviewSubmissionValidator.Validate(new(rating, "Valid fictional feedback.", true)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("          ")]
    [InlineData("Invalid\0feedback")]
    public void BothValidatorsRejectInvalidFeedback(string? comment)
    {
        Assert.Contains("comment", ReviewSubmissionValidator.Validate(new(3, comment, true)));
        var errors = ProviderReplySubmissionValidator.Validate(new(comment, true));
        Assert.Contains("comment", errors);
        Assert.DoesNotContain("rating", errors);
    }

    [Fact]
    public void BothValidatorsEnforceLengthAndPublicConsent()
    {
        foreach (var comment in new[] { new string('a', 10), new string('a', 2000), "Valid\nfeedback\ttext." })
        {
            Assert.Empty(ReviewSubmissionValidator.Validate(new(3, comment, true)));
            Assert.Empty(ProviderReplySubmissionValidator.Validate(new(comment, true)));
        }
        Assert.Contains("comment", ReviewSubmissionValidator.Validate(new(3, new string('a', 2001), true)));
        Assert.Contains("comment", ProviderReplySubmissionValidator.Validate(new(new string('a', 2001), true)));
        Assert.Contains("publishAcknowledged", ReviewSubmissionValidator.Validate(new(3, "Valid feedback.", false)));
        Assert.Contains("publishAcknowledged", ProviderReplySubmissionValidator.Validate(new("Valid feedback.", false)));
    }

    [Fact]
    public void UnicodeLengthUsesUtf16LikeBrowserValidation()
    {
        var fiveEmoji = string.Concat(Enumerable.Repeat("😀", 5));
        Assert.Equal(10, fiveEmoji.Length);
        Assert.Empty(ProviderReplySubmissionValidator.Validate(new(fiveEmoji, true)));
    }
}
