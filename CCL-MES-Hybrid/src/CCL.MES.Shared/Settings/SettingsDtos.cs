namespace CCL.MES.Shared.Settings;

/// <summary>
/// P10.6a — Settings/My Profile read shape. Returned by
/// <c>GET /api/v2/settings/me</c>. The username + role come from the
/// JWT identity claims; the display name + department + email come
/// from the DB row so a server-side admin update surfaces here on the
/// next page load.
/// </summary>
public sealed record SettingsProfileDto
{
    /// <summary>DB row id — operator never sees this; included for
    /// audit-log correlation.</summary>
    public long Id { get; init; }

    /// <summary>Stable login handle. Read-only here — operator must
    /// ask admin to change it.</summary>
    public string Username { get; init; } = "";

    /// <summary>Carbon-grade role string ("Admin" / "Supervisor" /
    /// "Engineer" / "QC" / "Operator"). Source-of-truth for the
    /// role badge.</summary>
    public string Role { get; init; } = "";

    /// <summary>Optional friendly name shown across the UI. Editable
    /// via PATCH /me.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Optional department string ("NPI" / "Production" /
    /// "QC" / …). Read-only here — admin sets it.</summary>
    public string? Department { get; init; }

    /// <summary>True when the user was created via admin temp-pwd
    /// flow and hasn't done a self-change yet. The Settings page
    /// surfaces this with a banner steering the operator to the
    /// password tab.</summary>
    public bool MustChangePassword { get; init; }

    /// <summary>UTC creation timestamp — informational.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>UTC last-modified timestamp — informational.</summary>
    public DateTime? UpdatedAt { get; init; }
}

/// <summary>
/// P10.6a — Settings/My Profile PATCH body. Only fields the operator
/// is allowed to edit appear here; the controller validates each
/// independently so future additions don't require all-or-nothing
/// transactions.
/// </summary>
public sealed record UpdateProfileRequest
{
    /// <summary>New display name. Null / whitespace clears the column
    /// (same semantics as the Web service).</summary>
    public string? DisplayName { get; init; }
}

/// <summary>
/// P10.6a — Settings/Change Password body. Both fields required;
/// the controller maps WrongCurrent → 422 (auth.wrong_current),
/// NewTooShort → 422 (auth.new_too_short), UserNotFound → 404.
/// </summary>
public sealed record ChangePasswordRequest
{
    /// <summary>Operator's existing password. Hashed-compare server
    /// side via <c>IPasswordHasher</c>.</summary>
    public string CurrentPassword { get; init; } = "";

    /// <summary>Replacement. ≥ 4 chars enforced server side; client
    /// SHOULD pre-validate length + match before posting.</summary>
    public string NewPassword { get; init; } = "";
}

/// <summary>
/// P10.6a — Settings/Change Password successful response. We deliberately
/// do NOT return a fresh JWT here; the operator's existing access token
/// stays valid until natural expiry. A future PR can add a forced
/// rotate-and-reissue path if compliance asks.
/// </summary>
public sealed record ChangePasswordResponse
{
    public bool Success { get; init; }
}
