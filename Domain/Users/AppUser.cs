namespace RuleForge.Domain.Users;

public sealed class AppUser
{
    public int AppUserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public AppRole Role { get; set; } = AppRole.User;
    public bool MustChangePassword { get; set; }
    public bool IsSystem { get; set; }
    public string? ThemePreference { get; set; }
    public bool? CampaignNavExpanded { get; set; }
    public bool? CompendiumNavExpanded { get; set; }

    public DateTime DateCreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime DateModifiedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? DateDeletedUtc { get; set; }
}
