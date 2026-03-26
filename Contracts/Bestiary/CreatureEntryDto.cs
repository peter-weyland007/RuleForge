using System.ComponentModel.DataAnnotations;

namespace RuleForge.Contracts.Bestiary;

public sealed class CreatureEntryDto
{
    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Description { get; set; }

    public int SortOrder { get; set; }
}
