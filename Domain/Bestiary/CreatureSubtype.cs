namespace RuleForge.Domain.Bestiary;

public sealed class CreatureSubtype
{
    public int CreatureSubtypeId { get; set; }
    public int? CreatureTypeId { get; set; }
    public CreatureType? CreatureType { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<CreatureCreatureSubtype> CreatureLinks { get; set; } = new();
}
