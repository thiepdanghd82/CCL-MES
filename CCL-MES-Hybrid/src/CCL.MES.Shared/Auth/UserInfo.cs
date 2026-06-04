namespace CCL.MES.Shared.Auth;

/// <summary>
/// Minimal projection of the authenticated user that the client UI needs:
/// id, username, role, display name, department, language preference. Mirrors
/// the claim set the legacy cookie issues (Login.cshtml.cs:101-112) so a
/// client that previously authenticated via cookie sees the same information
/// shape after switching to JWT.
/// </summary>
public sealed record UserInfo
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
    public string Role { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Department { get; init; } = "";

    /// <summary>UI culture preference. Empty string when the user has not
    /// chosen one — clients default to "en" per the project-wide
    /// <c>NeutralLanguage=en</c> mandate.</summary>
    public string Language { get; init; } = "";

    /// <summary>P10.6c — true after an admin reset the password OR
    /// the account was freshly created. The Hybrid client routes the
    /// user to the change-password flow at next login instead of the
    /// usual home screen. Mirrors the legacy
    /// <c>User.MustChangePassword</c> flag.</summary>
    public bool MustChangePassword { get; init; }
}
