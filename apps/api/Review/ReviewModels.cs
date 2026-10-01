namespace Marketplace.Api.Review;

public sealed class VerificationDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public string ReviewerId { get; set; } = "";
    public string Decision { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateTime DecidedAt { get; set; } = DateTime.UtcNow;
    public List<VerificationCheck> Checks { get; set; } = [];
}
public sealed class VerificationCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DecisionId { get; set; }
    public Guid OrganizationId { get; set; }
    public string Type { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool Simulated { get; set; } = true;
}
public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public Guid ApplicationId { get; set; }
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
public sealed class PlatformAccessAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public string Action { get; set; } = "";
    public string Source { get; set; } = "ServerOperator";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
