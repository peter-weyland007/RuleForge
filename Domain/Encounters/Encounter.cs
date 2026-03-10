namespace RuleForge.Domain.Encounters;

public sealed class Encounter
{
    public int EncounterId { get; set; }
    public int CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public EncounterType EncounterType { get; set; } = EncounterType.Combat;
    public string? Description { get; set; }
    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime DateModifiedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DateDeletedUtc { get; set; }
    public List<EncounterParticipant> Participants { get; set; } = new();
}
