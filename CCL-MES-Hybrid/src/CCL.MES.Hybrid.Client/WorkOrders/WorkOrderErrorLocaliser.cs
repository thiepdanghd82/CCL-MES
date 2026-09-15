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
            "work_order.not_found"        => "Không thấy lệnh sản xuất này trên máy chủ — quét lại mã WO.",
            "device.invalid_id"           => "Mã thiết bị không hợp lệ — báo IT.",
            "device.not_seen"             => "Máy trạm chưa được nhận diện — thử lại sau ít phút.",
            "scan.empty_payload"          => "Không đọc được nội dung mã — quét lại.",
            // P10.7a-1.3 codes — should not surface in normal flow because
            // the client always sends both headers; mapped for
            // defence-in-depth.
            "wo.if_match_required"        => "Dữ liệu trên màn hình đã cũ — quét lại mã WO.",
            "wo.idempotency_key_required" => "Yêu cầu thiếu khoá chống trùng — báo IT.",
            _                             => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an <see cref="CCL.MES.Shared.WorkOrders.AdvanceWorkOrderResponse.ErrorCode"/>
    /// (returned in-band on 200 / 409) into the banner. Note that
    /// <c>wo.state_conflict</c> is the most operationally important
    /// message — it's what the operator sees when another shift's
    /// kiosk got there first.</summary>
    public static string LocaliseAdvanceError(string code) => code switch
    {
        "WorkOrderNotFound"           => "WO not found.",
        "AlreadyAtFinalStep"          => "WO has reached the final step (Closed).",
        "RequiresSpecAndMaterials"    => "Missing the technical drawing or materials are not ready.",
        // P10.7a-1.3 amendment — operator-actionable copy. UI confirm-setup
        // (start/end timer + 4-eye lock) ships in P10.7c per breakdown §8;
        // until then the legacy `SetupConfirmed` bool is the gate + only
        // admin tools can flip it. Telling the operator to "báo admin"
        // is the right answer for this window.
        "RequiresSetupConfirmed"      => "Machine setup has not been confirmed. The setup-confirmation UI ships in P10.7c — contact admin/IT to confirm the status.",
        "IpqcNotPassed"               => "IPQC has not passed yet.",
        "NoProductionYet"             => "No production yet — cannot move to FQC.",
        "FqcNotPassed"                => "FQC has not passed yet.",
        "OqcOrRohsNotMet"             => "OQC has not passed or RoHS is not OK.",
        "InvalidStepTransition"       => "Invalid step transition.",
        // P10.7a-1.3 — concurrency + idempotency codes from the
        // RowVersion + Idempotency-Key contract retrofit.
        "wo.state_conflict"           => "Lệnh vừa được cập nhật ở nơi khác. Bấm lại \"Nhận / Bắt đầu\" để làm với bản mới nhất.",
        "wo.if_match_required"        => "Data session has not been reloaded — scan the WO again.",
        "wo.idempotency_key_required" => "Yêu cầu thiếu khoá chống trùng — báo IT.",
        _                             => $"Unknown error code ({code}).",
    };
}
