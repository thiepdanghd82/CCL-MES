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

/// <summary>
/// Danh sách vai cho GIAO DIỆN. Nguồn sự thật là
/// <c>CCL.MES.Domain.Auth.UserRole.All</c>, nhưng `CCL.MES.Hybrid.Razor` không
/// tham chiếu Domain được nên phải có bản sao ở tầng Shared.
///
/// <para><b>Hai bản sao là mầm lỗi, nên có test khoá chúng không lệch nhau</b>
/// (<c>AccountRoleOptionsTests</c>) — cùng cách xử lý như danh sách vai ký ở A4.</para>
///
/// <para><b>Sự cố 2026-09-15:</b> trang Quản lý tài khoản gõ tay
/// <c>{ "Admin", "Supervisor", "Engineer", "Qc", "Operator" }</c> — chữ
/// <c>"Qc"</c> KHÔNG khớp giá trị thật trong DB là <c>"QC"</c>, nên
/// <c>&lt;select&gt;</c> không tìm được option và hiện TRẮNG. Người dùng `qc`
/// trông như "chưa gán vai". Sau A4 mảng ấy còn thiếu hai ngạch kỹ sư mới, tức
/// admin KHÔNG gán được chúng từ giao diện.</para>
/// </summary>
public static class AccountRoleOptions
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "Admin", "Supervisor", "EngineerProduction", "EngineerQuality", "QC", "Operator",
    };

    /// <summary>Nhãn tiếng Việt cho từng vai — mã vai giữ nguyên tiếng Anh vì
    /// nó là GIÁ TRỊ lưu trong DB và trong claim, đổi là hỏng dữ liệu cũ.</summary>
    public static string Label(string? role) => role switch
    {
        "Admin"              => "Quản trị",
        "Supervisor"         => "Quản đốc",
        "EngineerProduction" => "Kỹ sư sản xuất",
        "EngineerQuality"    => "Kỹ sư chất lượng",
        "QC"                 => "QC",
        "Operator"           => "Vận hành",
        "Engineer"           => "Kỹ sư (vai cũ)",
        "Sys"                => "Hệ thống",
        _                    => role ?? "",
    };
}
