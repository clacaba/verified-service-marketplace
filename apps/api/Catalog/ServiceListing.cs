namespace Marketplace.Api.Catalog;

public sealed class ServiceListing
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrganizationId { get; set; }
    public Guid CategoryId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public long PriceMinor { get; set; }
    public string Currency { get; set; } = "PHP";
    public int DurationMinutes { get; set; }
    public string ServiceArea { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
