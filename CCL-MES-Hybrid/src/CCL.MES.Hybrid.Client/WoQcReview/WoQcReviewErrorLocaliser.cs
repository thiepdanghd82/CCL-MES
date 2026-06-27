using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.WoQcReview;

/// <summary>
/// P10.7e-3 — VN message bank for the FQC + OQC write surface. Mirrors
/// <see cref="IpqcReview.IpqcReviewErrorLocaliser"/> shape so xUnit
/// fixtures lock the wording without booting MAUI.
///
/// Covers every <see cref="ApiError.Code"/> the WoQcReviewController
/// emits (qc.* + oqc.* + wo.invalid_phase shared) plus the 428 / 400 /
/// 404 envelope codes and the in-band 409 wo.state_conflict +
/// http.empty_body codes returned via <see cref="CCL.MES.Shared.WoQcReview.WoQcSetResponse"/>.
///
/// Q5 OQC 3-sig client banners — Reviewer ≠ Inspector, Approver ≠
/// {Reviewer, Inspector} — are <see cref="Q5SameAsInspectorBanner"/>
/// and <see cref="Q5SameAsReviewerBanner"/> respectively. Each dashboard
/// renders the appropriate banner BEFORE the round-trip so the operator
/// understands without a server 422 bounce.
/// </summary>
public static class WoQcReviewErrorLocaliser
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
            "qc.invalid_kind"                       => "Invalid QC kind — must be FQC or OQC.",
            "qc.invalid_item_key"                   => "Invalid QC item key.",
            "qc.invalid_status"                     => "Item status must be OK or NG.",
            "qc.invalid_reason_code"                => "NG reason code is not in the catalog — choose one from the list.",
            "qc.invalid_ng_note"                    => "An NG note is required when marking NG (1-500 characters).",
            "qc.invalid_judgment"                   => "Judgment must be Pass / Reject (FQC) or Approve / Reject (OQC).",
            "qc.judgment_inconsistent"              => "Judgment does not match the rollup — review the NG items.",
            "qc.not_ready_for_judgment"             => "All QC items must be processed before judgment.",
            "qc.invalid_reason"                     => "A judgment reason is required (1-500 characters) when rejecting.",
            "qc.invalid_photo"                      => "Invalid image file — choose a JPG or PNG.",
            "qc.invalid_photo_mime"                 => "The image must be a JPG or PNG.",
            "qc.photo_too_large"                    => "Image is too large — 5 MB maximum.",
            "qc.photo_not_found"                    => "This image was not found — it may have been deleted.",
            "oqc.signature_out_of_order"            => "The Inspector must sign first, then the Reviewer, and finally the Approver.",
            "oqc.same_user_as_inspector"            => "The Reviewer/Approver must be DIFFERENT from the Inspector (3-sig policy).",
            "oqc.same_user_as_reviewer"             => "The Approver must be DIFFERENT from the Reviewer (3-sig policy).",
            _                                       => $"HTTP {statusCode} · {error.Code} · {error.MessageEn}",
        };

    /// <summary>Localise an in-band ErrorCode (returned on 200 / 409 /
    /// 422) into the operator banner.</summary>
    public static string LocaliseSetError(string code) => code switch
    {
        "wo.state_conflict"                     => "Another operation has already updated this WO. Reloading the latest state — try again.",
        "wo.if_match_required"                  => "Data session has not been reloaded — scan the WO again.",
        "wo.idempotency_key_required"           => "Request is missing the idempotency key — contact IT.",
        "qc.invalid_status"                     => "Item status must be OK or NG.",
        "qc.invalid_reason_code"                => "NG reason code is not in the catalog — choose one from the list.",
        "qc.invalid_ng_note"                    => "An NG note is required when marking NG (1-500 characters).",
        "qc.invalid_judgment"                   => "Judgment must be Pass / Reject (FQC) or Approve / Reject (OQC).",
        "qc.judgment_inconsistent"              => "Judgment does not match the rollup — review the NG items.",
        "qc.not_ready_for_judgment"             => "All QC items must be processed before judgment.",
        "qc.invalid_reason"                     => "A judgment reason is required (1-500 characters) when rejecting.",
        "oqc.signature_out_of_order"            => "The Inspector must sign first, then the Reviewer, and finally the Approver.",
        "oqc.same_user_as_inspector"            => "The Reviewer/Approver must be DIFFERENT from the Inspector (3-sig policy).",
        "oqc.same_user_as_reviewer"             => "The Approver must be DIFFERENT from the Reviewer (3-sig policy).",
        "http.empty_body"                       => "The server returned an empty response — contact IT.",
        _                                       => $"Unknown error code ({code}).",
    };

    /// <summary>Q5 client-side guard banner — current user equals
    /// <c>InspectedBy</c> on the OQC row. Rendered when the user opens
    /// OqcDashboard while logged in as the Inspector; the Reviewer +
    /// Approve buttons are disabled.</summary>
    public const string Q5SameAsInspectorBanner =
        "You signed as Inspector for this WO — the 3-sig policy requires " +
        "the Reviewer and Approver to be DIFFERENT from the Inspector. Sign out and sign in " +
        "with a different QC account to continue.";

    /// <summary>Q5 client-side guard banner — current user equals
    /// <c>ReviewedBy</c> on the OQC row. Rendered when the user opens
    /// the Approve sub-form after they signed as Reviewer.</summary>
    public const string Q5SameAsReviewerBanner =
        "You signed as Reviewer for this WO — the 3-sig policy requires " +
        "the Approver to be DIFFERENT from the Reviewer. Sign out and sign in with a different " +
        "QC account to continue.";
}
