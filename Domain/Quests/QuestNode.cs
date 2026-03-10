namespace RuleForge.Domain.Quests;
public sealed class QuestNode {
 public int QuestNodeId { get; set; }
 public int QuestId { get; set; }
 public string Title { get; set; } = string.Empty;
 public QuestNodeType NodeType { get; set; } = QuestNodeType.Scene;
 public int OrderIndex { get; set; }
 public string? BodyMarkdown { get; set; }
 public string? DmHints { get; set; }
 public int? EncounterId { get; set; }
 public double? CanvasX { get; set; }
 public double? CanvasY { get; set; }
 public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
 public DateTime DateModifiedUtc { get; set; } = DateTime.UtcNow;
 public DateTime? DateDeletedUtc { get; set; }
}
