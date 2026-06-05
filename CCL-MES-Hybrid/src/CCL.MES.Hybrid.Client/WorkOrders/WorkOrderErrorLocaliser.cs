using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.WorkOrders;

/// <summary>
/// P10.7a-1.3 — extracted from <c>WorkOrders.razor</c>'s @code block
/// so the client xUnit suite can prove every VN error message is
/// wired without booting the MAUI host. The Razor page now calls
/// these static methods directly; the behaviour is identical from
/// the operator's perspective.
/// </summary>
public static class WorkOrderErrorLocaliser
{
    /// <summary>Localise a server-side <c>ApiError.Code</c> (404 / 401 /
    /// 4xx envelope) into the operator-facing Vietnamese banner.</summary>
    public static string LocaliseApiError(int statusCode, ApiError error) =>
        error.Code switch
        {
            "work_order.not_found"        => "Không tìm thấy WO trên máy chủ.",
            "device.invalid_id"           => "Mã thiết bị không hợp lệ — báo IT.",
            "device.not_seen"             => "Trạm chưa nhận diện được — thử lại sau.",
            "scan.empty_payload"          => "Payload quét rỗng — quét lại.",
            // P10.7a-1.3 codes — should not surface in normal flow because
            // the client always sends both headers; mapped for
            // defence-in-depth.
            "wo.if_match_required"        => "Phiên dữ liệu hết hạn — quét lại WO.",
            "wo.idempotency_key_required" => "Yêu cầu thiếu khóa idempotency — báo IT.",
            _                             => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an <see cref="CCL.MES.Shared.WorkOrders.AdvanceWorkOrderResponse.ErrorCode"/>
    /// (returned in-band on 200 / 409) into the banner. Note that
    /// <c>wo.state_conflict</c> is the most operationally important
    /// message — it's what the operator sees when another shift's
    /// kiosk got there first.</summary>
    public static string LocaliseAdvanceError(string code) => code switch
    {
        "WorkOrderNotFound"           => "Không tìm thấy WO.",
        "AlreadyAtFinalStep"          => "WO đã đến bước cuối (Closed).",
        "RequiresSpecAndMaterials"    => "Thiếu bản vẽ kỹ thuật hoặc vật tư chưa sẵn sàng.",
        "RequiresSetupConfirmed"      => "Chưa xác nhận setup máy.",
        "IpqcNotPassed"               => "IPQC chưa Pass.",
        "NoProductionYet"             => "Chưa có sản lượng — không thể chuyển sang FQC.",
        "FqcNotPassed"                => "FQC chưa Pass.",
        "OqcOrRohsNotMet"             => "OQC chưa Pass hoặc RoHS chưa OK.",
        "InvalidStepTransition"       => "Chuyển bước không hợp lệ.",
        // P10.7a-1.3 — concurrency + idempotency codes from the
        // RowVersion + Idempotency-Key contract retrofit.
        "wo.state_conflict"           => "Một thao tác khác đã cập nhật WO này. Bấm 'Nhận / Bắt đầu' lần nữa để thử lại với phiên bản mới nhất.",
        "wo.if_match_required"        => "Phiên dữ liệu chưa được tải lại — quét lại WO.",
        "wo.idempotency_key_required" => "Yêu cầu thiếu khóa idempotency — báo IT.",
        _                             => $"Mã lỗi không xác định ({code}).",
    };
}
