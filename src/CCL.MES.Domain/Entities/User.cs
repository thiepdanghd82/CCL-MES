namespace CCL.MES.Domain.Entities;

/// <summary>
/// Application user. Phase 2 shipped login-only with Role/DisplayName/
/// LastLoginAt pre-wired. Phase 5 added the AdminOnly policy on top of
/// the existing string Role. Phase 6 Bước 4 adds <see cref="IsActive"/> +
/// <see cref="MustChangePassword"/> alongside the 5-role whitelist
/// (<c>CCL.MES.Domain.Auth.UserRole</c>) so admins can disable accounts
/// without deleting them and reset-password handoffs force a change at
/// next login.
/// </summary>
public class User : BaseEntity
{
    public string Username { get; set; } = "";

    /// <summary>PBKDF2 hash from ASP.NET Core <c>PasswordHasher&lt;User&gt;</c>.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>
    /// Role tag — one of <c>CCL.MES.Domain.Auth.UserRole.All</c>
    /// (Admin / Supervisor / Engineer / QC / Operator). The default
    /// is kept as <c>"User"</c> for backward-compat at the entity level;
    /// the Phase 6 startup seed silently migrates legacy values to
    /// <c>Operator</c> before whitelist enforcement runs.
    /// </summary>
    public string Role { get; set; } = "User";

    public string? DisplayName { get; set; }

    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Phase 6 Bước 4 — false = soft-disabled. Login refuses these
    /// accounts and the Account UI surfaces them with a "Disabled" badge.
    /// Defaults true so the upcoming migration backfills cleanly.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Phase 6 Bước 4 — true after an admin resets the password. The
    /// next login forces the user to set a new one. Default false so
    /// the migration backfill leaves existing accounts untouched.
    /// </summary>
    public bool MustChangePassword { get; set; } = false;
}
