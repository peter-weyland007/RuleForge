namespace RuleForge.Domain.Marketplace;

public sealed class MarketplaceImport
{
    public int MarketplaceImportId { get; set; }
    public int MarketplaceListingId { get; set; }
    public int MarketplaceListingVersionId { get; set; }
    public int ImportedByUserId { get; set; }
    public int AssetType { get; set; }
    public int NewEntityId { get; set; }
    public DateTime DateImportedUtc { get; set; } = DateTime.UtcNow;
}
