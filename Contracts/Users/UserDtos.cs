namespace RuleForge.Contracts.Users;

public sealed class UserResponse
{
    public int AppUserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Role { get; set; }
    public bool MustChangePassword { get; set; }
    public bool IsSystem { get; set; }
}

public sealed class UpsertUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Password { get; set; }
    public int Role { get; set; }
    public bool MustChangePassword { get; set; }
    public bool IsSystem { get; set; }
}


public sealed class LoginRequest { public string Username { get; set; } = string.Empty; public string Password { get; set; } = string.Empty; }
public sealed class LoginResponse { public int AppUserId { get; set; } public string Username { get; set; } = string.Empty; public int Role { get; set; } public bool MustChangePassword { get; set; } }
public sealed class ChangePasswordRequest { public string Username { get; set; } = string.Empty; public string CurrentPassword { get; set; } = string.Empty; public string NewPassword { get; set; } = string.Empty; }
