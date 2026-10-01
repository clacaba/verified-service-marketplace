using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Marketplace.Api.Data;
using Marketplace.Api.Organizations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
namespace Marketplace.Api.Identity;

public static class AuthEndpoints
{
    public sealed record RegisterRequest(string Name, string Email, string Password, string AccountType);
    public sealed record LoginRequest(string Email, string Password);
    public sealed record EmailRequest(string Email);
    public sealed record VerifyRequest(string UserId, string Token);
    public static void MapMarketplaceAuth(this WebApplication app)
    {
        var auth = app.MapGroup("/api/v1/auth").RequireRateLimiting("auth");
        auth.AddEndpointFilter(async (c, next) => { if (HttpMethods.IsPost(c.HttpContext.Request.Method)) { try { await c.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(c.HttpContext); } catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 400, title: "Refresh the page and try again.", detail: "Invalid CSRF token."); } } c.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(c); });
        auth.MapGet("/csrf", (HttpContext c, IAntiforgery antiforgery) => Results.Ok(new { token = antiforgery.GetAndStoreTokens(c).RequestToken }));
        auth.MapPost("/register", async (RegisterRequest input, UserManager<Account> users, MarketplaceDbContext db, IDataProtectionProvider protection, IConfiguration config) =>
        {
            var errors = new Dictionary<string, string[]>(); if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 150) errors["name"] = ["Enter a name of 1–150 characters."]; if (!ValidEmail(input.Email)) errors["email"] = ["Enter a valid email address."]; if (input.AccountType is not ("Customer" or "Provider")) errors["accountType"] = ["Choose Customer or Provider."]; if (string.IsNullOrEmpty(input.Password) || input.Password.Length > 128) errors["password"] = ["Enter a password no longer than 128 characters."]; if (errors.Count > 0) return Results.ValidationProblem(errors);
            var account = new Account { UserName = input.Email.Trim(), Email = input.Email.Trim(), DisplayName = input.Name.Trim(), AccountType = input.AccountType };
            var passwordResult = await users.PasswordValidators[0].ValidateAsync(users, account, input.Password); if (!passwordResult.Succeeded) return Results.ValidationProblem(new Dictionary<string, string[]> { ["password"] = passwordResult.Errors.Select(x => x.Description).ToArray() });
            if (await users.FindByEmailAsync(account.Email) != null) return Results.Accepted();
            await using var transaction = await db.Database.BeginTransactionAsync(); var result = await users.CreateAsync(account, input.Password); if (!result.Succeeded) { if (result.Errors.Any(x => x.Code.StartsWith("Duplicate", StringComparison.Ordinal))) return Results.Accepted(); return Results.ValidationProblem(new Dictionary<string, string[]> { ["account"] = result.Errors.Select(x => x.Description).ToArray() }); }
            await QueueEmail(account, users, db, protection, config); await transaction.CommitAsync(); return Results.Accepted();
        });
        auth.MapPost("/resend-verification", async (EmailRequest input, UserManager<Account> users, MarketplaceDbContext db, IDataProtectionProvider protection, IConfiguration config) => { if (!ValidEmail(input.Email)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = ["Enter a valid email address."] }); var account = await users.FindByEmailAsync(input.Email.Trim()); if (account != null && !account.EmailConfirmed) await QueueEmail(account, users, db, protection, config); return Results.Accepted(); });
        auth.MapPost("/verify-email", async (VerifyRequest input, UserManager<Account> users) => { var account = string.IsNullOrWhiteSpace(input.UserId) ? null : await users.FindByIdAsync(input.UserId); if (account is null || account.EmailConfirmed || string.IsNullOrEmpty(input.Token)) return Results.Problem(statusCode: 400, title: "Verification link is invalid or expired."); string token; try { token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(input.Token)); } catch (FormatException) { return Results.Problem(statusCode: 400, title: "Verification link is invalid or expired."); } var result = await users.ConfirmEmailAsync(account, token); if (!result.Succeeded) return Results.Problem(statusCode: 400, title: "Verification link is invalid or expired."); await users.UpdateSecurityStampAsync(account); return Results.NoContent(); });
        auth.MapPost("/login", async (LoginRequest input, UserManager<Account> users, MarketplaceDbContext db, HttpContext c) => { if (!ValidEmail(input.Email) || string.IsNullOrEmpty(input.Password) || input.Password.Length > 128) return InvalidLogin(); var account = await users.FindByEmailAsync(input.Email.Trim()); if (account is null) { _ = new PasswordHasher<Account>().HashPassword(new Account(), input.Password); return InvalidLogin(); } if (await users.IsLockedOutAsync(account)) return InvalidLogin(); if (!await users.CheckPasswordAsync(account, input.Password)) { await users.AccessFailedAsync(account); return InvalidLogin(); } await users.ResetAccessFailedCountAsync(account); var session = new AccountSession { UserId = account.Id, ExpiresAt = DateTime.UtcNow.AddHours(8) }; db.AccountSessions.Add(session); await db.SaveChangesAsync(); var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, account.Id), new Claim(ClaimTypes.Name, account.DisplayName), new Claim("session_id", session.Id.ToString())], IdentitySetup.Scheme)); await c.SignInAsync(IdentitySetup.Scheme, principal, new AuthenticationProperties { ExpiresUtc = new DateTimeOffset(session.ExpiresAt), IsPersistent = false }); return Results.Ok(await OrganizationEndpoints.UserSummary(account, db)); });
        auth.MapPost("/logout", async (HttpContext c, MarketplaceDbContext db) => { if (Guid.TryParse(c.User.FindFirstValue("session_id"), out var id)) { var session = await db.AccountSessions.SingleOrDefaultAsync(x => x.Id == id); if (session != null) { session.RevokedAt = DateTime.UtcNow; await db.SaveChangesAsync(); } } await c.SignOutAsync(IdentitySetup.Scheme); return Results.NoContent(); }).RequireAuthorization();
        app.MapGet("/api/v1/users/me", async (HttpContext c, UserManager<Account> users, MarketplaceDbContext db) => { c.Response.Headers.CacheControl = "no-store"; var account = await users.FindByIdAsync(c.User.FindFirstValue(ClaimTypes.NameIdentifier)!); return account is null ? Results.Unauthorized() : Results.Ok(await OrganizationEndpoints.UserSummary(account, db)); }).RequireAuthorization();
    }
    private static IResult InvalidLogin() => Results.Problem(statusCode: 401, title: "Invalid email or password.");
    private static bool ValidEmail(string? email) => !string.IsNullOrWhiteSpace(email) && email.Length <= 254 && new EmailAddressAttribute().IsValid(email);
    private static async Task QueueEmail(Account a, UserManager<Account> users, MarketplaceDbContext db, IDataProtectionProvider protection, IConfiguration config) { var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(await users.GenerateEmailConfirmationTokenAsync(a))); var baseUrl = config["Web:PublicOrigin"] ?? "http://localhost:3000"; if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var origin) || (origin.Scheme != "https" && !origin.IsLoopback)) throw new InvalidOperationException("Configure a trusted Web:PublicOrigin."); var link = new Uri(origin, "/verify-email") + "?userId=" + Uri.EscapeDataString(a.Id) + "&token=" + token; db.VerificationEmails.Add(new VerificationEmail { Recipient = a.Email!, ProtectedBody = protection.CreateProtector(VerificationEmailWorker.Purpose).Protect("Verify your email by opening this link within two hours:\n" + link) }); await db.SaveChangesAsync(); }
}
