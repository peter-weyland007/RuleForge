namespace RuleForge.Contracts.Characters;

public sealed class UpsertCharacterSkillRequest
{
    public int SkillId { get; set; }
    public bool IsProficient { get; set; }
    public bool HasExpertise { get; set; }
    public int? BonusOverride { get; set; }
}
