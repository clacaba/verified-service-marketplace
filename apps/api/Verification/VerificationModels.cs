namespace Marketplace.Api.Verification;

public sealed class VerificationApplication
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid ProviderId { get; set; }
    public Guid? ParentApplicationId { get; set; }
    public int RevisionNumber { get; set; }
    public string? ReviewStartedBy { get; set; }
    public DateTime? ReviewStartedAt { get; set; }
    public DateTime? ReviewLeaseExpiresAt { get; set; }
    public List<Marketplace.Api.Review.VerificationDecision> Decisions { get; set; } = [];
    public string Status { get; set; } = "Draft";
    public bool IsActive { get; set; } = true;
    public Guid Version { get; set; } = Guid.NewGuid();
    public int SubmissionVersion { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public string SnapshotJson { get; set; } = "{}";
    public List<VerificationDocument> Documents { get; set; } = [];
}
public sealed class VerificationDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ApplicationId { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
    public string EvidenceType { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string ObjectKey { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
public sealed class EvidenceDeletion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ObjectKey { get; set; } = "";
    public DateTime DueAt { get; set; } = DateTime.UtcNow;
    public int Attempts { get; set; }
}
public sealed class EvidenceOptions
{
    public int DraftRetentionDays { get; set; } = 30;
    public int SubmittedRetentionDays { get; set; } = 90;
    public int AccessSeconds { get; set; } = 60;
    public string[] IndividualRequired { get; set; } = ["Identity"];
    public string[] BusinessRequired { get; set; } = ["Identity", "BusinessRegistration"];
    public string[] TradeCategoryIds { get; set; } = ["10000000-0000-0000-0000-000000000002", "10000000-0000-0000-0000-000000000003"];
    public string TradeEvidence { get; set; } = "TradeQualification";
    public const int MaxBytes = 10 * 1024 * 1024;
    public static readonly string[] EvidenceTypes = ["Identity", "BusinessRegistration", "TradeQualification"];
}
