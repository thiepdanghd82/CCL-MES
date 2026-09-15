using CCL.MES.Domain.Auth;
using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Api.Policies;

/// <summary>
/// Quyền riêng cần có để cho một lệnh ĐI TIẾP, theo pha đang đứng.
///
/// <para>Thiệp chốt 2026-09-15: <i>"phê duyệt Prepress cũng là công nhân sản xuất,
/// nếu không có chấp nhận đặc biệt có thể move sang bước tiếp theo"</i>. Rời
/// <c>PREPRESS</c> là đứng ở một CỔNG và cho hàng qua ⇒ <c>ApproveProduction</c>.
/// Các pha còn lại chỉ là bước chuyển trong ca ⇒ <c>EditData</c>, như trước.</para>
///
/// <para><b>Vì sao không gác bằng <c>[Authorize(Policy=…)]</c>.</b> Attribute chạy
/// trước khi controller đọc được lệnh, nên nó không biết lệnh đang ở pha nào. Một
/// attribute duy nhất cho mọi pha thì hoặc siết cả những bước chuyển bình thường,
/// hoặc nới cổng Prepress — cả hai đều sai. Cổng vai <c>ShopFloorWrite</c> vẫn
/// nằm ở attribute; chỉ lớp quyền-riêng mới phải tính trong thân method.</para>
///
/// <para><b>Vì sao KHÔNG đòi cả hai quyền.</b> "Sửa dữ liệu" và "Phê duyệt sản
/// xuất" là hai cột riêng trên bảng phân quyền của Thiệp. Đòi cả hai nghĩa là
/// người được tick đúng một ô vẫn bị chặn — tức bảng nói dối về hệ thống. Có test
/// khoá chiều này (<c>Tat_Sua_du_lieu_KHONG_chan_duoc_cua_Prepress</c>).</para>
///
/// <para>Nhượng bộ (cho đi tiếp DÙ CÓ cái không đạt) là quyết định khác và có cổng
/// riêng của nó ở <c>PrepressController.SpecialAcceptMaterial</c> — gác bằng
/// <c>SpecialAccept</c>, không phải quyền này.</para>
/// </summary>
public static class WoAdvanceCapabilityPolicy
{
    public const string PrepressPhase = "PREPRESS";

    public static string CapabilityFor(string? mesPhase) =>
        string.Equals(mesPhase, PrepressPhase, StringComparison.OrdinalIgnoreCase)
            ? UserPermission.ApproveProduction
            : UserPermission.EditData;

    /// <summary>Nói rõ THIẾU quyền NÀO — "không có quyền" chung chung thì quản
    /// trị không biết phải tick ô nào trong Quản lý tài khoản.</summary>
    public static ApiError ForbiddenError(string capability) =>
        capability == UserPermission.ApproveProduction
            ? ApiError.Of("wo.advance_approve_production_forbidden",
                "Tài khoản này không có quyền Phê duyệt sản xuất nên chưa cho lệnh rời Prepress được.")
            : ApiError.Of("wo.advance_forbidden",
                "Tài khoản này không có quyền Sửa dữ liệu nên chưa chuyển bước cho lệnh được.");
}
