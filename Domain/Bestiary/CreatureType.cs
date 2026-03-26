namespace RuleForge.Domain.Bestiary;

public sealed class CreatureType
{
    public int CreatureTypeId { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<CreatureSubtype> Subtypes { get; set; } = new();
    public List<Creature> Creatures { get; set; } = new();
}
