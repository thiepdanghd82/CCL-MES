using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.RunningSurface;

/// <summary>
/// P10.7c-3 — VN message bank for the SETTING + RUNNING + PAUSED write
/// surface. Mirrors PrepressErrorLocaliser pattern: every operator-facing
/// banner string lives here so the xUnit suite can lock the wording
/// without booting MAUI. Setting/RunningDashboard.razor + the pause/finish
/// modals call these static methods directly.
/// </summary>
public static class RunningSurfaceErrorLocaliser
{
    /// <summary>Localise a server-side <see cref="ApiError.Code"/>
    /// (4xx envelope) into the operator-facing Vietnamese banner.
    /// Covers the 422 codes the RunningSurfaceController emits + the
    /// shared 428/400/404 codes the prelude raises.</summary>
    public static string LocaliseApiError(int statusCode, ApiError error) =>
        error.Code switch
        {
            "wo.not_found"                          => "Không tìm thấy WO trên máy chủ.",
            "wo.invalid_phase"                      => "WO không ở giai đoạn cho phép hành động này — tải lại trạng thái.",
            "wo.if_match_required"                  => "Phiên dữ liệu hết hạn — tải lại trạng thái.",
            "wo.idempotency_key_required"           => "Yêu cầu thiếu khóa idempotency — báo IT.",
            "running.setting_not_started"           => "WO chưa vào giai đoạn SETTING — không thể đánh dấu hoàn tất.",
            "running.invalid_body"                  => "Dữ liệu gửi không hợp lệ — báo IT.",
            "running.invalid_qty_delta"             => "Số lượng phải lớn hơn 0 (dùng \"Sửa số\" cho âm).",
            "running.invalid_reason_code"           => "Mã lỗi không có trong danh mục — chọn lại từ danh sách.",
            "running.invalid_ng_note"               => "Ghi chú NG bắt buộc khi nhập số NG (1-500 ký tự).",
            "running.invalid_note"                  => "Ghi chú dài quá 500 ký tự — rút gọn lại.",
            "running.invalid_correction_reason"     => "Lý do sửa bắt buộc (1-500 ký tự).",
            "running.linked_entry_not_found"        => "Không tìm thấy bản ghi gốc cần sửa — tải lại danh sách.",
            "running.linked_entry_wrong_wo"         => "Bản ghi cần sửa không thuộc WO này — chọn lại từ danh sách.",
            "running.no_active_session"             => "Chưa có phiên RUNNING — bấm \"Bắt đầu chạy\" trước.",
            "running.no_active_pause"               => "Không có phiên PAUSE nào đang mở — tải lại trạng thái.",
            "running.no_production"                 => "Chưa có sản lượng nào — không thể kết thúc WO.",
            _                                       => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an in-band <see cref="CCL.MES.Shared.RunningSurface.RunningSurfaceSetResponse.ErrorCode"/>
    /// (returned on 200 / 409) into the banner. The 409 path's
    /// <c>wo.state_conflict</c> is the most operationally important —
    /// it's the banner the operator sees when a parallel kiosk wrote
    /// first.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"           => "Một thao tác khác đã cập nhật WO này. Đang tải lại trạng thái mới nhất — thử lại.",
        "wo.if_match_required"        => "Phiên dữ liệu chưa được tải lại — quét lại WO.",
        "wo.idempotency_key_required" => "Yêu cầu thiếu khóa idempotency — báo IT.",
        "http.empty_body"             => "Máy chủ trả về phản hồi rỗng — báo IT.",
        _                             => $"Mã lỗi không xác định ({code}).",
    };
}
