using RuleForge.Domain.Characters;

namespace RuleForge.Contracts.Characters;

public sealed class CharacterSkillResponse
{
    public int SkillId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AbilityScoreType Ability { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsProficient { get; set; }
    public bool HasExpertise { get; set; }
    public int? BonusOverride { get; set; }
    public int AbilityModifier { get; set; }
    public int ProficiencyContribution { get; set; }
    public int TotalModifier { get; set; }
}
