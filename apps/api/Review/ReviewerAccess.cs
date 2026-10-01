using System.Security.Claims;
using Marketplace.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Api.Review;

public static class ReviewerAccess
{
    public static Task<bool> IsReviewer(MarketplaceDbContext db, HttpContext http)
    {
        var id = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        return db.Users.AnyAsync(x => x.Id == id && x.IsReviewer && x.EmailConfirmed);
    }
    public static async Task<bool> CanReview(MarketplaceDbContext db, Guid organizationId, HttpContext http) =>
        await IsReviewer(db, http) && !await db.OrganizationMemberships.AnyAsync(x => x.OrganizationId == organizationId && x.UserId == http.User.FindFirstValue(ClaimTypes.NameIdentifier));

    public static async Task<int> Provision(IServiceProvider services, string email, bool grant)
    {
        using var scope = services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MarketplaceDbContext>();
        var normalized = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Marketplace.Api.Identity.Account>>().NormalizeEmail(email);
        var account = await db.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized);
        if (account is null || (grant && !account.EmailConfirmed))
        {
            Console.Error.WriteLine("Use an existing verified account. No account or password was created.");
            return 1;
        }
        account.IsReviewer = grant;
        db.PlatformAccessAudits.Add(new PlatformAccessAudit { UserId = account.Id, Action = grant ? "reviewer.granted" : "reviewer.revoked" });
        await db.SaveChangesAsync();
        Console.WriteLine(grant ? "Reviewer access granted and audited." : "Reviewer access revoked and audited.");
        return 0;
    }
}
