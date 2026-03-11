using RuleForge.Domain.Common;

namespace RuleForge.Domain.Items;

public sealed class ItemShare
{
    public int ItemShareId { get; set; }
    public int ItemId { get; set; }
    public int SharedWithUserId { get; set; }
    public SharePermission Permission { get; set; } = SharePermission.View;
    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
}
