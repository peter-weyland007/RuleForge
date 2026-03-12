namespace RuleForge.Domain.Marketplace;

public sealed class MarketplaceAuditEvent
{
    public int MarketplaceAuditEventId { get; set; }
    public int MarketplaceListingId { get; set; }
    public int ActorUserId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public DateTime DateUtc { get; set; } = DateTime.UtcNow;
}
