using System;
using System.Collections.Generic;
using System.Linq;
using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.Specs;

/// <summary>
/// P10.5c-1 — Pure mapper from server-side Spec mutation error codes onto
/// operator-facing Vietnamese strings. Lives in the client lib (not in
/// the Shared assembly) because the VN strings are MAUI-local until the
/// resx infrastructure lands in P10.6 (Q12 i18n inline VN).
///
/// Server-side codes (from <c>CCL.MES.Application.SpecService</c> +
/// the controller mapping):
///   - <c>duplicate_spec_code</c>     — Create / Copy
///   - <c>validation</c>              — Create / Copy generic
///   - <c>not_found</c>               — Approve / Update / Trash / Restore / Copy source / Revise source / Supersede target
///   - <c>trashed</c>                 — Edit / Revise / Supersede on trashed source
///   - <c>immutable_status</c>        — Update on non-Draft
///   - <c>invalid_source_status</c>   — Revise on non-Approved/Released
///   - <c>reason_required</c>         — Revise missing reason
///   - <c>invalid_status</c>          — Supersede on non-Approved/Released
///   - <c>confirm_mismatch</c>        — Supersede confirmation SpecCode mismatch
///   - <c>already_trashed</c>         — Trash on already-trashed
///   - <c>active_work_orders</c>      — Trash blocked by active WO refs (count in Details)
///   - <c>not_trashed</c>             — Restore on non-trashed
///
/// P10.5c-2 — Spec xlsx import error codes:
///   - <c>import.no_file</c>                  — multipart 'file' part missing
///   - <c>import.invalid_extension</c>        — non-.xlsx upload
///   - <c>import.oversize</c>                 — &gt; 10 MB cap
///   - <c>import.invalid_content</c>          — content sniff failed (not a ZIP/xlsx)
///   - <c>import.parse_error</c>              — xlsx parser threw (corrupt / wrong layout)
///   - <c>import.no_parsed_payload</c>        — save received empty ParsedJson
///   - <c>import.invalid_parsed_payload</c>   — save received non-deserializable ParsedJson
///   - <c>import.invalid_mode</c>             — unknown save mode
///   - <c>import.spec_code_override_required</c> — SaveAsCopy without override
///   - <c>import.duplicate_ref_no</c>         — RefNo dup raced past preview
///   - <c>import.validation</c>               — legacy SaveAsync InvalidOp (Customer/PartNo)
///
/// P10.5e-1 — Drawings upload + download error codes:
///   - <c>drawing.no_file</c>             — multipart 'file' part missing
///   - <c>drawing.oversize</c>            — &gt; 10 MB cap (blob store + API)
///   - <c>drawing.invalid_extension</c>   — not in pdf/png/jpg/jpeg/svg/gif/webp/dwg/dxf/ai
///   - <c>drawing.invalid_kind</c>        — kind query param not a valid DrawingKind enum
///   - <c>drawing.forbidden</c>           — role gate (UploadAsync requires Admin/Engineer)
///   - <c>drawing.validation</c>          — legacy UploadAsync InvalidOp catch-all
///   - <c>drawing.not_found</c>           — version id missing OR cross-revision attempt
///   - <c>drawing.blob_missing</c>        — DB row exists but disk blob lost
///
/// P10.5e-2 — 3-role decide error codes:
///   - <c>drawing.invalid_role</c>        — Role query param not Npi/Production/Qc
///   - <c>drawing.invalid_decision</c>    — Decision not Approved/Rejected
///   - <c>drawing.department_mismatch</c> — CanActAs gate rejected (403)
///   - <c>drawing.comment_required</c>    — Reject without comment
///   - <c>drawing.invalid_state</c>       — Cannot decide on Superseded version
///
/// P10.5f — QC plan + capture error codes:
///   - <c>qc.forbidden</c>          — Admin/Engineer role gate
///   - <c>qc.invalid_stage</c>      — Unknown stage query param
///   - <c>qc.invalid_row</c>        — Criterion Name blank
///   - <c>qc.invalid_result</c>     — Unknown capture result
///   - <c>qc.reason_required</c>    — FAIL without NgReasonCode
///   - <c>qc.invalid_reason</c>     — NgReasonCode not active or unknown
///   - <c>qc.not_found</c>          — Revision / Criterion missing / cross-revision
///   - <c>qc.validation</c>         — Generic legacy validation fallthrough
///
/// P10.5g — Spec export (CSV / XLSX / PDF + sheet PDF) error codes:
///   - <c>export.failed</c>         — Server-side render exception (500)
///   - <c>export.no_data</c>        — Filter produced 0 rows (operator hint)
///   - <c>export.save_cancelled</c> — Save dialog dismissed (informational)
/// </summary>
public static class SpecMutationErrorMapper
{
    /// <summary>Map an <see cref="ApiException"/> raised from a Spec
    /// mutation call into a single Vietnamese error message ready for
    /// inline banner display. Falls back to MessageEn when the code is
    /// unrecognised so future server-side additions don't surface as a
    /// blank banner.</summary>
    public static string ToVietnameseMessage(ApiException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return ToVietnameseMessage(ex.ApiError);
    }

