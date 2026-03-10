namespace RuleForge.Domain.Encounters;

public sealed class EncounterParticipant
{
    public int EncounterParticipantId { get; set; }
    public int EncounterId { get; set; }
    public EncounterParticipantType ParticipantType { get; set; }
    public int SourceId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public int? ArmorClassSnapshot { get; set; }
    public int? HitPointsCurrent { get; set; }
    public int? InitiativeModifierSnapshot { get; set; }
    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
    public Encounter? Encounter { get; set; }
}
