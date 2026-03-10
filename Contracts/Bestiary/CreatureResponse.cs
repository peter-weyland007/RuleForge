namespace RuleForge.Contracts.Bestiary;

public sealed class CreatureResponse
{
    public int CreatureId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int? ArmorClass { get; set; }
    public int? HitPoints { get; set; }
    public int? InitiativeModifier { get; set; }
    public string? Speed { get; set; }
    public string? ChallengeRating { get; set; }
    public int? ExperiencePoints { get; set; }
    public int? PassivePerception { get; set; }

    public int? Strength { get; set; }
    public int? Dexterity { get; set; }
    public int? Constitution { get; set; }
    public int? Intelligence { get; set; }
    public int? Wisdom { get; set; }
    public int? Charisma { get; set; }
}
