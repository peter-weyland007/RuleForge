using System.ComponentModel.DataAnnotations;

namespace RuleForge.Contracts.Parties;

public sealed class UpsertPartyRequest
{
    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;
    [StringLength(2000)]
    public string? Description { get; set; }
    public int? CampaignId { get; set; }
}
