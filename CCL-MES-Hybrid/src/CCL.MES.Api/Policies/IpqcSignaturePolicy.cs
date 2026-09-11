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
    public static bool SignerRoleAllowed(string? signerRole) =>
        !string.IsNullOrWhiteSpace(signerRole)
        && (signerRole.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || signerRole.Equals("QC", StringComparison.OrdinalIgnoreCase));
}
