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
            "wo.not_found"                          => "WO not found on the server.",
            "wo.invalid_phase"                      => "WO is not in a phase that allows this action — reload the state.",
            "wo.if_match_required"                  => "Data session expired — reload the state.",
            "wo.idempotency_key_required"           => "Request is missing the idempotency key — contact IT.",
            "running.setting_not_started"           => "WO has not entered the SETTING phase — cannot mark it complete.",
            "running.invalid_body"                  => "Invalid request data — contact IT.",
            "running.invalid_qty_delta"             => "Quantity must be greater than 0 (use \"Correct count\" for negative values).",
            "running.invalid_reason_code"           => "Reason code is not in the catalog — choose one from the list.",
            "running.invalid_ng_note"               => "An NG note is required when entering an NG count (1-500 characters).",
            "running.invalid_note"                  => "Note is longer than 500 characters — shorten it.",
            "running.invalid_correction_reason"     => "A correction reason is required (1-500 characters).",
            "running.linked_entry_not_found"        => "The original record to correct was not found — reload the list.",
            "running.linked_entry_wrong_wo"         => "The record to correct does not belong to this WO — choose one from the list.",
            "running.no_active_session"             => "No RUNNING session yet — tap \"Start run\" first.",
            "running.no_active_pause"               => "No PAUSE session is open — reload the state.",
            "running.no_production"                 => "No production yet — cannot finish the WO.",
            _                                       => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an in-band <see cref="CCL.MES.Shared.RunningSurface.RunningSurfaceSetResponse.ErrorCode"/>
    /// (returned on 200 / 409) into the banner. The 409 path's
    /// <c>wo.state_conflict</c> is the most operationally important —
    /// it's the banner the operator sees when a parallel kiosk wrote
    /// first.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"           => "Another operation has already updated this WO. Reloading the latest state — try again.",
        "wo.if_match_required"        => "Data session has not been reloaded — scan the WO again.",
        "wo.idempotency_key_required" => "Request is missing the idempotency key — contact IT.",
        "http.empty_body"             => "The server returned an empty response — contact IT.",
        _                             => $"Unknown error code ({code}).",
    };
}
