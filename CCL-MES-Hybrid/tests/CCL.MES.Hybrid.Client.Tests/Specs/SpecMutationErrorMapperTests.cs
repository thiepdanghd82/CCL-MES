using CCL.MES.Hybrid.Client.Specs;
using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Hybrid.Client.Tests.Specs;

/// <summary>
/// P10.5c-1 — VN error mapper coverage. Each server-side code from
/// <c>SpecsController</c> + <c>SpecService</c> result envelopes is
/// pinned to its VN message so future server-side additions can't
/// silently fall through to a blank banner.
/// </summary>
public sealed class SpecMutationErrorMapperTests
{
    [Theory]
    [InlineData("duplicate_spec_code", "Spec code already exists — choose a different code.")]
    [InlineData("duplicate_part_no",   "This Part No already has a spec (Rev A) — change the Part No, or use Copy/Revise.")]
    [InlineData("not_found",           "Spec not found (it may have been deleted).")]
    [InlineData("trashed",             "This Spec is in the Trash — restore it before continuing.")]
    [InlineData("reason_required",     "The revision reason must be at least 5 characters.")]
    [InlineData("confirm_mismatch",    "The confirmation Spec code is incorrect — retype the current code exactly.")]
    [InlineData("already_trashed",     "This Spec is already in the Trash.")]
    [InlineData("not_trashed",         "This Spec is not in the Trash — no restore is needed.")]
    public void Simple_codes_map_to_VN_strings(string code, string expected)
    {
        Assert.Equal(expected, SpecMutationErrorMapper.ToVietnameseMessage(code));
    }

