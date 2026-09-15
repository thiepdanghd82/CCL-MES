namespace CCL.MES.Domain.Auth;

/// <summary>
/// Tám nhóm quyền hiển thị trên bảng phân quyền của tab Quản lý tài khoản
/// (Thiệp chốt 2026-09-15).
///
/// <para><b>Quan hệ với vai trò.</b> Vai vẫn là cơ chế phân quyền (Thiệp chốt
/// 2026-09-14). Tám cờ này là lớp RIÊNG TỪNG NGƯỜI đè lên mặc định của vai:
/// <c>null</c> = "theo vai", <c>true/false</c> = admin đã tick tường minh.</para>
///
/// <para><b>Vì sao mặc định phải SUY TỪ MA TRẬN POLICY chứ không gõ tay.</b>
/// Gõ tay là tạo ra bản sao thứ hai của sự thật; vài tháng sau ai đó sửa policy
/// mà quên bảng này, và bảng phân quyền bắt đầu NÓI DỐI về hệ thống. Bảng dưới
/// đây phản ánh đúng những gì `Program.cs` đang gác, và có test khoá.</para>
/// </summary>
public static class UserPermission
{
    public const string ViewData           = "ViewData";
    public const string EditData           = "EditData";
    public const string ApproveQc          = "ApproveQc";
    public const string ApproveProduction  = "ApproveProduction";
    public const string SpecialAccept      = "SpecialAccept";
    public const string ExportReport       = "ExportReport";
    public const string ManageUsers        = "ManageUsers";
    public const string SystemConfig       = "SystemConfig";

    /// <summary>Thứ tự hiển thị trên bảng — khớp thứ tự cột Thiệp đưa.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        ViewData, EditData, ApproveQc, ApproveProduction,
        SpecialAccept, ExportReport, ManageUsers, SystemConfig,
    };

    /// <summary>
    /// Mặc định theo vai, suy từ ma trận policy đang chạy:
    /// <list type="bullet">
    ///   <item><c>ViewData</c> — mọi vai đăng nhập được đều đọc được gì đó
    ///         (NpiRead · QcRead · IqcSpecRead).</item>
    ///   <item><c>EditData</c> — <c>ShopFloorWrite</c>: mọi vai đứng máy.</item>
    ///   <item><c>ApproveQc</c> — <c>QaApprove</c> / <c>IpqcSubmit</c>:
    ///         Admin · QC · Supervisor · kỹ sư CHẤT LƯỢNG.</item>
    ///   <item><c>ApproveProduction</c> — duyệt bước sản xuất:
    ///         Admin · Supervisor · kỹ sư SẢN XUẤT.</item>
    ///   <item><c>SpecialAccept</c> — <c>EngineerWaive</c> +
    ///         <c>SpecialAcceptRoles</c>: Admin · Supervisor · CẢ HAI ngạch kỹ sư.</item>
    ///   <item><c>ExportReport</c> — xuất báo cáo: từ Supervisor/kỹ sư trở lên.</item>
    ///   <item><c>ManageUsers</c> / <c>SystemConfig</c> — <c>AdminOnly</c>.</item>
    /// </list>
    /// </summary>
    public static bool DefaultForRole(string? role, string permission) => permission switch
    {
        ViewData          => IsKnown(role),
        EditData          => IsKnown(role) && !IsSys(role),
        ApproveQc         => Is(role, UserRole.Admin, UserRole.Supervisor,
                                 UserRole.Qc, UserRole.EngineerQuality),
        // Thiệp chốt 2026-09-15 (làm rõ): "phê duyệt sản xuất" là quyền cho
        // hàng ĐI TIẾP qua một cổng QC khi MỌI THỨ ĐẠT — IPQC bấm Cho chạy,
        // hoặc prepress chuyển sang bước sau. Khác hẳn "phê duyệt đặc biệt"
        // là cho đi tiếp DÙ CÓ cái không đạt.
        //
        // Nên người giữ quyền này chính là người đứng cổng: QC ở IPQC, công
        // nhân sản xuất ở prepress. Bảng Thiệp vẽ ban đầu KHÔNG tick cho
        // operator và qc — nhưng theo giải thích sau đó thì chính họ là người
        // bấm, và siết theo bảng vẽ sẽ làm CHUYỀN ĐỨNG. Theo giải thích.
        ApproveProduction => IsKnown(role) && !IsSys(role),
        SpecialAccept     => Is(role, UserRole.Admin, UserRole.Supervisor,
                                 UserRole.EngineerProduction, UserRole.EngineerQuality,
                                 UserRole.Engineer),
        ExportReport      => Is(role, UserRole.Admin, UserRole.Supervisor,
                                 UserRole.EngineerProduction, UserRole.EngineerQuality,
                                 UserRole.Engineer, UserRole.Qc),
        ManageUsers       => Is(role, UserRole.Admin),
        SystemConfig      => Is(role, UserRole.Admin),
        _                 => false,
    };

    /// <summary>
    /// Quyền THẬT SỰ của một người: cờ riêng nếu admin đã tick, ngược lại theo vai.
    /// </summary>
    public static bool Effective(string? role, string permission, bool? explicitFlag)
        => explicitFlag ?? DefaultForRole(role, permission);

    /// <summary>Vai cũ <c>Engineer</c> chưa được gán lại: cho quyền của ngạch
    /// SẢN XUẤT trừ phần duyệt sản xuất — đúng bằng những gì nó vốn có trước A4,
    /// không nhiều hơn. Rộng thêm cho vai chờ-gán-lại là cấp quyền cho người
    /// không ai chịu trách nhiệm.</summary>
    private static bool IsKnown(string? role) =>
        !string.IsNullOrWhiteSpace(role)
        && (UserRole.IsValid(role) || UserRole.IsLegacyEngineer(role) || IsSys(role));

    private static bool IsSys(string? role) => UserRole.IsSystemAccount(role);

    private static bool Is(string? role, params string[] allowed) =>
        !string.IsNullOrWhiteSpace(role)
        && allowed.Any(a => a.Equals(role, StringComparison.OrdinalIgnoreCase));
}
