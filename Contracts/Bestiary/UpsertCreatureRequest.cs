using System.ComponentModel.DataAnnotations;

namespace RuleForge.Contracts.Bestiary;

public sealed class UpsertCreatureRequest
{
    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    public int? ArmorClass { get; set; }
    public int? HitPoints { get; set; }
    public int? InitiativeModifier { get; set; }

    [StringLength(80)]
    public string? Speed { get; set; }

    [StringLength(32)]
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
