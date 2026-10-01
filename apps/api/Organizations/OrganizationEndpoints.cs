using Marketplace.Api.Providers;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Marketplace.Api.Data;
using Marketplace.Api.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Organizations;

public static class OrganizationEndpoints
{
    public sealed record CreateRequest(string Name, string ProviderType);
    public sealed record InvitationRequest(string Email, string Role);
    public sealed record AcceptRequest(string Token);

    public static void MapMarketplaceOrganizations(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization();
        group.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            if (!HttpMethods.IsGet(context.HttpContext.Request.Method))
            {
                try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
                catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 400, title: "Refresh the page and try again.", detail: "Invalid CSRF token."); }
            }
            return await next(context);
        });

        group.MapPost("/organizations", async (CreateRequest input, HttpContext context, MarketplaceDbContext db, UserManager<Account> users) =>
        {
            var userId = UserId(context);
            var account = await users.FindByIdAsync(userId);
            if (account is null || (account.AccountType != "Provider" && !await db.OrganizationMemberships.AnyAsync(x => x.UserId == userId && x.Active))) return Results.Forbid();
            if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 150 || input.ProviderType is not ("Individual" or "Business"))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["organization"] = ["Enter a name of 1–150 characters and choose Individual or Business."] });
            var organization = new Organization { Name = input.Name.Trim(), ProviderType = input.ProviderType };
            db.Organizations.Add(organization);
            db.ProviderProfiles.Add(new ProviderProfile { OrganizationId = organization.Id, DisplayName = organization.Name, ProviderType = organization.ProviderType });
            db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = organization.Id, UserId = userId, Role = "Owner" });
            Audit(db, organization.Id, userId, "organization.created", organization.Id.ToString());
            await db.SaveChangesAsync();
            return Results.Created("/api/v1/organizations/" + organization.Id, Summary(organization, "Owner"));
        });

        group.MapGet("/organizations/{organizationId:guid}", async (Guid organizationId, HttpContext context, MarketplaceDbContext db) =>
        {
            var membership = await Membership(db, organizationId, UserId(context));
            if (membership is null) return Results.NotFound();
            var organization = await db.Organizations.SingleAsync(x => x.Id == organizationId);
            return Results.Ok(Summary(organization, membership.Role));
        });

        group.MapGet("/organizations/{organizationId:guid}/members", async (Guid organizationId, int? page, int? pageSize, HttpContext context, MarketplaceDbContext db) =>
        {
            var membership = await Membership(db, organizationId, UserId(context));
            if (membership is null) return Results.NotFound();
            if (membership.Role != "Owner") return Results.Forbid();
            var currentPage = page ?? 1;
            var size = pageSize ?? 20;
            if (currentPage < 1 || currentPage > 100000 || size < 1 || size > 100) return Results.Problem(statusCode: 400, title: "Invalid pagination.");
            var totalCount = await db.OrganizationMemberships.CountAsync(x => x.OrganizationId == organizationId);
            var members = await (from member in db.OrganizationMemberships
                                 join user in db.Users on member.UserId equals user.Id
                                 where member.OrganizationId == organizationId
                                 orderby member.CreatedAt, member.UserId
                                 select new { userId = member.UserId, name = user.DisplayName, email = user.Email, role = member.Role, active = member.Active }).Skip((currentPage - 1) * size).Take(size).ToListAsync();
            return Results.Ok(new { items = members, page = currentPage, pageSize = size, totalCount });
        });

        group.MapPost("/organizations/{organizationId:guid}/invitations", async (Guid organizationId, InvitationRequest input, HttpContext context, MarketplaceDbContext db, UserManager<Account> users, IDataProtectionProvider protection, IConfiguration config) =>
        {
            var actorId = UserId(context);
            var membership = await Membership(db, organizationId, actorId);
            if (membership is null) return Results.NotFound();
            if (membership.Role != "Owner") return Results.Forbid();
            if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 254 || !new EmailAddressAttribute().IsValid(input.Email) || input.Role != "Staff")
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["invitation"] = ["Enter a valid email and the Staff role."] });
            var email = input.Email.Trim();
            var existing = await users.FindByEmailAsync(email);
            if (existing != null && await db.OrganizationMemberships.AnyAsync(x => x.OrganizationId == organizationId && x.UserId == existing.Id && x.Active))
                return Results.Problem(statusCode: 409, title: "This person is already an active member.");
            var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            var invitation = new OrganizationInvitation { OrganizationId = organizationId, Email = email, NormalizedEmail = users.NormalizeEmail(email)!, TokenHash = Hash(token) };
            var organization = await db.Organizations.SingleAsync(x => x.Id == organizationId);
            var origin = config["Web:PublicOrigin"] ?? "http://localhost:3000";
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !uri.IsLoopback)) throw new InvalidOperationException("Configure a trusted Web:PublicOrigin.");
            var link = new Uri(uri, "/invitations/accept") + "?token=" + token;
            db.OrganizationInvitations.Add(invitation);
            db.VerificationEmails.Add(new VerificationEmail
            {
                Recipient = email,
                Subject = "You are invited to a marketplace organization",
                ProtectedBody = protection.CreateProtector(VerificationEmailWorker.Purpose).Protect("You are invited to " + organization.Name + ". Register or sign in using this email, verify it, and accept within seven days:\n" + link)
            });
            Audit(db, organizationId, actorId, "member.invited", invitation.Id.ToString());
            await db.SaveChangesAsync();
            return Results.Created("/api/v1/organizations/" + organizationId, new { id = invitation.Id, email, role = "Staff", expiresAt = invitation.ExpiresAt });
        }).RequireRateLimiting("auth");

        group.MapPost("/invitations/accept", async (AcceptRequest input, HttpContext context, MarketplaceDbContext db, UserManager<Account> users) =>
        {
            if (string.IsNullOrWhiteSpace(input.Token) || input.Token.Length > 256) return InvalidInvitation();
            var account = await users.FindByIdAsync(UserId(context));
            if (account is null || !account.EmailConfirmed) return Results.Problem(statusCode: 403, title: "Verify the invited email before accepting.");
            var tokenHash = Hash(input.Token);
            await using var transaction = await db.Database.BeginTransactionAsync();
            var invitation = await db.OrganizationInvitations.SingleOrDefaultAsync(x => x.TokenHash == tokenHash);
            if (invitation is null || invitation.AcceptedAt != null || invitation.ExpiresAt <= DateTime.UtcNow || invitation.NormalizedEmail != account.NormalizedEmail) return InvalidInvitation();
            var member = await db.OrganizationMemberships.SingleOrDefaultAsync(x => x.OrganizationId == invitation.OrganizationId && x.UserId == account.Id);
            if (member?.Active == true) return Results.Problem(statusCode: 409, title: "You are already an active member.");
            if (member is null)
                db.OrganizationMemberships.Add(new OrganizationMembership { OrganizationId = invitation.OrganizationId, UserId = account.Id, Role = "Staff" });
            else { member.Active = true; member.Role = "Staff"; }
            invitation.AcceptedAt = DateTime.UtcNow;
            invitation.Version = Guid.NewGuid();
            Audit(db, invitation.OrganizationId, account.Id, "member.joined", account.Id);
            try { await db.SaveChangesAsync(); await transaction.CommitAsync(); }
            catch (DbUpdateException) { return Results.Problem(statusCode: 409, title: "Invitation changed. Refresh before retrying."); }
            return Results.NoContent();
        }).RequireRateLimiting("auth");

        group.MapDelete("/organizations/{organizationId:guid}/members/{userId}", async (Guid organizationId, string userId, HttpContext context, MarketplaceDbContext db) =>
        {
            var actor = await Membership(db, organizationId, UserId(context));
            if (actor is null) return Results.NotFound();
            if (actor.Role != "Owner") return Results.Forbid();
            var member = await Membership(db, organizationId, userId);
            if (member is null) return Results.NotFound();
            if (member.Role == "Owner") return Results.Problem(statusCode: 409, title: "The owner cannot be removed. Ownership transfer is not available yet.");
            member.Active = false;
            var removedUser = await db.Users.SingleAsync(x => x.Id == userId);
            var outstanding = await db.OrganizationInvitations.Where(x => x.OrganizationId == organizationId && x.NormalizedEmail == removedUser.NormalizedEmail && x.AcceptedAt == null).ToListAsync();
            foreach (var invitation in outstanding) { invitation.ExpiresAt = DateTime.UtcNow; invitation.Version = Guid.NewGuid(); }
            Audit(db, organizationId, UserId(context), "member.deactivated", userId);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    public static async Task<object> UserSummary(Account account, MarketplaceDbContext db)
    {
        var memberships = await (from member in db.OrganizationMemberships
                                 join organization in db.Organizations on member.OrganizationId equals organization.Id
                                 where member.UserId == account.Id && member.Active
                                 orderby organization.Name, organization.Id
                                 select new { member.OrganizationId, organization.Name, member.Role, member.Active }).ToListAsync();
        var capabilities = new HashSet<string> { "Customer", account.AccountType };
        if (account.IsReviewer && account.EmailConfirmed) capabilities.Add("Reviewer");
        if (memberships.Count != 0) capabilities.Add("Provider");
        return new
        {
            id = account.Id,
            name = account.DisplayName,
            email = account.Email,
            emailConfirmed = account.EmailConfirmed,
            capabilities = capabilities.ToArray(),
            memberships = memberships.Select(m => new { organizationId = m.OrganizationId, name = m.Name, role = m.Role, active = m.Active, permissions = OrganizationPermissions.For(m.Role) })
        };
    }

    private static string UserId(HttpContext context) => context.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private static Task<OrganizationMembership?> Membership(MarketplaceDbContext db, Guid organizationId, string userId) =>
        db.OrganizationMemberships.SingleOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserId == userId && x.Active);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static IResult InvalidInvitation() => Results.Problem(statusCode: 400, title: "Invitation is invalid, expired, already used, or belongs to another email.");
    private static object Summary(Organization organization, string role) => new { id = organization.Id, name = organization.Name, providerType = organization.ProviderType, permissions = OrganizationPermissions.For(role) };
    private static void Audit(MarketplaceDbContext db, Guid organizationId, string actorId, string action, string resourceId) =>
        db.OrganizationAuditEvents.Add(new OrganizationAuditEvent { OrganizationId = organizationId, ActorId = actorId, Action = action, ResourceId = resourceId });
}
