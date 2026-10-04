namespace Marketplace.Api.CustomerReviews;

public sealed record ReviewInput(int Rating, string? Comment, bool PublishAcknowledged);
public sealed record ReplyInput(string? Comment, bool PublishAcknowledged);
