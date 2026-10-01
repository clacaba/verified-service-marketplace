using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Api.Data;
using Marketplace.Api.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed class OrganizationTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            connection.Open();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<MarketplaceDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<MarketplaceDbContext>>();
                services.AddDbContext<MarketplaceDbContext>(options => options.UseSqlite(connection));
            });
        }
        public HttpClient Browser()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), HandleCookies = true });
            using var scope = Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>().Database.EnsureCreated();
            return client;
        }
        public async Task<string> InviteToken(string email)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var queued = await db.VerificationEmails.SingleAsync(x => x.Recipient == email && x.Subject.Contains("invited"));
            var body = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(VerificationEmailWorker.Purpose).Unprotect(queued.ProtectedBody);
            return body.Split("?token=")[1];
        }
        public async Task Verify(string email)
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
            var user = await db.Users.SingleAsync(x => x.Email == email);
            user.EmailConfirmed = true;
            await db.SaveChangesAsync();
        }
    }
    private static async Task<HttpResponseMessage> Mutate(HttpClient client, string route, object body, HttpMethod? method = null)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(method ?? HttpMethod.Post, "/api/v1/" + route) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-Token", csrf.GetProperty("token").GetString());
        return await client.SendAsync(request);
    }
    private static async Task SignIn(HttpClient client, string email, string type = "Provider")
    {
        Assert.Equal(HttpStatusCode.Accepted, (await Mutate(client, "auth/register", new { name = "Test User", email, password = "StrongPassword!123", accountType = type })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Mutate(client, "auth/login", new { email, password = "StrongPassword!123" })).StatusCode);
    }
    private static async Task<Guid> Create(HttpClient owner)
    {
        var response = await Mutate(owner, "organizations", new { name = "CleanPro", providerType = "Business" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = await response.Content.ReadFromJsonAsync<JsonElement>();
        return data.GetProperty("id").GetGuid();
    }
    [Fact]
    public async Task CreationAtomicallyGrantsOwnershipAndUpdatesSessionSummary()
    {
        await using var factory = new Factory(); using var owner = factory.Browser();
        await SignIn(owner, "owner@example.test"); var id = await Create(owner);
        var me = await owner.GetFromJsonAsync<JsonElement>("/api/v1/users/me");
        Assert.Equal(id, me.GetProperty("memberships")[0].GetProperty("organizationId").GetGuid());
        Assert.Equal("Owner", me.GetProperty("memberships")[0].GetProperty("role").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await Mutate(owner, $"organizations/{id}/members/{me.GetProperty("id").GetString()}", new { }, HttpMethod.Delete)).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        Assert.Equal(1, await db.Organizations.CountAsync());
        Assert.Equal(1, await db.OrganizationMemberships.CountAsync());
        Assert.Equal(1, await db.OrganizationAuditEvents.CountAsync());
    }
    [Fact]
    public async Task UnrelatedUsersCannotReadOrWriteOrganizationData()
    {
        await using var factory = new Factory(); using var owner = factory.Browser(); using var outsider = factory.Browser();
        await SignIn(owner, "owner@example.test"); await SignIn(outsider, "other@example.test"); var id = await Create(owner);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/organizations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/organizations/{id}/members")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Mutate(outsider, $"organizations/{id}/invitations", new { email = "staff@example.test", role = "Staff" })).StatusCode);
    }
    [Fact]
    public async Task VerifiedRecipientCanJoinOnceAndLosesAccessOnDeactivation()
    {
        await using var factory = new Factory(); using var owner = factory.Browser(); using var staff = factory.Browser();
        await SignIn(owner, "owner@example.test"); await SignIn(staff, "staff@example.test", "Customer"); var id = await Create(owner);
        var invitation = await Mutate(owner, $"organizations/{id}/invitations", new { email = "staff@example.test", role = "Staff" });
        Assert.Equal(HttpStatusCode.Created, invitation.StatusCode);
        Assert.DoesNotContain("token", await invitation.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var token = await factory.InviteToken("staff@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(staff, "invitations/accept", new { token })).StatusCode);
        await factory.Verify("staff@example.test");
        Assert.Equal(HttpStatusCode.NoContent, (await Mutate(staff, "invitations/accept", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Mutate(staff, "invitations/accept", new { token })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"/api/v1/organizations/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(staff, $"organizations/{id}/invitations", new { email = "other@example.test", role = "Staff" })).StatusCode);
        var me = await staff.GetFromJsonAsync<JsonElement>("/api/v1/users/me");
        var staffId = me.GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await Mutate(owner, $"organizations/{id}/members/{staffId}", new { }, HttpMethod.Delete)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/v1/organizations/{id}")).StatusCode);
        var after = await staff.GetFromJsonAsync<JsonElement>("/api/v1/users/me");
        Assert.Equal(0, after.GetProperty("memberships").GetArrayLength());
    }
    [Fact]
    public async Task InvitationsRejectWrongRecipientsAndExpiration()
    {
        await using var factory = new Factory(); using var owner = factory.Browser(); using var other = factory.Browser();
        await SignIn(owner, "owner@example.test"); await SignIn(other, "other@example.test"); await factory.Verify("other@example.test"); var id = await Create(owner);
        await Mutate(owner, $"organizations/{id}/invitations", new { email = "staff@example.test", role = "Staff" });
        var token = await factory.InviteToken("staff@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, (await Mutate(other, "invitations/accept", new { token })).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>(); var invite = await db.OrganizationInvitations.SingleAsync();
            Assert.NotEqual(token, invite.TokenHash); invite.ExpiresAt = DateTime.UtcNow.AddDays(-1); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await Mutate(other, "invitations/accept", new { token })).StatusCode);
    }
    [Fact]
    public async Task CustomerCannotCreateOrganizationAndMutationRequiresCsrf()
    {
        await using var factory = new Factory(); using var customer = factory.Browser();
        await SignIn(customer, "customer@example.test", "Customer");
        Assert.Equal(HttpStatusCode.Forbidden, (await Mutate(customer, "organizations", new { name = "Invalid", providerType = "Business" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync("/api/v1/organizations", new { name = "Invalid", providerType = "Business" })).StatusCode);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<HttpClient, string> ProfileCsrfTokens = new();
    private static async Task<HttpResponseMessage> SaveProfile(HttpClient client, Guid id, object input, string? etag, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/organizations/{id}/provider-profile") { Content = JsonContent.Create(input) };
        if (etag != null) request.Headers.TryAddWithoutValidation("If-Match", etag);
        if (csrf)
        {
            if (!ProfileCsrfTokens.TryGetValue(client, out var value))
            {
                var token = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
                value = token.GetProperty("token").GetString()!;
                ProfileCsrfTokens.Add(client, value);
            }
            request.Headers.Add("X-CSRF-Token", value);
        }
        return await client.SendAsync(request);
    }
    private static object Draft(string name = "CleanPro", Guid[]? categories = null) =>
        new { displayName = name, providerType = "Business", biography = "Friendly home cleaning.", categoryIds = categories ?? [], serviceArea = "Local service area", imageId = (Guid?)null };
    [Fact]
    public async Task ProviderDraftIsCreatedWithOrganizationAndCanBeSavedAndRevisited()
    {
        await using var factory = new Factory(); using var owner = factory.Browser();
        await SignIn(owner, "owner@example.test"); var id = await Create(owner);
        var get = await owner.GetAsync($"/api/v1/organizations/{id}/provider-profile");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal("no-store", get.Headers.CacheControl?.ToString());
        var original = await get.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", original.GetProperty("status").GetString());
        Assert.False(original.GetProperty("readyForVerification").GetBoolean());
        var category = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var saved = await SaveProfile(owner, id, Draft("Changed name", [category]), get.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.NotEqual(get.Headers.ETag, saved.Headers.ETag);
        var again = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{id}/provider-profile");
        Assert.Equal("Changed name", again.GetProperty("displayName").GetString());
        Assert.True(again.GetProperty("readyForVerification").GetBoolean());
        Assert.Equal(category, again.GetProperty("categoryIds")[0].GetGuid());
        var cleared = await SaveProfile(owner, id, new { displayName = "Draft", providerType = "Individual", biography = "", categoryIds = Array.Empty<Guid>(), serviceArea = "" }, saved.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var incomplete = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(incomplete.GetProperty("readyForVerification").GetBoolean());
        Assert.Equal(0, incomplete.GetProperty("categoryIds").GetArrayLength());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        Assert.Equal(2, await db.OrganizationAuditEvents.CountAsync(x => x.Action == "provider-profile.updated"));
        Assert.Equal("Individual", (await db.Organizations.SingleAsync()).ProviderType);
    }
    [Fact]
    public async Task ProviderDraftRejectsMissingStaleAndWildcardVersionsWithoutOverwriting()
    {
        await using var factory = new Factory(); using var owner = factory.Browser();
        await SignIn(owner, "owner@example.test"); var id = await Create(owner);
        var get = await owner.GetAsync($"/api/v1/organizations/{id}/provider-profile");
        var etag = get.Headers.ETag!.ToString();
        Assert.Equal((HttpStatusCode)428, (await SaveProfile(owner, id, Draft(), null)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await SaveProfile(owner, id, Draft(), "*")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SaveProfile(owner, id, Draft("First edit"), etag)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await SaveProfile(owner, id, Draft("Stale edit"), etag)).StatusCode);
        var current = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/organizations/{id}/provider-profile");
        Assert.Equal("First edit", current.GetProperty("displayName").GetString());
    }
    [Fact]
    public async Task ProviderDraftEnforcesStaffReadOnlyIsolationAndImmediateRevocation()
    {
        await using var factory = new Factory(); using var owner = factory.Browser(); using var staff = factory.Browser(); using var outsider = factory.Browser();
        await SignIn(owner, "owner@example.test"); await SignIn(staff, "staff@example.test", "Customer"); await SignIn(outsider, "other@example.test"); var id = await Create(owner);
        await Mutate(owner, $"organizations/{id}/invitations", new { email = "staff@example.test", role = "Staff" });
        await factory.Verify("staff@example.test");
        Assert.Equal(HttpStatusCode.NoContent, (await Mutate(staff, "invitations/accept", new { token = await factory.InviteToken("staff@example.test") })).StatusCode);
        var read = await staff.GetAsync($"/api/v1/organizations/{id}/provider-profile");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SaveProfile(staff, id, Draft(), read.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/v1/organizations/{id}/provider-profile")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SaveProfile(outsider, id, Draft(), read.Headers.ETag!.ToString())).StatusCode);
        var me = await staff.GetFromJsonAsync<JsonElement>("/api/v1/users/me");
        await Mutate(owner, $"organizations/{id}/members/{me.GetProperty("id").GetString()}", new { }, HttpMethod.Delete);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.GetAsync($"/api/v1/organizations/{id}/provider-profile")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SaveProfile(staff, id, Draft(), read.Headers.ETag!.ToString())).StatusCode);
    }
    [Fact]
    public async Task ProviderDraftValidatesFieldsCategoriesImagesAndCsrf()
    {
        await using var factory = new Factory(); using var owner = factory.Browser();
        await SignIn(owner, "owner@example.test"); var id = await Create(owner);
        var get = await owner.GetAsync($"/api/v1/organizations/{id}/provider-profile");
        var etag = get.Headers.ETag!.ToString();
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveProfile(owner, id, Draft(), etag, false)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveProfile(owner, id, Draft(""), etag)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveProfile(owner, id, Draft(categories: [Guid.NewGuid()]), etag)).StatusCode);
        var category = Guid.Parse("10000000-0000-0000-0000-000000000001");
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveProfile(owner, id, Draft(categories: [category, category]), etag)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveProfile(owner, id, new { displayName = "Safe", providerType = "Admin", biography = new string('x', 4001), serviceArea = new string('x', 251), imageId = Guid.NewGuid() }, etag)).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>(); (await db.ServiceCategories.SingleAsync(x => x.Id == category)).Active = false; await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await SaveProfile(owner, id, Draft(categories: [category]), etag)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SaveProfile(owner, id, Draft(), etag)).StatusCode);
    }
    [Fact]
    public async Task CategoriesArePublicActiveAndPaginatedAndProfilesRequireAuthentication()
    {
        await using var factory = new Factory(); using var anonymous = factory.Browser();
        var page = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/categories?page=1&pageSize=2");
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.Equal(4, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/v1/categories?pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/v1/organizations/{Guid.NewGuid()}/provider-profile")).StatusCode);
    }
}
