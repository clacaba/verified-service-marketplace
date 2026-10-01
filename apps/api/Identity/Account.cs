using Microsoft.AspNetCore.Identity;
namespace Marketplace.Api.Identity;

public sealed class Account : IdentityUser { public string DisplayName { get; set; } = ""; public string AccountType { get; set; } = "Customer"; }
public sealed class AccountSession { public Guid Id { get; set; } = Guid.NewGuid(); public string UserId { get; set; } = ""; public DateTime ExpiresAt { get; set; } public DateTime? RevokedAt { get; set; } }
public sealed class VerificationEmail
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string Recipient { get; set; } = ""; public string Subject { get; set; } = "Verify your marketplace email";
    public string ProtectedBody { get; set; } = ""; public DateTime CreatedAt { get; set; } = DateTime.UtcNow; public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow; public DateTime? SentAt { get; set; }
    public int Attempts { get; set; }
}
