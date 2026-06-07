using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.IpqcReview;

/// <summary>
/// P10.7d-3 — VN message bank for the IPQC + QA Approval write surface.
/// Mirrors <see cref="CCL.MES.Hybrid.Client.RunningSurface.RunningSurfaceErrorLocaliser"/>
/// pattern so xUnit can lock wording without booting MAUI.
///
/// Covers every <see cref="ApiError.Code"/> the IpqcReviewController
/// emits (7 ipqc.* + 3 qa.* + 1 wo.invalid_phase shared) plus the
/// shared 428 / 400 / 404 envelope codes and the in-band 409
/// state-conflict + http.empty_body codes returned via
/// <see cref="CCL.MES.Shared.IpqcReview.IpqcSetResponse"/>.
/// </summary>
public static class IpqcReviewErrorLocaliser
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
            "ipqc.invalid_status"                   => "Trạng thái slot phải là OK hoặc NG.",
            "ipqc.invalid_reason_code"              => "Mã NG không có trong danh mục — chọn lại từ danh sách.",
            "ipqc.invalid_ng_note"                  => "Ghi chú NG bắt buộc khi đánh NG (1-500 ký tự).",
            "ipqc.invalid_judgment"                 => "Phán quyết phải là Go Run / Stop Line / Special Accept.",
            "ipqc.judgment_inconsistent"            => "Có slot NG — không được chọn Go Run; chọn Stop Line hoặc Special Accept.",
            "ipqc.not_ready_for_judgment"           => "Phải xử lý đủ 4 slot (Material + 3 Print) trước khi phán quyết.",
            "ipqc.invalid_special_accept_reason"    => "Lý do Special Accept bắt buộc (1-500 ký tự).",
            "qa.invalid_outcome"                    => "Kết quả QA phải là Approve hoặc Reject.",
            "qa.invalid_qa_reason"                  => "Lý do QA bắt buộc (1-500 ký tự).",
            "qa.same_user_as_ipqc_submitter"        => "Người duyệt QA phải KHÁC người nộp IPQC (chính sách dual-sig). Chuyển QA cho người khác.",
            _                                       => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an in-band
    /// <see cref="CCL.MES.Shared.IpqcReview.IpqcSetResponse.ErrorCode"/>
    /// (returned on 200 / 409 / 422) into the operator-facing banner.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"                     => "Một thao tác khác đã cập nhật WO này. Đang tải lại trạng thái mới nhất — thử lại.",
        "wo.if_match_required"                  => "Phiên dữ liệu chưa được tải lại — quét lại WO.",
        "wo.idempotency_key_required"           => "Yêu cầu thiếu khóa idempotency — báo IT.",
        "qa.same_user_as_ipqc_submitter"        => "Người duyệt QA phải KHÁC người nộp IPQC (chính sách dual-sig). Chuyển QA cho người khác.",
        "ipqc.judgment_inconsistent"            => "Có slot NG — không được chọn Go Run; chọn Stop Line hoặc Special Accept.",
        "ipqc.not_ready_for_judgment"           => "Phải xử lý đủ 4 slot (Material + 3 Print) trước khi phán quyết.",
        "ipqc.invalid_special_accept_reason"    => "Lý do Special Accept bắt buộc (1-500 ký tự).",
        "qa.invalid_qa_reason"                  => "Lý do QA bắt buộc (1-500 ký tự).",
        "http.empty_body"                       => "Máy chủ trả về phản hồi rỗng — báo IT.",
        _                                       => $"Mã lỗi không xác định ({code}).",
    };

    /// <summary>Q3 dual-sig client-side guard. Banner shown when the
    /// signed-in user matches the IPQC submitter — the Approve button
    /// is disabled and this string is rendered inline so the operator
    /// understands BEFORE the server bounces them with 422.</summary>
    public const string Q3SameUserBanner =
        "Bạn đã nộp IPQC cho WO này — chính sách dual-sig yêu cầu một người khác duyệt QA. " +
        "Đăng xuất và đăng nhập bằng tài khoản QC khác để tiếp tục.";
}
