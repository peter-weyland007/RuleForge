namespace RuleForge.Domain.Bestiary;

public sealed class Creature
{
    public int CreatureId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public bool IsPublic { get; set; }
    public int? OwnerAppUserId { get; set; }

    public string? Size { get; set; }
    public string? CreatureType { get; set; }
    public string? CreatureSubtype { get; set; }
    public int? CreatureTypeId { get; set; }
    public CreatureType? Type { get; set; }
    public List<CreatureCreatureSubtype> CreatureSubtypeLinks { get; set; } = new();

    public int? ArmorClass { get; set; }
    public string? ArmorClassNotes { get; set; }
    public int? HitPoints { get; set; }
    public string? HitDice { get; set; }
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
    public string? Traits { get; set; }
    public string? Actions { get; set; }
    public string? BonusActions { get; set; }
    public string? Reactions { get; set; }
    public List<CreatureTrait> TraitList { get; set; } = new();
    public List<CreatureAction> ActionList { get; set; } = new();
    public List<CreatureBonusAction> BonusActionList { get; set; } = new();
    public List<CreatureReaction> ReactionList { get; set; } = new();

    public int? Strength { get; set; }
    public int? Dexterity { get; set; }
    public int? Constitution { get; set; }
    public int? Intelligence { get; set; }
    public int? Wisdom { get; set; }
    public int? Charisma { get; set; }

    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime DateModifiedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DateDeletedUtc { get; set; }
}
