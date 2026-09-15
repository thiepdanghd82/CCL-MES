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

    /// <summary>
    /// Phase 8 PR #28 — Department tag (npi / production / qc / sales / planning…)
    /// dùng cho Drawing 3-role approval mapping (Q5):
    ///   NPI slot      → Role=Engineer + Department=npi
    ///   Production    → Role=Engineer + Department=production (hoặc Role=Supervisor)
    ///   QC slot       → Role=Qc (any department)
    /// Nullable + default null cho backfill cleanly. UI Phase 8/9 sẽ wire dropdown
    /// trong Settings → Account Control → user edit form.
    /// </summary>
    public string? Department { get; set; }
    // ── Quyền RIÊNG từng người (Thiệp chốt 2026-09-15) ──────────────
    //
    // NULL = "theo vai trò" — đây là điểm mấu chốt: migration thêm 8 cột này
    // KHÔNG đổi hành vi của bất kỳ ai, vì mọi dòng cũ đều NULL và vẫn suy ra
    // quyền từ vai như trước. Cờ chỉ có nghĩa khi admin tick tường minh.
    //
    // Vì sao 8 cột tường minh chứ không một số nguyên bitmask: hồ sơ phân quyền
    // phải ĐỌC ĐƯỢC bằng SQL khi khách audit hỏi "ai được duyệt QC tháng 3".
    // Bitmask tiết kiệm vài byte và trả giá bằng việc không ai tra nổi.
    //
    // Mã cột giữ tiếng Anh vì nó là GIÁ TRỊ trong claim và trong wire format.

    /// <summary>Xem dữ liệu.</summary>
    public bool? PermViewData { get; set; }

    /// <summary>Nhập / sửa dữ liệu.</summary>
    public bool? PermEditData { get; set; }

    /// <summary>Phê duyệt QC.</summary>
    public bool? PermApproveQc { get; set; }

    /// <summary>Phê duyệt sản xuất.</summary>
    public bool? PermApproveProduction { get; set; }

    /// <summary>Phê duyệt đặc biệt (nhận vật tư/hàng không đạt).</summary>
    public bool? PermSpecialAccept { get; set; }

    /// <summary>Xuất báo cáo.</summary>
    public bool? PermExportReport { get; set; }

    /// <summary>Quản lý người dùng.</summary>
    public bool? PermManageUsers { get; set; }

    /// <summary>Cấu hình hệ thống.</summary>
    public bool? PermSystemConfig { get; set; }

}
