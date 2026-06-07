using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.WoQcReview;

/// <summary>
/// P10.7e-3 — VN message bank for the FQC + OQC write surface. Mirrors
/// <see cref="IpqcReview.IpqcReviewErrorLocaliser"/> shape so xUnit
/// fixtures lock the wording without booting MAUI.
///
/// Covers every <see cref="ApiError.Code"/> the WoQcReviewController
/// emits (qc.* + oqc.* + wo.invalid_phase shared) plus the 428 / 400 /
/// 404 envelope codes and the in-band 409 wo.state_conflict +
/// http.empty_body codes returned via <see cref="CCL.MES.Shared.WoQcReview.WoQcSetResponse"/>.
///
/// Q5 OQC 3-sig client banners — Reviewer ≠ Inspector, Approver ≠
/// {Reviewer, Inspector} — are <see cref="Q5SameAsInspectorBanner"/>
/// and <see cref="Q5SameAsReviewerBanner"/> respectively. Each dashboard
/// renders the appropriate banner BEFORE the round-trip so the operator
/// understands without a server 422 bounce.
/// </summary>
public static class WoQcReviewErrorLocaliser
{
    /// <summary>Localise a server-side <see cref="ApiError.Code"/>
    /// (4xx envelope) into the operator-facing Vietnamese banner.</summary>
    public static string LocaliseApiError(int statusCode, ApiError error) =>
        error.Code switch
        {
            "wo.not_found"                          => "Không tìm thấy WO trên máy chủ.",
            "wo.invalid_phase"                      => "WO không ở giai đoạn cho phép hành động này — tải lại trạng thái.",
            "wo.if_match_required"                  => "Phiên dữ liệu hết hạn — tải lại trạng thái.",
            "wo.idempotency_key_required"           => "Yêu cầu thiếu khóa idempotency — báo IT.",
            "qc.invalid_kind"                       => "Loại QC không hợp lệ — phải là FQC hoặc OQC.",
            "qc.invalid_item_key"                   => "Khóa item QC không hợp lệ.",
            "qc.invalid_status"                     => "Trạng thái item phải là OK hoặc NG.",
            "qc.invalid_reason_code"                => "Mã NG không có trong danh mục — chọn lại từ danh sách.",
            "qc.invalid_ng_note"                    => "Ghi chú NG bắt buộc khi đánh NG (1-500 ký tự).",
            "qc.invalid_judgment"                   => "Phán quyết phải là Pass / Reject (FQC) hoặc Approve / Reject (OQC).",
            "qc.judgment_inconsistent"              => "Phán quyết không khớp với rollup — kiểm tra lại item NG.",
            "qc.not_ready_for_judgment"             => "Phải xử lý đủ các item QC trước khi phán quyết.",
            "qc.invalid_reason"                     => "Lý do phán quyết bắt buộc (1-500 ký tự) khi Reject.",
            "qc.invalid_photo"                      => "Tệp ảnh không hợp lệ — chọn JPG hoặc PNG.",
            "qc.invalid_photo_mime"                 => "Ảnh phải là JPG hoặc PNG.",
            "qc.photo_too_large"                    => "Ảnh quá lớn — tối đa 5 MB.",
            "qc.photo_not_found"                    => "Không tìm thấy ảnh này — có thể đã bị xoá.",
            "oqc.signature_out_of_order"            => "Phải Inspector ký trước, rồi Reviewer, sau cùng Approver.",
            "oqc.same_user_as_inspector"            => "Reviewer/Approver phải KHÁC Inspector (chính sách 3-sig).",
            "oqc.same_user_as_reviewer"             => "Approver phải KHÁC Reviewer (chính sách 3-sig).",
            _                                       => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an in-band ErrorCode (returned on 200 / 409 /
    /// 422) into the operator banner.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"                     => "Một thao tác khác đã cập nhật WO này. Đang tải lại trạng thái mới nhất — thử lại.",
        "wo.if_match_required"                  => "Phiên dữ liệu chưa được tải lại — quét lại WO.",
        "wo.idempotency_key_required"           => "Yêu cầu thiếu khóa idempotency — báo IT.",
        "qc.invalid_status"                     => "Trạng thái item phải là OK hoặc NG.",
        "qc.invalid_reason_code"                => "Mã NG không có trong danh mục — chọn lại từ danh sách.",
        "qc.invalid_ng_note"                    => "Ghi chú NG bắt buộc khi đánh NG (1-500 ký tự).",
        "qc.invalid_judgment"                   => "Phán quyết phải là Pass / Reject (FQC) hoặc Approve / Reject (OQC).",
        "qc.judgment_inconsistent"              => "Phán quyết không khớp với rollup — kiểm tra lại item NG.",
        "qc.not_ready_for_judgment"             => "Phải xử lý đủ các item QC trước khi phán quyết.",
        "qc.invalid_reason"                     => "Lý do phán quyết bắt buộc (1-500 ký tự) khi Reject.",
        "oqc.signature_out_of_order"            => "Phải Inspector ký trước, rồi Reviewer, sau cùng Approver.",
        "oqc.same_user_as_inspector"            => "Reviewer/Approver phải KHÁC Inspector (chính sách 3-sig).",
        "oqc.same_user_as_reviewer"             => "Approver phải KHÁC Reviewer (chính sách 3-sig).",
        "http.empty_body"                       => "Máy chủ trả về phản hồi rỗng — báo IT.",
        _                                       => $"Mã lỗi không xác định ({code}).",
    };

    /// <summary>Q5 client-side guard banner — current user equals
    /// <c>InspectedBy</c> on the OQC row. Rendered when the user opens
    /// OqcDashboard while logged in as the Inspector; the Reviewer +
    /// Approve buttons are disabled.</summary>
    public const string Q5SameAsInspectorBanner =
        "Bạn đã ký Inspector cho WO này — chính sách 3-sig yêu cầu " +
        "Reviewer và Approver KHÁC Inspector. Đăng xuất và đăng nhập " +
        "bằng tài khoản QC khác để tiếp tục.";

    /// <summary>Q5 client-side guard banner — current user equals
    /// <c>ReviewedBy</c> on the OQC row. Rendered when the user opens
    /// the Approve sub-form after they signed as Reviewer.</summary>
    public const string Q5SameAsReviewerBanner =
        "Bạn đã ký Reviewer cho WO này — chính sách 3-sig yêu cầu " +
        "Approver KHÁC Reviewer. Đăng xuất và đăng nhập bằng tài khoản " +
        "QC khác để tiếp tục.";
}
