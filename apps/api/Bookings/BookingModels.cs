namespace Marketplace.Api.Bookings;

public sealed record WorkingWindow(int Day, int StartMinute, int EndMinute);
public sealed record DateWindow(int StartMinute, int EndMinute);
public sealed record ScheduleException(string Date, DateWindow[] Windows);
public sealed record ScheduleInput(string TimeZone, int BufferMinutes, int MinimumNoticeMinutes, WorkingWindow[] Weekly, ScheduleException[] Exceptions);
public sealed class ProviderSchedule
{
    public Guid Id { get; set; } // One capacity per organization; version serializes reservation/calendar mutations.
    public string ConfigurationJson { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
}
public sealed class Booking
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ServiceId { get; set; }
    public Guid PropertyId { get; set; }
    public string CustomerId { get; set; } = "";
    public string Status { get; set; } = "PendingProviderConfirmation";
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public DateTime ReservedFrom { get; set; }
    public DateTime ReservedUntil { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string SnapshotJson { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class BookingEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public string ActorId { get; set; } = ""; // system for expiry, otherwise account id
    public string Status { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
public sealed class BookingNotice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public string UserId { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class BookingOptions { public int PendingMinutes { get; set; } = 60; }
