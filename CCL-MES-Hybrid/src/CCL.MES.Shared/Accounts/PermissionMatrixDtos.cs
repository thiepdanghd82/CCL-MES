namespace CCL.MES.Shared.Accounts;

/// <summary>
/// Bảng phân quyền của tab Quản lý tài khoản (Thiệp chốt 2026-09-15).
/// Mỗi dòng là một người; mỗi cờ là một trong 8 nhóm quyền.
/// </summary>
public sealed record PermissionMatrixRow
{
    public long UserId { get; init; }
    public string Username { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Role { get; init; } = "";
    public string? Department { get; init; }
    public bool IsActive { get; init; }

    /// <summary>Quyền THẬT SỰ — cờ riêng nếu có, ngược lại theo vai.
    /// Khoá = tên quyền trong <c>UserPermission.All</c>.</summary>
    public Dictionary<string, bool> Effective { get; init; } = new();

    /// <summary>Quyền admin đã tick TƯỜNG MINH. Khoá vắng mặt = "theo vai".
    /// Tách khỏi <see cref="Effective"/> để giao diện phân biệt được
    /// "đang theo vai" với "đã bị chỉnh riêng" — hai thứ trông giống nhau
    /// trên màn hình nhưng khác hẳn nhau khi truy trách nhiệm.</summary>
    public Dictionary<string, bool> Explicit { get; init; } = new();

    /// <summary>Số quyền đang có — cột "Số quyền" trên bảng.</summary>
    public int GrantedCount { get; init; }
}

public sealed record PermissionMatrixView
{
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<PermissionMatrixRow> Rows { get; init; } = Array.Empty<PermissionMatrixRow>();

    /// <summary>Tổng số người CÓ quyền, theo từng cột — dòng cuối bảng.</summary>
    public Dictionary<string, int> Totals { get; init; } = new();

    /// <summary>Người đang đăng nhập + vai, để khối đầu bảng hiển thị.</summary>
    public string CurrentUsername { get; init; } = "";
    public string CurrentRole { get; init; } = "";

    /// <summary>Chỉ Admin mới tick được. Giao diện dùng để dựng/không dựng ô tick
    /// (RBAC-by-omission); server vẫn chặn 403 bất kể giao diện làm gì.</summary>
    public bool CanEdit { get; init; }
}

/// <summary>
/// Sửa quyền riêng của MỘT người.
///
/// <para><b>Vì sao có chữ ký.</b> Đổi phân quyền là hành vi nhạy cảm nhất trong
/// hệ — người có quyền này tự cấp được mọi quyền khác. Nên bắt gõ lại mật khẩu
/// của chính admin ngay tại điểm sửa, đúng như ký duyệt IPQC: một máy bỏ quên
/// phiên admin đang mở không thể bị người đi ngang sửa quyền.</para>
/// </summary>
public sealed record UpdateUserPermissionsRequest
{
    /// <summary>Khoá = tên quyền; giá trị <c>null</c> = trả về "theo vai".</summary>
    public Dictionary<string, bool?> Permissions { get; init; } = new();

    public string? SignerUsername { get; init; }
    public string? SignerPassword { get; init; }
}

/// <summary>Kết quả một lần ghi quyền — giao diện cần Ok + câu để hiện, không
/// cần ném exception ra giữa màn hình xưởng.</summary>
public sealed record PermissionWriteResult
{
    public bool Ok { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
}
