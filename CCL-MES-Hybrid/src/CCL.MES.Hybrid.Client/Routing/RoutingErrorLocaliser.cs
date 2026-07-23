using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.Routing;

/// <summary>
/// P11-3 — VN message bank cho leg fork-join surface (RoutingController).
/// Mirror RunningSurfaceErrorLocaliser: mọi banner operator-facing ở đây
/// để xUnit lock wording không cần boot MAUI. LegsDashboard.razor gọi
/// trực tiếp các method static.
/// </summary>
public static class RoutingErrorLocaliser
{
    /// <summary>Localise ApiError.Code (4xx envelope) từ RoutingController.</summary>
    public static string LocaliseApiError(int statusCode, ApiError error) =>
        error.Code switch
        {
            "wo.not_found"                => "Không tìm thấy WO trên máy chủ.",
            "leg.not_found"               => "Không tìm thấy công đoạn (leg) này — tải lại danh sách.",
            "wo.if_match_required"        => "Phiên dữ liệu hết hạn — quét lại WO.",
            "wo.idempotency_key_required" => "Thiếu khoá idempotency — báo IT.",
            "leg.invalid_phase"           => "Không thể chuyển công đoạn sang trạng thái này — tải lại.",
            "leg.inputs_not_ready"        => "Chưa thể chạy: bán thành phẩm (in/tape) chưa xong hoặc thiếu số lượng.",
            "leg.invalid_reason"          => "Cần nhập lý do rework (1-500 ký tự).",
            "routing.unmapped"            => "Có công đoạn trong routing chưa map được — cần người duyệt cấu hình, KHÔNG tự đoán.",
            "routing.invalid_dag"         => "Sơ đồ công đoạn (DAG) không hợp lệ — báo IT.",
            _                             => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise in-band ErrorCode (200/409) từ LegSetResponse /
    /// LegMaterializeResponse.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"           => "Công đoạn này vừa được cập nhật bởi thao tác khác. Đang tải trạng thái mới — thử lại.",
        "wo.if_match_required"        => "Chưa tải lại phiên dữ liệu — quét lại WO.",
        "wo.idempotency_key_required" => "Thiếu khoá idempotency — báo IT.",
        "http.empty_body"            => "Máy chủ trả về rỗng — báo IT.",
        _                            => $"Mã lỗi không xác định ({code}).",
    };
}
