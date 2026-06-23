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
            "wo.not_found"                      => "WO not found on the server.",
            "wo.material_row_not_found"         => "Material row not found — reload the checklist.",
            "wo.invalid_phase"                  => "WO is not in the PREPRESS phase — cannot record the check.",
            "wo.if_match_required"              => "Data session expired — reload the checklist.",
            "wo.idempotency_key_required"       => "Request is missing the idempotency key — contact IT.",
            "prepress.invalid_status"           => "Invalid status — only Pending / OK / NG are accepted.",
            "prepress.invalid_reason_code"      => "NG reason code is not in the Scrap catalog — choose a valid code.",
            "prepress.invalid_ng_note"          => "An NG note is required when setting NG (1-500 characters).",
            _                                   => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an in-band <see cref="CCL.MES.Shared.Prepress.PrepressSetResponse.ErrorCode"/>
    /// (returned on 200 / 409) into the banner. The 409 path's
    /// <c>wo.state_conflict</c> is the most operationally important —
    /// it's the banner the operator sees when a parallel kiosk wrote
    /// first.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"           => "Another operation has already updated this checklist. Reloading the latest state — try recording again.",
        "wo.if_match_required"        => "Data session has not been reloaded — scan the WO again.",
        "wo.idempotency_key_required" => "Request is missing the idempotency key — contact IT.",
        "http.empty_body"             => "The server returned an empty response — contact IT.",
        _                             => $"Unknown error code ({code}).",
    };

    // ── Scan materials (PREPRESS) — client-side match outcomes ──────────
    // These are NOT server errors: the barcode is matched against the WO's
    // BOM in-app (MaterialBarcodeMatcher) before any PUT. Wording lives here
    // so the scan flow stays testable without booting MAUI.

    /// <summary>Banner for a non-success scan match (NoMatch / Multiple / EmptyCode).</summary>
    public static string ScanOutcomeMessage(MaterialMatchOutcome outcome, string partNo) => outcome switch
    {
        MaterialMatchOutcome.NoMatch   => $"Part {partNo} is not in this WO's BOM — record by hand if correct.",
        MaterialMatchOutcome.Multiple  => $"Part {partNo} is on more than one BOM line — record it by hand.",
        MaterialMatchOutcome.EmptyCode => "Could not read a part number from the scan — try again.",
        _                              => "",
    };

    /// <summary>Banner when the scanned material row was just recorded OK.</summary>
    public static string ScanRecordedOk(string materialCode) => $"✓ {materialCode} recorded OK.";

    /// <summary>Banner when the scanned material row was already OK.</summary>
    public static string ScanAlreadyOk(string materialCode) => $"{materialCode} is already OK.";
}
