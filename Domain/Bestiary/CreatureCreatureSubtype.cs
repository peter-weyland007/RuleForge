namespace RuleForge.Domain.Bestiary;

public sealed class CreatureCreatureSubtype
{
    public int CreatureCreatureSubtypeId { get; set; }
    public int CreatureId { get; set; }
    public Creature? Creature { get; set; }
    public int CreatureSubtypeId { get; set; }
    public CreatureSubtype? CreatureSubtype { get; set; }
    public int SortOrder { get; set; }
}
