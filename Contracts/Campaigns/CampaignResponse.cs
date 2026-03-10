namespace RuleForge.Contracts.Campaigns;

public sealed class CampaignResponse
{
    public int CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
