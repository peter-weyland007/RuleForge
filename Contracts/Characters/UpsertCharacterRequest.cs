using System.ComponentModel.DataAnnotations;
using RuleForge.Domain.Characters;

namespace RuleForge.Contracts.Characters;

public sealed class UpsertCharacterRequest
{
    public int? CampaignId { get; set; }
    public int? PartyId { get; set; }

    [Required]
    public CharacterType CharacterType { get; set; } = CharacterType.PC;

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    public int? OwnerAppUserId { get; set; }
    public string? PlayerName { get; set; }

    public int? ArmorClass { get; set; }
    public int? HitPointsCurrent { get; set; }
    public int? HitPointsMax { get; set; }
    public int? TempHitPoints { get; set; }
    public int? InitiativeModifier { get; set; }
    public string? Speed { get; set; }

    public int? Strength { get; set; }
    public int? Dexterity { get; set; }
    public int? Constitution { get; set; }
    public int? Intelligence { get; set; }
    public int? Wisdom { get; set; }
    public int? Charisma { get; set; }
    public int? ProficiencyBonus { get; set; }

    public int? Level { get; set; }
    public string? ClassName { get; set; }
    public string? SubclassName { get; set; }
    public string? RaceName { get; set; }
    public string? SubraceName { get; set; }
    public int? PassivePerception { get; set; }

    public string? Conditions { get; set; }
    public string? Notes { get; set; }
}
