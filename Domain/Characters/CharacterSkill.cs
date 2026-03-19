namespace RuleForge.Domain.Characters;

public sealed class CharacterSkill
{
    public int CharacterSkillId { get; set; }
    public int CharacterId { get; set; }
    public int SkillId { get; set; }
    public bool IsProficient { get; set; }
    public bool HasExpertise { get; set; }
    public int? BonusOverride { get; set; }

    public Character? Character { get; set; }
    public Skill? Skill { get; set; }
}
