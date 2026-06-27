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
            "work_order.not_found"        => "WO not found on the server.",
            "device.invalid_id"           => "Invalid device ID — contact IT.",
            "device.not_seen"             => "Station not recognized yet — try again later.",
            "scan.empty_payload"          => "Empty scan payload — scan again.",
            // P10.7a-1.3 codes — should not surface in normal flow because
            // the client always sends both headers; mapped for
            // defence-in-depth.
            "wo.if_match_required"        => "Data session expired — scan the WO again.",
            "wo.idempotency_key_required" => "Request is missing the idempotency key — contact IT.",
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
        "wo.state_conflict"           => "Another operation has already updated this WO. Tap 'Accept / Start' again to retry with the latest version.",
        "wo.if_match_required"        => "Data session has not been reloaded — scan the WO again.",
        "wo.idempotency_key_required" => "Request is missing the idempotency key — contact IT.",
        _                             => $"Unknown error code ({code}).",
    };
}
