using RuleForge.Domain.Common;
namespace RuleForge.Domain.Quests;
public sealed class QuestShare { public int QuestShareId {get;set;} public int QuestId {get;set;} public int SharedWithUserId {get;set;} public SharePermission Permission {get;set;} = SharePermission.View; public DateTime DateCreatedUtc {get;set;} = DateTime.UtcNow; }