    [Fact]
    public void Validation_with_message_passes_through_messageEn()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("validation", messageEn: "ProductId required");
        Assert.Contains("ProductId required", msg);
        Assert.StartsWith("The data is not yet valid", msg);
    }

    [Fact]
    public void Validation_with_blank_messageEn_uses_generic_VN()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("validation");
        Assert.Equal("The data is not yet valid.", msg);
    }

    [Theory]
    [InlineData("immutable_status", "Approved",  "Only Draft revisions can be edited. (Current: Approved)")]
    [InlineData("immutable_status", null,        "Only Draft revisions can be edited.")]
    [InlineData("invalid_source_status", "Draft",  "Only an Approved or Released Spec can be revised. (Current: Draft)")]
    [InlineData("invalid_status",        "Released",  "Only an Approved or Released Spec can be marked as Superseded. (Current: Released)")]
    public void Status_aware_codes_append_currentStatus(string code, string? status, string expected)
    {
        Assert.Equal(expected, SpecMutationErrorMapper.ToVietnameseMessage(code, currentStatus: status));
    }

    [Fact]
    public void DuplicateWarning_names_colliding_fields_and_asks_for_reason()
    {
        var err = new CCL.MES.Shared.Envelopes.ApiError
        {
            Code = "duplicate_warning",
            MessageEn = "dup",
            Details = new Dictionary<string, string> { ["dupFields"] = "partno,spec" },
        };
        var msg = SpecMutationErrorMapper.ToVietnameseMessage(err);
        Assert.Contains("Part No", msg);
        Assert.Contains("Spec", msg);
        Assert.Contains("reason", msg, StringComparison.OrdinalIgnoreCase);

        var fields = SpecMutationErrorMapper.DuplicateFields(err);
        Assert.Equal(new[] { "partno", "spec" }, fields);
    }

    [Fact]
    public void ActiveWorkOrders_with_count_includes_number()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("active_work_orders", activeWoCount: 3);
        Assert.Contains("3 Work Order", msg);
    }

    [Fact]
    public void ActiveWorkOrders_without_count_falls_back_to_generic()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("active_work_orders");
        Assert.Contains("there are still Work Orders", msg);
    }

    [Fact]
    public void Unknown_code_with_messageEn_passes_through_messageEn()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("some_new_code", messageEn: "Server fallback text");
        Assert.Equal("Server fallback text", msg);
    }

    [Fact]
    public void Unknown_code_blank_messageEn_includes_code_for_diagnosability()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("brand_new_code");
        Assert.Contains("brand_new_code", msg);
    }

    [Fact]
    public void Auth_codes_map_to_session_expired_VN()
    {
        Assert.StartsWith("Your session has expired", SpecMutationErrorMapper.ToVietnameseMessage("auth.invalid_credentials"));
        Assert.StartsWith("Your session has expired", SpecMutationErrorMapper.ToVietnameseMessage("auth.bad_claim"));
    }

    [Fact]
    public void Http_non_success_includes_status_text()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("http.non_success", messageEn: "HTTP 503");
        Assert.Contains("HTTP 503", msg);
    }

    [Fact]
    public void Http_non_success_no_longer_double_prefixes_HTTP_when_upstream_passes_bare_code()
    {
        // P10.6a hotfix — Henry filed "Lỗi máy chủ (HTTP HTTP 404)" on
        // PR #91 because the upstream synthesiser
        // (CclApiClient.ThrowOnNonSuccess) was prepending "HTTP " to
        // MessageEn AND the VN mapper template also wraps with
        // "(HTTP …)". The upstream now passes the bare status code
        // ("404") so the mapper produces the single-prefixed output.
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("http.non_success", messageEn: "404");
        Assert.DoesNotContain("HTTP HTTP", msg);
        Assert.Contains("HTTP 404", msg);
        Assert.Equal("Server error (HTTP 404).", msg);
    }

    [Fact]
    public void ApiException_overload_routes_through_ApiError()
    {
        var ex = new ApiException(409, new ApiError
        {
            Code = "duplicate_spec_code",
            MessageEn = "Already exists",
        });
        var msg = SpecMutationErrorMapper.ToVietnameseMessage(ex);
        Assert.Equal("Spec code already exists — choose a different code.", msg);
    }

    [Fact]
    public void ApiError_with_details_currentStatus_uses_status_aware_suffix()
    {
        var err = new ApiError
        {
            Code = "immutable_status",
            MessageEn = "Only Draft revisions are editable",
            Details = new Dictionary<string, string> { ["currentStatus"] = "Released" },
        };
        var msg = SpecMutationErrorMapper.ToVietnameseMessage(err);
        Assert.Contains("Released", msg);
    }

    // ── P10.5c-2 — Spec xlsx import codes ───────────────────────────

    [Theory]
    [InlineData("import.no_file",                 "No file selected to upload.")]
    [InlineData("import.invalid_extension",       "Only .xlsx files are supported.")]
    [InlineData("import.oversize",                "The file exceeds 10 MB — choose a smaller file.")]
    [InlineData("import.invalid_content",         "The file is not a valid xlsx format.")]
    [InlineData("import.no_parsed_payload",       "The preview session has expired — select the file again.")]
    [InlineData("import.invalid_parsed_payload",  "The preview data is invalid — select the file again.")]
    [InlineData("import.invalid_mode",            "Invalid save option — please try again.")]
    [InlineData("import.spec_code_override_required", "You must enter a new Spec code when choosing Save as copy.")]
    [InlineData("import.duplicate_ref_no",        "A Spec with the same REF NO already exists — choose Supersede or Save as copy.")]
    public void Import_simple_codes_map_to_VN(string code, string expected)
    {
        Assert.Equal(expected, SpecMutationErrorMapper.ToVietnameseMessage(code));
    }

    [Fact]
    public void Import_parse_error_with_blank_message_uses_generic_VN()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("import.parse_error");
        Assert.StartsWith("Could not read the xlsx file", msg);
    }

    [Fact]
    public void Import_parse_error_with_message_passes_through()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("import.parse_error", messageEn: "Header row missing");
        Assert.Contains("Header row missing", msg);
        Assert.StartsWith("Could not read the xlsx file", msg);
    }

    [Fact]
    public void Import_validation_with_message_passes_through()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("import.validation", messageEn: "Customer field required");
        Assert.Contains("Customer field required", msg);
        Assert.StartsWith("The data is not yet valid", msg);
    }

    [Fact]
    public void Import_validation_with_blank_message_uses_generic_VN()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("import.validation");
        Assert.Equal("The data is not yet valid — check Customer / Part No.", msg);
    }

    [Fact]
    public void Unknown_import_code_falls_back_to_generic()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("import.future_code_never_seen");
        // The default branch surfaces the code so operators can screenshot
        // for ops triage rather than seeing a blank banner.
        Assert.Contains("import.future_code_never_seen", msg);
    }

    // ── P10.5e-1 — Drawings upload + download codes ─────────────────

    [Theory]
    [InlineData("drawing.no_file",           "No drawing file selected to upload.")]
    [InlineData("drawing.oversize",          "The drawing file exceeds 10 MB — choose a smaller file.")]
    [InlineData("drawing.invalid_kind",      "Invalid drawing kind.")]
    [InlineData("drawing.forbidden",         "Your account is not allowed to upload drawings (Admin or Engineer required).")]
    [InlineData("drawing.not_found",         "Drawing not found (it may have been deleted).")]
    [InlineData("drawing.blob_missing",      "The drawing file is no longer on the server — please upload it again.")]
    public void Drawing_simple_codes_map_to_VN(string code, string expected)
    {
        Assert.Equal(expected, SpecMutationErrorMapper.ToVietnameseMessage(code));
    }

    [Fact]
    public void Drawing_invalid_extension_lists_allowed_set()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("drawing.invalid_extension");
        Assert.Contains("PDF", msg);
        Assert.Contains("PNG", msg);
        Assert.Contains("DWG", msg);
    }

    [Fact]
    public void Drawing_validation_with_message_passes_through()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("drawing.validation", messageEn: "ProductRevision 999 not found.");
        Assert.Contains("ProductRevision 999 not found", msg);
        Assert.StartsWith("The drawing is not yet valid", msg);
    }

    [Fact]
    public void Drawing_validation_blank_message_uses_generic_VN()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("drawing.validation");
        Assert.Equal("The drawing is not yet valid.", msg);
    }

    // ── P10.5f — QC plan + capture codes ───────────────────────────

    [Theory]
    [InlineData("qc.forbidden",        "Your account is not allowed to edit QC (Admin or Engineer required).")]
    [InlineData("qc.invalid_stage",    "Invalid stage name — only IpqcPrint / IpqcCut / Fqc / Oqc are accepted.")]
    [InlineData("qc.invalid_result",   "Invalid result — only Pass / Fail / Na are accepted.")]
    [InlineData("qc.reason_required",  "You must select a reason code when the result is FAIL.")]
    [InlineData("qc.invalid_reason",   "The reason code is invalid or no longer in use.")]
    [InlineData("qc.not_found",        "QC plan / criterion not found (it may have been deleted).")]
    public void Qc_simple_codes_map_to_VN(string code, string expected)
    {
        Assert.Equal(expected, SpecMutationErrorMapper.ToVietnameseMessage(code));
    }

    [Fact]
    public void Qc_invalid_row_blank_falls_back_to_generic_VN()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("qc.invalid_row");
        Assert.Equal("The criterion name cannot be empty.", msg);
    }

    [Fact]
    public void Qc_invalid_row_with_message_passes_through()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("qc.invalid_row", messageEn: "Row 3 name blank");
        Assert.Contains("Row 3 name blank", msg);
    }

    [Fact]
    public void Qc_validation_blank_message_uses_generic_VN()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("qc.validation");
        Assert.Equal("The QC data is not yet valid.", msg);
    }

    [Fact]
    public void Qc_validation_with_message_pass_through()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("qc.validation", messageEn: "Stage missing");
        Assert.Contains("Stage missing", msg);
        Assert.StartsWith("The QC data is not yet valid", msg);
    }

    // ── P10.5g — Spec export codes ──────────────────────────────────

    [Theory]
    [InlineData("export.no_data",        "No data matches the filter — change the conditions and export again.")]
    [InlineData("export.save_cancelled", "You cancelled the save dialog — the file is still in the app's downloads folder.")]
    public void Export_simple_codes_map_to_VN(string code, string expected)
    {
        Assert.Equal(expected, SpecMutationErrorMapper.ToVietnameseMessage(code));
    }

    [Fact]
    public void Export_failed_with_blank_message_uses_generic_VN()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("export.failed");
        Assert.StartsWith("Export failed", msg);
    }

    [Fact]
    public void Export_failed_with_message_passes_through()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("export.failed", messageEn: "ClosedXML threw on row 17");
        Assert.Contains("ClosedXML threw on row 17", msg);
        Assert.StartsWith("Export failed", msg);
    }

    // ── P10.6a — Settings / My Profile + My Password codes ──────────

    [Theory]
    [InlineData("profile.not_found",             "Account information not found — sign in again and retry.")]
    [InlineData("profile.invalid_body",          "The update data is invalid.")]
    [InlineData("profile.display_name_too_long", "The display name cannot exceed 100 characters.")]
    [InlineData("auth.wrong_current",            "The current password is incorrect.")]
    [InlineData("auth.new_too_short",            "The new password must be at least 4 characters.")]
    [InlineData("auth.missing_fields",           "Please fill in all the required fields.")]
    public void Settings_codes_map_to_VN(string code, string expected)
    {
        Assert.Equal(expected, SpecMutationErrorMapper.ToVietnameseMessage(code));
    }
}
