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

    /// <summary>P10.7a-2.1 — system audit-only role for the seeded
    /// <c>sys-recovery</c> account that owns SYS_RECOVERY audit rows
    /// when the admin force-phase endpoint fires. Deliberately NOT in
    /// <see cref="All"/> so it cannot be assigned via Account Control
    /// (Create/Update reject). The seed writes it directly bypassing
    /// the whitelist; AccountControlService guards mutation paths so
    /// admins cannot edit / reset / disable sys accounts from the UI.</summary>
    public const string Sys        = "Sys";

    /// <summary>All valid role strings in display order.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { Admin, Supervisor, Engineer, Qc, Operator };

    public static bool IsValid(string? role) =>
        !string.IsNullOrEmpty(role) && All.Contains(role);

    /// <summary>P10.7a-2.1 — true when the role is the protected
    /// audit-only <c>Sys</c> tag. AccountControlService uses this to
    /// short-circuit mutation requests at every surface (Update,
    /// ResetPassword) before any other validation runs.</summary>
    public static bool IsSystemAccount(string? role) =>
        string.Equals(role, Sys, StringComparison.Ordinal);
}
