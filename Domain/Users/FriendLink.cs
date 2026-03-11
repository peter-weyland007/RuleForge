namespace RuleForge.Domain.Users;

public sealed class FriendLink
{
    public int FriendLinkId { get; set; }
    public int RequesterUserId { get; set; }
    public int AddresseeUserId { get; set; }
    public FriendRequestStatus Status { get; set; } = FriendRequestStatus.Pending;
    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DateRespondedUtc { get; set; }
}
