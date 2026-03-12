namespace RuleForge.Domain.Marketplace;

public sealed class MarketplaceListingVersion
{
    public int MarketplaceListingVersionId { get; set; }
    public int MarketplaceListingId { get; set; }
    public string VersionLabel { get; set; } = "1";
    public string PayloadJson { get; set; } = "{}";
    public string? Changelog { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
}
