namespace CCL.MES.Domain.Entities;

/// <summary>
/// Application user. Phase 2 ships login-only; the <see cref="Role"/> column +
/// <see cref="DisplayName"/> + <see cref="LastLoginAt"/> are pre-wired so a
/// later phase can add an RBAC layer without a schema migration.
/// </summary>
public class User : BaseEntity
{
    public string Username { get; set; } = "";

    /// <summary>PBKDF2 hash from ASP.NET Core <c>PasswordHasher&lt;User&gt;</c>.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>Free-form role tag. Phase 2 only uses <c>"Admin"</c>; future RBAC will check this.</summary>
    public string Role { get; set; } = "User";

    public string? DisplayName { get; set; }

    public DateTime? LastLoginAt { get; set; }
}
