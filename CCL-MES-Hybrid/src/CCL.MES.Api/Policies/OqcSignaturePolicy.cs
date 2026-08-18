using CCL.MES.Application.Services;

namespace CCL.MES.Api.Policies;

/// <summary>Bước nào của chuỗi 3 chữ ký OQC đang được thực hiện.</summary>
public enum OqcSignatureStep
{
    /// <summary>Chữ ký 2 — Reviewer.</summary>
    Review,
    /// <summary>Chữ ký 3 — Approver (chốt, đẩy WO sang SHIPPED).</summary>
    Approve,
}

/// <summary>
/// Kết quả kiểm. <see cref="DenyReason"/> khác null nghĩa là vi phạm luật
/// TÁCH VAI (không phải sai thứ tự) — controller phải emit audit *_DENIED cho
/// nhánh này, vì "một người cố ký hai vai" là hành vi cần truy được, khác hẳn
/// với "bấm nhầm khi chưa tới lượt".
/// </summary>
public sealed record OqcSignatureVerdict
{
    public bool Allowed { get; init; }
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public string? DenyReason { get; init; }

    public static OqcSignatureVerdict Allow() => new() { Allowed = true };
}

/// <summary>
/// Luật chuỗi 3 chữ ký OQC — Inspector → Reviewer → Approver (Q5).
///
/// <para><b>Vì sao tách ra khỏi controller.</b> Đây là luật an toàn chất lượng
/// nghiêm ngặt nhất của hệ: nó tồn tại để MỘT người không thể tự mình đẩy một
/// lô hàng ra khỏi nhà máy. Trước đây luật sống rải trong ba action HTTP của
/// <c>WoQcReviewController</c> (1.500 dòng), nên muốn test nó phải dựng
/// <c>WebApplicationFactory</c> + DB + auth — đắt tới mức các nhánh biên thực
/// tế không được phủ. Ở dạng hàm thuần, mọi tổ hợp cờ × vai kiểm được bằng
/// unit test chạy trong vài mili-giây.</para>
///
/// <para><b>Thuần — không I/O.</b> Nhận trạng thái chữ ký hiện có + người đang
/// ký + cờ cấu hình, trả phán quyết. Việc ghi audit / dựng HTTP response vẫn ở
/// controller. Đây đúng ranh giới "policy quyết định, controller thực thi".</para>
///
/// <para><b>Hai hàm chứ không một</b> — cố ý. Trong action <c>approve</c>, hai
/// nhóm kiểm bị NGẮT QUÃNG bởi phần kiểm outcome/lý do reject. Gộp thành một
/// lời gọi sẽ đổi mã lỗi trả về cho request vừa sai outcome vừa trùng vai —
/// một thay đổi hành vi âm thầm ở ca biên. Giữ hai hàm để chèn đúng chỗ cũ.</para>
/// </summary>
public static class OqcSignaturePolicy
{
    public const string OutOfOrder      = "oqc.signature_out_of_order";
    public const string SameAsInspector = "oqc.same_user_as_inspector";
    public const string SameAsReviewer  = "oqc.same_user_as_reviewer";

    /// <summary>Kiểm THỨ TỰ: người trước đã ký chưa.</summary>
    public static OqcSignatureVerdict CheckOrder(
        OqcSignatureStep step, string? inspectedBy, string? reviewedBy)
    {
        if (string.IsNullOrEmpty(inspectedBy))
            return new OqcSignatureVerdict
            {
                Allowed = false,
                ErrorCode = OutOfOrder,
                Message = step == OqcSignatureStep.Review
                    ? "Inspector must sign before Reviewer."
                    : "Inspector must sign before Approver.",
            };

        if (step == OqcSignatureStep.Approve && string.IsNullOrEmpty(reviewedBy))
            return new OqcSignatureVerdict
            {
                Allowed = false,
                ErrorCode = OutOfOrder,
                Message = "Reviewer must sign before Approver.",
            };

        return OqcSignatureVerdict.Allow();
    }

    /// <summary>
    /// Kiểm TÁCH VAI theo cờ. So khớp KHÔNG phân biệt hoa thường: cùng một
    /// người đăng nhập bằng "QC01" hay "qc01" vẫn là một người — nếu so khớp
    /// phân biệt hoa thường thì luật tách vai vượt qua được chỉ bằng cách gõ
    /// khác kiểu chữ.
    /// </summary>
    public static OqcSignatureVerdict CheckDistinct(
        OqcSignatureStep step, string? inspectedBy, string? reviewedBy,
        string actor, WoQcSigPolicyOptions options)
    {
        bool Same(string? a) => !string.IsNullOrEmpty(a)
            && string.Equals(a, actor, StringComparison.OrdinalIgnoreCase);

        if (step == OqcSignatureStep.Review)
        {
            if (options.OqcRequireDistinctReviewer && Same(inspectedBy))
                return Deny(SameAsInspector, "same_user_as_inspector");
            return OqcSignatureVerdict.Allow();
        }

        // Approve — thứ tự kiểm giữ NGUYÊN như bản trong controller:
        // Approver ≠ Reviewer trước, rồi Approver ≠ Inspector.
        if (options.OqcRequireDistinctApprover && Same(reviewedBy))
            return Deny(SameAsReviewer, "same_user_as_reviewer");

        if (options.OqcRequireApproverDistinctFromInspector && Same(inspectedBy))
            return Deny(SameAsInspector, "same_user_as_inspector");

        return OqcSignatureVerdict.Allow();

        static OqcSignatureVerdict Deny(string code, string reason) => new()
        {
            Allowed = false,
            ErrorCode = code,
            DenyReason = reason,
        };
    }
}
