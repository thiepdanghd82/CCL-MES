using CCL.MES.Api.Policies;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Auth;

/// <summary>
/// Đối chiếu CHỮ KÝ ĐIỆN TỬ tại điểm ký — dùng chung cho mọi surface cần
/// "gõ lại tài khoản + mật khẩu của chính bạn thì mới ký được".
///
/// <para><b>Vì sao tách ra.</b> Bản đầu (2026-09-11) nằm private trong
/// <c>IpqcReviewController</c>. Ngày 14-09 cần đúng luật ấy cho waiver vật tư;
/// chép sang là lặp lại y nguyên cái bẫy L54 — sửa một chỗ, ba chỗ còn lại giữ
/// nguyên cái sai. Bốn tính chất dưới đây quá dễ mất khi chép tay:</para>
/// <list type="number">
///   <item>khoá thử-sai kiểm TRƯỚC khi tra bảng Users — không tốn phép so hash,
///         và không rò thời gian cho người dò biết tài khoản nào có thật;</item>
///   <item>MỘT mã lỗi chung cho không-có-tài-khoản / sai-mật-khẩu / tài-khoản-tắt;</item>
///   <item>vai NGƯỜI KÝ kiểm riêng, không dựa quyền của phiên đang mở;</item>
///   <item>mật khẩu KHÔNG BAO GIỜ rời khỏi hàm này.</item>
/// </list>
///
/// <para>Lớp này KHÔNG ghi audit và KHÔNG dựng phản hồi HTTP — mỗi surface có
/// mã audit riêng. Nó chỉ trả PHÁN QUYẾT; controller quyết định ghi gì.</para>
/// </summary>
public sealed class ElectronicSignatureVerifier
{
    private readonly MesDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ReauthThrottle _reauth;

    public ElectronicSignatureVerifier(
        MesDbContext db, IPasswordHasher<User> hasher, ReauthThrottle reauth)
    {
        _db = db; _hasher = hasher; _reauth = reauth;
    }

    /// <summary>Kết quả một lần đối chiếu. <c>ErrorCode == null</c> ⇒ ký hợp lệ.</summary>
    /// <param name="LockMinutes">Số phút còn bị khoá (khi đang khoá), hoặc 0 khi
    /// CHÍNH lần này làm tài khoản bị khoá — để audit ghi lại đúng thời điểm.</param>
    public sealed record Result(
        string? ErrorCode, string? Message, string? TypedUsername,
        string? Username, string? Role, int? LockMinutes)
    {
        public bool Ok => ErrorCode is null;
    }

    /// <summary>
    /// Đối chiếu tài khoản + mật khẩu người ký gõ vào.
    /// </summary>
    /// <param name="signerRoleAllowed">Vai nào được ký Ở SURFACE NÀY. Truyền vào
    /// chứ không cố định: người phán định IPQC (Admin·QC) khác người phê duyệt
    /// waiver vật tư (Admin·Engineer·Supervisor).</param>
    public async Task<Result> VerifyAsync(
        string? signerUsername, string? signerPassword,
        Func<string?, bool> signerRoleAllowed,
        CancellationToken ct = default)
    {
        var typed = signerUsername?.Trim();

        var shape = IpqcSignaturePolicy.ValidateShape(typed, signerPassword);
        if (shape is not null)
            return new Result(shape.Value.ErrorCode, shape.Value.Message, typed, null, null, null);

        // ① Đang bị khoá thì dừng NGAY, không đụng tới bảng Users.
        if (_reauth.LockedFor(typed) is { } left)
        {
            var mins = (int)Math.Ceiling(left.TotalMinutes);
            return new Result(IpqcSignaturePolicy.SignatureLocked,
                $"Tài khoản đang tạm khoá do gõ sai nhiều lần. Thử lại sau {mins} phút.",
                typed, null, null, mins);
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == typed, ct);

        var ok = user is not null
                 && user.IsActive
                 && _hasher.VerifyHashedPassword(user, user.PasswordHash, signerPassword!)
                    != PasswordVerificationResult.Failed;

        if (!ok)
        {
            var lockedNow = _reauth.RegisterFailure(typed);
            return new Result(IpqcSignaturePolicy.SignatureInvalid,
                "Tài khoản hoặc mật khẩu không đúng.",
                typed, null, null, lockedNow ? 0 : null);
        }

        // ② Mật khẩu ĐÚNG nhưng là mật khẩu SEED (chưa ai tự đặt) thì chữ ký
        //    không chứng minh được gì — seed đặt mật khẩu = tên tài khoản, ai
        //    cũng gõ được. Kiểm SAU khi so mật khẩu: đặt trước thì lộ tài khoản
        //    nào tồn tại cho người dò.
        if (user!.MustChangePassword)
        {
            _reauth.RegisterFailure(typed);
            return new Result(IpqcSignaturePolicy.SignaturePasswordNotSet,
                "Tài khoản này chưa đặt mật khẩu riêng nên chưa ký được. Đăng nhập một lần để đổi mật khẩu, rồi ký lại.",
                typed, null, null, null);
        }

        // ③ Mật khẩu đúng KHÔNG có nghĩa là được ký ở đây. Kiểm vai của NGƯỜI KÝ,
        //    không phải vai của phiên đang mở — đó chính là lý do cho phép hai
        //    người khác nhau cùng dùng một máy.
        if (!signerRoleAllowed(user.Role))
        {
            _reauth.RegisterFailure(typed);
            return new Result(IpqcSignaturePolicy.SignerNotAllowed,
                "Tài khoản này không có quyền ký ở bước này.",
                typed, null, null, null);
        }

        _reauth.RegisterSuccess(typed);
        return new Result(null, null, typed, user.Username, user.Role, null);
    }
}
