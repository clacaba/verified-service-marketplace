using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Api.CustomerReviews;
using Marketplace.Api.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed partial class VerificationTests
{
    private sealed class ProviderReplyRaceInterceptor(Factory factory) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (factory.ProviderReplyRace != null && eventData.Context!.ChangeTracker.Entries<ProviderReviewReply>().Any(e => e.State == EntityState.Added))
            { var callback = factory.ProviderReplyRace; factory.ProviderReplyRace = null; await callback(); }
            return result;
        }
    }
    private static JsonContent ReplyBody(string comment = "Thank you for your fictional feedback.", bool acknowledged = true) => JsonContent.Create(new { comment, publishAcknowledged = acknowledged });
    private static async Task<(Guid Org, Guid Booking, Guid Provider)> ReplySetup(Factory factory, Browser owner, Browser reviewer, Browser customer)
    {
        var (org, service, property, start) = await BookingSetup(factory, owner, reviewer, customer);
        var booking = await CompleteReviewBooking(factory, owner, customer, service, property, start);
        Assert.Equal(HttpStatusCode.Created, (await customer.Send($"bookings/{booking}/review", body: ReviewBody(2))).StatusCode);
        Guid provider = Guid.Empty; await factory.Update(async db => provider = (await db.ProviderProfiles.SingleAsync()).Id);
        return (org, booking, provider);
    }
    [Theory]
    [InlineData("Owner")]
    [InlineData("Staff")]
    public async Task OwnerAndVerifiedActiveStaffCanReplyOnceWithoutChangingCustomerReview(string role)
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser(); using var sc = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(oc); var reviewer = new Browser(rc); var customer = new Browser(cc); var staff = new Browser(sc);
        var (org, booking, provider) = await ReplySetup(factory, owner, reviewer, customer); var actor = owner;
        if (role == "Staff") { await staff.Login("reply-staff@example.test"); await factory.Update(async db => { var user = await db.Users.SingleAsync(u => u.Email == "reply-staff@example.test"); user.EmailConfirmed = true; db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = org, UserId = user.Id, Role = "Staff" }); }); actor = staff; }
        var path = $"bookings/{booking}/review";
        Assert.True((await actor.Client.GetFromJsonAsync<JsonElement>("/api/v1/" + path)).GetProperty("canReply").GetBoolean());
        var response = await actor.Send(path+"/reply", body: ReplyBody("  Thank you for the fictional feedback.\nWe appreciate it.  "));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode); Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal("Provider response", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("authorLabel").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Send(path+"/reply", body: ReplyBody())).StatusCode);
        var customerRead = await cc.GetFromJsonAsync<JsonElement>("/api/v1/"+path); Assert.False(customerRead.GetProperty("canReply").GetBoolean()); Assert.Equal(2, customerRead.GetProperty("review").GetProperty("rating").GetInt32()); Assert.Equal("Thank you for the fictional feedback.\nWe appreciate it.", customerRead.GetProperty("review").GetProperty("reply").GetProperty("comment").GetString());
        var publicRead = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/providers/{provider}/reviews"); Assert.Equal(2, publicRead.GetProperty("averageRating").GetDouble()); Assert.Equal(1, publicRead.GetProperty("totalCount").GetInt32()); Assert.Equal("Provider response", publicRead.GetProperty("items")[0].GetProperty("reply").GetProperty("authorLabel").GetString());
        foreach(var field in new[]{"actorId","bookingId","organizationId","customerId","reply-staff@example.test"}) Assert.DoesNotContain(field, publicRead.ToString());
        await factory.Update(async db => { Assert.Equal(1,await db.ProviderReviewReplies.CountAsync()); Assert.Equal(1,await db.OrganizationAuditEvents.CountAsync(a=>a.Action=="review.replied")); });
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await actor.Send(path+"/reply",HttpMethod.Put,ReplyBody())).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await actor.Send(path+"/reply",HttpMethod.Delete)).StatusCode);
    }
    [Fact]
    public async Task ReplyRequiresProviderMembershipVerifiedEmailAndCsrfAndRevocationBlocksAccess()
    {
        await using var factory = new Factory(); using var oc = factory.Browser(); using var rc = factory.Browser(); using var cc = factory.Browser(); using var sc = factory.Browser(); using var anonymous = factory.Browser();
        var owner=new Browser(oc);var reviewer=new Browser(rc);var customer=new Browser(cc);var staff=new Browser(sc);
        var (org,booking,_) = await ReplySetup(factory,owner,reviewer,customer);var path=$"bookings/{booking}/review/reply";
        Assert.Equal(HttpStatusCode.Unauthorized,(await anonymous.PostAsync("/api/v1/"+path,ReplyBody())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await customer.Send(path,body:ReplyBody())).StatusCode);
        await staff.Login("reply-outsider@example.test"); Assert.Equal(HttpStatusCode.NotFound,(await staff.Send(path,body:ReplyBody())).StatusCode);
        await factory.Update(async db=>{var user=await db.Users.SingleAsync(u=>u.Email=="reply-outsider@example.test");db.OrganizationMemberships.Add(new OrganizationMembership{OrganizationId=org,UserId=user.Id,Role="Staff"});});
        Assert.Equal(HttpStatusCode.Forbidden,(await staff.Send(path,body:ReplyBody())).StatusCode);
        Assert.False((await sc.GetFromJsonAsync<JsonElement>($"/api/v1/bookings/{booking}/review")).GetProperty("canReply").GetBoolean());
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.Send(path,body:ReplyBody(),csrf:false)).StatusCode);
        await factory.Update(async db=>{var user=await db.Users.SingleAsync(u=>u.Email=="reply-outsider@example.test");user.EmailConfirmed=true;(await db.OrganizationMemberships.SingleAsync(m=>m.UserId==user.Id)).Active=false;});
        Assert.Equal(HttpStatusCode.NotFound,(await staff.Send(path,body:ReplyBody())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await sc.GetAsync($"/api/v1/bookings/{booking}/review")).StatusCode);
        await factory.Update(async db=>Assert.Equal(0,await db.ProviderReviewReplies.CountAsync()));
    }
    [Fact]
    public async Task RepliesRejectMissingOrHiddenReviewsNonCompletedBookingsAndOwnCustomerReviews()
    {
        await using var factory=new Factory();using var oc=factory.Browser();using var rc=factory.Browser();using var cc=factory.Browser();var owner=new Browser(oc);var reviewer=new Browser(rc);var customer=new Browser(cc);
        var (org,booking,_) = await ReplySetup(factory,owner,reviewer,customer);var path=$"bookings/{booking}/review/reply";
        await factory.Update(async db=>(await db.BookingReviews.SingleAsync()).Status="Hidden");Assert.Equal(HttpStatusCode.Conflict,(await owner.Send(path,body:ReplyBody())).StatusCode);
        await factory.Update(async db=>{(await db.BookingReviews.SingleAsync()).Status="Published";(await db.Bookings.SingleAsync()).Status="CancelledByCustomer";});Assert.Equal(HttpStatusCode.Conflict,(await owner.Send(path,body:ReplyBody())).StatusCode);
        await factory.Update(async db=>{(await db.Bookings.SingleAsync()).Status="Completed";var user=await db.Users.SingleAsync(u=>u.Email=="booking-customer@example.test");db.OrganizationMemberships.Add(new OrganizationMembership{OrganizationId=org,UserId=user.Id,Role="Staff"});});
        Assert.Equal(HttpStatusCode.Forbidden,(await customer.Send(path,body:ReplyBody())).StatusCode);
        Assert.False((await cc.GetFromJsonAsync<JsonElement>($"/api/v1/bookings/{booking}/review")).GetProperty("canReply").GetBoolean());
        await factory.Update(async db=>db.BookingReviews.Remove(await db.BookingReviews.SingleAsync()));
        Assert.Equal(HttpStatusCode.NotFound,(await owner.Send(path,body:ReplyBody())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await owner.Send($"bookings/{Guid.NewGuid()}/review/reply",body:ReplyBody())).StatusCode);
    }
    [Fact]
    public async Task ReplyValidationRejectsEmptyOversizedControlCharacterUnacknowledgedAndForgedFields()
    {
        await using var factory=new Factory();using var oc=factory.Browser();using var rc=factory.Browser();using var cc=factory.Browser();var owner=new Browser(oc);var reviewer=new Browser(rc);var customer=new Browser(cc);
        var (_,booking,_) = await ReplySetup(factory,owner,reviewer,customer);var path=$"bookings/{booking}/review/reply";
        foreach(var comment in new string?[]{null,"", "   ","short",new string('a',2001),"Bad\0fictional response."}) Assert.Equal(HttpStatusCode.BadRequest,(await owner.Send(path,body:JsonContent.Create(new{comment,publishAcknowledged=true}))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.Send(path,body:ReplyBody(acknowledged:false))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.Send(path,body:JsonContent.Create(new{comment="Valid fictional response.",publishAcknowledged=true,rating=5,actorId="forged"}))).StatusCode);
        Assert.Equal(HttpStatusCode.Created,(await owner.Send(path,body:ReplyBody("😀😀😀😀😀"))).StatusCode);
    }
    [Fact]
    public async Task PublicRepliesDisappearWithHiddenReviewOrProviderUnpublicationWithoutLosingPrivateHistory()
    {
        await using var factory=new Factory();using var oc=factory.Browser();using var rc=factory.Browser();using var cc=factory.Browser();using var anonymous=factory.Browser();var owner=new Browser(oc);var reviewer=new Browser(rc);var customer=new Browser(cc);
        var (org,booking,provider) = await ReplySetup(factory,owner,reviewer,customer);Assert.Equal(HttpStatusCode.Created,(await owner.Send($"bookings/{booking}/review/reply",body:ReplyBody())).StatusCode);
        await factory.Update(async db=>(await db.BookingReviews.SingleAsync()).Status="Hidden");var hidden=await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/providers/{provider}/reviews");Assert.Equal(0,hidden.GetProperty("totalCount").GetInt32());Assert.Empty(hidden.GetProperty("items").EnumerateArray());
        Assert.Equal(JsonValueKind.Object,(await cc.GetFromJsonAsync<JsonElement>($"/api/v1/bookings/{booking}/review")).GetProperty("review").GetProperty("reply").ValueKind);
        await factory.Update(async db=>(await db.BookingReviews.SingleAsync()).Status="Published");Assert.Equal(HttpStatusCode.OK,(await Publish(owner,org,false)).StatusCode);Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync($"/api/v1/providers/{provider}/reviews")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await Publish(owner,org)).StatusCode);await factory.Update(async db=>(await db.VerificationChecks.SingleAsync()).RevokedAt=DateTime.UtcNow);Assert.Equal(HttpStatusCode.NotFound,(await anonymous.GetAsync($"/api/v1/providers/{provider}/reviews")).StatusCode);
    }
    [Fact]
    public async Task RacingRepliesReturnConflictAndRollbackTheLosingAuditEvent()
    {
        await using var factory=new Factory();using var oc=factory.Browser();using var rc=factory.Browser();using var cc=factory.Browser();var owner=new Browser(oc);var reviewer=new Browser(rc);var customer=new Browser(cc);
        var (org,booking,_) = await ReplySetup(factory,owner,reviewer,customer);
        factory.ProviderReplyRace=()=>factory.Update(async db=>{var actor=(await db.Users.SingleAsync(u=>u.Email=="owner@example.test")).Id;db.ProviderReviewReplies.Add(new ProviderReviewReply{BookingId=booking,ActorId=actor,Comment="Winning fictional response."});db.OrganizationAuditEvents.Add(new OrganizationAuditEvent{OrganizationId=org,ActorId=actor,Action="review.replied",ResourceId=booking.ToString()});});
        Assert.Equal(HttpStatusCode.Conflict,(await owner.Send($"bookings/{booking}/review/reply",body:ReplyBody())).StatusCode);
        await factory.Update(async db=>{Assert.Equal(1,await db.ProviderReviewReplies.CountAsync());Assert.Equal("Winning fictional response.",(await db.ProviderReviewReplies.SingleAsync()).Comment);Assert.Equal(1,await db.OrganizationAuditEvents.CountAsync(a=>a.Action=="review.replied"));Assert.Equal(2,(await db.BookingReviews.SingleAsync()).Rating);});
    }
}
