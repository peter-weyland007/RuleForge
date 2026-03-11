namespace RuleForge.Contracts.Users;

public sealed class FriendSummary
{
    public int AppUserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public sealed class FriendRequestView
{
    public int FriendLinkId { get; set; }
    public int RequesterUserId { get; set; }
    public string RequesterUsername { get; set; } = string.Empty;
    public int AddresseeUserId { get; set; }
    public string AddresseeUsername { get; set; } = string.Empty;
    public int Status { get; set; }
    public DateTime DateCreatedUtc { get; set; }
}

public sealed class FriendsOverviewResponse
{
    public List<FriendSummary> Friends { get; set; } = new();
    public List<FriendRequestView> Incoming { get; set; } = new();
    public List<FriendRequestView> Outgoing { get; set; } = new();
}

public sealed class SendFriendRequestRequest
{
    public string Target { get; set; } = string.Empty;
}

public sealed class RespondFriendRequestRequest
{
    public int FriendLinkId { get; set; }
    public bool Accept { get; set; }
}
