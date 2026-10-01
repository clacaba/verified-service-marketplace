using System.Security.Claims;
using System.Text.Json;
using Marketplace.Api.Catalog;
using Marketplace.Api.Data;
using Marketplace.Api.Identity;
using Marketplace.Api.Organizations;
using Marketplace.Api.Verification;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.Api.Bookings;

public static class BookingEndpoints
{
    public sealed record CreateInput(Guid ServiceId, Guid PropertyId, DateTimeOffset StartsAt);
    public sealed record DecisionInput(string Action, string Reason);
    private static string UserId(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static string Tag(Guid version) => $"\"{version:D}\"";
    private static IResult Problem(int status, string title) => Results.Problem(statusCode: status, title: title);
    private static IResult? Match(Guid version, HttpContext http) => string.IsNullOrWhiteSpace(http.Request.Headers.IfMatch) ? Problem(428, "If-Match is required.") : http.Request.Headers.IfMatch.ToString() != Tag(version) ? Problem(412, "This record changed. Reload before continuing.") : null;
    private static object Summary(Booking b) => new { b.Id, b.OrganizationId, b.ServiceId, b.PropertyId, b.Status, b.StartsAt, b.EndsAt, b.ExpiresAt, b.Version, b.CreatedAt, snapshot = JsonSerializer.Deserialize<JsonElement>(b.SnapshotJson) };
    internal static async Task Transition(Booking b, string status, string actor, string reason, MarketplaceDbContext db, IDataProtectionProvider protection)
    {
        b.Status = status; b.Version = Guid.NewGuid(); db.BookingEvents.Add(new BookingEvent { BookingId = b.Id, Status = status, ActorId = actor, Reason = reason });
        var recipients = await db.OrganizationMemberships.Where(m => m.OrganizationId == b.OrganizationId && m.Active).Select(m => m.UserId).ToListAsync(); recipients.Add(b.CustomerId);
        foreach (var recipient in recipients.Distinct())
        {
            db.BookingNotices.Add(new BookingNotice { BookingId = b.Id, UserId = recipient, Message = "Booking: " + status + ". Sign in to view the booking." });
            var account = await db.Users.SingleAsync(u => u.Id == recipient);
            if (account.EmailConfirmed && account.Email != null) db.VerificationEmails.Add(new VerificationEmail { Recipient = account.Email, Subject = "Your marketplace booking has an update", ProtectedBody = protection.CreateProtector(VerificationEmailWorker.Purpose).Protect("Your fictional marketplace booking has changed to " + status + ". Sign in and open My bookings. No payment has been taken.") });
        }
    }
    private static IQueryable<Booking> Participant(MarketplaceDbContext db, string user) => db.Bookings.Where(b => b.CustomerId == user || db.OrganizationMemberships.Any(m => m.OrganizationId == b.OrganizationId && m.UserId == user && m.Active));
    public static void MapMarketplaceBookings(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext; http.Response.Headers.CacheControl = "no-store"; http.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (!HttpMethods.IsGet(http.Request.Method))
            {
                try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException) { return Problem(400, "Invalid CSRF token."); }
            }
            try { return await next(context); }
            catch (DbUpdateConcurrencyException) { return Problem(409, "Availability or booking changed. Reload and retry; no reservation was saved."); }
        });
        group.MapGet("/organizations/{organizationId:guid}/availability", async (Guid organizationId, HttpContext http, MarketplaceDbContext db) =>
        {
            if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == UserId(http) && m.Active)) return Results.NotFound();
            var schedule = await db.ProviderSchedules.AsNoTracking().SingleOrDefaultAsync(s => s.Id == organizationId);
            if (schedule == null) { http.Response.Headers.ETag = Tag(Guid.Empty); return Results.Ok(new { configured = false, configuration = new ScheduleInput("Asia/Manila", 0, 60, [], []) }); }
            http.Response.Headers.ETag = Tag(schedule.Version); return Results.Ok(new { configured = true, configuration = Scheduling.Read(schedule) });
        });
        group.MapPut("/organizations/{organizationId:guid}/availability", async (Guid organizationId, ScheduleInput input, HttpContext http, MarketplaceDbContext db) =>
        {
            var member = await db.OrganizationMemberships.SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == UserId(http) && m.Active);
            if (member == null) return Results.NotFound(); if (member.Role != "Owner") return Results.Forbid();
            var schedule = await db.ProviderSchedules.SingleOrDefaultAsync(s => s.Id == organizationId); var error = Match(schedule?.Version ?? Guid.Empty, http); if (error != null) return error;
            var validation = Scheduling.Validate(input); if (validation != null) return Problem(400, validation);
            // Existing reservations remain valid; new bookings use the replacement rules.
            if (schedule == null) { schedule = new ProviderSchedule { Id = organizationId }; db.ProviderSchedules.Add(schedule); }
            schedule.ConfigurationJson = JsonSerializer.Serialize(input); schedule.Version = Guid.NewGuid();
            db.OrganizationAuditEvents.Add(new OrganizationAuditEvent { OrganizationId = organizationId, ActorId = UserId(http), Action = "availability.updated", ResourceId = organizationId.ToString() });
            try { await db.SaveChangesAsync(); } catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Problem(409, "Availability was configured concurrently. Reload."); }
            http.Response.Headers.ETag = Tag(schedule.Version); return Results.Ok(new { configured = true, configuration = input });
        });
        app.MapGet("/api/v1/services/{serviceId:guid}/availability", async (Guid serviceId, DateTimeOffset from, DateTimeOffset until, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            http.Response.Headers.CacheControl = "no-store"; var now = DateTime.UtcNow;
            if (until <= from || until - from > TimeSpan.FromDays(7) || from.UtcDateTime < now.AddMinutes(-1) || until.UtcDateTime > now.AddDays(30)) return Problem(400, "Choose a future UTC range of at most seven days within the next 30 days.");
            var offer = await CatalogEndpoints.PublicQuery(db, policy.Value, now).SingleOrDefaultAsync(s => s.Id == serviceId); if (offer == null) return Results.NotFound();
            var schedule = await db.ProviderSchedules.AsNoTracking().SingleOrDefaultAsync(s => s.Id == offer.OrganizationId); if (schedule == null) return Results.Ok(new { items = Array.Empty<object>(), timeZone = "Asia/Manila" });
            var configuration = Scheduling.Read(schedule); var reservations = await db.Bookings.AsNoTracking().Where(b => b.OrganizationId == offer.OrganizationId && (b.Status == "Confirmed" || b.Status == "InProgress" || b.Status == "PendingProviderConfirmation" && b.ExpiresAt > now) && b.ReservedFrom < until.UtcDateTime.AddDays(1) && b.ReservedUntil > from.UtcDateTime.AddDays(-1)).ToListAsync();
            var slots = new List<object>(); var first = new DateTime((from.UtcTicks + TimeSpan.TicksPerMinute - 1) / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute, DateTimeKind.Utc);
            for (var start = first; start < until.UtcDateTime; start = start.AddMinutes(1))
            {
                if (!Scheduling.Fits(configuration, start, offer.DurationMinutes, now) || reservations.Any(b => b.ReservedFrom < start.AddMinutes(offer.DurationMinutes + configuration.BufferMinutes) && b.ReservedUntil > start.AddMinutes(-configuration.BufferMinutes))) continue;
                slots.Add(new { startsAt = start, endsAt = start.AddMinutes(offer.DurationMinutes) });
            }
            if (!await CatalogEndpoints.PublicQuery(db, policy.Value, DateTime.UtcNow).AnyAsync(s => s.Id == serviceId && s.Version == offer.Version)) return Results.NotFound();
            return Results.Ok(new { items = slots, configuration.TimeZone, disclosure = "Slots are not held until a booking request succeeds." });
        });
        group.MapPost("/bookings", async (CreateInput input, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy, IOptions<BookingOptions> options, IDataProtectionProvider protection) =>
        {
            var user = UserId(http); if (!await db.Users.AnyAsync(u => u.Id == user && u.EmailConfirmed)) return Problem(403, "Verify your email before booking.");
            var now = DateTime.UtcNow; var start = input.StartsAt.UtcDateTime;
            var property = await db.CustomerProperties.SingleOrDefaultAsync(p => p.Id == input.PropertyId && p.CustomerId == user && p.ArchivedAt == null); if (property == null) return Results.NotFound();
            var offer = await CatalogEndpoints.PublicQuery(db, policy.Value, now).SingleOrDefaultAsync(s => s.Id == input.ServiceId); if (offer == null) return Results.NotFound();
            if (await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == offer.OrganizationId && m.UserId == user && m.Active)) return Problem(409, "You cannot book your own provider organization.");
            var schedule = await db.ProviderSchedules.SingleOrDefaultAsync(s => s.Id == offer.OrganizationId);
            if (schedule == null) return Problem(409, "This provider has not configured availability."); var configuration = Scheduling.Read(schedule);
            if (start <= now || !Scheduling.Fits(configuration, start, offer.DurationMinutes, now)) return Problem(409, "This start time is outside current availability or notice limits.");
            var reservedFrom = start.AddMinutes(-configuration.BufferMinutes); var reservedUntil = start.AddMinutes(offer.DurationMinutes + configuration.BufferMinutes);
            if (await db.Bookings.AnyAsync(b => b.OrganizationId == offer.OrganizationId && (b.Status == "Confirmed" || b.Status == "InProgress" || b.Status == "PendingProviderConfirmation" && b.ExpiresAt > now) && b.ReservedFrom < reservedUntil && b.ReservedUntil > reservedFrom)) return Problem(409, "This slot is no longer available.");
            // Every reservation/calendar mutation changes the same row. EF's database compare-and-swap and
            // SaveChanges transaction roll back the entire insert if another request won that capacity.
            schedule.Version = Guid.NewGuid(); property.Version = Guid.NewGuid(); // Serialize with property edits/archive.
            var trackedOffer = await db.ServiceListings.SingleAsync(s => s.Id == offer.Id);
            if (trackedOffer.Version != offer.Version) return Problem(409, "This service changed. Reload its current offer.");
            trackedOffer.Version = Guid.NewGuid(); // Serialize price/duration/publication edits with snapshots.
            var provider = await db.ProviderProfiles.SingleAsync(p => p.OrganizationId == offer.OrganizationId);
            if (!await CatalogEndpoints.PublicQuery(db, policy.Value, DateTime.UtcNow).AnyAsync(s => s.Id == offer.Id && s.Version == offer.Version)) return Results.NotFound();
            provider.Version = Guid.NewGuid(); // Profile edits/verification decisions cannot race a stale eligible snapshot.
            var snapshot = JsonSerializer.Serialize(new { service = new { offer.Title, offer.Description, offer.PriceMinor, offer.Currency, offer.DurationMinutes, offer.ServiceArea }, property = new { property.Label, property.AddressLine1, property.AddressLine2, property.City, property.Region, property.PostalCode, property.CountryCode }, configuration.TimeZone, configuration.BufferMinutes });
            var expires = now.AddMinutes(options.Value.PendingMinutes); if (expires > start) expires = start;
            var b = new Booking { OrganizationId = offer.OrganizationId, ServiceId = offer.Id, PropertyId = property.Id, CustomerId = user, StartsAt = start, EndsAt = start.AddMinutes(offer.DurationMinutes), ReservedFrom = reservedFrom, ReservedUntil = reservedUntil, ExpiresAt = expires, SnapshotJson = snapshot };
            db.Bookings.Add(b); await Transition(b, b.Status, user, "Customer requested service.", db, protection);
            await db.SaveChangesAsync(); http.Response.Headers.ETag = Tag(b.Version); return Results.Created($"/api/v1/bookings/{b.Id}", Summary(b));
        });
        group.MapGet("/bookings", async (Guid? organizationId, int? page, int? pageSize, HttpContext http, MarketplaceDbContext db) =>
        {
            var current = page ?? 1; var size = pageSize ?? 20; if (current is < 1 or > 100000 || size is < 1 or > 100) return Problem(400, "Invalid pagination.");
            var user = UserId(http); var query = Participant(db, user).AsNoTracking();
            if (organizationId != null) { if (!await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == organizationId && m.UserId == user && m.Active)) return Results.NotFound(); query = query.Where(b => b.OrganizationId == organizationId); } else query = query.Where(b => b.CustomerId == user);
            var rows = await query.OrderByDescending(b => b.StartsAt).ThenBy(b => b.Id).Skip((current - 1) * size).Take(size).ToListAsync();
            return Results.Ok(new { items = rows.Select(Summary), totalCount = await query.CountAsync(), page = current, pageSize = size });
        });
        group.MapGet("/bookings/{bookingId:guid}", async (Guid bookingId, HttpContext http, MarketplaceDbContext db) =>
        {
            var b = await Participant(db, UserId(http)).AsNoTracking().SingleOrDefaultAsync(b => b.Id == bookingId); if (b == null) return Results.NotFound();
            http.Response.Headers.ETag = Tag(b.Version); var events = await db.BookingEvents.Where(e => e.BookingId == bookingId).OrderBy(e => e.OccurredAt).Select(e => new { e.Status, e.Reason, e.OccurredAt }).ToListAsync();
            return Results.Ok(new { booking = Summary(b), events });
        });
        group.MapPost("/bookings/{bookingId:guid}/decisions", async (Guid bookingId, DecisionInput input, HttpContext http, MarketplaceDbContext db, IDataProtectionProvider protection) =>
        {
            var user = UserId(http); var b = await Participant(db, user).SingleOrDefaultAsync(b => b.Id == bookingId); if (b == null) return Results.NotFound(); var error = Match(b.Version, http); if (error != null) return error;
            var now = DateTime.UtcNow; if (b.Status == "PendingProviderConfirmation" && b.ExpiresAt <= now) return Problem(409, "This request expired; wait for the expiry update and reload.");
            if (input.Reason == null || input.Reason.Trim().Length > 500 || input.Reason.Any(char.IsControl)) return Problem(400, "Use a reason of at most 500 characters without control characters.");
            var customer = b.CustomerId == user; var provider = await db.OrganizationMemberships.AnyAsync(m => m.OrganizationId == b.OrganizationId && m.UserId == user && m.Active);
            string? status = input.Action switch
            {
                "confirm" when provider && b.Status == "PendingProviderConfirmation" && now < b.StartsAt => "Confirmed",
                "decline" when provider && b.Status == "PendingProviderConfirmation" => "Declined",
                "start" when provider && b.Status == "Confirmed" && now >= b.StartsAt => "InProgress",
                "complete" when provider && b.Status == "InProgress" => "Completed",
                "cancel" when b.Status is "PendingProviderConfirmation" or "Confirmed" && now < b.StartsAt => customer ? "CancelledByCustomer" : provider ? "CancelledByProvider" : null,
                _ => null
            };
            if (status == null) return Problem(409, "This action is not allowed for your role or the booking's current state/time.");
            if (status is "Declined" or "CancelledByProvider" or "CancelledByCustomer" && string.IsNullOrWhiteSpace(input.Reason)) return Problem(400, "Provide a reason for declining or cancelling.");
            var schedule = await db.ProviderSchedules.SingleAsync(s => s.Id == b.OrganizationId); schedule.Version = Guid.NewGuid();
            await Transition(b, status, user, input.Reason.Trim(), db, protection); await db.SaveChangesAsync(); http.Response.Headers.ETag = Tag(b.Version); return Results.Ok(Summary(b));
        });
        group.MapGet("/booking-notifications", async (int? page, HttpContext http, MarketplaceDbContext db) =>
        {
            var current = page ?? 1; if (current is < 1 or > 100000) return Problem(400, "Invalid pagination.");
            var user = UserId(http); var participants = Participant(db, user); var query = db.BookingNotices.Where(n => n.UserId == user && participants.Any(b => b.Id == n.BookingId));
            return Results.Ok(new { items = await query.OrderByDescending(n => n.CreatedAt).ThenBy(n => n.Id).Skip((current - 1) * 20).Take(20).Select(n => new { n.Id, n.BookingId, n.Message, n.CreatedAt }).ToListAsync(), totalCount = await query.CountAsync() });
        });
    }
}
