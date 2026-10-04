namespace Marketplace.Api.CustomerReviews;

public sealed class ProviderReviewReply
{
    public Guid BookingId { get; set; }
    public string ActorId { get; set; } = "";
    public string Comment { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
