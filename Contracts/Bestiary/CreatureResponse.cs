namespace RuleForge.Contracts.Bestiary;

public sealed class CreatureResponse
{
    public int CreatureId { get; set; }
    public bool IsSystem { get; set; }
    public int? OwnerAppUserId { get; set; }
    public string? OwnerUsername { get; set; }
    public bool OwnerIsAdmin { get; set; }
    public bool UserCanEdit { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Size { get; set; }
    public string? CreatureType { get; set; }
    public string? CreatureSubtype { get; set; }
    public int? CreatureTypeId { get; set; }
    public List<int> CreatureSubtypeIds { get; set; } = new();
    public List<string> CreatureSubtypes { get; set; } = new();

    public int? ArmorClass { get; set; }
    public int? HitPoints { get; set; }
    public int? InitiativeModifier { get; set; }
    public string? Speed { get; set; }
    public int? WalkSpeed { get; set; }
    public int? FlySpeed { get; set; }
    public int? SwimSpeed { get; set; }
    public int? ClimbSpeed { get; set; }
    public int? BurrowSpeed { get; set; }
    public string? ChallengeRating { get; set; }
    public int? ExperiencePoints { get; set; }
    public int? PassivePerception { get; set; }
    public int? BlindsightRange { get; set; }
    public int? DarkvisionRange { get; set; }
    public int? TremorsenseRange { get; set; }
    public int? TruesightRange { get; set; }
    public string? OtherSenses { get; set; }
    public string? Languages { get; set; }
    public bool UnderstandsButCannotSpeak { get; set; }
    public List<CreatureEntryDto> Traits { get; set; } = new();
    public List<CreatureEntryDto> Actions { get; set; } = new();

    public int? Strength { get; set; }
    public int? Dexterity { get; set; }
    public int? Constitution { get; set; }
    public int? Intelligence { get; set; }
    public int? Wisdom { get; set; }
    public int? Charisma { get; set; }
}
