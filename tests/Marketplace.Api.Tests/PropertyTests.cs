using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed partial class VerificationTests
{
    private static object Address(string label = "Demo home", string countryCode = "PH") => new { label, addressLine1 = "123 Fictional Street", addressLine2 = "Unit 1", city = "Demo City", region = "Demo Region", postalCode = "1234", countryCode };
    [Fact]
    public async Task PropertiesArePrivateAndOnlyTheirCustomerCanReadOrChangeThem()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var owner = new Browser(client); await owner.Login("property-owner@example.test");
        var created = await owner.Send("properties", body: JsonContent.Create(Address())); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var property = await created.Content.ReadFromJsonAsync<JsonElement>(); var id = property.GetProperty("id").GetGuid(); var etag = created.Headers.ETag!.ToString();
        Assert.DoesNotContain("customerId", property.ToString()); Assert.Equal("no-store", created.Headers.CacheControl!.ToString());
        using var otherClient = factory.Browser(); var other = new Browser(otherClient); await other.Login("outsider@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync($"/api/v1/properties/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Send($"properties/{id}", HttpMethod.Put, JsonContent.Create(Address("Stolen")), etag)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Send($"properties/{id}/archive", etag: etag)).StatusCode);
        var list = await otherClient.GetFromJsonAsync<JsonElement>("/api/v1/properties"); Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
        using var anonymous = factory.Browser(); Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/properties")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/v1/properties", Address())).StatusCode);
    }
    [Fact]
    public async Task PropertyUpdatesRequireCsrfAndExactVersionAndArchiveRetainsRecord()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var browser = new Browser(client); await browser.Login("property@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send("properties", body: JsonContent.Create(Address()), csrf: false)).StatusCode);
        var created = await browser.Send("properties", body: JsonContent.Create(Address())); var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var tag = created.Headers.ETag!.ToString();
        Assert.Equal((HttpStatusCode)428, (await browser.Send($"properties/{id}", HttpMethod.Put, JsonContent.Create(Address()))).StatusCode);
        var updated = await browser.Send($"properties/{id}", HttpMethod.Put, JsonContent.Create(Address("Second label")), tag); Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await browser.Send($"properties/{id}/archive", etag: tag)).StatusCode);
        var archived = await browser.Send($"properties/{id}/archive", etag: updated.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.OK, archived.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.Send($"properties/{id}", HttpMethod.Put, JsonContent.Create(Address()), archived.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.Send($"properties/{id}/archive", etag: archived.Headers.ETag!.ToString())).StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/properties"); Assert.Equal(0, list.GetProperty("totalCount").GetInt32());
        var retained = await client.GetFromJsonAsync<JsonElement>($"/api/v1/properties/{id}"); Assert.Equal("Second label", retained.GetProperty("label").GetString()); Assert.NotEqual(JsonValueKind.Null, retained.GetProperty("archivedAt").ValueKind);
        await factory.Update(async db => Assert.Equal(1, await db.CustomerProperties.CountAsync()));
    }
    [Fact]
    public async Task PropertiesValidateAddressesRejectOverpostingAndBoundPagination()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var browser = new Browser(client); await browser.Login("validation@example.test");
        foreach (var input in new[] {Address(""), Address(new string('x', 81)), Address("Bad\nLabel"), Address(countryCode: "XX"), Address(countryCode: "ph")})
            Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send("properties", body: JsonContent.Create(input))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send("properties", body: JsonContent.Create(new {label = "Overposting", customerId = "another-user"}))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/properties?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/properties?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await browser.Send("properties", body: JsonContent.Create(Address("  Trimmed  ")))).StatusCode);
        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/properties?pageSize=1"); Assert.Equal("Trimmed", list.GetProperty("items")[0].GetProperty("label").GetString());
    }
}
