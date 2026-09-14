using CCL.MES.Domain.Auth;

namespace CCL.MES.Api.Policies;

/// <summary>
/// Luật của CHỮ KÝ ĐIỆN TỬ khi duyệt IPQC (Thiệp chốt 2026-09-11):
/// người đánh giá IPQC tại chuyền phải gõ lại đúng tài khoản và mật khẩu của
/// chính họ thì mới duyệt và chuyển bước được.
///
/// <para>Đây không phải đăng nhập lại cả phiên — đây là <b>ký tại điểm ký</b>,
/// đúng thông lệ hồ sơ chất lượng (21 CFR Part 11 §11.200 gọi là "signature
/// manifestation"). Chữ ký gắn vào một hành động cụ thể, không gắn vào phiên.</para>
///
/// <para><b>Người ký CÓ THỂ khác người đang đăng nhập.</b> Cả chuyền dùng chung
/// một máy; người đánh giá IPQC đi tới, ký bằng tài khoản của mình rồi đi. Đó
/// chính là lý do ký điện tử tồn tại. Nhưng hồ sơ phải ghi CẢ HAI — ai đang mở
/// máy và ai đã ký — nếu không thì không truy được ai đứng đó.</para>
///
/// <para>Lớp thuần: chỉ kiểm HÌNH DẠNG yêu cầu. Việc đối chiếu mật khẩu nằm ở
/// controller qua <c>IPasswordHasher</c> — không bao giờ tự so chuỗi.</para>
/// </summary>
public static class IpqcSignaturePolicy
{
    public const string SignatureRequired = "ipqc.signature_required";
    public const string SignatureInvalid  = "ipqc.signature_invalid";
    public const string SignerNotAllowed  = "ipqc.signer_not_allowed";
    public const string SignatureLocked   = "ipqc.signature_locked";

    /// <summary>
    /// Tài khoản còn dùng mật khẩu seed (chưa từng tự đặt mật khẩu riêng).
    ///
    /// <para><b>Vì sao chặn.</b> Seed đặt mật khẩu = chính tên tài khoản
    /// (CLAUDE.md §0). Một mật khẩu đoán được thì chữ ký KHÔNG chứng minh được
    /// ai đã quyết định — mà đó là toàn bộ lý do chữ ký tồn tại. Đo 2026-09-14:
    /// 3/4 tài khoản ký được waiver (engineer · supervisor · OQC) đang ở trạng
    /// thái này, tức ai đứng ở máy cũng ký thay họ được.</para>
    ///
    /// <para>Không phải ràng buộc mới với người dùng thật: lần đăng nhập đầu
    /// tiên hệ thống vốn đã bắt họ đổi mật khẩu.</para>
    /// </summary>
    public const string SignaturePasswordNotSet = "ipqc.signature_password_not_set";

    /// <summary>Chặn trên để một request không nuốt nổi bộ nhớ; không phải luật
    /// mật khẩu (luật ấy thuộc về lúc đặt mật khẩu).</summary>
    public const int MaxUsernameLength = 128;
    public const int MaxPasswordLength = 256;

    /// <summary>
    /// Yêu cầu có mang đủ hình dạng một chữ ký không.
    ///
    /// <para>Thiếu ô nào cũng trả CÙNG MỘT mã <see cref="SignatureRequired"/> —
    /// không nói rõ thiếu tên hay thiếu mật khẩu, vì tách ra chẳng giúp người
    /// dùng thật (họ nhìn thấy ô nào trống) mà lại giúp người dò.</para>
    /// </summary>
    public static (string ErrorCode, string Message)? ValidateShape(
        string? signerUsername, string? signerPassword)
    {
        if (string.IsNullOrWhiteSpace(signerUsername) || string.IsNullOrWhiteSpace(signerPassword))
            return (SignatureRequired,
                "Nhập tài khoản và mật khẩu của người đánh giá IPQC để ký duyệt.");

        if (signerUsername.Trim().Length > MaxUsernameLength || signerPassword.Length > MaxPasswordLength)
            return (SignatureInvalid, "Tài khoản hoặc mật khẩu không hợp lệ.");

        return null;
    }

    /// <summary>
    /// Vai của NGƯỜI KÝ có được phép phán định IPQC không.
    ///
    /// <para>Phải kiểm riêng, không dựa vào quyền của phiên đang mở: người đang
    /// đăng nhập có quyền IPQC không nói lên người ký có quyền. Ngược lại cũng
    /// vậy — chính vì thế mới cho phép hai người khác nhau.</para>
    ///
    /// <para>Cùng tập vai với policy <c>IpqcSubmit</c> (§5.5.0): Admin · QC.</para>
    /// </summary>
    /// <summary>Vai được PHÁN ĐỊNH IPQC. PHẢI khớp policy <c>IpqcSubmit</c>.</summary>
    public static readonly IReadOnlyList<string> JudgmentSignerRoles = new[]
    {
        UserRole.Admin, UserRole.Qc, UserRole.EngineerQuality,
        UserRole.Engineer,   // bí danh cũ
    };

    public static bool SignerRoleAllowed(string? signerRole) =>
        !string.IsNullOrWhiteSpace(signerRole)
        && JudgmentSignerRoles.Any(r => r.Equals(signerRole, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Vai của NGƯỜI KÝ waiver vật tư (Thiệp chốt 2026-09-14).
    ///
    /// <para>Tập vai khác với phán định IPQC, và khác là ĐÚNG: phán định là việc
    /// của QC trên chuyền; còn ký duyệt một lô vật tư lệch dữ liệu IQC là quyết
    /// định KỸ THUẬT — ai chịu trách nhiệm nếu lô ấy hỏng hàng. Cùng tập vai với
    /// policy <c>EngineerWaive</c>: Admin · Engineer · Supervisor.</para>
    ///
    /// <para>Giữ hai hàm tách rời thay vì một hàm có cờ: trộn lại thì một ngày
    /// nào đó QC ký được waiver kỹ thuật mà không ai nhận ra.</para>
    /// </summary>
    /// <summary>Vai được ký waiver. PHẢI khớp policy <c>EngineerWaive</c> trong
    /// <c>Program.cs</c> — hai danh sách ở hai nơi là đúng bệnh L83, nên có test
    /// khoá chúng không lệch nhau.</summary>
    public static readonly IReadOnlyList<string> WaiverSignerRoles = new[]
    {
        UserRole.Admin, UserRole.Supervisor,
        UserRole.EngineerProduction, UserRole.EngineerQuality,
        UserRole.Engineer,   // bí danh cũ — đọc được, không cấp mới
    };

    public static bool WaiverSignerRoleAllowed(string? signerRole) =>
        !string.IsNullOrWhiteSpace(signerRole)
        && WaiverSignerRoles.Any(r => r.Equals(signerRole, StringComparison.OrdinalIgnoreCase));
}
