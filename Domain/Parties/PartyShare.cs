using RuleForge.Domain.Common;
namespace RuleForge.Domain.Parties;
public sealed class PartyShare { public int PartyShareId {get;set;} public int PartyId {get;set;} public int SharedWithUserId {get;set;} public SharePermission Permission {get;set;} = SharePermission.View; public DateTime DateCreatedUtc {get;set;} = DateTime.UtcNow; }
