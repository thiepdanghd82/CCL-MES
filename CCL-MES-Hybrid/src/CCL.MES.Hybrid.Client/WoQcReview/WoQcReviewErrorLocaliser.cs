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
            "wo.not_found"                          => "Không thấy lệnh sản xuất này trên máy chủ — quét lại mã WO.",
            "wo.invalid_phase"                      => "Lệnh không còn ở bước cho phép thao tác này — nạp lại màn hình.",
            "wo.if_match_required"                  => "Dữ liệu trên màn hình đã cũ — nạp lại rồi làm lại.",
            "wo.idempotency_key_required"           => "Yêu cầu thiếu khoá chống trùng — báo IT.",
            "qc.invalid_kind"                       => "Loại kiểm không hợp lệ — phải là FQC hoặc OQC.",
            "qc.invalid_item_key"                   => "Hạng mục kiểm không hợp lệ — nạp lại màn hình.",
            "qc.invalid_status"                     => "Trạng thái hạng mục phải là OK hoặc NG.",
            "qc.invalid_reason_code"                => "Mã lỗi NG không có trong danh mục — chọn một mã trong danh sách.",
            "qc.invalid_ng_note"                    => "Đánh NG thì bắt buộc ghi mô tả (1–500 ký tự).",
            "qc.invalid_judgment"                   => "Kết luận phải là Đạt / Từ chối (FQC) hoặc Duyệt / Từ chối (OQC).",
            "qc.judgment_inconsistent"              => "Kết luận không khớp với kết quả các hạng mục — xem lại những hạng mục NG.",
            "qc.not_ready_for_judgment"             => "Còn hạng mục chưa xác nhận OK/NG — chấm hết rồi mới kết luận được.",
            "qc.invalid_reason"                     => "Từ chối thì bắt buộc ghi lý do (1–500 ký tự).",
            "qc.invalid_photo"                      => "File ảnh không hợp lệ — chọn ảnh JPG hoặc PNG.",
            "qc.invalid_photo_mime"                 => "Ảnh phải là JPG hoặc PNG.",
            "qc.photo_too_large"                    => "Ảnh quá nặng — tối đa 5 MB.",
            "qc.photo_not_found"                    => "Không thấy ảnh này — có thể đã bị xoá.",
            "oqc.signature_out_of_order"            => "Phải ký đúng thứ tự: người kiểm ký trước, rồi người soát, cuối cùng người duyệt.",
            "oqc.same_user_as_inspector"            => "Người soát và người duyệt phải KHÁC người kiểm — nguyên tắc ba chữ ký. Nhờ người khác ký.",
            "oqc.same_user_as_reviewer"             => "Người duyệt phải KHÁC người soát — nguyên tắc ba chữ ký. Nhờ người khác duyệt.",
            _                                       => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an in-band ErrorCode (returned on 200 / 409 /
    /// 422) into the operator banner.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"                     => "Lệnh vừa được cập nhật ở nơi khác. Màn hình đang lấy lại trạng thái mới — bấm lại.",
        "wo.if_match_required"                  => "Data session has not been reloaded — scan the WO again.",
        "wo.idempotency_key_required"           => "Yêu cầu thiếu khoá chống trùng — báo IT.",
        "qc.invalid_status"                     => "Trạng thái hạng mục phải là OK hoặc NG.",
        "qc.invalid_reason_code"                => "Mã lỗi NG không có trong danh mục — chọn một mã trong danh sách.",
        "qc.invalid_ng_note"                    => "Đánh NG thì bắt buộc ghi mô tả (1–500 ký tự).",
        "qc.invalid_judgment"                   => "Kết luận phải là Đạt / Từ chối (FQC) hoặc Duyệt / Từ chối (OQC).",
        "qc.judgment_inconsistent"              => "Kết luận không khớp với kết quả các hạng mục — xem lại những hạng mục NG.",
        "qc.not_ready_for_judgment"             => "Còn hạng mục chưa xác nhận OK/NG — chấm hết rồi mới kết luận được.",
        "qc.invalid_reason"                     => "Từ chối thì bắt buộc ghi lý do (1–500 ký tự).",
        "oqc.signature_out_of_order"            => "Phải ký đúng thứ tự: người kiểm ký trước, rồi người soát, cuối cùng người duyệt.",
        "oqc.same_user_as_inspector"            => "Người soát và người duyệt phải KHÁC người kiểm — nguyên tắc ba chữ ký. Nhờ người khác ký.",
        "oqc.same_user_as_reviewer"             => "Người duyệt phải KHÁC người soát — nguyên tắc ba chữ ký. Nhờ người khác duyệt.",
        "http.empty_body"                       => "Máy chủ trả về phản hồi rỗng — báo IT.",
        _                                       => $"Unknown error code ({code}).",
    };

    /// <summary>Q5 client-side guard banner — current user equals
    /// <c>InspectedBy</c> on the OQC row. Rendered when the user opens
    /// OqcDashboard while logged in as the Inspector; the Reviewer +
    /// Approve buttons are disabled.</summary>
    public const string Q5SameAsInspectorBanner =
        "You signed as Inspector for this WO — the 3-sig policy requires " +
        "the Reviewer and Approver to be DIFFERENT from the Inspector. Sign out and sign in " +
        "with a different QC account to continue.";

    /// <summary>Q5 client-side guard banner — current user equals
    /// <c>ReviewedBy</c> on the OQC row. Rendered when the user opens
    /// the Approve sub-form after they signed as Reviewer.</summary>
    public const string Q5SameAsReviewerBanner =
        "You signed as Reviewer for this WO — the 3-sig policy requires " +
        "the Approver to be DIFFERENT from the Reviewer. Sign out and sign in with a different " +
        "QC account to continue.";
}
