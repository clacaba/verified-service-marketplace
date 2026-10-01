using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Amazon.S3;
using Marketplace.Api.Data;
using Marketplace.Api.Organizations;
using Marketplace.Api.Providers;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marketplace.Api.Verification;

public static class VerificationEndpoints
{
    public static void MapMarketplaceVerification(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().RequireRateLimiting("evidence");
        group.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext; http.Response.Headers.CacheControl = "no-store";
            http.Response.Headers["Referrer-Policy"] = "no-referrer";
            if (!HttpMethods.IsGet(http.Request.Method))
            {
                try { await http.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException) { return Problem(400, "Refresh the page and try again. Invalid CSRF token."); }
            }
            try { return await next(context); }
            catch (DbUpdateConcurrencyException) { return Problem(412, "The application changed. Reload before retrying."); }
            catch (DbUpdateException) { return Problem(409, "The application changed or an active application already exists."); }
            catch (Exception e) when (e is AmazonS3Exception or EvidenceUnavailableException or IOException or System.Net.Sockets.SocketException || (e is OperationCanceledException && !http.RequestAborted.IsCancellationRequested))
            { return Problem(503, "Private storage or malware scanning is unavailable. Retry later."); }
        });
        const string path = "/organizations/{organizationId:guid}/verifications";
        group.MapPost(path, async (Guid organizationId, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            var permission = await Authorize(db, organizationId, http, true);
            if (permission != null) return permission;
            if (await db.VerificationApplications.AnyAsync(x => x.OrganizationId == organizationId && x.IsActive)) return Problem(409, "An active verification application already exists.");
            var profile = await db.ProviderProfiles.SingleAsync(x => x.OrganizationId == organizationId);
            var application = new VerificationApplication { OrganizationId = organizationId, ProviderId = profile.Id };
            db.VerificationApplications.Add(application);
            Audit(db, organizationId, http, "verification.created", application.Id);
            await db.SaveChangesAsync();
            http.Response.Headers.ETag = Tag(application.Version);
            return Results.Created($"/api/v1/organizations/{organizationId}/verifications/{application.Id}", await Summary(application, db, policy.Value));
        });
        group.MapGet(path, async (Guid organizationId, int? page, int? pageSize, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            var permission = await Authorize(db, organizationId, http, false); if (permission != null) return permission;
            var current = page ?? 1; var size = pageSize ?? 20;
            if (current < 1 || current > 100000 || size < 1 || size > 100) return Problem(400, "Invalid pagination.");
            var query = db.VerificationApplications.Where(x => x.OrganizationId == organizationId);
            var applications = await query.Include(x => x.Documents).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((current - 1) * size).Take(size).ToListAsync();
            var items = new List<object>(); foreach (var application in applications) items.Add(await Summary(application, db, policy.Value));
            return Results.Ok(new { items, page = current, pageSize = size, totalCount = await query.CountAsync() });
        });
        group.MapGet(path + "/{applicationId:guid}", async (Guid organizationId, Guid applicationId, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            var permission = await Authorize(db, organizationId, http, false); if (permission != null) return permission;
            var application = await Application(db, organizationId, applicationId); if (application is null) return Results.NotFound();
            http.Response.Headers.ETag = Tag(application.Version);
            return Results.Ok(await Summary(application, db, policy.Value));
        });
        group.MapPost(path + "/{applicationId:guid}/documents", async (Guid organizationId, Guid applicationId, HttpContext http, MarketplaceDbContext db, IEvidenceStore store, IEvidenceScanner scanner, IOptions<EvidenceOptions> policy) =>
        {
            var permission = await Authorize(db, organizationId, http, true); if (permission != null) return permission;
            var application = await Application(db, organizationId, applicationId); if (application is null) return Results.NotFound();
            var precondition = Editable(application, http); if (precondition != null) return precondition;
            if (application.Documents.Count(x => x.DeletedAt == null) >= 20) return Problem(409, "Maximum of 20 evidence documents per application.");
            if (!http.Request.HasFormContentType) return Problem(400, "Use multipart/form-data with file and evidenceType.");
            if (http.Request.ContentLength > EvidenceOptions.MaxBytes + 1024 * 1024) return Problem(413, "File must not exceed 10 MiB.");
            IFormCollection form;
            try { form = await http.Request.ReadFormAsync(http.RequestAborted); }
            catch (InvalidDataException) { return Problem(400, "Invalid multipart upload."); }
            if (form.Files.Count != 1 || form.Files[0].Name != "file" || form["evidenceType"].Count != 1 || form["demoAcknowledged"].ToString() != "true" || form.Keys.Any(k => k is not ("evidenceType" or "demoAcknowledged"))) return Problem(400, "Supply exactly one file, evidenceType, and acknowledge fictional demo evidence.");
            var type = form["evidenceType"].ToString(); var upload = form.Files[0];
            if (!EvidenceOptions.EvidenceTypes.Contains(type)) return Problem(400, "Unknown evidence type.");
            if (upload.Length == 0 || upload.Length > EvidenceOptions.MaxBytes) return Problem(413, "Upload a nonempty file of at most 10 MiB.");
            using var input = upload.OpenReadStream(); using var buffer = new MemoryStream();
            await input.CopyToAsync(buffer, http.RequestAborted);
            if (buffer.Length > EvidenceOptions.MaxBytes) return Problem(413, "File must not exceed 10 MiB.");
            var bytes = buffer.ToArray(); var detected = EvidenceFileValidation.Detect(bytes);
            if (detected is null || detected != upload.ContentType) return Problem(400, "File content must match its PDF, JPEG, or PNG content type.");
            if (!await scanner.IsClean(bytes, http.RequestAborted)) return Problem(400, "Upload rejected by malware scanning.");
            var document = new VerificationDocument
            {
                ApplicationId = application.Id, EvidenceType = type, ContentType = detected,
                FileName = EvidenceFileValidation.SafeName(upload.FileName, detected), Size = bytes.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)), ObjectKey = "evidence/" + Guid.NewGuid().ToString("N"),
                ExpiresAt = DateTime.UtcNow.AddDays(policy.Value.DraftRetentionDays)
            };
            // Persist a cleanup reservation before writing bytes. A crash or stale save cannot strand an object.
            var cleanup = new EvidenceDeletion { ObjectKey = document.ObjectKey, DueAt = DateTime.UtcNow.AddHours(1) };
            db.EvidenceDeletions.Add(cleanup); await db.SaveChangesAsync();
            await store.Put(document.ObjectKey, bytes, http.RequestAborted);
            db.VerificationDocuments.Add(document);
            application.Documents.Add(document); application.Version = Guid.NewGuid();
            db.EvidenceDeletions.Remove(cleanup);
            Audit(db, organizationId, http, "verification.document-uploaded", document.Id);
            await db.SaveChangesAsync();
            http.Response.Headers.ETag = Tag(application.Version);
            return Results.Created($"/api/v1/organizations/{organizationId}/verifications/{applicationId}", DocumentSummary(document));
        });
        group.MapDelete(path + "/{applicationId:guid}/documents/{documentId:guid}", async (Guid organizationId, Guid applicationId, Guid documentId, HttpContext http, MarketplaceDbContext db) =>
        {
            var permission = await Authorize(db, organizationId, http, true); if (permission != null) return permission;
            var application = await Application(db, organizationId, applicationId); if (application is null) return Results.NotFound();
            var precondition = Editable(application, http); if (precondition != null) return precondition;
            var document = application.Documents.SingleOrDefault(x => x.Id == documentId && x.DeletedAt == null); if (document is null) return Results.NotFound();
            document.DeletedAt = DateTime.UtcNow; document.Version = Guid.NewGuid(); application.Version = Guid.NewGuid();
            db.EvidenceDeletions.Add(new EvidenceDeletion { ObjectKey = document.ObjectKey });
            Audit(db, organizationId, http, "verification.document-removed", document.Id);
            await db.SaveChangesAsync(); http.Response.Headers.ETag = Tag(application.Version);
            return Results.NoContent();
        });
        group.MapPost(path + "/{applicationId:guid}/submit", async (Guid organizationId, Guid applicationId, HttpContext http, MarketplaceDbContext db, IOptions<EvidenceOptions> policy) =>
        {
            var permission = await Authorize(db, organizationId, http, true); if (permission != null) return permission;
            var application = await Application(db, organizationId, applicationId); if (application is null) return Results.NotFound();
            var precondition = Editable(application, http); if (precondition != null) return precondition;
            var actor = await db.Users.SingleAsync(x => x.Id == UserId(http));
            if (!actor.EmailConfirmed) return Problem(403, "Verify your email before submitting evidence.");
            var profile = await db.ProviderProfiles.Include(x => x.Categories).SingleAsync(x => x.Id == application.ProviderId && x.OrganizationId == organizationId);
            var categoryIds = profile.Categories.Select(x => x.CategoryId).ToArray();
            if (string.IsNullOrWhiteSpace(profile.DisplayName) || string.IsNullOrWhiteSpace(profile.Biography) || string.IsNullOrWhiteSpace(profile.ServiceArea) || categoryIds.Length == 0 || await db.ServiceCategories.CountAsync(x => x.Active && categoryIds.Contains(x.Id)) != categoryIds.Length)
                return Problem(400, "Complete the provider profile and choose active categories before submitting.");
            var required = Required(profile, policy.Value);
            var documents = application.Documents.Where(x => x.DeletedAt == null && x.ExpiresAt > DateTime.UtcNow).ToArray();
            if (required.Any(type => !documents.Any(x => x.EvidenceType == type))) return Problem(400, "Upload current evidence for every required type before submitting.");
            application.Status = "Submitted"; application.SubmittedAt = DateTime.UtcNow; application.SubmissionVersion = 1; application.Version = Guid.NewGuid();
            application.SnapshotJson = JsonSerializer.Serialize(new { profile.Id, profile.DisplayName, profile.ProviderType, profile.Biography, profile.ServiceArea, categoryIds, profileVersion = profile.Version, requiredEvidence = required, documents = documents.Select(x => new { x.Id, x.EvidenceType, x.Sha256, x.ContentType, x.Size }) }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            foreach (var document in documents) { document.ExpiresAt = DateTime.UtcNow.AddDays(policy.Value.SubmittedRetentionDays); document.Version = Guid.NewGuid(); }
            // Detect profile edits racing submission; evidence and snapshot save in the same transaction.
            profile.Version = Guid.NewGuid();
            Audit(db, organizationId, http, "verification.submitted", application.Id);
            await db.SaveChangesAsync(); http.Response.Headers.ETag = Tag(application.Version);
            return Results.Ok(await Summary(application, db, policy.Value));
        });
        group.MapPost("/verification-documents/{documentId:guid}/access", async (Guid documentId, HttpContext http, MarketplaceDbContext db, IDataProtectionProvider protection, IOptions<EvidenceOptions> policy) =>
        {
            var document = await db.VerificationDocuments.SingleOrDefaultAsync(x => x.Id == documentId && x.DeletedAt == null && x.ExpiresAt > DateTime.UtcNow);
            if (document is null) return Results.NotFound();
            var application = await db.VerificationApplications.SingleAsync(x => x.Id == document.ApplicationId);
            var permission = await Authorize(db, application.OrganizationId, http, false); if (permission != null) return permission;
            var lifetime = TimeSpan.FromSeconds(policy.Value.AccessSeconds);
            var token = protection.CreateProtector("Marketplace.EvidenceAccess.v1").ToTimeLimitedDataProtector().Protect(document.Id + "|" + UserId(http) + "|" + http.User.FindFirstValue("session_id"), lifetime);
            Audit(db, application.OrganizationId, http, "verification.document-access-granted", document.Id); await db.SaveChangesAsync();
            return Results.Ok(new { url = "/api/v1/verification-documents/" + document.Id + "/content", token, expiresAt = DateTime.UtcNow.Add(lifetime) });
        });
        group.MapGet("/verification-documents/{documentId:guid}/content", async (Guid documentId, HttpContext http, MarketplaceDbContext db, IDataProtectionProvider protection, IEvidenceStore store) =>
        {
            var token = http.Request.Headers["X-Evidence-Access"].ToString(); if (token.Length is 0 or > 4096) return Results.NotFound();
            string grant;
            try { grant = protection.CreateProtector("Marketplace.EvidenceAccess.v1").ToTimeLimitedDataProtector().Unprotect(token); }
            catch (CryptographicException) { return Results.NotFound(); }
            if (grant != documentId + "|" + UserId(http) + "|" + http.User.FindFirstValue("session_id")) return Results.NotFound();
            var document = await db.VerificationDocuments.SingleOrDefaultAsync(x => x.Id == documentId && x.DeletedAt == null && x.ExpiresAt > DateTime.UtcNow); if (document is null) return Results.NotFound();
            var application = await db.VerificationApplications.SingleAsync(x => x.Id == document.ApplicationId);
            var permission = await Authorize(db, application.OrganizationId, http, false); if (permission != null) return permission;
            var bytes = await store.Get(document.ObjectKey, http.RequestAborted);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != document.Sha256) return Problem(503, "Evidence integrity check failed.");
            Audit(db, application.OrganizationId, http, "verification.document-downloaded", document.Id); await db.SaveChangesAsync();
            http.Response.Headers["X-Content-Type-Options"] = "nosniff"; http.Response.Headers["Content-Security-Policy"] = "sandbox";
            return Results.File(bytes, "application/octet-stream", document.FileName, enableRangeProcessing: false);
        });
    }
    private static string UserId(HttpContext http) => http.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static string Tag(Guid version) => "\"" + version.ToString("D") + "\"";
    private static IResult Problem(int status, string title) => Results.Problem(statusCode: status, title: title);
    private static async Task<IResult?> Authorize(MarketplaceDbContext db, Guid organizationId, HttpContext http, bool write)
    {
        var member = await db.OrganizationMemberships.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserId == UserId(http) && x.Active);
        return member is null ? Results.NotFound() : write && member.Role != "Owner" ? Results.Forbid() : null;
    }
    private static Task<VerificationApplication?> Application(MarketplaceDbContext db, Guid organizationId, Guid id) =>
        db.VerificationApplications.Include(x => x.Documents).SingleOrDefaultAsync(x => x.Id == id && x.OrganizationId == organizationId);
    private static IResult? Editable(VerificationApplication application, HttpContext http)
    {
        var expected = http.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(expected)) return Problem(428, "If-Match is required. Reload the application.");
        if (expected != Tag(application.Version)) return Problem(412, "The application changed. Reload before retrying.");
        if (application.Status != "Draft") return Problem(409, "Submitted evidence is immutable.");
        return null;
    }
    private static string[] Required(ProviderProfile profile, EvidenceOptions policy) =>
        (profile.ProviderType == "Business" ? policy.BusinessRequired : policy.IndividualRequired)
        .Concat(profile.Categories.Any(x => policy.TradeCategoryIds.Contains(x.CategoryId.ToString("D"))) ? [policy.TradeEvidence] : Array.Empty<string>()).Distinct().ToArray();
    private static object DocumentSummary(VerificationDocument document) => new { document.Id, document.EvidenceType, fileName = document.FileName, document.ContentType, document.Size, document.UploadedAt, document.ExpiresAt, scanStatus = "Clean", simulated = true };
    private static async Task<object> Summary(VerificationApplication application, MarketplaceDbContext db, EvidenceOptions policy)
    {
        var profile = await db.ProviderProfiles.Include(x => x.Categories).SingleAsync(x => x.Id == application.ProviderId);
        var snapshot = JsonSerializer.Deserialize<JsonElement>(application.SnapshotJson);
        var required = application.Status == "Draft" ? Required(profile, policy) : snapshot.GetProperty("requiredEvidence").EnumerateArray().Select(x => x.GetString()!).ToArray();
        return new { application.Id, application.ProviderId, application.OrganizationId, application.Status, application.SubmissionVersion, application.SubmittedAt, application.Version, documents = application.Documents.Where(x => x.DeletedAt == null && x.ExpiresAt > DateTime.UtcNow).OrderBy(x => x.UploadedAt).Select(DocumentSummary), requiredEvidence = required, decisions = Array.Empty<object>(), submittedSnapshot = application.Status == "Draft" ? (JsonElement?)null : snapshot, simulated = true };
    }
    private static void Audit(MarketplaceDbContext db, Guid organizationId, HttpContext http, string action, Guid resource) =>
        db.OrganizationAuditEvents.Add(new OrganizationAuditEvent { OrganizationId = organizationId, ActorId = UserId(http), Action = action, ResourceId = resource.ToString() });
}
