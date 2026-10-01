using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Marketplace.Api.Data;
using Marketplace.Api.Organizations;
using Marketplace.Api.Verification;
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

public sealed class VerificationTests
{
    private sealed class Store : IEvidenceStore
    {
        public Dictionary<string, byte[]> Objects { get; } = [];
        public bool Fail { get; set; }
        public Func<Task>? OnPut { get; set; }
        public async Task Put(string key, byte[] bytes, CancellationToken ct) { if (Fail) throw new EvidenceUnavailableException(); Objects[key] = bytes; if (OnPut != null) await OnPut(); }
        public Task<byte[]> Get(string key, CancellationToken ct) => Task.FromResult(Objects[key]);
        public Task Delete(string key, CancellationToken ct) { if (Fail) throw new EvidenceUnavailableException(); Objects.Remove(key); return Task.CompletedTask; }
    }
    private sealed class Scanner : IEvidenceScanner
    {
        public bool Clean { get; set; } = true;
        public bool Fail { get; set; }
        public Task<bool> IsClean(byte[] bytes, CancellationToken ct) => Fail ? throw new EvidenceUnavailableException() : Task.FromResult(Clean);
    }
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public Store Storage { get; } = new(); public Scanner Scan { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing"); builder.UseSetting("ConnectionStrings:Marketplace", "Host=unused");
            connection.Open();
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<MarketplaceDbContext>>(); services.RemoveAll<IDbContextOptionsConfiguration<MarketplaceDbContext>>();
                services.AddDbContext<MarketplaceDbContext>(options => options.UseSqlite(connection));
                services.RemoveAll<IEvidenceStore>(); services.AddSingleton<IEvidenceStore>(Storage);
                services.RemoveAll<IEvidenceScanner>(); services.AddSingleton<IEvidenceScanner>(Scan);
            });
        }
        public HttpClient Browser()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://localhost"), HandleCookies = true });
            using var scope = Services.CreateScope(); scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>().Database.EnsureCreated();
            return client;
        }
        public async Task Update(Func<MarketplaceDbContext, Task> operation)
        {
            using var scope = Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>(); await operation(db); await db.SaveChangesAsync();
        }
    }
    private sealed class Browser(HttpClient client)
    {
        public HttpClient Client { get; } = client; private string? token;
        public async Task<HttpResponseMessage> Send(string route, HttpMethod? method = null, HttpContent? body = null, string? etag = null, bool csrf = true)
        {
            method ??= HttpMethod.Post;
            using var request = new HttpRequestMessage(method, "/api/v1/" + route) { Content = body ?? JsonContent.Create(new { }) };
            if (csrf)
            {
                token ??= (await Client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf")).GetProperty("token").GetString();
                request.Headers.Add("X-CSRF-Token", token);
            }
            if (etag != null) request.Headers.TryAddWithoutValidation("If-Match", etag);
            return await Client.SendAsync(request);
        }
        public async Task Login(string email)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await Send("auth/register", body: JsonContent.Create(new { name = "Demo Owner", email, password = "StrongPassword!123", accountType = "Provider" }))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await Send("auth/login", body: JsonContent.Create(new { email, password = "StrongPassword!123" }))).StatusCode);
            token = null;
        }
    }
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\nFictional portfolio evidence only\n%%EOF\n");
    private static MultipartFormDataContent File(string type, byte[]? bytes = null, string mime = "application/pdf", string filename = "fictional.pdf", bool acknowledged = true)
    {
        var form = new MultipartFormDataContent(); var content = new ByteArrayContent(bytes ?? Pdf);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
        form.Add(content, "file", filename); form.Add(new StringContent(type), "evidenceType");
        if (acknowledged) form.Add(new StringContent("true"), "demoAcknowledged");
        return form;
    }
    private static async Task<(Guid Organization, Guid Application, string Etag)> Setup(Factory factory, Browser browser, bool verified = true, bool business = false, bool trade = false)
    {
        await browser.Login("owner@example.test");
        if (verified) await factory.Update(async db => (await db.Users.SingleAsync()).EmailConfirmed = true);
        var organization = await browser.Send("organizations", body: JsonContent.Create(new { name = "Demo Provider", providerType = business ? "Business" : "Individual" }));
        var id = (await organization.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var profile = await browser.Client.GetAsync($"/api/v1/organizations/{id}/provider-profile");
        var category = trade ? "10000000-0000-0000-0000-000000000003" : "10000000-0000-0000-0000-000000000001";
        Assert.Equal(HttpStatusCode.OK, (await browser.Send($"organizations/{id}/provider-profile", HttpMethod.Put, JsonContent.Create(new { displayName = "Demo Provider", providerType = business ? "Business" : "Individual", biography = "Fictional services.", categoryIds = new[] { category }, serviceArea = "Demo area" }), profile.Headers.ETag!.ToString())).StatusCode);
        var created = await browser.Send($"organizations/{id}/verifications");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var application = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        return (id, application, created.Headers.ETag!.ToString());
    }
    [Fact]
    public async Task SubmissionFreezesSnapshotAndLocksEvidenceAndDuplicateApplications()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var browser = new Browser(client);
        var (org, id, etag) = await Setup(factory, browser); var route = $"organizations/{org}/verifications/{id}";
        Assert.Equal(HttpStatusCode.Conflict, (await browser.Send($"organizations/{org}/verifications")).StatusCode);
        var upload = await browser.Send(route + "/documents", body: File("Identity"), etag: etag);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var document = await upload.Content.ReadFromJsonAsync<JsonElement>(); Assert.DoesNotContain("objectKey", document.ToString()); Assert.DoesNotContain("evidence/", document.ToString());
        var saved = await browser.Send(route + "/submit", etag: upload.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var snapshot = await saved.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("Submitted", snapshot.GetProperty("status").GetString());
        Assert.Equal(1, snapshot.GetProperty("submissionVersion").GetInt32()); Assert.Equal("Demo Provider", snapshot.GetProperty("submittedSnapshot").GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await browser.Send(route + "/documents", body: File("Identity"), etag: saved.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.Send(route + "/documents/" + document.GetProperty("id").GetString(), HttpMethod.Delete, etag: saved.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await browser.Send(route + "/submit", etag: saved.Headers.ETag!.ToString())).StatusCode);
        await factory.Update(async db => { var profile = await db.ProviderProfiles.SingleAsync(); profile.DisplayName = "Later profile"; });
        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/" + route);
        Assert.Equal("Demo Provider", after.GetProperty("submittedSnapshot").GetProperty("displayName").GetString());
    }
    [Fact]
    public async Task SubmissionRequiresVerifiedEmailCompleteProfileAndConfiguredEvidence()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var browser = new Browser(client);
        var (org, id, etag) = await Setup(factory, browser, false, true, true); var route = $"organizations/{org}/verifications/{id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await browser.Send(route + "/submit", etag: etag)).StatusCode);
        await factory.Update(async db => (await db.Users.SingleAsync()).EmailConfirmed = true);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send(route + "/submit", etag: etag)).StatusCode);
        foreach (var type in new[] { "Identity", "BusinessRegistration", "TradeQualification" })
        {
            var uploaded = await browser.Send(route + "/documents", body: File(type), etag: etag);
            Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode); etag = uploaded.Headers.ETag!.ToString();
        }
        await factory.Update(async db => (await db.ProviderProfiles.SingleAsync()).Biography = "");
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send(route + "/submit", etag: etag)).StatusCode);
        await factory.Update(async db => (await db.ProviderProfiles.SingleAsync()).Biography = "Complete");
        Assert.Equal(HttpStatusCode.OK, (await browser.Send(route + "/submit", etag: etag)).StatusCode);
    }
    [Fact]
    public async Task InvalidUploadsAndScannerFailuresDoNotStoreDocuments()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var browser = new Browser(client);
        var (org, id, etag) = await Setup(factory, browser); var route = $"organizations/{org}/verifications/{id}/documents";
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send(route, body: File("Identity"), etag: etag, csrf: false)).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await browser.Send(route, body: File("Identity"))).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await browser.Send(route, body: File("Identity"), etag: "\"stale\"")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send(route, body: File("Unknown"), etag: etag)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send(route, body: File("Identity", Encoding.UTF8.GetBytes("<script>bad</script>")), etag: etag)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send(route, body: File("Identity", mime: "image/png"), etag: etag)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send(route, body: File("Identity", acknowledged: false), etag: etag)).StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await browser.Send(route, body: File("Identity", new byte[EvidenceOptions.MaxBytes + 1]), etag: etag)).StatusCode);
        factory.Scan.Clean = false;
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.Send(route, body: File("Identity"), etag: etag)).StatusCode);
        factory.Scan.Fail = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await browser.Send(route, body: File("Identity"), etag: etag)).StatusCode);
        Assert.Empty(factory.Storage.Objects);
        await factory.Update(async db => Assert.Equal(0, await db.VerificationDocuments.CountAsync()));
    }
    [Fact]
    public async Task DownloadsAreSessionBoundAuditedAndRevokedWithMembership()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); using var otherClient = factory.Browser();
        var owner = new Browser(client); var other = new Browser(otherClient); var (org, id, etag) = await Setup(factory, owner); await other.Login("staff@example.test");
        await factory.Update(async db => { var staff = await db.Users.SingleAsync(x => x.Email == "staff@example.test"); db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = org, UserId = staff.Id, Role = "Staff" }); });
        var uploaded = await owner.Send($"organizations/{org}/verifications/{id}/documents", body: File("Identity", filename: "../../unsafe<script>.pdf"), etag: etag);
        var doc = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await otherClient.GetAsync($"/api/v1/organizations/{org}/verifications/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.Send($"organizations/{org}/verifications/{id}/documents", body: File("Identity"), etag: uploaded.Headers.ETag!.ToString())).StatusCode);
        var granted = await owner.Send($"verification-documents/{doc}/access"); var access = await granted.Content.ReadFromJsonAsync<JsonElement>();
        var url = access.GetProperty("url").GetString()!; var token = access.GetProperty("token").GetString()!;
        using var request = new HttpRequestMessage(HttpMethod.Get, url); request.Headers.Add("X-Evidence-Access", token);
        var download = await client.SendAsync(request); Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Pdf, await download.Content.ReadAsByteArrayAsync()); Assert.Equal("no-store", download.Headers.CacheControl?.ToString());
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition?.DispositionType);
        using var stolen = new HttpRequestMessage(HttpMethod.Get, url); stolen.Headers.Add("X-Evidence-Access", token);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.SendAsync(stolen)).StatusCode);
        var staffAccess = await other.Send($"verification-documents/{doc}/access"); var staffGrant = await staffAccess.Content.ReadFromJsonAsync<JsonElement>();
        await factory.Update(async db => { var user = await db.Users.SingleAsync(x => x.Email == "staff@example.test"); (await db.OrganizationMemberships.SingleAsync(x => x.UserId == user.Id)).Active = false; });
        using var revoked = new HttpRequestMessage(HttpMethod.Get, url); revoked.Headers.Add("X-Evidence-Access", staffGrant.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.SendAsync(revoked)).StatusCode);
        var expired = factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("Marketplace.EvidenceAccess.v1").ToTimeLimitedDataProtector().Protect("expired", DateTimeOffset.UtcNow.AddSeconds(-1));
        using var stale = new HttpRequestMessage(HttpMethod.Get, url); stale.Headers.Add("X-Evidence-Access", expired);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(stale)).StatusCode);
        await factory.Update(async db => Assert.True(await db.OrganizationAuditEvents.AnyAsync(x => x.Action == "verification.document-downloaded")));
    }
    [Fact]
    public async Task CrossOrganizationApplicationAndDocumentIdsNeverAuthorizeAccess()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); using var outsiderClient = factory.Browser();
        var owner = new Browser(client); var outsider = new Browser(outsiderClient); var (org, id, etag) = await Setup(factory, owner); await outsider.Login("outsider@example.test");
        var uploaded = await owner.Send($"organizations/{org}/verifications/{id}/documents", body: File("Identity"), etag: etag);
        var doc = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync($"/api/v1/organizations/{org}/verifications")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Send($"organizations/{org}/verifications/{id}/submit", etag: uploaded.Headers.ETag!.ToString())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Send($"verification-documents/{doc}/access")).StatusCode);
        var own = await owner.Send("organizations", body: JsonContent.Create(new { name = "Other", providerType = "Individual" })); var otherOrg = (await own.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/organizations/{otherOrg}/verifications/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Send($"organizations/{otherOrg}/verifications/{id}/documents/{doc}", HttpMethod.Delete, etag: uploaded.Headers.ETag!.ToString())).StatusCode);
    }
    [Fact]
    public async Task DraftDeletionAndExpiredRetentionQueuePrivateObjectCleanup()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var owner = new Browser(client); var (org, id, etag) = await Setup(factory, owner);
        var route = $"organizations/{org}/verifications/{id}"; var uploaded = await owner.Send(route + "/documents", body: File("Identity"), etag: etag);
        var doc = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await owner.Send(route + "/submit", etag: etag)).StatusCode);
        var removed = await owner.Send(route + "/documents/" + doc, HttpMethod.Delete, etag: uploaded.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Send($"verification-documents/{doc}/access")).StatusCode);
        var worker = ActivatorUtilities.CreateInstance<EvidenceRetentionWorker>(factory.Services); await worker.Sweep(CancellationToken.None); Assert.Empty(factory.Storage.Objects);
        var second = await owner.Send(route + "/documents", body: File("Identity"), etag: removed.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        await factory.Update(async db => { var document = await db.VerificationDocuments.SingleAsync(x => x.DeletedAt == null); document.ExpiresAt = DateTime.UtcNow.AddSeconds(-1); });
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.Send(route + "/submit", etag: second.Headers.ETag!.ToString())).StatusCode);
        await worker.Sweep(CancellationToken.None); Assert.Empty(factory.Storage.Objects);
    }
    [Fact]
    public async Task StorageFailureLeavesRetryableCleanupReservationWithoutMetadata()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var owner = new Browser(client); var (org, id, etag) = await Setup(factory, owner);
        factory.Storage.Fail = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await owner.Send($"organizations/{org}/verifications/{id}/documents", body: File("Identity"), etag: etag)).StatusCode);
        await factory.Update(async db =>
        {
            Assert.Equal(0, await db.VerificationDocuments.CountAsync()); var job = await db.EvidenceDeletions.SingleAsync(); job.DueAt = DateTime.UtcNow.AddSeconds(-1);
        });
        var worker = ActivatorUtilities.CreateInstance<EvidenceRetentionWorker>(factory.Services); await worker.Sweep(CancellationToken.None);
        await factory.Update(async db => Assert.Equal(1, (await db.EvidenceDeletions.SingleAsync()).Attempts));
    }
    [Theory]
    [InlineData("<html>fake PDF</html>")]
    [InlineData("%PDF-1.4 truncated")]
    public void FileContentCannotBeInferredFromExtension(string text) => Assert.Null(EvidenceFileValidation.Detect(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public async Task UploadRaceRollsBackMetadataAndKeepsCleanupReservation()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var owner = new Browser(client);
        var (org, id, etag) = await Setup(factory, owner);
        factory.Storage.OnPut = () => factory.Update(async db => (await db.VerificationApplications.SingleAsync()).Version = Guid.NewGuid());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await owner.Send($"organizations/{org}/verifications/{id}/documents", body: File("Identity"), etag: etag)).StatusCode);
        Assert.Single(factory.Storage.Objects);
        await factory.Update(async db =>
        {
            Assert.Equal(0, await db.VerificationDocuments.CountAsync());
            (await db.EvidenceDeletions.SingleAsync()).DueAt = DateTime.UtcNow.AddSeconds(-1);
        });
        await ActivatorUtilities.CreateInstance<EvidenceRetentionWorker>(factory.Services).Sweep(CancellationToken.None);
        Assert.Empty(factory.Storage.Objects);
    }
}
