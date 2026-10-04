namespace Marketplace.Api.CustomerReviews;

public static class ReviewSubmissionValidator
{
    public static Dictionary<string, string[]> Validate(ReviewInput input)
    {
        var errors = ReviewFeedbackValidator.Validate(input.Comment, input.PublishAcknowledged);
        if (input.Rating is < 1 or > 5) errors["rating"] = ["Choose a whole-number rating from 1 to 5."];
        return errors;
    }
}

public static class ProviderReplySubmissionValidator
{
    public static Dictionary<string, string[]> Validate(ReplyInput input) =>
        ReviewFeedbackValidator.Validate(input.Comment, input.PublishAcknowledged);
}

internal static class ReviewFeedbackValidator
{
    internal static Dictionary<string, string[]> Validate(string? comment, bool publishAcknowledged)
    {
        var errors = new Dictionary<string, string[]>();
        if (comment == null || comment.Trim().Length is < 10 or > 2000 ||
            comment.Any(c => char.IsControl(c) && c is not '\n' and not '\r' and not '\t'))
            errors["comment"] = ["Write 10–2000 characters; only line breaks and tabs are allowed as control characters."];
        if (!publishAcknowledged)
            errors["publishAcknowledged"] = ["Confirm that your rating and feedback will be public."];
        return errors;
    }
}
