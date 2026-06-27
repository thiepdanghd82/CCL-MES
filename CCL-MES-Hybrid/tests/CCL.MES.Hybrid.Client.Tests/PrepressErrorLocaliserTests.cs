using CCL.MES.Hybrid.Client.Prepress;
using CCL.MES.Shared.Envelopes;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// P10.7b-3 — locks every Vietnamese banner the operator sees on
/// the PREPRESS dashboard. Same shape as
/// <see cref="WorkOrderErrorLocaliserTests"/>: if a future PR
/// changes a VN string, the locked test fails so the operator's
/// screen can't drift silently.
/// </summary>
public sealed class PrepressErrorLocaliserTests
{
    [Theory]
    [InlineData("wo.not_found", "WO not found on the server.")]
    [InlineData("wo.material_row_not_found", "Material row not found — reload the checklist.")]
    [InlineData("wo.invalid_phase", "WO is not in the PREPRESS phase — cannot record the check.")]
    [InlineData("wo.if_match_required", "Data session expired — reload the checklist.")]
    [InlineData("wo.idempotency_key_required", "Request is missing the idempotency key — contact IT.")]
    [InlineData("prepress.invalid_status", "Invalid status — only Pending / OK / NG are accepted.")]
    [InlineData("prepress.invalid_reason_code", "NG reason code is not in the Scrap catalog — choose a valid code.")]
    [InlineData("prepress.invalid_ng_note", "An NG note is required when setting NG (1-500 characters).")]
    public void Locked_VN_banner_for_each_api_error_code(string code, string expected)
    {
        var error = new ApiError { Code = code, MessageEn = "ignored" };
        Assert.Equal(expected, PrepressErrorLocaliser.LocaliseApiError(422, error));
    }

    [Fact]
    public void Invalid_phase_banner_explains_what_operator_should_do()
    {
        var error = new ApiError { Code = "wo.invalid_phase", MessageEn = "ignored" };
        var msg = PrepressErrorLocaliser.LocaliseApiError(422, error);
        Assert.Contains("PREPRESS", msg);
        Assert.Contains("cannot record the check", msg);
    }

    [Fact]
    public void Unknown_api_code_falls_through_with_status_and_messageEn()
    {
        var error = new ApiError { Code = "novel.code", MessageEn = "some english msg" };
        var msg = PrepressErrorLocaliser.LocaliseApiError(418, error);
        Assert.Contains("418", msg);
        Assert.Contains("novel.code", msg);
        Assert.Contains("some english msg", msg);
    }

    [Theory]
    [InlineData("wo.state_conflict", "Another operation has already updated this checklist. Reloading the latest state — try recording again.")]
    [InlineData("wo.if_match_required", "Data session has not been reloaded — scan the WO again.")]
    [InlineData("wo.idempotency_key_required", "Request is missing the idempotency key — contact IT.")]
    [InlineData("http.empty_body", "The server returned an empty response — contact IT.")]
    public void Locked_VN_banner_for_in_band_set_error(string code, string expected)
    {
        Assert.Equal(expected, PrepressErrorLocaliser.LocaliseSetError(code));
    }

    [Fact]
    public void Unknown_in_band_code_falls_through_with_code_in_message()
    {
        var msg = PrepressErrorLocaliser.LocaliseSetError("future.code");
        Assert.Contains("future.code", msg);
    }
}
