namespace Marketplace.Api.Providers;

public sealed class ProviderProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public string DisplayName { get; set; } = "";
    public string ProviderType { get; set; } = "Individual";
    public string Biography { get; set; } = "";
    public string ServiceArea { get; set; } = "";
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime? PublishedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<ProviderCategory> Categories { get; set; } = [];
}
public sealed class ServiceCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; } = true;
}
public sealed class ProviderCategory
{
    public Guid ProfileId { get; set; }
    public Guid CategoryId { get; set; }
}
