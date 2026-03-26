namespace RuleForge.Domain.Bestiary;

public sealed class CreatureBonusAction
{
    public int CreatureBonusActionId { get; set; }
    public int CreatureId { get; set; }
    public Creature? Creature { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}
