using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.IpqcReview;

/// <summary>
/// P10.7d-3 — VN message bank for the IPQC + QA Approval write surface.
/// Mirrors <see cref="CCL.MES.Hybrid.Client.RunningSurface.RunningSurfaceErrorLocaliser"/>
/// pattern so xUnit can lock wording without booting MAUI.
///
/// Covers every <see cref="ApiError.Code"/> the IpqcReviewController
/// emits (7 ipqc.* + 3 qa.* + 1 wo.invalid_phase shared) plus the
/// shared 428 / 400 / 404 envelope codes and the in-band 409
/// state-conflict + http.empty_body codes returned via
/// <see cref="CCL.MES.Shared.IpqcReview.IpqcSetResponse"/>.
/// </summary>
public static class IpqcReviewErrorLocaliser
{
    /// <summary>Localise a server-side <see cref="ApiError.Code"/>
    /// (4xx envelope) into the operator-facing Vietnamese banner.</summary>
    public static string LocaliseApiError(int statusCode, ApiError error) =>
        error.Code switch
        {
            "wo.not_found"                          => "WO not found on the server.",
            "wo.invalid_phase"                      => "WO is not in a phase that allows this action — reload the state.",
            "wo.if_match_required"                  => "Data session expired — reload the state.",
            "wo.idempotency_key_required"           => "Request is missing the idempotency key — contact IT.",
            "ipqc.invalid_status"                   => "Slot status must be OK or NG.",
            "ipqc.invalid_reason_code"              => "NG reason code is not in the catalog — choose one from the list.",
            "ipqc.invalid_ng_note"                  => "An NG note is required when marking NG (1-500 characters).",
            "ipqc.invalid_judgment"                 => "Judgment must be Go Run / Stop Line / Special Accept.",
            "ipqc.judgment_inconsistent"            => "There is an NG slot — Go Run is not allowed; choose Stop Line or Special Accept.",
            "ipqc.not_ready_for_judgment"           => "All 4 slots (Material + 3 Print) must be processed before judgment.",
            "ipqc.invalid_special_accept_reason"    => "A Special Accept reason is required (1-500 characters).",
            "qa.invalid_outcome"                    => "QA outcome must be Approve or Reject.",
            "qa.invalid_qa_reason"                  => "A QA reason is required (1-500 characters).",
            "qa.same_user_as_ipqc_submitter"        => "The QA approver must be DIFFERENT from the IPQC submitter (dual-sig policy). Reassign QA to someone else.",
            // ── IPQC first-article — MATERIAL (SYSTEM) reconciliation (h-3) ──
            "ipqc.invalid_material_line"            => "That material line does not exist on this WO — reload the grid.",
            "material.invalid_outcome"              => "The waiver decision must be Approve or Reject.",
            "material.invalid_reason"               => "A reason is required for the waiver decision (1-500 characters).",
            "material.not_divergent"                => "This material line is not divergent — no Engineer waiver is required.",
            "material.same_user_as_confirmer"       => "The Engineer approving the divergence must be DIFFERENT from the operator who confirmed the row (dual-sig policy).",
            _                                       => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an in-band
    /// <see cref="CCL.MES.Shared.IpqcReview.IpqcSetResponse.ErrorCode"/>
    /// (returned on 200 / 409 / 422) into the operator-facing banner.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"                     => "Another operation has already updated this WO. Reloading the latest state — try again.",
        "wo.if_match_required"                  => "Data session has not been reloaded — scan the WO again.",
        "wo.idempotency_key_required"           => "Request is missing the idempotency key — contact IT.",
        "qa.same_user_as_ipqc_submitter"        => "The QA approver must be DIFFERENT from the IPQC submitter (dual-sig policy). Reassign QA to someone else.",
        "ipqc.judgment_inconsistent"            => "There is an NG slot — Go Run is not allowed; choose Stop Line or Special Accept.",
        "ipqc.not_ready_for_judgment"           => "All 4 slots (Material + 3 Print) must be processed before judgment.",
        "ipqc.invalid_special_accept_reason"    => "A Special Accept reason is required (1-500 characters).",
        "qa.invalid_qa_reason"                  => "A QA reason is required (1-500 characters).",
        // ── IPQC first-article — MATERIAL (SYSTEM) reconciliation (h-3) ──
        "ipqc.invalid_material_line"            => "That material line does not exist on this WO — reload the grid.",
        "material.invalid_outcome"              => "The waiver decision must be Approve or Reject.",
        "material.invalid_reason"               => "A reason is required for the waiver decision (1-500 characters).",
        "material.not_divergent"                => "This material line is not divergent — no Engineer waiver is required.",
        "material.same_user_as_confirmer"       => "The Engineer approving the divergence must be DIFFERENT from the operator who confirmed the row (dual-sig policy).",
        "http.empty_body"                       => "The server returned an empty response — contact IT.",
        _                                       => $"Unknown error code ({code}).",
    };

    /// <summary>Q3 dual-sig client-side guard. Banner shown when the
    /// signed-in user matches the IPQC submitter — the Approve button
    /// is disabled and this string is rendered inline so the operator
    /// understands BEFORE the server bounces them with 422.</summary>
    public const string Q3SameUserBanner =
        "You submitted IPQC for this WO — the dual-sig policy requires a different person to approve QA. " +
        "Sign out and sign in with a different QC account to continue.";
}
