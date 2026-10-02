using Marketplace.Api.Bookings;

namespace Marketplace.Api.CustomerReviews;

public sealed class BookingReview
{
    public Guid BookingId { get; set; }
    public Booking Booking { get; set; } = null!;
    public int Rating { get; set; }
    public string Comment { get; set; } = "";
    public string Status { get; set; } = "Published";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
