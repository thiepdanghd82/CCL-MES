using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.Prepress;

/// <summary>
/// P10.7b-3 — VN message bank for the PREPRESS row-write surface.
/// Mirrors WorkOrderErrorLocaliser pattern: every operator-facing
/// banner string lives here so the xUnit suite can lock the wording
/// without booting MAUI. PrepressDashboard.razor + the 3 child
/// components call these static methods directly.
/// </summary>
public static class PrepressErrorLocaliser
{
    /// <summary>Localise a server-side <see cref="ApiError.Code"/>
    /// (4xx envelope) into the operator-facing Vietnamese banner.
    /// Covers the 422 codes the PrepressController emits + the shared
    /// 428/400 codes the prelude raises.</summary>
    public static string LocaliseApiError(int statusCode, ApiError error) =>
        error.Code switch
        {
            "wo.not_found"                      => "Không tìm thấy WO trên máy chủ.",
            "wo.material_row_not_found"         => "Không tìm thấy dòng vật tư — tải lại checklist.",
            "wo.invalid_phase"                  => "WO không ở giai đoạn PREPRESS — không thể ghi check.",
            "wo.if_match_required"              => "Phiên dữ liệu hết hạn — tải lại checklist.",
            "wo.idempotency_key_required"       => "Yêu cầu thiếu khóa idempotency — báo IT.",
            "prepress.invalid_status"           => "Trạng thái không hợp lệ — chỉ chấp nhận Pending / OK / NG.",
            "prepress.invalid_reason_code"      => "Mã lỗi NG không có trong danh mục Scrap — chọn mã hợp lệ.",
            "prepress.invalid_ng_note"          => "Ghi chú NG bắt buộc khi đặt NG (1-500 ký tự).",
            _                                   => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an in-band <see cref="CCL.MES.Shared.Prepress.PrepressSetResponse.ErrorCode"/>
    /// (returned on 200 / 409) into the banner. The 409 path's
    /// <c>wo.state_conflict</c> is the most operationally important —
    /// it's the banner the operator sees when a parallel kiosk wrote
    /// first.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"           => "Một thao tác khác đã cập nhật checklist này. Đang tải lại trạng thái mới nhất — thử ghi lại.",
        "wo.if_match_required"        => "Phiên dữ liệu chưa được tải lại — quét lại WO.",
        "wo.idempotency_key_required" => "Yêu cầu thiếu khóa idempotency — báo IT.",
        "http.empty_body"             => "Máy chủ trả về phản hồi rỗng — báo IT.",
        _                             => $"Mã lỗi không xác định ({code}).",
    };
}
