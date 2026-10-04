using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Api.CustomerReviews;
using Marketplace.Api.Data;
using Marketplace.Api.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed partial class VerificationTests
{
    private sealed class CustomerReviewRaceInterceptor(Factory factory) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (factory.CustomerReviewRace != null && eventData.Context!.ChangeTracker.Entries<BookingReview>().Any(e => e.State == EntityState.Added))
            {
                var callback = factory.CustomerReviewRace; factory.CustomerReviewRace = null; await callback();
            }
            return result;
        }
    }
    [Fact]
    public async Task RacingReviewInsertReturnsConflictAndRetainsOnlyTheWinningFeedback()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser();
        var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc);
        var (_, service, property, start) = await BookingSetup(factory, owner, reviewer, customer);
        var id = await CompleteReviewBooking(factory, owner, customer, service, property, start);
        factory.CustomerReviewRace = () => factory.Update(db => { db.BookingReviews.Add(new BookingReview { BookingId = id, Rating = 3, Comment = "Winning fictional feedback." }); return Task.CompletedTask; });
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send($"bookings/{id}/review", body: ReviewBody())).StatusCode);
        var readback = await cc.GetFromJsonAsync<JsonElement>($"/api/v1/bookings/{id}/review");
        Assert.Equal(3, readback.GetProperty("review").GetProperty("rating").GetInt32());
        await factory.Update(async db => Assert.Equal(1, await db.BookingReviews.CountAsync()));
    }
    private static async Task<Guid> CompleteReviewBooking(Factory factory, Browser owner, Browser customer, Guid service, Guid property, DateTime start)
    {
        var created = await RequestBooking(customer, service, property, start);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var confirmed = await owner.Send($"bookings/{id}/decisions", body: JsonContent.Create(new { action = "confirm", reason = "" }), etag: created.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        await factory.Update(async db =>
        {
            var booking = await db.Bookings.SingleAsync(b => b.Id == id);
            booking.StartsAt = DateTime.UtcNow.AddMinutes(-1); booking.ReservedFrom = booking.StartsAt;
            booking.EndsAt = booking.StartsAt.AddMinutes(60); booking.ReservedUntil = booking.EndsAt;
        });
        var began = await owner.Send($"bookings/{id}/decisions", body: JsonContent.Create(new { action = "start", reason = "Fictional work started." }), etag: confirmed.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, began.StatusCode);
        var completed = await owner.Send($"bookings/{id}/decisions", body: JsonContent.Create(new { action = "complete", reason = "Fictional work completed." }), etag: began.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        return id;
    }
    private static JsonContent ReviewBody(int rating = 5, string comment = "Fictional service was clear and helpful.", bool acknowledged = true)
        => JsonContent.Create(new { rating, comment, publishAcknowledged = acknowledged });

    [Fact]
    public async Task CompletedBookingCustomerCanPublishExactlyOneReviewAndProviderCannotReplaceIt()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser();
        var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc);
        var (_, service, property, start) = await BookingSetup(factory, owner, reviewer, customer);
        var id = await CompleteReviewBooking(factory, owner, customer, service, property, start); var path = $"bookings/{id}/review";
        Assert.True((await cc.GetFromJsonAsync<JsonElement>("/api/v1/" + path)).GetProperty("canReview").GetBoolean());
        var saved = await customer.Send(path, body: ReviewBody(comment: "  Helpful fictional service.\nClear communication.  "));
        Assert.Equal(HttpStatusCode.Created, saved.StatusCode); Assert.Equal("no-store", saved.Headers.CacheControl!.ToString());
        Assert.Equal("Helpful fictional service.\nClear communication.", (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("comment").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await customer.Send(path, body: ReviewBody())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Send(path, body: ReviewBody())).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await owner.Send(path, HttpMethod.Delete)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await customer.Send(path, HttpMethod.Put, ReviewBody())).StatusCode);
        var retained = await oc.GetFromJsonAsync<JsonElement>("/api/v1/" + path); Assert.False(retained.GetProperty("canReview").GetBoolean()); Assert.Equal(5, retained.GetProperty("review").GetProperty("rating").GetInt32());
    }
    [Fact]
    public async Task ReviewsRejectAnonymousOutsidersUnverifiedCustomersAffiliatedCustomersAndMissingCsrf()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser(); using var xc = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc); var outsider = new Browser(xc);
        var (org, service, property, start) = await BookingSetup(factory, owner, reviewer, customer); var id = await CompleteReviewBooking(factory, owner, customer, service, property, start); var path = $"bookings/{id}/review";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/" + path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/" + path, ReviewBody())).StatusCode);
        await outsider.Login("review-outsider@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await xc.GetAsync("/api/v1/" + path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Send(path, body: ReviewBody())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Send(path, body: ReviewBody(), csrf: false)).StatusCode);
        await factory.Update(async db => (await db.Users.SingleAsync(u => u.Email == "booking-customer@example.test")).EmailConfirmed = false);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(path, body: ReviewBody())).StatusCode);
        Assert.False((await cc.GetFromJsonAsync<JsonElement>("/api/v1/" + path)).GetProperty("canReview").GetBoolean());
        await factory.Update(async db => { var account = await db.Users.SingleAsync(u => u.Email == "booking-customer@example.test"); account.EmailConfirmed = true; db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = org, UserId = account.Id, Role = "Staff" }); });
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Send(path, body: ReviewBody())).StatusCode);
        Assert.False((await cc.GetFromJsonAsync<JsonElement>("/api/v1/" + path)).GetProperty("canReview").GetBoolean());
        await factory.Update(async db => { Assert.Equal(0, await db.BookingReviews.CountAsync()); (await db.OrganizationMemberships.SingleAsync(m => m.OrganizationId == org && m.Role == "Staff")).Active = false; });
        Assert.Equal(HttpStatusCode.Created, (await customer.Send(path, body: ReviewBody())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await xc.GetAsync("/api/v1/" + path)).StatusCode);
    }
    [Fact]
    public async Task EveryNonCompletedBookingStatusRejectsReviews()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser();
        var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc);
        var (_, service, property, start) = await BookingSetup(factory, owner, reviewer, customer);
        var created = await RequestBooking(customer, service, property, start); var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        foreach (var status in new[] { "PendingProviderConfirmation", "Confirmed", "InProgress", "Declined", "Expired", "CancelledByCustomer", "CancelledByProvider" })
        {
            await factory.Update(async db => (await db.Bookings.SingleAsync()).Status = status);
            Assert.False((await cc.GetFromJsonAsync<JsonElement>($"/api/v1/bookings/{id}/review")).GetProperty("canReview").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict, (await customer.Send($"bookings/{id}/review", body: ReviewBody())).StatusCode);
        }
    }
    [Fact]
    public async Task ReviewValidationRejectsInvalidRatingsFeedbackConsentAndOverposting()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser();
        var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc);
        var (_, service, property, start) = await BookingSetup(factory, owner, reviewer, customer); var id = await CompleteReviewBooking(factory, owner, customer, service, property, start); var path = $"bookings/{id}/review";
        foreach (var input in new[] { new ReviewInput(0,"Valid fictional feedback.",true), new(6,"Valid fictional feedback.",true), new(3,null,true), new(3,"short",true), new(3,"          ",true), new(3,new string('a',2001),true), new(3,"Bad\0fictional feedback.",true), new(3,"Valid fictional feedback.",false) })
            Assert.Equal(HttpStatusCode.BadRequest, (await customer.Send(path, body: JsonContent.Create(input))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Send(path, body: JsonContent.Create(new { rating = 3.5, comment = "Valid feedback.", publishAcknowledged = true }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.Send(path, body: JsonContent.Create(new { rating = 5, comment = "Valid feedback.", publishAcknowledged = true, customerId = "forged" }))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await customer.Send(path, body: ReviewBody(1,"😀😀😀😀😀"))).StatusCode);
    }
    [Fact]
    public async Task PublicReviewsArePagedAggregatedPrivacySafeAndFailClosedWithProviderVisibility()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc);
        var (org, service, property, start) = await BookingSetup(factory, owner, reviewer, customer);
        Guid provider = Guid.Empty; await factory.Update(async db => provider = (await db.ProviderProfiles.SingleAsync()).Id);
        var path = $"/api/v1/providers/{provider}/reviews";
        var empty = await anonymous.GetFromJsonAsync<JsonElement>(path); Assert.Equal(0,empty.GetProperty("totalCount").GetInt32()); Assert.Equal(JsonValueKind.Null,empty.GetProperty("averageRating").ValueKind);
        for (var index = 0; index < 2; index++) { var id = await CompleteReviewBooking(factory,owner,customer,service,property,start.AddDays(index)); Assert.Equal(HttpStatusCode.Created,(await customer.Send($"bookings/{id}/review",body:ReviewBody(index==0?5:3))).StatusCode); }
        var response = await anonymous.GetAsync(path+"?pageSize=1"); Assert.Equal("no-store",response.Headers.CacheControl!.ToString()); var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2,json.GetProperty("totalCount").GetInt32()); Assert.Equal(4,json.GetProperty("averageRating").GetDouble()); Assert.Equal(1,json.GetProperty("items").GetArrayLength());
        Assert.Equal(1,(await anonymous.GetFromJsonAsync<JsonElement>(path+"?page=2&pageSize=1")).GetProperty("items").GetArrayLength());
        foreach(var privateField in new[]{"bookingId","customerId","organizationId","propertyId","email","Fictional Street"}) Assert.DoesNotContain(privateField,json.ToString());
        foreach(var query in new[]{"page=0","pageSize=51","page=100001"}) Assert.Equal(HttpStatusCode.BadRequest,(await anonymous.GetAsync(path+"?"+query)).StatusCode);
        await factory.Update(async db => (await db.BookingReviews.OrderBy(r=>r.Rating).FirstAsync()).Status="Hidden");
        var visible = await anonymous.GetFromJsonAsync<JsonElement>(path); Assert.Equal(1,visible.GetProperty("totalCount").GetInt32()); Assert.Equal(5,visible.GetProperty("averageRating").GetDouble());
        Assert.Equal(HttpStatusCode.OK,(await Publish(owner,org,false)).StatusCode); Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Publish(owner,org)).StatusCode);
        await factory.Update(async db => (await db.VerificationChecks.SingleAsync()).RevokedAt=DateTime.UtcNow);
        Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync(path)).StatusCode);
        await factory.Update(async db => Assert.Equal(2,await db.BookingReviews.CountAsync()));
    }
    [Fact]
    public async Task DatabaseEnforcesOneReviewPerBookingAndValidRatingEvenWithoutHttpPrechecks()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser();
        var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc);
        var (_, service, property, start) = await BookingSetup(factory, owner, reviewer, customer); var id = await CompleteReviewBooking(factory,owner,customer,service,property,start);
        await Assert.ThrowsAsync<DbUpdateException>(()=>factory.Update(db=>{db.BookingReviews.Add(new BookingReview{BookingId=id,Rating=6,Comment="Invalid fictional rating."});return Task.CompletedTask;}));
        Assert.Equal(HttpStatusCode.Created,(await customer.Send($"bookings/{id}/review",body:ReviewBody())).StatusCode);
        await Assert.ThrowsAsync<DbUpdateException>(()=>factory.Update(db=>{db.BookingReviews.Add(new BookingReview{BookingId=id,Rating=2,Comment="Duplicate fictional review."});return Task.CompletedTask;}));
        await factory.Update(async db => Assert.Equal(1,await db.BookingReviews.CountAsync()));
    }
}
