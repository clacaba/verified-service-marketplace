using System.Globalization;
using System.Text.Json;

namespace Marketplace.Api.Bookings;

public static class Scheduling
{
    public static ScheduleInput Read(ProviderSchedule schedule) => JsonSerializer.Deserialize<ScheduleInput>(schedule.ConfigurationJson)!;
    public static string? Validate(ScheduleInput input)
    {
        if (string.IsNullOrWhiteSpace(input.TimeZone) || input.TimeZone.Length > 100 || input.BufferMinutes is < 0 or > 120 || input.MinimumNoticeMinutes is < 0 or > 10080
            || input.Weekly == null || input.Exceptions == null || input.Weekly.Length > 42 || input.Exceptions.Length > 60) return "Invalid timezone, buffer, notice, or schedule bounds.";
        try { if (!TimeZoneInfo.TryConvertIanaIdToWindowsId(input.TimeZone, out _) && input.TimeZone != "UTC") return "Use an IANA timezone such as Asia/Manila."; TimeZoneInfo.FindSystemTimeZoneById(input.TimeZone); }
        catch (TimeZoneNotFoundException) { return "Unknown timezone."; }
        catch (InvalidTimeZoneException) { return "Invalid timezone."; }
        bool ValidWindows(IEnumerable<DateWindow> windows)
        {
            var ordered = windows.OrderBy(w => w.StartMinute).ToArray();
            return ordered.Length <= 6 && ordered.All(w => w.StartMinute >= 0 && w.EndMinute <= 1440 && w.EndMinute > w.StartMinute && w.StartMinute % 15 == 0 && w.EndMinute % 15 == 0)
                && !ordered.Zip(ordered.Skip(1)).Any(pair => pair.First.EndMinute > pair.Second.StartMinute);
        }
        if (input.Weekly.Any(w => w == null || w.Day is < 0 or > 6) || input.Weekly.GroupBy(w => w.Day).Any(g => !ValidWindows(g.Select(w => new DateWindow(w.StartMinute, w.EndMinute))))) return "Weekly windows must be nonoverlapping, same-day, 15-minute boundaries. Day 0 is Sunday.";
        if (input.Exceptions.Any(e => e == null || e.Windows == null || e.Windows.Any(w => w == null) || !DateOnly.TryParseExact(e.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || !ValidWindows(e.Windows))
            || input.Exceptions.Select(e => e.Date).Distinct().Count() != input.Exceptions.Length) return "Exceptions require unique YYYY-MM-DD dates and valid replacement windows; [] closes the day.";
        return null;
    }
    public static bool Fits(ScheduleInput schedule, DateTime start, int duration, DateTime now)
    {
        if (start.Kind != DateTimeKind.Utc || start < now.AddMinutes(schedule.MinimumNoticeMinutes) || start > now.AddDays(30) || start.Second != 0 || start.Ticks % TimeSpan.TicksPerMinute != 0) return false;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZone);
        var from = TimeZoneInfo.ConvertTimeFromUtc(start.AddMinutes(-schedule.BufferMinutes), zone);
        var until = TimeZoneInfo.ConvertTimeFromUtc(start.AddMinutes(duration + schedule.BufferMinutes), zone);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(start, zone);
        if (localStart.Minute % 15 != 0 || zone.IsAmbiguousTime(from) || zone.IsAmbiguousTime(until) || zone.IsAmbiguousTime(localStart)
            || zone.GetUtcOffset(start.AddMinutes(-schedule.BufferMinutes)) != zone.GetUtcOffset(start.AddMinutes(duration + schedule.BufferMinutes))) return false;
        // Midnight is permitted as an end boundary, never an overnight working window.
        var endMinute = until.Date == from.Date.AddDays(1) && until.TimeOfDay == TimeSpan.Zero ? 1440 : (int)until.TimeOfDay.TotalMinutes;
        if (until.Date != from.Date && endMinute != 1440) return false;
        var exception = schedule.Exceptions.SingleOrDefault(e => e.Date == from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var windows = exception != null ? exception.Windows : schedule.Weekly.Where(w => w.Day == (int)from.DayOfWeek).Select(w => new DateWindow(w.StartMinute, w.EndMinute));
        return windows.Any(w => from.TimeOfDay.TotalMinutes >= w.StartMinute && endMinute <= w.EndMinute);
    }
    public static bool Blocks(Booking b, DateTime now) => b.Status is "Confirmed" or "InProgress" || b.Status == "PendingProviderConfirmation" && b.ExpiresAt > now;
}
