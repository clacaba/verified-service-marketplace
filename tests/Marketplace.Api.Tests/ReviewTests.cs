using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marketplace.Api.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Marketplace.Api.Tests;

public sealed partial class VerificationTests
{
    private sealed class DecisionRaceInterceptor(Factory factory) : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override async ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData, Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (factory.DecisionRace != null && eventData.Context!.ChangeTracker.Entries<VerificationDecision>().Any(x => x.State == EntityState.Added))
            {
                var race = factory.DecisionRace; factory.DecisionRace = null; await race();
            }
            return result;
        }
    }
    private static async Task GrantReviewer(Factory factory, Browser reviewer, string email = "reviewer@example.test")
    {
        await reviewer.Login(email);
        await factory.Update(async db => (await db.Users.SingleAsync(x => x.Email == email)).EmailConfirmed = true);
        Assert.Equal(0, await ReviewerAccess.Provision(factory.Services, email, true));
    }
    private static async Task<(Guid Org, Guid App, Guid Document, string Etag)> Submitted(Factory factory, Browser owner)
    {
        var (org, app, etag) = await Setup(factory, owner);
        var upload = await owner.Send($"organizations/{org}/verifications/{app}/documents", body: File("Identity"), etag: etag);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var document = (await upload.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var submit = await owner.Send($"organizations/{org}/verifications/{app}/submit", etag: upload.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        return (org, app, document, submit.Headers.ETag!.ToString());
    }
    private static object Decide(string decision, string reason = "Please correct the fictional identity evidence.", bool identity = false) =>
        new { decision, reason, checks = identity ? new[] { new { type = "Identity", expiresAt = (DateTimeOffset?)null } } : [] };

    [Fact]
    public async Task ReviewerProvisioningIsExplicitVerifiedAuditedAndRevocable()
    {
        await using var factory = new Factory(); using var client = factory.Browser(); var reviewer = new Browser(client);
        await reviewer.Login("reviewer@example.test");
        Assert.Equal(1, await ReviewerAccess.Provision(factory.Services, "reviewer@example.test", true));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/verifications")).StatusCode);
        await factory.Update(async db => (await db.Users.SingleAsync()).EmailConfirmed = true);
        Assert.Equal(0, await ReviewerAccess.Provision(factory.Services, "reviewer@example.test", true));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/admin/verifications")).StatusCode);
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/users/me"); Assert.Contains("Reviewer", me.GetProperty("capabilities").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(0, await ReviewerAccess.Provision(factory.Services, "reviewer@example.test", false));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/admin/verifications")).StatusCode);
        await factory.Update(async db => Assert.Equal(2, await db.PlatformAccessAudits.CountAsync()));
    }
    [Fact]
    public async Task QueueAndReviewClaimsEnforceRolesVersionsAndLeaseOwnership()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser(); using var secondClient = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient); var second = new Browser(secondClient);
        var (_, app, _, etag) = await Submitted(factory, owner); await GrantReviewer(factory, reviewer); await GrantReviewer(factory, second, "second@example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.GetAsync("/api/v1/admin/verifications")).StatusCode);
        var queue = await reviewerClient.GetFromJsonAsync<JsonElement>("/api/v1/admin/verifications?pageSize=1"); Assert.Equal(1, queue.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewerClient.GetAsync("/api/v1/admin/verifications?status=Draft")).StatusCode);
        Assert.Equal((HttpStatusCode)428, (await reviewer.Send($"admin/verifications/{app}/start-review")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.Send($"admin/verifications/{app}/start-review", etag: etag, csrf: false)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Rejected")), etag: etag)).StatusCode);
        var claim = await reviewer.Send($"admin/verifications/{app}/start-review", etag: etag); Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await second.Send($"admin/verifications/{app}/start-review", etag: etag)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await second.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Rejected")), etag: claim.Headers.ETag!.ToString())).StatusCode);
        await factory.Update(async db => (await db.VerificationApplications.SingleAsync()).ReviewLeaseExpiresAt = DateTime.UtcNow.AddSeconds(-1));
        var reclaimed = await second.Send($"admin/verifications/{app}/start-review", etag: claim.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.OK, reclaimed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Rejected")), etag: reclaimed.Headers.ETag!.ToString())).StatusCode);
    }
    [Fact]
    public async Task DecisionAndNotificationsCommitOnceAndRemainRecipientPrivate()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (_, app, _, etag) = await Submitted(factory, owner); await GrantReviewer(factory, reviewer);
        var claim = await reviewer.Send($"admin/verifications/{app}/start-review", etag: etag); var claimed = claim.Headers.ETag!.ToString();
        var decided = await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("AdditionalInformationRequired")), etag: claimed); Assert.Equal(HttpStatusCode.OK, decided.StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Rejected")), etag: claimed)).StatusCode);
        var notifications = await ownerClient.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        var id = notifications.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.Send($"notifications/{id}", HttpMethod.Patch, JsonContent.Create(new { read = true }))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.Send($"notifications/{id}", HttpMethod.Patch, JsonContent.Create(new { read = true }))).StatusCode);
        await factory.Update(async db =>
        {
            Assert.Equal(1, await db.VerificationDecisions.CountAsync()); Assert.Equal(1, await db.Notifications.CountAsync());
            Assert.Equal(1, await db.OrganizationAuditEvents.CountAsync(x => x.Action == "verification.decided"));
            var mail = await db.VerificationEmails.SingleAsync(x => x.Subject.Contains("has an update")); Assert.DoesNotContain("new decision", mail.ProtectedBody);
        });
    }
    [Fact]
    public async Task CorrectionCreatesFreshLinkedDraftAndPreservesEarlierSnapshotAndDocuments()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (org, app, _, etag) = await Submitted(factory, owner); await GrantReviewer(factory, reviewer);
        var claim = await reviewer.Send($"admin/verifications/{app}/start-review", etag: etag);
        var rejected = await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Rejected")), etag: claim.Headers.ETag!.ToString());
        var original = await rejected.Content.ReadFromJsonAsync<JsonElement>(); var snapshot = original.GetProperty("submittedSnapshot").GetRawText();
        var correction = await owner.Send($"organizations/{org}/verifications/{app}/revisions", etag: rejected.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.Created, correction.StatusCode);
        var draft = await correction.Content.ReadFromJsonAsync<JsonElement>(); var revision = draft.GetProperty("id").GetGuid();
        Assert.Equal(app, draft.GetProperty("parentApplicationId").GetGuid()); Assert.Equal(0, draft.GetProperty("documents").GetArrayLength());
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await owner.Send($"organizations/{org}/verifications/{app}/revisions", etag: rejected.Headers.ETag!.ToString())).StatusCode);
        var originalGet = await ownerClient.GetAsync($"/api/v1/organizations/{org}/verifications/{app}"); var old = await originalGet.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(snapshot, old.GetProperty("submittedSnapshot").GetRawText()); Assert.Equal(1, old.GetProperty("documents").GetArrayLength()); Assert.Equal(1, old.GetProperty("decisions").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Send($"organizations/{org}/verifications/{app}/revisions", etag: originalGet.Headers.ETag!.ToString())).StatusCode);
        var fresh = await owner.Send($"organizations/{org}/verifications/{revision}/documents", body: File("Identity"), etag: correction.Headers.ETag!.ToString());
        var resubmitted = await owner.Send($"organizations/{org}/verifications/{revision}/submit", etag: fresh.Headers.ETag!.ToString()); Assert.Equal(HttpStatusCode.OK, resubmitted.StatusCode);
        Assert.Equal(2, (await resubmitted.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submissionVersion").GetInt32());
        await factory.Update(async db => { Assert.Equal(2, await db.VerificationApplications.CountAsync()); Assert.Equal(1, await db.VerificationApplications.CountAsync(x => x.IsActive)); Assert.Equal(2, await db.VerificationDocuments.CountAsync()); });
    }
    [Fact]
    public async Task ApprovalRequiresCurrentEvidenceExplicitChecksAndRevokesChecksOnProfileChanges()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (org, app, _, etag) = await Submitted(factory, owner); await GrantReviewer(factory, reviewer);
        var claim = await reviewer.Send($"admin/verifications/{app}/start-review", etag: etag); etag = claim.Headers.ETag!.ToString();
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Rejected", "")), etag: etag)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Approved", "")), etag: etag)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(new { decision = "Approved", checks = new[] { new { type = "Identity" } }, simulated = false }), etag: etag)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(new { decision = "Approved", checks = new[] { new { type = "Identity", expiresAt = DateTimeOffset.UtcNow.AddYears(1) } } }), etag: etag)).StatusCode);
        var approved = await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Approved", "", true)), etag: etag); Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.Send($"organizations/{org}/verifications/{app}/revisions", etag: approved.Headers.ETag!.ToString())).StatusCode);
        await factory.Update(async db => Assert.True((await db.VerificationChecks.SingleAsync()).Simulated));
        var profile = await ownerClient.GetAsync($"/api/v1/organizations/{org}/provider-profile");
        Assert.Equal(HttpStatusCode.OK, (await owner.Send($"organizations/{org}/provider-profile", HttpMethod.Put, JsonContent.Create(new { displayName = "Changed provider", providerType = "Individual", biography = "New details", categoryIds = new[] { "10000000-0000-0000-0000-000000000001" }, serviceArea = "Demo area" }), profile.Headers.ETag!.ToString())).StatusCode);
        await factory.Update(async db => Assert.NotNull((await db.VerificationChecks.SingleAsync()).RevokedAt));
        var original = await ownerClient.GetAsync($"/api/v1/organizations/{org}/verifications/{app}");
        Assert.True((await original.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("canCreateRevision").GetBoolean());
        Assert.Equal(HttpStatusCode.Created, (await owner.Send($"organizations/{org}/verifications/{app}/revisions", etag: original.Headers.ETag!.ToString())).StatusCode);
    }
    [Fact]
    public async Task ProfileOrEvidenceChangesPreventApproval()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (_, app, _, etag) = await Submitted(factory, owner); await GrantReviewer(factory, reviewer);
        var claim = await reviewer.Send($"admin/verifications/{app}/start-review", etag: etag); etag = claim.Headers.ETag!.ToString();
        await factory.Update(async db => (await db.ProviderProfiles.SingleAsync()).DisplayName = "Changed");
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Approved", "", true)), etag: etag)).StatusCode);
        await factory.Update(async db => { (await db.ProviderProfiles.SingleAsync()).DisplayName = "Demo Provider"; (await db.VerificationDocuments.SingleAsync()).ExpiresAt = DateTime.UtcNow.AddSeconds(-1); });
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Approved", "", true)), etag: etag)).StatusCode);
        await factory.Update(async db => { Assert.Equal(0, await db.VerificationDecisions.CountAsync()); Assert.Equal(0, await db.Notifications.CountAsync()); });
    }

    [Fact]
    public async Task ChangedEvidencePolicyRequiresCorrectedSubmissionBeforeApproval()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (_, app, _, etag) = await Submitted(factory, owner); await GrantReviewer(factory, reviewer);
        var claim = await reviewer.Send($"admin/verifications/{app}/start-review", etag: etag);
        factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Marketplace.Api.Verification.EvidenceOptions>>().Value.IndividualRequired = ["Identity", "TradeQualification"];
        Assert.Equal(HttpStatusCode.Conflict, (await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Approved", "", true)), etag: claim.Headers.ETag!.ToString())).StatusCode);
        await factory.Update(async db => Assert.Equal(0, await db.VerificationDecisions.CountAsync()));
    }
    [Fact]
    public async Task ReviewersCannotReviewTheirOwnOrganizationsOrReadDraftEvidence()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (org, app, _, etag) = await Submitted(factory, owner); await GrantReviewer(factory, reviewer);
        Assert.Equal(0, await ReviewerAccess.Provision(factory.Services, "owner@example.test", true));
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.Send($"admin/verifications/{app}/start-review", etag: etag)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await ownerClient.GetAsync($"/api/v1/admin/verifications/{app}")).StatusCode);
        var ownQueue = await ownerClient.GetFromJsonAsync<JsonElement>("/api/v1/admin/verifications"); Assert.Equal(0, ownQueue.GetProperty("totalCount").GetInt32());
        var draftOrg = await owner.Send("organizations", body: JsonContent.Create(new { name = "Draft org", providerType = "Individual" }));
        var otherOrg = (await draftOrg.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var created = await owner.Send($"organizations/{otherOrg}/verifications"); var draft = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var uploaded = await owner.Send($"organizations/{otherOrg}/verifications/{draft}/documents", body: File("Identity"), etag: created.Headers.ETag!.ToString());
        var document = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await reviewerClient.GetAsync($"/api/v1/admin/verifications/{draft}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await reviewer.Send($"verification-documents/{document}/access")).StatusCode);
    }
    [Fact]
    public async Task ReviewerDocumentGrantStopsWorkingWhenReviewerAccessIsRevoked()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (_, _, doc, _) = await Submitted(factory, owner); await GrantReviewer(factory, reviewer);
        var access = await reviewer.Send($"verification-documents/{doc}/access"); Assert.Equal(HttpStatusCode.OK, access.StatusCode);
        var grant = await access.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, await ReviewerAccess.Provision(factory.Services, "reviewer@example.test", false));
        using var download = new HttpRequestMessage(HttpMethod.Get, grant.GetProperty("url").GetString()); download.Headers.Add("X-Evidence-Access", grant.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await reviewerClient.SendAsync(download)).StatusCode);
    }

    [Fact]
    public async Task RacingDecisionRollsBackChecksAuditNotificationAndEmailTogether()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (_, app, _, etag) = await Submitted(factory, owner); await GrantReviewer(factory, reviewer);
        var claim = await reviewer.Send($"admin/verifications/{app}/start-review", etag: etag);
        factory.DecisionRace = () => factory.Update(async db => (await db.VerificationApplications.SingleAsync()).Version = Guid.NewGuid());
        var result = await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Approved", "", true)), etag: claim.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.PreconditionFailed, result.StatusCode);
        await factory.Update(async db =>
        {
            Assert.Equal("UnderReview", (await db.VerificationApplications.SingleAsync()).Status);
            Assert.Equal(0, await db.VerificationDecisions.CountAsync()); Assert.Equal(0, await db.VerificationChecks.CountAsync()); Assert.Equal(0, await db.Notifications.CountAsync());
            Assert.Equal(0, await db.VerificationEmails.CountAsync(x => x.Subject.Contains("has an update")));
            Assert.Equal(0, await db.OrganizationAuditEvents.CountAsync(x => x.Action == "verification.decided"));
        });
    }

    [Fact]
    public async Task NotificationsDisappearWhenOrganizationMembershipIsDeactivated()
    {
        await using var factory = new Factory(); using var ownerClient = factory.Browser(); using var reviewerClient = factory.Browser();
        var owner = new Browser(ownerClient); var reviewer = new Browser(reviewerClient);
        var (_, app, _, etag) = await Submitted(factory, owner); await GrantReviewer(factory, reviewer);
        var claim = await reviewer.Send($"admin/verifications/{app}/start-review", etag: etag);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.Send($"admin/verifications/{app}/decisions", body: JsonContent.Create(Decide("Rejected")), etag: claim.Headers.ETag!.ToString())).StatusCode);
        var list = await ownerClient.GetFromJsonAsync<JsonElement>("/api/v1/notifications"); var id = list.GetProperty("items")[0].GetProperty("id").GetGuid();
        await factory.Update(async db => (await db.OrganizationMemberships.SingleAsync()).Active = false);
        var hidden = await ownerClient.GetFromJsonAsync<JsonElement>("/api/v1/notifications"); Assert.Equal(0, hidden.GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.Send($"notifications/{id}", HttpMethod.Patch, JsonContent.Create(new { read = true }))).StatusCode);
    }
}
