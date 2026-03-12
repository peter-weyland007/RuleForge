using RuleForge.Domain.Common;

namespace RuleForge.Domain.Bestiary;

public sealed class CreatureShare
{
    public int CreatureShareId { get; set; }
    public int CreatureId { get; set; }
    public int SharedWithUserId { get; set; }
    public SharePermission Permission { get; set; } = SharePermission.View;
    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
}
