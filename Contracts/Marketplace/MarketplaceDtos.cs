namespace RuleForge.Contracts.Marketplace;

public enum MarketplaceAssetType { Item = 1, Creature = 2 }
public enum MarketplaceOwnershipType { Creator = 1, Community = 2 }
public enum MarketplaceListingState { PublicCreatorOwned = 1, Removed = 2, PublicCommunityOwned = 3 }

public sealed class PublishMarketplaceRequest
{
    public int AssetType { get; set; }
    public int SourceEntityId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string[]? Tags { get; set; }
}

public class MarketplaceListingDto
{
    public int MarketplaceListingId { get; set; }
    public int AssetType { get; set; }
    public int SourceEntityId { get; set; }
    public int? OwnerUserId { get; set; }
    public int OwnershipType { get; set; }
    public int State { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? TagsJson { get; set; }
    public int? LatestVersionId { get; set; }
    public DateTime DateCreatedUtc { get; set; }
    public DateTime DateModifiedUtc { get; set; }
}

public sealed class MarketplaceListingDetailDto : MarketplaceListingDto
{
    public string PayloadJson { get; set; } = "{}";
    public string? VersionLabel { get; set; }
}

public sealed class MarketplaceImportDto
{
    public int MarketplaceImportId { get; set; }
    public int MarketplaceListingId { get; set; }
    public int MarketplaceListingVersionId { get; set; }
    public int ImportedByUserId { get; set; }
    public int AssetType { get; set; }
    public int NewEntityId { get; set; }
    public DateTime DateImportedUtc { get; set; }
}
