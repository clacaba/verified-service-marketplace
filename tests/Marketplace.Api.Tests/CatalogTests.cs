using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed partial class VerificationTests
{
    private static readonly Guid Cleaning = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static object Offer(string title = "Demo cleaning", long priceMinor = 25000, Guid? categoryId = null, string currency = "PHP", int durationMinutes = 60, string description = "Fictional cleaning service.", string area = "Demo area") =>
        new { title, description, categoryId = categoryId ?? Cleaning, priceMinor, currency, durationMinutes, serviceArea = area };
    private static async Task<(Guid Id, string Etag)> CreateOffer(Browser owner, Guid org, object? input = null)
    {
        var response = await owner.Send($"organizations/{org}/services", body: JsonContent.Create(input ?? Offer()));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return ((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(), response.Headers.ETag!.ToString());
    }
    private static async Task<HttpResponseMessage> OfferPublication(Browser owner, Guid org, Guid service, string etag, bool published = true) =>
        await owner.Send($"organizations/{org}/services/{service}/publication", body: JsonContent.Create(new { published }), etag: etag);
    [Fact]
    public async Task CatalogDraftsPublishEditUnpublishAndArchiveWithExactVersions()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (org, _, _) = await ApprovedPublicSetup(factory, owner, reviewer);
        var (id, etag) = await CreateOffer(owner, org);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/services/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await OfferPublication(owner, org, id, etag)).StatusCode);
        await Publish(owner, org);
        var published = await OfferPublication(owner, org, id, etag); Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        var publicResponse = await anonymous.GetAsync($"/api/v1/services/{id}"); Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        Assert.Equal("no-store", publicResponse.Headers.CacheControl!.ToString());
        var projection = await publicResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(new[] { "category", "description", "disclosure", "durationMinutes", "id", "priceMinor", "provider", "serviceArea", "title", "currency", "images" }.Order(), projection.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(25000, projection.GetProperty("priceMinor").GetInt64());
        Assert.DoesNotContain("organizationId", projection.ToString()); Assert.DoesNotContain("version", projection.ToString());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await OfferPublication(owner, org, id, etag, false)).StatusCode);
        var route = $"organizations/{org}/services/{id}";
        var saved = await owner.Send(route, HttpMethod.Put, JsonContent.Create(Offer("Changed offer")), published.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode); Assert.Equal("Draft", (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/services/{id}")).StatusCode);
        var republished = await OfferPublication(owner, org, id, saved.Headers.ETag!.ToString());
        var unpublished = await OfferPublication(owner, org, id, republished.Headers.ETag!.ToString(), false);
        Assert.Equal(HttpStatusCode.OK, unpublished.StatusCode);
        var archive = await owner.Send(route + "/archive", etag: unpublished.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.OK, archive.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Send(route, HttpMethod.Put, JsonContent.Create(Offer()), archive.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await OfferPublication(owner, org, id, archive.Headers.ETag!.ToString())).StatusCode);
        await factory.Update(async db => Assert.Equal(6, await db.OrganizationAuditEvents.CountAsync(x => x.Action.StartsWith("service."))));
    }
    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("unpublished")]
    [InlineData("category")]
    public async Task PublishedOffersDisappearWhenProviderEligibilityChanges(string change)
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (org, _, _) = await ApprovedPublicSetup(factory, owner, reviewer); await Publish(owner, org);
        var (id, etag) = await CreateOffer(owner, org); var published = await OfferPublication(owner, org, id, etag);
        await factory.Update(async db =>
        {
            if (change == "expired") (await db.VerificationChecks.SingleAsync()).ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
            if (change == "revoked") (await db.VerificationChecks.SingleAsync()).RevokedAt = DateTime.UtcNow;
            if (change == "unpublished") (await db.ProviderProfiles.SingleAsync()).PublishedAt = null;
            if (change == "category") (await db.ServiceCategories.SingleAsync(x => x.Id == Cleaning)).Active = false;
        });
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/v1/services/{id}")).StatusCode);
        var list = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/services");
        Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await OfferPublication(owner, org, id, published.Headers.ETag!.ToString(), false)).StatusCode);
    }
    [Fact]
    public async Task CatalogMembershipCsrfAndVersionChecksAreMandatory()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var staffClient = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(ownerClient); var staff = new Browser(staffClient); var (org, _, _) = await Setup(factory, owner);
        var (id, etag) = await CreateOffer(owner, org); var route = $"organizations/{org}/services/{id}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/" + route)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send(route, HttpMethod.Put, JsonContent.Create(Offer()), etag, false)).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await owner.Send(route, HttpMethod.Put, JsonContent.Create(Offer()))).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await owner.Send(route, HttpMethod.Put, JsonContent.Create(Offer()), "*")).StatusCode);
        await staff.Login("staff@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await staffClient.GetAsync("/api/v1/" + route)).StatusCode);
        await factory.Update(async db => db.OrganizationMemberships.Add(new Marketplace.Api.Organizations.OrganizationMembership { OrganizationId = org, UserId = (await db.Users.SingleAsync(u => u.Email == "staff@example.test")).Id, Role = "Staff", Active = true }));
        Assert.Equal(HttpStatusCode.OK, (await staffClient.GetAsync("/api/v1/" + route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Send($"organizations/{org}/services", body: JsonContent.Create(Offer()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Send(route, HttpMethod.Put, JsonContent.Create(Offer()), etag)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await OfferPublication(staff, org, id, etag)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.Send(route + "/archive", etag: etag)).StatusCode);
        await factory.Update(async db => (await db.OrganizationMemberships.SingleAsync(m => m.OrganizationId == org && m.Role == "Staff")).Active = false);
        Assert.Equal(HttpStatusCode.NotFound, (await staffClient.GetAsync("/api/v1/" + route)).StatusCode);
        var another = await owner.Send("organizations", body: JsonContent.Create(new { name = "Another", providerType = "Individual" }));
        var otherOrg = (await another.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await ownerClient.GetAsync($"/api/v1/organizations/{otherOrg}/services/{id}")).StatusCode);
    }
    [Fact]
    public async Task CatalogRejectsInvalidFieldsAndCategoryOutsideProviderProfile()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var owner = new Browser(client); var (org, _, _) = await Setup(factory, owner);
        var invalid = new object[] {Offer(title: " "), Offer(title: new string('x', 151)), Offer(description: ""), Offer(description: new string('x', 4001)),
            Offer(area: " "), Offer(priceMinor: 0), Offer(priceMinor: 100000001), Offer(currency: "USD"), Offer(durationMinutes: 14), Offer(durationMinutes: 16), Offer(durationMinutes: 1441),
            Offer(categoryId: Guid.Parse("10000000-0000-0000-0000-000000000002"))};
        foreach (var input in invalid) Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send($"organizations/{org}/services", body: JsonContent.Create(input))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send($"organizations/{org}/services", body: JsonContent.Create(new { title = "Injected", status = "Published" }))).StatusCode);
        await factory.Update(async db => Assert.Equal(0, await db.ServiceListings.CountAsync()));
    }
    [Fact]
    public async Task CatalogSearchFiltersSortsAndPagesOnlyEligibleOffers()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser(); using var anonymous = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient); var (org, provider, _) = await ApprovedPublicSetup(factory, owner, reviewer); await Publish(owner, org);
        var (a, atag) = await CreateOffer(owner, org, Offer("Kitchen cleaning", 30000)); await OfferPublication(owner, org, a, atag);
        var (b, btag) = await CreateOffer(owner, org, Offer("Bathroom cleaning", 15000, area: "Other district")); await OfferPublication(owner, org, b, btag);
        await CreateOffer(owner, org, Offer("Unpublished draft", 100));
        var list = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/services?sort=priceAsc&pageSize=1");
        Assert.Equal(2, list.GetProperty("totalCount").GetInt32()); Assert.Equal(b, list.GetProperty("items")[0].GetProperty("id").GetGuid());
        list = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/services?sort=priceDesc&pageSize=1"); Assert.Equal(a, list.GetProperty("items")[0].GetProperty("id").GetGuid());
        list = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/services?q=KITCHEN&area=demo&minPriceMinor=20000&maxPriceMinor=40000&providerId={provider}&categoryId={Cleaning}");
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32()); Assert.Equal(a, list.GetProperty("items")[0].GetProperty("id").GetGuid());
        list = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/services?q=%25"); Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
        list = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/services?page=2&pageSize=1&sort=priceAsc"); Assert.Equal(a, list.GetProperty("items")[0].GetProperty("id").GetGuid());
        foreach (var filter in new[] { "page=0", "pageSize=101", "page=100001", "minPriceMinor=-1", "minPriceMinor=400&maxPriceMinor=100", "sort=unknown", "categoryId=bad" })
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/v1/services?" + filter)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await ownerClient.GetAsync($"/api/v1/organizations/{org}/services?status=Unknown")).StatusCode);
    }
}
