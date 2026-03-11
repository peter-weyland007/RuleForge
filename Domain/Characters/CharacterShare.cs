using RuleForge.Domain.Common;

namespace RuleForge.Domain.Characters;

public sealed class CharacterShare
{
    public int CharacterShareId { get; set; }
    public int CharacterId { get; set; }
    public int SharedWithUserId { get; set; }
    public SharePermission Permission { get; set; } = SharePermission.View;
    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
}
