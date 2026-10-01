using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Api.Bookings;
using Marketplace.Api.Data;
using Marketplace.Api.Organizations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed partial class VerificationTests
{
    private sealed class BookingRaceInterceptor(Factory factory) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (factory.BookingRace != null && eventData.Context!.ChangeTracker.Entries<Booking>().Any(e => e.State == EntityState.Added))
            {
                var callback = factory.BookingRace; factory.BookingRace = null; await callback();
            }
            return result;
        }
    }
    private static ScheduleInput Schedule(int buffer = 0, int notice = 0, string zone = "UTC") => new(zone, buffer, notice, Enumerable.Range(0, 7).Select(day => new WorkingWindow(day, 0, 1440)).ToArray(), []);
    private static async Task<(Guid Org, Guid Service, Guid Property, DateTime Start)> BookingSetup(Factory factory, Browser owner, Browser reviewer, Browser customer)
    {
        var (org, _, _) = await ApprovedPublicSetup(factory, owner, reviewer); await Publish(owner, org);
        var (service, etag) = await CreateOffer(owner, org); Assert.Equal(HttpStatusCode.OK, (await OfferPublication(owner, org, service, etag)).StatusCode);
        var current = await owner.Client.GetAsync($"/api/v1/organizations/{org}/availability");
        Assert.Equal(HttpStatusCode.OK, (await owner.Send($"organizations/{org}/availability", HttpMethod.Put, JsonContent.Create(Schedule()), current.Headers.ETag!.ToString())).StatusCode);
        await customer.Login("booking-customer@example.test"); await factory.Update(async db => (await db.Users.SingleAsync(u => u.Email == "booking-customer@example.test")).EmailConfirmed = true);
        var property = await customer.Send("properties", body: JsonContent.Create(Address())); Assert.Equal(HttpStatusCode.Created, property.StatusCode);
        var propertyId = (await property.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var start = DateTime.UtcNow.Date.AddDays(2).AddHours(12); return (org, service, propertyId, start);
    }
    private static Task<HttpResponseMessage> RequestBooking(Browser customer, Guid service, Guid property, DateTime start) => customer.Send("bookings", body: JsonContent.Create(new {serviceId = service, propertyId = property, startsAt = start}));
    [Fact]
    public async Task ReservationsBlockOverlapsAndCaptureImmutableServerSnapshots()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser(); var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc);
        var (org, service, property, start) = await BookingSetup(factory, owner, reviewer, customer);
        var created = await RequestBooking(customer, service, property, start); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var json = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = json.GetProperty("id").GetGuid(); Assert.Equal(25000, json.GetProperty("snapshot").GetProperty("service").GetProperty("PriceMinor").GetInt64());
        Assert.Equal(HttpStatusCode.Conflict, (await RequestBooking(customer, service, property, start.AddMinutes(30))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RequestBooking(customer, service, property, start.AddMinutes(60))).StatusCode);
        var address = await cc.GetAsync($"/api/v1/properties/{property}"); Assert.Equal(HttpStatusCode.OK, (await customer.Send($"properties/{property}", HttpMethod.Put, JsonContent.Create(Address("New label")), address.Headers.ETag!.ToString())).StatusCode);
        var offer = await oc.GetAsync($"/api/v1/organizations/{org}/services/{service}"); await owner.Send($"organizations/{org}/services/{service}", HttpMethod.Put, JsonContent.Create(Offer(priceMinor: 50000)), offer.Headers.ETag!.ToString());
        var detail = await cc.GetFromJsonAsync<JsonElement>($"/api/v1/bookings/{id}"); var snapshot = detail.GetProperty("booking").GetProperty("snapshot"); Assert.Equal("Demo home", snapshot.GetProperty("property").GetProperty("Label").GetString()); Assert.Equal(25000, snapshot.GetProperty("service").GetProperty("PriceMinor").GetInt64());
        Assert.Equal(HttpStatusCode.NotFound, (await RequestBooking(customer, service, property, start.AddDays(1))).StatusCode);
    }
    [Fact]
    public async Task BookingRequiresVerifiedEmailOwnedPropertyEligibleOfferValidSlotAndCsrf()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser(); using var xc = factory.Browser(); var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc); var outsider = new Browser(xc);
        var (_, service, property, start) = await BookingSetup(factory, owner, reviewer, customer); await outsider.Login("booking-outsider@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await RequestBooking(outsider, service, property, start)).StatusCode);
        await factory.Update(async db => (await db.Users.SingleAsync(u => u.Email == "booking-outsider@example.test")).EmailConfirmed = true);
        Assert.Equal(HttpStatusCode.NotFound, (await RequestBooking(outsider, service, property, start)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Send("bookings", body: JsonContent.Create(new {serviceId = service, propertyId = property, startsAt = start}), csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await RequestBooking(customer, service, property, start.AddMinutes(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await RequestBooking(customer, service, property, start.AddDays(31))).StatusCode);
        await factory.Update(async db => (await db.VerificationChecks.SingleAsync()).RevokedAt = DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.NotFound, (await RequestBooking(customer, service, property, start)).StatusCode);
    }
    [Fact]
    public async Task StaffDecisionsAreAuthorizedVersionedAuditedAndRevocationHidesAddresses()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser(); using var sc = factory.Browser(); var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc); var staff = new Browser(sc);
        var (org, service, property, start) = await BookingSetup(factory, owner, reviewer, customer); await staff.Login("booking-staff@example.test");
        var created = await RequestBooking(customer, service, property, start); var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var tag = created.Headers.ETag!.ToString();
        Assert.Equal(HttpStatusCode.NotFound, (await sc.GetAsync($"/api/v1/bookings/{id}")).StatusCode);
        await factory.Update(async db => {var user = await db.Users.SingleAsync(u => u.Email == "booking-staff@example.test"); db.OrganizationMemberships.Add(new OrganizationMembership {OrganizationId = org, UserId = user.Id, Role = "Staff"});});
        Assert.Equal(HttpStatusCode.OK, (await sc.GetAsync($"/api/v1/bookings/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send($"bookings/{id}/decisions", body: JsonContent.Create(new {action = "confirm", reason = ""}), etag: tag)).StatusCode);
        var confirmed = await staff.Send($"bookings/{id}/decisions", body: JsonContent.Create(new {action = "confirm", reason = ""}), etag: tag); Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await staff.Send($"bookings/{id}/decisions", body: JsonContent.Create(new {action = "decline", reason = "Unavailable"}), etag: tag)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.Send($"bookings/{id}/decisions", body: JsonContent.Create(new {action = "start", reason = ""}), etag: confirmed.Headers.ETag!.ToString())).StatusCode);
        var cancelled = await customer.Send($"bookings/{id}/decisions", body: JsonContent.Create(new {action = "cancel", reason = "Changed plans"}), etag: confirmed.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RequestBooking(customer, service, property, start)).StatusCode);
        await factory.Update(async db => {var user = await db.Users.SingleAsync(u => u.Email == "booking-staff@example.test"); (await db.OrganizationMemberships.SingleAsync(m => m.UserId == user.Id)).Active = false; Assert.Equal(3, await db.BookingEvents.CountAsync(e => e.BookingId == id));});
        Assert.Equal(HttpStatusCode.NotFound, (await sc.GetAsync($"/api/v1/bookings/{id}")).StatusCode);
        var notices = await sc.GetFromJsonAsync<JsonElement>("/api/v1/booking-notifications"); Assert.Equal(0, notices.GetProperty("totalCount").GetInt32());
    }
    [Fact]
    public async Task ExpiredPendingRequestReleasesCapacityWithoutWaitingForWorker()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser(); var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc);
        var (_, service, property, start) = await BookingSetup(factory, owner, reviewer, customer); var created = await RequestBooking(customer, service, property, start); var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await factory.Update(async db => (await db.Bookings.SingleAsync()).ExpiresAt = DateTime.UtcNow.AddSeconds(-1));
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Send($"bookings/{id}/decisions", body: JsonContent.Create(new {action = "confirm", reason = ""}), etag: created.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await RequestBooking(customer, service, property, start)).StatusCode);
        using var scope = factory.Services.CreateScope(); await BookingExpiryWorker.Sweep(scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>(), scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>());
        var after = await cc.GetFromJsonAsync<JsonElement>($"/api/v1/bookings/{id}"); Assert.Equal("Expired", after.GetProperty("booking").GetProperty("status").GetString());
    }
    [Theory]
    [InlineData("calendar")]
    [InlineData("property")]
    [InlineData("service")]
    [InlineData("profile")]
    public async Task DatabaseCalendarRaceRollsBackReservationEventsNotificationsAndEmails(string resource)
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser(); var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc);
        var (_, service, property, start) = await BookingSetup(factory, owner, reviewer, customer);
        factory.BookingRace = () => factory.Update(async db =>
        {
            if (resource == "calendar") (await db.ProviderSchedules.SingleAsync()).Version = Guid.NewGuid();
            if (resource == "property") (await db.CustomerProperties.SingleAsync()).Version = Guid.NewGuid();
            if (resource == "service") (await db.ServiceListings.SingleAsync()).Version = Guid.NewGuid();
            if (resource == "profile") (await db.ProviderProfiles.SingleAsync()).Version = Guid.NewGuid();
        });
        Assert.Equal(HttpStatusCode.Conflict, (await RequestBooking(customer, service, property, start)).StatusCode);
        await factory.Update(async db => {Assert.Equal(0, await db.Bookings.CountAsync()); Assert.Equal(0, await db.BookingEvents.CountAsync()); Assert.Equal(0, await db.BookingNotices.CountAsync()); Assert.Equal(0, await db.VerificationEmails.CountAsync(e => e.Subject.Contains("booking")));});
        Assert.Equal(HttpStatusCode.Created, (await RequestBooking(customer, service, property, start)).StatusCode);
    }
    [Fact]
    public async Task PublicSlotsRespectReservationsAndDoNotExposeCustomerAddresses()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser(); using var anonymous = factory.Browser(); var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc);
        var (_, service, property, start) = await BookingSetup(factory, owner, reviewer, customer);
        var path = $"/api/v1/services/{service}/availability?from={Uri.EscapeDataString(start.ToString("O"))}&until={Uri.EscapeDataString(start.AddHours(2).ToString("O"))}";
        var before = await anonymous.GetFromJsonAsync<JsonElement>(path); Assert.Contains(before.GetProperty("items").EnumerateArray(), s => s.GetProperty("startsAt").GetDateTime() == start);
        var created = await RequestBooking(customer, service, property, start); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var after = await anonymous.GetFromJsonAsync<JsonElement>(path); Assert.DoesNotContain(after.GetProperty("items").EnumerateArray(), s => s.GetProperty("startsAt").GetDateTime() < start.AddHours(1));
        Assert.DoesNotContain("Fictional Street", after.ToString()); Assert.DoesNotContain("customerId", after.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync($"/api/v1/services/{service}/availability?from={start:O}&until={start.AddDays(8):O}")).StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await factory.Update(async db => (await db.VerificationChecks.SingleAsync()).RevokedAt = DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(path)).StatusCode); Assert.Equal(HttpStatusCode.OK, (await cc.GetAsync($"/api/v1/bookings/{id}")).StatusCode);
    }
    [Fact]
    public async Task StartAndCompleteRequireProviderAndValidTransitionAndPreventLateCancellation()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser(); var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc);
        var (_, service, property, start) = await BookingSetup(factory, owner, reviewer, customer); var created = await RequestBooking(customer, service, property, start); var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var confirmed = await owner.Send($"bookings/{id}/decisions", body: JsonContent.Create(new {action = "confirm", reason = ""}), etag: created.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        await factory.Update(async db => {var b = await db.Bookings.SingleAsync(); b.StartsAt = DateTime.UtcNow.AddMinutes(-1); b.ReservedFrom = b.StartsAt; b.EndsAt = b.StartsAt.AddMinutes(60); b.ReservedUntil = b.EndsAt;});
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send($"bookings/{id}/decisions", body: JsonContent.Create(new {action = "cancel", reason = "Late"}), etag: confirmed.Headers.ETag!.ToString())).StatusCode);
        var began = await owner.Send($"bookings/{id}/decisions", body: JsonContent.Create(new {action = "start", reason = "Arrived"}), etag: confirmed.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.OK, began.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send($"bookings/{id}/decisions", body: JsonContent.Create(new {action = "complete", reason = ""}), etag: began.Headers.ETag!.ToString())).StatusCode);
        var completed = await owner.Send($"bookings/{id}/decisions", body: JsonContent.Create(new {action = "complete", reason = "Work finished"}), etag: began.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.OK, completed.StatusCode); Assert.Equal("Completed", (await completed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Send($"bookings/{id}/decisions", body: JsonContent.Create(new {action = "confirm", reason = ""}), etag: completed.Headers.ETag!.ToString())).StatusCode);
    }
    [Fact]
    public async Task AvailabilityUpdatesRequireOwnerCsrfAndExactVersion()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser(); var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc);
        var (org, _, _, _) = await BookingSetup(factory, owner, reviewer, customer); var path = $"organizations/{org}/availability"; var current = await oc.GetAsync("/api/v1/" + path);
        Assert.Equal(HttpStatusCode.NotFound, (await cc.GetAsync("/api/v1/" + path)).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await owner.Send(path, HttpMethod.Put, JsonContent.Create(Schedule()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send(path, HttpMethod.Put, JsonContent.Create(Schedule()), current.Headers.ETag!.ToString(), csrf: false)).StatusCode);
        var saved = await owner.Send(path, HttpMethod.Put, JsonContent.Create(Schedule(buffer: 15)), current.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await owner.Send(path, HttpMethod.Put, JsonContent.Create(Schedule()), current.Headers.ETag!.ToString())).StatusCode);
        await factory.Update(async db => { var user = await db.Users.SingleAsync(u => u.Email == "booking-customer@example.test"); db.OrganizationMemberships.Add(new OrganizationMembership {OrganizationId = org, UserId = user.Id, Role = "Staff"});});
        Assert.Equal(HttpStatusCode.OK, (await cc.GetAsync("/api/v1/" + path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(path, HttpMethod.Put, JsonContent.Create(Schedule()), saved.Headers.ETag!.ToString())).StatusCode);
    }
    [Fact]
    public void SchedulingValidatesBreaksExceptionsNoticeBuffersAndDst()
    {
        Assert.Null(Scheduling.Validate(Schedule())); Assert.NotNull(Scheduling.Validate(Schedule(zone: "Bad/Zone")));
        var broken = Schedule() with {Weekly = [new WorkingWindow(1, 540, 720), new WorkingWindow(1, 700, 900)]}; Assert.NotNull(Scheduling.Validate(broken));
        var date = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc); var now = date.AddDays(-1);
        var split = Schedule() with {Weekly = [new WorkingWindow(1, 540, 720), new WorkingWindow(1, 780, 1020)]};
        Assert.False(Scheduling.Fits(split, date.AddMinutes(-30), 60, now)); Assert.True(Scheduling.Fits(split, date.AddHours(1), 60, now));
        Assert.False(Scheduling.Fits(split with {BufferMinutes = 15}, date.AddHours(1), 60, now));
        Assert.False(Scheduling.Fits(split with {Exceptions = [new ScheduleException("2026-10-05", [])]}, date.AddHours(1), 60, now));
        var autumn = new DateTime(2026, 11, 1, 5, 30, 0, DateTimeKind.Utc); Assert.False(Scheduling.Fits(Schedule(zone: "America/New_York"), autumn, 30, autumn.AddDays(-1)));
        var spring = new DateTime(2026, 3, 8, 6, 30, 0, DateTimeKind.Utc); Assert.False(Scheduling.Fits(Schedule(zone: "America/New_York"), spring, 60, spring.AddDays(-1)));
    }
}
