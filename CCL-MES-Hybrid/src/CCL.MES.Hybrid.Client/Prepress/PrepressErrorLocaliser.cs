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
            "prepress.special_accept_forbidden" => "Only a PD leader (Engineer) or Supervisor can special-accept a material.",
            // Nói người vận hành phải LÀM GÌ, không nói luật bị vi phạm. Câu
            // "lot_required" trần thì họ đọc xong vẫn không biết gõ vào đâu.
            "prepress.lot_required"             => "Enter the lot number printed on the roll before confirming this line OK.",
            "prepress.part_scan_required"       => "Scan the material label before confirming this line OK.",
            "prepress.part_scan_mismatch"       => "The scanned part does not match this BOM line — you are holding the wrong material.",
            "prepress.lot_not_released"         => "This lot has not been passed by IQC — get a released lot, or ask a PD leader to Special Accept.",

            // Mã lỗi LÔ — trước đây chỉ với tới được ở đường tiêu thụ nên chưa
            // ai dịch. Từ khi gắn nhãn lô cũng chặn thật khi SAI VẬT TƯ, chúng
            // hiện ngay trên màn Pre-press. Không có bốn dòng này thì người vận
            // hành nhận nguyên chuỗi "HTTP 422 · lot.part_mismatch · …".
            "lot.part_mismatch"                 => "This lot belongs to a different material — check the roll label against the BOM line.",
            "lot.rejected"                      => "This lot was rejected by IQC — do not load it. Get a replacement lot.",
            "lot.not_released"                  => "This lot has not been released by IQC yet — wait for the IQC verdict.",
            "lot.expired"                       => "This lot is past its expiry date — ask QC to re-test or use another lot.",
            "lot.not_found"                     => "No such lot in the system — check the number, or ask the warehouse to register it at IQC.",
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
        MaterialMatchOutcome.AllOk     => $"All BOM lines for {partNo} are already OK.",
        MaterialMatchOutcome.EmptyCode => "Could not read a part number from the scan — try again.",
        _                              => "",
    };

    /// <summary>Banner when the scanned material row was just recorded OK.</summary>
    public static string ScanRecordedOk(string materialCode) => $"✓ {materialCode} recorded OK.";

    /// <summary>Banner when the scanned material row was already OK.</summary>
    public static string ScanAlreadyOk(string materialCode) => $"{materialCode} is already OK.";

    /// <summary>Banner when a manually-typed Part Scan does NOT match the
    /// line's own code — operator must use Special Accept to override.</summary>
    public static string ScanMismatch(string typedPart, string lineCode) =>
        $"Typed {typedPart} ≠ line {lineCode} — use Special Accept to record OK.";
}
