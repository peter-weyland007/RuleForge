namespace RuleForge.Contracts.Encounters;

public sealed class EncounterResponse
{
    public int EncounterId { get; set; }
    public int? CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int EncounterType { get; set; }
    public string? Description { get; set; }
    public List<EncounterParticipantResponse> Participants { get; set; } = new();
}

public sealed class EncounterParticipantResponse
{
    public int EncounterParticipantId { get; set; }
    public int ParticipantType { get; set; }
    public int SourceId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public int? ArmorClassSnapshot { get; set; }
    public int? HitPointsCurrent { get; set; }
    public int? InitiativeModifierSnapshot { get; set; }
}

public sealed class UpsertEncounterRequest
{
    public int? CampaignId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int EncounterType { get; set; } = 1;
    public string? Description { get; set; }
    public List<UpsertEncounterParticipantRequest> Participants { get; set; } = new();
}

public sealed class UpsertEncounterParticipantRequest
{
    public int ParticipantType { get; set; }
    public int SourceId { get; set; }
    public string NameSnapshot { get; set; } = string.Empty;
    public int? ArmorClassSnapshot { get; set; }
    public int? HitPointsCurrent { get; set; }
    public int? InitiativeModifierSnapshot { get; set; }
}

public sealed class EncounterOptionsResponse
{
    public List<EncounterOptionItem> Characters { get; set; } = new();
    public List<EncounterOptionItem> Creatures { get; set; } = new();
    public List<EncounterOptionItem> Campaigns { get; set; } = new();
    public List<EncounterOptionItem> Parties { get; set; } = new();
}

public sealed class EncounterOptionItem
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? ArmorClass { get; set; }
    public int? HitPoints { get; set; }
    public int? InitiativeModifier { get; set; }
    public int? ParticipantType { get; set; }
    public int? PartyId { get; set; }
}
