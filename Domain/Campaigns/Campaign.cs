namespace RuleForge.Domain.Campaigns;

public sealed class Campaign
{
    public int CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? OwnerAppUserId { get; set; }
    public string? Description { get; set; }
    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime DateModifiedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DateDeletedUtc { get; set; }
}
