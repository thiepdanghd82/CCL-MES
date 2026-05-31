namespace CCL.MES.Domain.Auth;

/// <summary>
/// Phase 6 Bước 4 — RBAC role whitelist. 5 roles cover the factory:
/// Admin (god) · Supervisor (oversight) · Engineer (NPI + WI write) ·
/// QC (quality gates) · Operator (run actions). Stored as a string on
/// <c>User.Role</c> so the cookie principal claim and JSON wire format
/// stay unchanged from Phase 2; this class is the single source of
/// truth for valid values.
/// </summary>
public static class UserRole
{
    public const string Admin      = "Admin";
    public const string Supervisor = "Supervisor";
    public const string Engineer   = "Engineer";
    public const string Qc         = "QC";
    public const string Operator   = "Operator";

    /// <summary>All valid role strings in display order.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { Admin, Supervisor, Engineer, Qc, Operator };

    public static bool IsValid(string? role) =>
        !string.IsNullOrEmpty(role) && All.Contains(role);
}
