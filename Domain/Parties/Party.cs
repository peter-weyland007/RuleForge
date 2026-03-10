namespace RuleForge.Domain.Parties;

public sealed class Party
{
    public int PartyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int CampaignId { get; set; }
    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime DateModifiedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DateDeletedUtc { get; set; }
}
