namespace RuleForge.Domain.Characters;

public sealed class Skill
{
    public int SkillId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public AbilityScoreType Ability { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<CharacterSkill> CharacterSkills { get; set; } = new List<CharacterSkill>();
}
