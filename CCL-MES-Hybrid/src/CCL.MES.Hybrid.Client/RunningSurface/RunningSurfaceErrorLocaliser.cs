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
            "wo.not_found"                          => "Không thấy lệnh sản xuất này trên máy chủ — quét lại mã WO.",
            "wo.invalid_phase"                      => "Lệnh không còn ở bước cho phép thao tác này — nạp lại màn hình.",
            "wo.if_match_required"                  => "Dữ liệu trên màn hình đã cũ — nạp lại rồi làm lại.",
            "wo.idempotency_key_required"           => "Yêu cầu thiếu khoá chống trùng — báo IT.",
            "running.setting_not_started"           => "Lệnh chưa vào bước cài đặt nên chưa Hoàn tất được — nạp lại màn hình.",
            "running.invalid_body"                  => "Dữ liệu gửi lên không hợp lệ — báo IT.",
            // Lô bị IQC đánh trượt SAU khi Pre-press đã gắn. Câu phải nói rõ
            // HAI đường ra, vì lúc này WO đã qua Pre-press nên người vận hành
            // không tự sửa dòng vật tư được nữa.
            "run.material_lot_unusable"             => "Có lô vật tư không còn được IQC thả — thay lô, hoặc nhờ kỹ sư chấp nhận đặc biệt, rồi mới chạy máy.",
            "running.invalid_qty_delta"             => "Số lượng phải lớn hơn 0 — muốn trừ bớt thì dùng \"Sửa sản lượng\".",
            "running.invalid_reason_code"           => "Mã lý do không có trong danh mục — chọn một mã trong danh sách.",
            "running.invalid_ng_note"               => "Nhập số lượng NG thì bắt buộc ghi mô tả (1–500 ký tự).",
            "running.invalid_note"                  => "Ghi chú dài quá 500 ký tự — rút ngắn lại.",
            "running.invalid_correction_reason"     => "Bắt buộc ghi lý do sửa (1–500 ký tự).",
            "running.linked_entry_not_found"        => "Không thấy bản ghi cần sửa — nạp lại danh sách.",
            "running.linked_entry_wrong_wo"         => "Bản ghi cần sửa không thuộc lệnh này — chọn một dòng trong danh sách.",
            "running.no_active_session"             => "Máy chưa chạy phiên nào — bấm \"Bắt đầu chạy\" trước đã.",
            "running.no_active_pause"               => "Không có lần dừng nào đang mở — nạp lại màn hình.",
            "running.no_production"                 => "Chưa có sản lượng nào nên chưa kết thúc lệnh được.",
            "setting.incomplete"                    => "Còn hạng mục cài đặt chưa OK — xác nhận hết rồi mới Hoàn tất được.",
            _                                       => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an in-band <see cref="CCL.MES.Shared.RunningSurface.RunningSurfaceSetResponse.ErrorCode"/>
    /// (returned on 200 / 409) into the banner. The 409 path's
    /// <c>wo.state_conflict</c> is the most operationally important —
    /// it's the banner the operator sees when a parallel kiosk wrote
    /// first.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"           => "Lệnh vừa được cập nhật ở nơi khác. Màn hình đang lấy lại trạng thái mới — bấm lại.",
        "wo.if_match_required"        => "Data session has not been reloaded — scan the WO again.",
        "wo.idempotency_key_required" => "Yêu cầu thiếu khoá chống trùng — báo IT.",
        "http.empty_body"             => "Máy chủ trả về phản hồi rỗng — báo IT.",
        "setting.incomplete"          => "Còn hạng mục cài đặt chưa OK — xác nhận hết rồi mới Hoàn tất được.",
        _                             => $"Unknown error code ({code}).",
    };
}
