namespace RuleForge.Domain.Quests;
public sealed class QuestChoice {
 public int QuestChoiceId { get; set; }
 public int QuestId { get; set; }
 public int FromNodeId { get; set; }
 public int ToNodeId { get; set; }
 public string Label { get; set; } = string.Empty;
 public string? ConditionExpression { get; set; }
 public string? EffectsJson { get; set; }
 public int OrderIndex { get; set; }
 public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
 public DateTime DateModifiedUtc { get; set; } = DateTime.UtcNow;
 public DateTime? DateDeletedUtc { get; set; }
}
