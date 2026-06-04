namespace CCL.MES.Shared.Accounts;

/// <summary>
/// P10.6c — projection of <c>CCL.MES.Domain.Entities.User</c> for the
/// admin Account Control surface. Mirror of the legacy
/// <c>UserAdminVm</c> shape but POCO-record so the Hybrid grid
/// renders without depending on Domain.
///
/// Never includes the PBKDF2 hash. Username + Role + IsActive are
/// the operationally-relevant columns; LastLoginAt + UpdatedAt give
/// the admin a forensic hint about whether the account is in active
/// use before they disable it.
/// </summary>
public sealed record AccountDto
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
    public string Role { get; init; } = "";
    public string? DisplayName { get; init; }
    public string? Department { get; init; }
    public bool IsActive { get; init; }
    public bool MustChangePassword { get; init; }
    public DateTime? LastLoginAtUtc { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? UpdatedAtUtc { get; init; }
}