    public static string ToVietnameseMessage(ApiError err)
    {
        ArgumentNullException.ThrowIfNull(err);
        return err.Code switch
        {
            "duplicate_warning"     => DuplicateWarningMessage(err),
            "duplicate_part_no"     => "This Part No already has a spec (Rev A) — change the Part No, or use Copy/Revise.",
            "duplicate_spec_code"   => "Spec code already exists — choose a different code.",
            "validation"            => string.IsNullOrWhiteSpace(err.MessageEn) ? "The data is not yet valid." : $"The data is not yet valid: {err.MessageEn}",
            "not_found"             => "Spec not found (it may have been deleted).",
            "trashed"               => "This Spec is in the Trash — restore it before continuing.",
            "immutable_status"      => CurrentStatusSuffix("Only Draft revisions can be edited.", err),
            "invalid_source_status" => CurrentStatusSuffix("Only an Approved or Released Spec can be revised.", err),
            "reason_required"       => "The revision reason must be at least 5 characters.",
            "invalid_status"        => CurrentStatusSuffix("Only an Approved or Released Spec can be marked as Superseded.", err),
            "confirm_mismatch"      => "The confirmation Spec code is incorrect — retype the current code exactly.",
            "already_trashed"       => "This Spec is already in the Trash.",
            "active_work_orders"    => ActiveWoSuffix(err),
            "not_trashed"           => "This Spec is not in the Trash — no restore is needed.",
            "auth.invalid_credentials" or "auth.bad_claim" => "Your session has expired — please sign in again.",
            "http.non_success"      => $"Server error (HTTP {err.MessageEn}).",
            // P10.5c-2 — Spec xlsx import codes.
            "import.no_file"                  => "No file selected to upload.",
            "import.invalid_extension"        => "Only .xlsx files are supported.",
            "import.oversize"                 => "The file exceeds 10 MB — choose a smaller file.",
            "import.invalid_content"          => "The file is not a valid xlsx format.",
            "import.parse_error"              => string.IsNullOrWhiteSpace(err.MessageEn) ? "Could not read the xlsx file — wrong layout or corrupted file." : $"Could not read the xlsx file: {err.MessageEn}",
            "import.no_parsed_payload"        => "The preview session has expired — select the file again.",
            "import.invalid_parsed_payload"   => "The preview data is invalid — select the file again.",
            "import.invalid_mode"             => "Invalid save option — please try again.",
            "import.spec_code_override_required" => "You must enter a new Spec code when choosing Save as copy.",
            "import.duplicate_ref_no"         => "A Spec with the same REF NO already exists — choose Supersede or Save as copy.",
            "import.validation"               => string.IsNullOrWhiteSpace(err.MessageEn) ? "The data is not yet valid — check Customer / Part No." : $"The data is not yet valid: {err.MessageEn}",
            // P10.5e-1 — Drawings upload + download codes.
            "drawing.no_file"             => "No drawing file selected to upload.",
            "drawing.oversize"            => "The drawing file exceeds 10 MB — choose a smaller file.",
            "drawing.invalid_extension"   => "Invalid file format — only PDF / PNG / JPG / SVG / GIF / WEBP / DWG / DXF / AI are supported.",
            "drawing.invalid_kind"        => "Invalid drawing kind.",
            "drawing.forbidden"           => "Your account is not allowed to upload drawings (Admin or Engineer required).",
            "drawing.validation"          => string.IsNullOrWhiteSpace(err.MessageEn) ? "The drawing is not yet valid." : $"The drawing is not yet valid: {err.MessageEn}",
            "drawing.not_found"           => "Drawing not found (it may have been deleted).",
            "drawing.blob_missing"        => "The drawing file is no longer on the server — please upload it again.",
            // P10.5e-2 — Decide chain.
            "drawing.invalid_role"        => "Invalid chip role — only NPI / Production / QC are accepted.",
            "drawing.invalid_decision"    => "Invalid decision — only Approve / Reject are accepted.",
            "drawing.department_mismatch" => "Your account is not allowed to approve this chip — route it to the correct Department/Role.",
            "drawing.comment_required"    => "A reason is required when rejecting.",
            "drawing.invalid_state"       => "Cannot approve because this version is already Superseded.",
            // P10.5f — QC plan + capture codes.
            "qc.forbidden"        => "Your account is not allowed to edit QC (Admin or Engineer required).",
            "qc.invalid_stage"    => "Invalid stage name — only IpqcPrint / IpqcCut / Fqc / Oqc are accepted.",
            "qc.invalid_row"      => string.IsNullOrWhiteSpace(err.MessageEn) ? "The criterion name cannot be empty." : err.MessageEn,
            "qc.invalid_result"   => "Invalid result — only Pass / Fail / Na are accepted.",
            "qc.reason_required"  => "You must select a reason code when the result is FAIL.",
            "qc.invalid_reason"   => "The reason code is invalid or no longer in use.",
            "qc.not_found"        => "QC plan / criterion not found (it may have been deleted).",
            "qc.validation"       => string.IsNullOrWhiteSpace(err.MessageEn) ? "The QC data is not yet valid." : $"The QC data is not yet valid: {err.MessageEn}",
            // P10.5g — Spec export codes.
            "export.failed"         => string.IsNullOrWhiteSpace(err.MessageEn) ? "Export failed — please try again." : $"Export failed: {err.MessageEn}",
            "export.no_data"        => "No data matches the filter — change the conditions and export again.",
            "export.save_cancelled" => "You cancelled the save dialog — the file is still in the app's downloads folder.",
            // P10.6a — Settings / My Profile + My Password codes.
            "profile.not_found"            => "Account information not found — sign in again and retry.",
            "profile.invalid_body"         => "The update data is invalid.",
            "profile.display_name_too_long" => "The display name cannot exceed 100 characters.",
            "auth.wrong_current"           => "The current password is incorrect.",
            "auth.new_too_short"           => "The new password must be at least 4 characters.",
            "auth.missing_fields"          => "Please fill in all the required fields.",
            _                       => string.IsNullOrWhiteSpace(err.MessageEn) ? $"Unknown error ({err.Code})." : err.MessageEn,
        };
    }

