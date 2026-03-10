namespace RuleForge.Contracts.Quests;

public sealed class QuestResponse
{
    public int QuestId { get; set; }
    public int? CampaignId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public int Mode { get; set; }
    public bool UseChoiceMode { get; set; }
    public int? StartNodeId { get; set; }
    public List<QuestNodeResponse> Nodes { get; set; } = new();
    public List<QuestChoiceResponse> Choices { get; set; } = new();
}

public sealed class QuestNodeResponse
{
    public int QuestNodeId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int NodeType { get; set; }
    public int OrderIndex { get; set; }
    public string? BodyMarkdown { get; set; }
    public string? DmHints { get; set; }
    public int? EncounterId { get; set; }
    public double? CanvasX { get; set; }
    public double? CanvasY { get; set; }
}

public sealed class QuestChoiceResponse
{
    public int QuestChoiceId { get; set; }
    public int FromNodeId { get; set; }
    public int ToNodeId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? ConditionExpression { get; set; }
    public string? EffectsJson { get; set; }
    public int OrderIndex { get; set; }
}

public sealed class UpsertQuestRequest
{
    public int? CampaignId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public int Mode { get; set; } = 3;
    public bool UseChoiceMode { get; set; } = true;
    public int? StartNodeId { get; set; }
}

public sealed class UpsertQuestNodeRequest
{
    public string Title { get; set; } = string.Empty;
    public int NodeType { get; set; } = 1;
    public int OrderIndex { get; set; }
    public string? BodyMarkdown { get; set; }
    public string? DmHints { get; set; }
    public int? EncounterId { get; set; }
    public double? CanvasX { get; set; }
    public double? CanvasY { get; set; }
}

public sealed class UpsertQuestChoiceRequest
{
    public int FromNodeId { get; set; }
    public int ToNodeId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? ConditionExpression { get; set; }
    public string? EffectsJson { get; set; }
    public int OrderIndex { get; set; }
}
