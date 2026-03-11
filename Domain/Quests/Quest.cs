namespace RuleForge.Domain.Quests;
public sealed class Quest {
 public int QuestId { get; set; }
 public int CampaignId { get; set; }
 public string Title { get; set; } = string.Empty;
 public int? OwnerAppUserId { get; set; }
 public string? Summary { get; set; }
 public QuestMode Mode { get; set; } = QuestMode.Hybrid;
 public bool UseChoiceMode { get; set; } = true;
 public int? StartNodeId { get; set; }
 public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
 public DateTime DateModifiedUtc { get; set; } = DateTime.UtcNow;
 public DateTime? DateDeletedUtc { get; set; }
}
