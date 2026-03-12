namespace RuleForge.Domain.Marketplace;

public sealed class MarketplaceListing
{
    public int MarketplaceListingId { get; set; }
    public int AssetType { get; set; }
    public int SourceEntityId { get; set; }
    public int? OwnerUserId { get; set; }
    public int OwnershipType { get; set; } = 1;
    public int State { get; set; } = 1;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? TagsJson { get; set; }
    public int? LatestVersionId { get; set; }
    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime DateModifiedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DateRemovedUtc { get; set; }
}
