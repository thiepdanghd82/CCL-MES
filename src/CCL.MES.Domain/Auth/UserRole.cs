namespace CCL.MES.Domain.Auth;

/// <summary>
/// Phase 6 Bước 4 — RBAC role whitelist. Stored as a string on
/// <c>User.Role</c> so the cookie principal claim and JSON wire format
/// stay unchanged from Phase 2; this class is the single source of
/// truth for valid values.
///
/// <para><b>A4 (Thiệp chốt 2026-09-14) — tách ngạch kỹ sư.</b> Xưởng có hai
/// ngạch kỹ sư làm hai việc khác nhau, và luồng khác nhau:</para>
/// <list type="bullet">
///   <item><b>Kỹ sư sản xuất</b> — xác nhận trong luồng sản xuất (prepress ·
///         SETTING · RUNNING), soạn spec/bản vẽ công đoạn.</item>
///   <item><b>Kỹ sư chất lượng</b> — xác nhận trong luồng IQC · IPQC · OQC ·
///         FQC, soạn tiêu chuẩn IQC và kế hoạch QC.</item>
/// </list>
/// <para>Cả hai đều ký được waiver vật tư lệch, miễn khác người đã xác nhận
/// dòng (luật 4-mắt giữ nguyên).</para>
/// </summary>
public static class UserRole
{
    public const string Admin      = "Admin";
    public const string Supervisor = "Supervisor";

    /// <summary>
    /// Vai kỹ sư CŨ, gộp cả hai ngạch. <b>Còn đọc được, KHÔNG cấp mới.</b>
    ///
    /// <para>Không xoá vì <c>Users.Role</c> là chuỗi: xoá hằng này thì mọi tài
    /// khoản đang mang "Engineer" trên DB live rơi vào vai không hợp lệ —
    /// nghĩa là mất quyền im lặng giữa ca. Giữ lại, để ngoài <see cref="All"/>
    /// nên Account Control không gán mới được, và <see cref="IsLegacyEngineer"/>
    /// cho phép policy vẫn nhận chúng cho tới khi được gán lại tường minh.</para>
    /// </summary>
    public const string Engineer   = "Engineer";

    /// <summary>Kỹ sư SẢN XUẤT — luồng sản xuất, spec/bản vẽ công đoạn.</summary>
    public const string EngineerProduction = "EngineerProduction";

    /// <summary>Kỹ sư CHẤT LƯỢNG — luồng IQC · IPQC · OQC · FQC, tiêu chuẩn
    /// IQC và kế hoạch QC.</summary>
    public const string EngineerQuality    = "EngineerQuality";

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

    /// <summary>Vai CẤP MỚI ĐƯỢC, theo thứ tự hiển thị. <c>Engineer</c> cũ cố ý
    /// KHÔNG có mặt: đọc được nhưng không gán mới.</summary>
    public static readonly IReadOnlyList<string> All =
        new[] { Admin, Supervisor, EngineerProduction, EngineerQuality, Qc, Operator };

    public static bool IsValid(string? role) =>
        !string.IsNullOrEmpty(role) && All.Contains(role);

    /// <summary>Tài khoản còn mang vai <c>Engineer</c> gộp của thời trước A4.
    /// Policy nhận chúng như cả hai ngạch cho tới khi admin gán lại tường minh —
    /// mất quyền giữa ca nguy hiểm hơn quyền rộng thêm vài ngày.</summary>
    public static bool IsLegacyEngineer(string? role) =>
        string.Equals(role, Engineer, StringComparison.Ordinal);

    /// <summary>P10.7a-2.1 — true when the role is the protected
    /// audit-only <c>Sys</c> tag. AccountControlService uses this to
    /// short-circuit mutation requests at every surface (Update,
    /// ResetPassword) before any other validation runs.</summary>
    public static bool IsSystemAccount(string? role) =>
        string.Equals(role, Sys, StringComparison.Ordinal);
}