    /// <summary>Map by <c>code</c> + the optional <c>currentStatus</c> detail
    /// without needing a full ApiException — useful for unit tests that
    /// reconstruct the mapping table.</summary>
    public static string ToVietnameseMessage(string code, string? currentStatus = null, int? activeWoCount = null, string? messageEn = null)
    {
        var details = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(currentStatus)) details["currentStatus"] = currentStatus!;
        if (activeWoCount is not null) details["activeWoCount"] = activeWoCount.Value.ToString();
        return ToVietnameseMessage(new ApiError
        {
            Code = code,
            MessageEn = messageEn ?? "",
            Details = details.Count == 0 ? null : details,
        });
    }

    /// <summary>P10.10 — parse the colliding identity fields from a
    /// <c>duplicate_warning</c> error (Details["dupFields"], comma-separated:
    /// ifscode | partno | spec). Empty when absent.</summary>
    public static IReadOnlyList<string> DuplicateFields(ApiError err)
    {
        if (err.Details is not null && err.Details.TryGetValue("dupFields", out var raw) && !string.IsNullOrWhiteSpace(raw))
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Array.Empty<string>();
    }

    private static string DuplicateWarningMessage(ApiError err)
    {
        var fields = DuplicateFields(err);
        var labels = fields.Select(f => f switch
        {
            "ifscode" => "IFS code",
            "partno"  => "Part No",
            "spec"    => "Spec",
            _          => f,
        }).ToList();
        var which = labels.Count switch
        {
            0 => "An identity field",
            1 => labels[0],
            _ => string.Join(", ", labels.Take(labels.Count - 1)) + " and " + labels[^1],
        };
        return $"{which} already exists on a saved spec. Enter a reason to create it anyway.";
    }

    private static string CurrentStatusSuffix(string baseMessage, ApiError err)
    {
        if (err.Details is not null && err.Details.TryGetValue("currentStatus", out var status) && !string.IsNullOrWhiteSpace(status))
            return $"{baseMessage} (Current: {status})";
        return baseMessage;
    }

    private static string ActiveWoSuffix(ApiError err)
    {
        if (err.Details is not null && err.Details.TryGetValue("activeWoCount", out var raw) && int.TryParse(raw, out var count))
            return $"Cannot delete: {count} Work Order(s) are still using this Spec.";
        return "Cannot delete: there are still Work Orders using this Spec.";
    }
}
