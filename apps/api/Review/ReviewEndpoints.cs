using System.Security.Claims;
using System.Text.Json;
using Marketplace.Api.Data;
using Marketplace.Api.Identity;
using Marketplace.Api.Organizations;
using Marketplace.Api.Verification;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.Api.Review;

public static class ReviewEndpoints
{
    public sealed record CheckInput(string Type, DateTimeOffset? ExpiresAt);
    public sealed record DecisionInput(string Decision, string? Reason, CheckInput[]? Checks);
    public sealed record ReadInput(bool Read);
    private static readonly string[] Statuses = ["Submitted", "UnderReview", "Approved", "Rejected", "AdditionalInformationRequired"];
    public static void MapMarketplaceReview(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().RequireRateLimiting("evidence");
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext; http.Response.Headers.CacheControl = "no-store";
            if (!HttpMethods.IsGet(http.Request.Method))
            {
                try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException) { return Problem(400, "Invalid CSRF token. Refresh before retrying."); }
            }
            try { return await next(context); }
            catch (DbUpdateConcurrencyException) { return Problem(412, "The application changed. Reload before retrying."); }
            catch (DbUpdateException) { return Problem(409, "The application already changed or has a correction draft."); }
        });
        group.MapGet("/admin/verifications", async (string? status, int? page, int? pageSize, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            if (!await ReviewerAccess.IsReviewer(db, http)) return Results.Forbid();
            var current = page ?? 1; var size = pageSize ?? 20;
            if (current < 1 || current > 100000 || size < 1 || size > 100 || (status != null && !Statuses.Contains(status))) return Problem(400, "Invalid queue filter or pagination.");
            var userId = UserId(http);
            var query = db.VerificationApplications.Where(x => !db.OrganizationMemberships.Any(m => m.OrganizationId == x.OrganizationId && m.UserId == userId));
            query = status == null ? query.Where(x => x.Status == "Submitted" || x.Status == "UnderReview") : query.Where(x => x.Status == status);
            var applications = await query.Include(x => x.Documents).Include(x => x.Decisions).ThenInclude(x => x.Checks).AsSplitQuery().OrderBy(x => x.SubmittedAt).ThenBy(x => x.Id).Skip((current - 1) * size).Take(size).ToListAsync();
            var items = new List<object>(); foreach (var application in applications) items.Add(await VerificationEndpoints.Summary(application, db, policy.Value));
            return Results.Ok(new { items, page = current, pageSize = size, totalCount = await query.CountAsync() });
        });
        group.MapGet("/admin/verifications/{applicationId:guid}", async (Guid applicationId, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            if (!await ReviewerAccess.IsReviewer(db, http)) return Results.Forbid();
            var application = await Load(db, applicationId); if (application is null || application.Status == "Draft") return Results.NotFound();
            if (!await ReviewerAccess.CanReview(db, application.OrganizationId, http)) return Results.Forbid();
            http.Response.Headers.ETag = Tag(application.Version);
            return Results.Ok(await VerificationEndpoints.Summary(application, db, policy.Value));
        });
        group.MapPost("/admin/verifications/{applicationId:guid}/start-review", async (Guid applicationId, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            if (!await ReviewerAccess.IsReviewer(db, http)) return Results.Forbid();
            var application = await Load(db, applicationId); if (application is null || application.Status == "Draft") return Results.NotFound();
            if (!await ReviewerAccess.CanReview(db, application.OrganizationId, http)) return Results.Forbid();
            var precondition = Match(application, http); if (precondition != null) return precondition;
            if (application.Status == "UnderReview" && application.ReviewLeaseExpiresAt > DateTime.UtcNow && await db.Users.AnyAsync(x => x.Id == application.ReviewStartedBy && x.IsReviewer && x.EmailConfirmed))
                return Problem(409, "This review is already claimed. Wait for its lease to expire.");
            if (application.Status is not ("Submitted" or "UnderReview")) return Problem(409, "Only a submitted application can enter review.");
            application.Status = "UnderReview"; application.ReviewStartedBy = UserId(http); application.ReviewStartedAt = DateTime.UtcNow; application.ReviewLeaseExpiresAt = DateTime.UtcNow.AddMinutes(30); application.Version = Guid.NewGuid();
            Audit(db, application, http, "verification.review-started");
            await db.SaveChangesAsync(); http.Response.Headers.ETag = Tag(application.Version);
            return Results.Ok(await VerificationEndpoints.Summary(application, db, policy.Value));
        });
        group.MapPost("/admin/verifications/{applicationId:guid}/decisions", async (Guid applicationId, DecisionInput input, HttpContext http, MarketplaceDbContext db, IDataProtectionProvider protection, IConfiguration config, IOptions<EvidenceOptions> policy) =>
        {
            if (!await ReviewerAccess.IsReviewer(db, http)) return Results.Forbid();
            var application = await Load(db, applicationId); if (application is null || application.Status == "Draft") return Results.NotFound();
            if (!await ReviewerAccess.CanReview(db, application.OrganizationId, http)) return Results.Forbid();
            var precondition = Match(application, http); if (precondition != null) return precondition;
            if (application.Status != "UnderReview" || application.ReviewStartedBy != UserId(http) || application.ReviewLeaseExpiresAt is null || application.ReviewLeaseExpiresAt <= DateTime.UtcNow) return Problem(409, "Claim a current review lease before deciding.");
            if (input.Decision is not ("Approved" or "Rejected" or "AdditionalInformationRequired")) return Problem(400, "Choose a supported decision.");
            var reason = input.Reason?.Trim() ?? "";
            if (reason.Length > 2000 || (input.Decision != "Approved" && reason.Length == 0)) return Problem(400, "Rejection and information requests need a reason of 1–2,000 characters.");
            var selected = input.Checks ?? [];
            if (selected.Length > 20 || selected.Any(x => x is null || !EvidenceOptions.EvidenceTypes.Contains(x.Type)) || selected.Select(x => x.Type).Distinct().Count() != selected.Length) return Problem(400, "Choose distinct supported checks.");
            if (input.Decision != "Approved" && selected.Length != 0) return Problem(400, "Only approval can record checks.");
            var decision = new VerificationDecision { ApplicationId = application.Id, ReviewerId = UserId(http), Decision = input.Decision, Reason = reason };
            if (input.Decision == "Approved")
            {
                var snapshot = JsonSerializer.Deserialize<JsonElement>(application.SnapshotJson);
                var required = snapshot.GetProperty("requiredEvidence").EnumerateArray().Select(x => x.GetString()!).ToArray();
                if (selected.Length != required.Length || required.Any(type => !selected.Any(x => x.Type == type))) return Problem(400, "Approval must check every required evidence type, and no extra types.");
                var profile = await db.ProviderProfiles.Include(x => x.Categories).SingleAsync(x => x.Id == application.ProviderId);
                if (!MatchesProfile(snapshot, profile)) return Problem(409, "The provider profile changed since submission. Request corrected evidence.");
                if (!VerificationEndpoints.Required(profile, policy.Value).Order().SequenceEqual(required.Order())) return Problem(409, "Evidence requirements changed since submission. Request corrected evidence.");
                var categoryIds = profile.Categories.Select(x => x.CategoryId).ToArray();
                if (await db.ServiceCategories.CountAsync(x => x.Active && categoryIds.Contains(x.Id)) != categoryIds.Length) return Problem(409, "The submitted categories are no longer active.");
                var now = DateTime.UtcNow;
                foreach (var check in selected)
                {
                    var documents = application.Documents.Where(x => x.EvidenceType == check.Type && x.DeletedAt == null && x.ExpiresAt > now).ToArray();
                    if (documents.Length == 0) return Problem(409, "Required evidence has expired or is unavailable. Request corrected evidence.");
                    var maximum = documents.Min(x => x.ExpiresAt);
                    var expiry = check.ExpiresAt?.UtcDateTime ?? maximum;
                    if (expiry <= now || expiry > maximum) return Problem(400, "Check expiry must be in the future and not exceed evidence retention.");
                    decision.Checks.Add(new VerificationCheck { DecisionId = decision.Id, OrganizationId = application.OrganizationId, Type = check.Type, ExpiresAt = expiry, Simulated = true });
                    foreach (var document in documents) document.Version = Guid.NewGuid(); // Serialize with retention.
                }
                // Serialize approval with profile edits so obsolete details cannot gain current checks.
                profile.Version = Guid.NewGuid();
            }
            application.Status = input.Decision; application.Version = Guid.NewGuid(); application.ReviewLeaseExpiresAt = null;
            db.VerificationDecisions.Add(decision); application.Decisions.Add(decision);
            Audit(db, application, http, "verification.decided");
            var members = await (from membership in db.OrganizationMemberships join user in db.Users on membership.UserId equals user.Id where membership.OrganizationId == application.OrganizationId && membership.Active select new { user.Id, user.Email, user.EmailConfirmed, membership.Role }).ToListAsync();
            foreach (var member in members)
            {
                db.Notifications.Add(new Notification { UserId = member.Id, ApplicationId = application.Id, Message = "Verification: " + input.Decision + (reason.Length == 0 ? "." : ". " + reason) });
                if (member.Role == "Owner" && member.EmailConfirmed)
                {
                    var origin = config["Web:PublicOrigin"] ?? "http://localhost:3000";
                    if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !uri.IsLoopback)) throw new InvalidOperationException("Configure trusted Web:PublicOrigin.");
                    var link = new Uri(uri, "/verification") + "?organizationId=" + application.OrganizationId;
                    db.VerificationEmails.Add(new VerificationEmail { Recipient = member.Email!, Subject = "Your marketplace verification has an update", ProtectedBody = protection.CreateProtector(VerificationEmailWorker.Purpose).Protect("Your fictional verification application has a new decision. Sign in to view it:\n" + link) });
                }
            }
            await db.SaveChangesAsync(); http.Response.Headers.ETag = Tag(application.Version);
            return Results.Ok(await VerificationEndpoints.Summary(application, db, policy.Value));
        });
        group.MapPost("/organizations/{organizationId:guid}/verifications/{applicationId:guid}/revisions", async (Guid organizationId, Guid applicationId, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            var member = await db.OrganizationMemberships.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserId == UserId(http) && x.Active);
            if (member is null) return Results.NotFound(); if (member.Role != "Owner") return Results.Forbid();
            var original = await db.VerificationApplications.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.Id == applicationId); if (original is null) return Results.NotFound();
            var precondition = Match(original, http); if (precondition != null) return precondition;
            var renewal = false;
            if (original.Status == "Approved")
            {
                var required = JsonSerializer.Deserialize<JsonElement>(original.SnapshotJson).GetProperty("requiredEvidence").EnumerateArray().Select(x => x.GetString()!).ToArray();
                var currentTypes = await db.VerificationChecks.Where(c => c.RevokedAt == null && c.ExpiresAt > DateTime.UtcNow && db.VerificationDecisions.Any(d => d.Id == c.DecisionId && d.ApplicationId == original.Id)).Select(c => c.Type).ToListAsync();
                renewal = required.Any(type => !currentTypes.Contains(type));
            }
            if ((original.Status is not ("Rejected" or "AdditionalInformationRequired") && !renewal) || !original.IsActive) return Problem(409, "Only the latest rejected, information-request, or invalidated/expired approval can have a new draft.");
            await using var transaction = await db.Database.BeginTransactionAsync();
            original.IsActive = false; original.Version = Guid.NewGuid(); await db.SaveChangesAsync();
            var revision = new VerificationApplication { OrganizationId = organizationId, ProviderId = original.ProviderId, ParentApplicationId = original.Id, RevisionNumber = original.RevisionNumber + 1 };
            db.VerificationApplications.Add(revision); Audit(db, revision, http, "verification.revision-created");
            await db.SaveChangesAsync(); await transaction.CommitAsync();
            http.Response.Headers.ETag = Tag(revision.Version);
            return Results.Created($"/api/v1/organizations/{organizationId}/verifications/{revision.Id}", await VerificationEndpoints.Summary(revision, db, policy.Value));
        });
        group.MapGet("/notifications", async (int? page, int? pageSize, HttpContext http, MarketplaceDbContext db) =>
        {
            var current = page ?? 1; var size = pageSize ?? 20;
            if (current < 1 || current > 100000 || size < 1 || size > 100) return Problem(400, "Invalid pagination.");
            var userId = UserId(http);
            var query = db.Notifications.Where(x => x.UserId == userId && db.VerificationApplications.Any(a => a.Id == x.ApplicationId && db.OrganizationMemberships.Any(m => m.OrganizationId == a.OrganizationId && m.UserId == userId && m.Active)));
            var items = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((current - 1) * size).Take(size).Select(x => new { x.Id, x.ApplicationId, organizationId = db.VerificationApplications.Where(a => a.Id == x.ApplicationId).Select(a => a.OrganizationId).First(), x.Message, x.CreatedAt, read = x.ReadAt != null }).ToListAsync();
            return Results.Ok(new { items, page = current, pageSize = size, totalCount = await query.CountAsync() });
        });
        group.MapPatch("/notifications/{notificationId:guid}", async (Guid notificationId, ReadInput input, HttpContext http, MarketplaceDbContext db) =>
        {
            if (!input.Read) return Problem(400, "Only marking a notification read is supported.");
            var userId = UserId(http);
            var notification = await db.Notifications.SingleOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId);
            if (notification is null) return Results.NotFound();
            var application = await db.VerificationApplications.SingleAsync(x => x.Id == notification.ApplicationId);
            if (!await db.OrganizationMemberships.AnyAsync(x => x.OrganizationId == application.OrganizationId && x.UserId == userId && x.Active)) return Results.NotFound();
            notification.ReadAt ??= DateTime.UtcNow; await db.SaveChangesAsync(); return Results.NoContent();
        });
    }
    private static Task<VerificationApplication?> Load(MarketplaceDbContext db, Guid id) => db.VerificationApplications.Include(x => x.Documents).Include(x => x.Decisions).ThenInclude(x => x.Checks).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id);
    private static IResult? Match(VerificationApplication application, HttpContext http)
    {
        var expected = http.Request.Headers.IfMatch.ToString();
        return string.IsNullOrWhiteSpace(expected) ? Problem(428, "If-Match is required.") : expected != Tag(application.Version) ? Problem(412, "The application changed. Reload before retrying.") : null;
    }
    private static bool MatchesProfile(JsonElement snapshot, Marketplace.Api.Providers.ProviderProfile profile) =>
        snapshot.GetProperty("displayName").GetString() == profile.DisplayName && snapshot.GetProperty("providerType").GetString() == profile.ProviderType
        && snapshot.GetProperty("biography").GetString() == profile.Biography && snapshot.GetProperty("serviceArea").GetString() == profile.ServiceArea
        && snapshot.GetProperty("categoryIds").EnumerateArray().Select(x => x.GetGuid()).Order().SequenceEqual(profile.Categories.Select(x => x.CategoryId).Order());
    private static string UserId(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static string Tag(Guid version) => "\"" + version.ToString("D") + "\"";
    private static IResult Problem(int status, string title) => Results.Problem(statusCode: status, title: title);
    private static void Audit(MarketplaceDbContext db, VerificationApplication application, HttpContext http, string action) =>
        db.OrganizationAuditEvents.Add(new OrganizationAuditEvent { OrganizationId = application.OrganizationId, ActorId = UserId(http), Action = action, ResourceId = application.Id.ToString() });
}
