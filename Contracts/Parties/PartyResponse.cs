namespace RuleForge.Contracts.Parties;

public sealed class PartyResponse
{
    public int PartyId { get; set; }
    public int? OwnerAppUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? CampaignId { get; set; }
    public int MemberCount { get; set; }
}
