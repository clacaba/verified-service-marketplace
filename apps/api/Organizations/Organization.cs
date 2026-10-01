namespace Marketplace.Api.Organizations;

public sealed class Organization
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string ProviderType { get; set; } = "Individual";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class OrganizationMembership
{
    public Guid OrganizationId { get; set; }
    public string UserId { get; set; } = "";
    public string Role { get; set; } = "Staff";
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
public sealed class OrganizationInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string NormalizedEmail { get; set; } = "";
    public string Email { get; set; } = "";
    public string Role { get; set; } = "Staff";
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);
    public DateTime? AcceptedAt { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
public sealed class OrganizationAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string ActorId { get; set; } = "";
    public string Action { get; set; } = "";
    public string ResourceId { get; set; } = "";
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
public static class OrganizationPermissions
{
    public static string[] For(string role) => role == "Owner"
        ? ["organization:read", "members:manage", "profile:read", "profile:write", "verification:read", "verification:write", "bookings:read", "bookings:manage"]
        : ["organization:read", "profile:read", "verification:read", "bookings:read"];
}
