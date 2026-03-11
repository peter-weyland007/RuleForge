using RuleForge.Domain.Common;
namespace RuleForge.Domain.Campaigns;
public sealed class CampaignShare { public int CampaignShareId {get;set;} public int CampaignId {get;set;} public int SharedWithUserId {get;set;} public SharePermission Permission {get;set;} = SharePermission.View; public DateTime DateCreatedUtc {get;set;} = DateTime.UtcNow; }
