using System.Net;
using CCL.MES.Hybrid.Client.WorkOrders;
using CCL.MES.Shared.Envelopes;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// P10.7a-1.3 — locks every Vietnamese banner the operator sees when
/// the advance pipeline fails. These tests replace the "tap and look
/// for the right text" portion of the original Catalyst checkpoint
/// item 5. If a future PR changes a VN string, the locked test fails
/// and the operator's screen doesn't drift silently.
/// </summary>
public sealed class WorkOrderErrorLocaliserTests
{
    // ── LocaliseAdvanceError — in-band (200/409) codes ─────────────

    [Theory]
    [InlineData("WorkOrderNotFound", "WO not found.")]
    [InlineData("AlreadyAtFinalStep", "WO has reached the final step (Closed).")]
    [InlineData("RequiresSpecAndMaterials", "Missing the technical drawing or materials are not ready.")]
    [InlineData("RequiresSetupConfirmed", "Machine setup has not been confirmed. The setup-confirmation UI ships in P10.7c — contact admin/IT to confirm the status.")]
    [InlineData("IpqcNotPassed", "IPQC has not passed yet.")]
    [InlineData("NoProductionYet", "No production yet — cannot move to FQC.")]
    [InlineData("FqcNotPassed", "FQC has not passed yet.")]
    [InlineData("OqcOrRohsNotMet", "OQC has not passed or RoHS is not OK.")]
    [InlineData("InvalidStepTransition", "Invalid step transition.")]
    public void Legacy_state_machine_error_codes_have_locked_VN_strings(string code, string expected)
    {
        Assert.Equal(expected, WorkOrderErrorLocaliser.LocaliseAdvanceError(code));
    }

    [Fact]
    public void State_conflict_banner_explains_what_operator_should_do()
    {
        var msg = WorkOrderErrorLocaliser.LocaliseAdvanceError("wo.state_conflict");
        // Operator-actionable guidance is the contract — "tap again"
        // is what the client does after adopting the new ETag.
        Assert.Contains("Another operation", msg);
        Assert.Contains("Accept / Start", msg);
        Assert.Contains("latest version", msg);
    }

    [Fact]
    public void If_match_required_banner_tells_operator_to_rescan()
    {
        var msg = WorkOrderErrorLocaliser.LocaliseAdvanceError("wo.if_match_required");
        Assert.Contains("Data session", msg);
        Assert.Contains("scan the WO again", msg);
    }

    [Fact]
    public void Idempotency_key_required_banner_tells_operator_to_call_IT()
    {
        var msg = WorkOrderErrorLocaliser.LocaliseAdvanceError("wo.idempotency_key_required");
        Assert.Contains("contact IT", msg);
    }

    [Fact]
    public void Unknown_advance_error_falls_back_with_the_code()
    {
        var msg = WorkOrderErrorLocaliser.LocaliseAdvanceError("SomeNewServerErrorCode");
        Assert.Contains("Unknown error code", msg);
        Assert.Contains("SomeNewServerErrorCode", msg);
    }

    // ── LocaliseApiError — 4xx envelope codes (ApiException path) ──

    [Theory]
    [InlineData("work_order.not_found", "WO not found on the server.")]
    [InlineData("device.invalid_id", "Invalid device ID — contact IT.")]
    [InlineData("device.not_seen", "Station not recognized yet — try again later.")]
    [InlineData("scan.empty_payload", "Empty scan payload — scan again.")]
    [InlineData("wo.if_match_required", "Data session expired — scan the WO again.")]
    [InlineData("wo.idempotency_key_required", "Request is missing the idempotency key — contact IT.")]
    public void Known_api_error_codes_have_locked_VN_strings(string code, string expected)
    {
        var apiErr = new ApiError { Code = code, MessageEn = "server-side detail" };
        Assert.Equal(expected, WorkOrderErrorLocaliser.LocaliseApiError(400, apiErr));
    }

    [Fact]
    public void Unknown_api_error_falls_back_to_HTTP_diagnostic_string()
    {
        var apiErr = new ApiError { Code = "http.non_success", MessageEn = "500" };
        var msg = WorkOrderErrorLocaliser.LocaliseApiError(500, apiErr);
        Assert.Contains("HTTP 500", msg);
        Assert.Contains("http.non_success", msg);
        Assert.Contains("500", msg);
    }
}
