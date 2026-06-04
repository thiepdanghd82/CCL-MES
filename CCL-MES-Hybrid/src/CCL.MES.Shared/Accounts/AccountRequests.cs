namespace CCL.MES.Shared.Accounts;

/// <summary>
/// P10.6c — wire shapes for the 4 mutation endpoints.
/// Validation rules live server-side (AccountControlService); the
/// DTOs themselves are simple data carriers with no embedded
/// invariants. Each error path returns a stable <c>accounts.*</c>
/// code mapped by the existing VN mapper.
/// </summary>
public sealed record CreateAccountRequest
{
    public string Username { get; init; } = "";
    public string? DisplayName { get; init; }
    public string Role { get; init; } = "";
    public string? Department { get; init; }
    public string Password { get; init; } = "";
}

public sealed record UpdateAccountRequest
{
    /// <summary>When non-null, replaces the user's display name.</summary>
    public string? DisplayName { get; init; }

    /// <summary>When non-null, replaces the user's role. MUST be in
    /// <c>CCL.MES.Domain.Auth.UserRole.All</c>.</summary>
    public string? Role { get; init; }

    /// <summary>When non-null, replaces the department.</summary>
    public string? Department { get; init; }

    /// <summary>When non-null, sets active flag.</summary>
    public bool? IsActive { get; init; }
}

public sealed record ResetPasswordRequest
{
    public string NewPassword { get; init; } = "";
}

/// <summary>Paged response envelope for <c>GET /api/v2/admin/users</c>.</summary>
public sealed record AccountPagedResult
{
    public IReadOnlyList<AccountDto> Items { get; init; } = Array.Empty<AccountDto>();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
