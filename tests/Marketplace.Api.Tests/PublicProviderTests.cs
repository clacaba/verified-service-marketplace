using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed partial class VerificationTests
{
    private static async Task<(Guid Org, Guid Provider, Guid App)> ApprovedPublicSetup(Factory factory, Browser owner, Browser reviewer)
    {
        var (org, app, _, etag) = await Submitted(factory, owner);
        await GrantReviewer(factory, reviewer);
        var claimed = await reviewer.Send($"admin/verifications/{app}/start-review", etag: etag);
        var approved = await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Approved", "", true)), etag: claimed.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var profile = await owner.Client.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{org}/provider-profile");
        return (org, profile.GetProperty("id").GetGuid(), app);
    }
    private static async Task<HttpResponseMessage> Publish(Browser owner, Guid org, bool published = true)
    {
        var profile = await owner.Client.GetAsync($"/api/v1/organizations/{org}/provider-profile");
        return await owner.Send($"organizations/{org}/provider-profile/publication", body: JsonContent.Create(new { published }), etag: profile.Headers.ETag!.ToString());
    }
    [Fact]
    public async Task PublicProfilesRequireExplicitPublicationAndNeverLeakEvidenceOrReviewData()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (org, provider, _) = await ApprovedPublicSetup(factory, owner, reviewer);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/providers/{provider}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Publish(owner, org)).StatusCode);
        var response = await anonymous.GetAsync($"/api/v1/providers/{provider}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "badges", "biography", "categories", "disclosure", "displayName", "id", "providerType", "serviceArea" }, json.EnumerateObject().Select(p => p.Name).Order().ToArray());
        var badge = json.GetProperty("badges")[0]; Assert.Equal("Identity", badge.GetProperty("type").GetString());
        Assert.True(badge.GetProperty("simulated").GetBoolean()); Assert.Contains("simulated", badge.GetProperty("label").GetString());
        Assert.Contains("fictional", json.GetProperty("disclosure").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Publish(owner, org, false)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/providers/{provider}")).StatusCode);
        await factory.Update(async db => Assert.Equal(2, await db.OrganizationAuditEvents.CountAsync(x => x.Action == "provider-profile.published" || x.Action == "provider-profile.unpublished")));
    }
    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("category")]
    [InlineData("owner")]
    [InlineData("application")]
    public async Task PublicVisibilityReevaluatesCurrentEligibility(string change)
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (org, provider, _) = await ApprovedPublicSetup(factory, owner, reviewer); Assert.Equal(HttpStatusCode.OK, (await Publish(owner, org)).StatusCode);
        await factory.Update(async db =>
        {
            switch (change)
            {
                case "expired": (await db.VerificationChecks.SingleAsync()).ExpiresAt = DateTime.UtcNow.AddSeconds(-1); break;
                case "revoked": (await db.VerificationChecks.SingleAsync()).RevokedAt = DateTime.UtcNow; break;
                case "category": (await db.ServiceCategories.SingleAsync(c => c.Name == "Cleaning")).Active = false; break;
                case "owner": (await db.Users.SingleAsync(u => u.Email == "owner@example.test")).EmailConfirmed = false; break;
                case "application": (await db.VerificationApplications.SingleAsync()).IsActive = false; break;
            }
        });
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/providers/{provider}")).StatusCode);
        var list = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/providers");
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32()); Assert.Equal(0, list.GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await Publish(owner, org)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Publish(owner, org, false)).StatusCode);
    }
    [Fact]
    public async Task PublicationEnforcesCsrfOwnerAndExactProfileVersionAndRejectsDrafts()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var outsiderClient = factory.Browser();
        var owner = new Browser(ownerClient); var outsider = new Browser(outsiderClient);
        var (org, _, _) = await Setup(factory, owner);
        var profile = await ownerClient.GetAsync($"/api/v1/organizations/{org}/provider-profile"); var etag = profile.Headers.ETag!.ToString();
        var route = $"organizations/{org}/provider-profile/publication"; var body = new { published = true };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send(route, body: JsonContent.Create(body), etag: etag, csrf: false)).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await owner.Send(route, body: JsonContent.Create(body))).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await owner.Send(route, body: JsonContent.Create(body), etag: "*")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Send(route, body: JsonContent.Create(body), etag: etag)).StatusCode);
        await outsider.Login("outsider@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Send(route, body: JsonContent.Create(body), etag: etag)).StatusCode);
        await factory.Update(async db => db.OrganizationMemberships.Add(new Marketplace.Api.Organizations.OrganizationMembership { OrganizationId = org, UserId = (await db.Users.SingleAsync(u => u.Email == "outsider@example.test")).Id, Role = "Staff", Active = true }));
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.Send(route, body: JsonContent.Create(body), etag: etag)).StatusCode);
    }
    [Fact]
    public async Task MaterialEditsClearPublicationAndRevokePublicBadges()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (org, provider, _) = await ApprovedPublicSetup(factory, owner, reviewer); var published = await Publish(owner, org);
        var saved = await owner.Send($"organizations/{org}/provider-profile", HttpMethod.Put, JsonContent.Create(new
        {
            displayName = "Changed provider",
            providerType = "Individual",
            biography = "Fictional services.",
            serviceArea = "Demo area",
            categoryIds = new[] { "10000000-0000-0000-0000-000000000001" }
        }), published.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode); Assert.Equal(JsonValueKind.Null, (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("publishedAt").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/providers/{provider}")).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await owner.Send($"organizations/{org}/provider-profile/publication", body: JsonContent.Create(new { published = false }), etag: published.Headers.ETag!.ToString())).StatusCode);
    }
    [Fact]
    public async Task BusinessTradeProfilesRequireEverySpecificCurrentBadge()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (org, app, etag) = await Setup(factory, owner, business: true, trade: true);
        var types = new[] { "Identity", "BusinessRegistration", "TradeQualification" };
        foreach (var type in types)
        {
            var uploaded = await owner.Send($"organizations/{org}/verifications/{app}/documents", body: File(type), etag: etag);
            Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode); etag = uploaded.Headers.ETag!.ToString();
        }
        var submitted = await owner.Send($"organizations/{org}/verifications/{app}/submit", etag: etag);
        await GrantReviewer(factory, reviewer);
        var claim = await reviewer.Send($"admin/verifications/{app}/start-review", etag: submitted.Headers.ETag!.ToString());
        var decision = await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(new { decision = "Approved", reason = "", checks = types.Select(type => new { type, expiresAt = (DateTime?)null }).ToArray() }), etag: claim.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, decision.StatusCode); Assert.Equal(HttpStatusCode.OK, (await Publish(owner, org)).StatusCode);
        var profile = await ownerClient.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{org}/provider-profile");
        var provider = profile.GetProperty("id").GetGuid();
        var publicProfile = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/providers/{provider}");
        Assert.Equal(types.Order(), publicProfile.GetProperty("badges").EnumerateArray().Select(b => b.GetProperty("type").GetString()).Order());
        foreach (var type in types)
        {
            await factory.Update(async db => (await db.VerificationChecks.SingleAsync(c => c.Type == type)).RevokedAt = DateTime.UtcNow);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/providers/{provider}")).StatusCode);
            await factory.Update(async db => (await db.VerificationChecks.SingleAsync(c => c.Type == type)).RevokedAt = null);
        }
    }
    [Fact]
    public async Task DirectoryCountsOnlyEligibleProfilesAndValidatesPaginationAndCategoryFilter()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (org, provider, _) = await ApprovedPublicSetup(factory, owner, reviewer); await Publish(owner, org);
        foreach (var query in new[] { "page=0", "page=100001", "pageSize=101", "categoryId=invalid" })
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/v1/providers?" + query)).StatusCode);
        var list = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/providers?pageSize=1&categoryId=10000000-0000-0000-0000-000000000001");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32()); Assert.Equal(provider, list.GetProperty("items")[0].GetProperty("id").GetGuid());
        var empty = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/providers?categoryId=10000000-0000-0000-0000-000000000002");
        Assert.Equal(0, empty.GetProperty("totalCount").GetInt32());
    }
}
