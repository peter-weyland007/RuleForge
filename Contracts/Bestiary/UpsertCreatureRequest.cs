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
    public int? WalkSpeed { get; set; }
    public int? FlySpeed { get; set; }
    public int? SwimSpeed { get; set; }
    public int? ClimbSpeed { get; set; }
    public int? BurrowSpeed { get; set; }

    [StringLength(32)]
    public string? ChallengeRating { get; set; }

    public int? ExperiencePoints { get; set; }
    public int? PassivePerception { get; set; }
    public int? BlindsightRange { get; set; }
    public int? DarkvisionRange { get; set; }
    public int? TremorsenseRange { get; set; }
    public int? TruesightRange { get; set; }

    [StringLength(250)]
    public string? OtherSenses { get; set; }

    [StringLength(250)]
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
    public bool IsSystem { get; set; }
    public int? OwnerAppUserId { get; set; }
}
