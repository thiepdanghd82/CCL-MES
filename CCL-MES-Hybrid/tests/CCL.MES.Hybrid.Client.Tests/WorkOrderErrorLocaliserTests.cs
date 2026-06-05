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
    [InlineData("WorkOrderNotFound", "Không tìm thấy WO.")]
    [InlineData("AlreadyAtFinalStep", "WO đã đến bước cuối (Closed).")]
    [InlineData("RequiresSpecAndMaterials", "Thiếu bản vẽ kỹ thuật hoặc vật tư chưa sẵn sàng.")]
    [InlineData("RequiresSetupConfirmed", "Setup máy chưa được xác nhận. UI xác nhận setup ship trong P10.7c — báo admin/IT để xác nhận trạng thái.")]
    [InlineData("IpqcNotPassed", "IPQC chưa Pass.")]
    [InlineData("NoProductionYet", "Chưa có sản lượng — không thể chuyển sang FQC.")]
    [InlineData("FqcNotPassed", "FQC chưa Pass.")]
    [InlineData("OqcOrRohsNotMet", "OQC chưa Pass hoặc RoHS chưa OK.")]
    [InlineData("InvalidStepTransition", "Chuyển bước không hợp lệ.")]
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
        Assert.Contains("Một thao tác khác", msg);
        Assert.Contains("Nhận / Bắt đầu", msg);
        Assert.Contains("phiên bản mới nhất", msg);
    }

    [Fact]
    public void If_match_required_banner_tells_operator_to_rescan()
    {
        var msg = WorkOrderErrorLocaliser.LocaliseAdvanceError("wo.if_match_required");
        Assert.Contains("Phiên dữ liệu", msg);
        Assert.Contains("quét lại", msg);
    }

    [Fact]
    public void Idempotency_key_required_banner_tells_operator_to_call_IT()
    {
        var msg = WorkOrderErrorLocaliser.LocaliseAdvanceError("wo.idempotency_key_required");
        Assert.Contains("báo IT", msg);
    }

    [Fact]
    public void Unknown_advance_error_falls_back_with_the_code()
    {
        var msg = WorkOrderErrorLocaliser.LocaliseAdvanceError("SomeNewServerErrorCode");
        Assert.Contains("Mã lỗi không xác định", msg);
        Assert.Contains("SomeNewServerErrorCode", msg);
    }

    // ── LocaliseApiError — 4xx envelope codes (ApiException path) ──

    [Theory]
    [InlineData("work_order.not_found", "Không tìm thấy WO trên máy chủ.")]
    [InlineData("device.invalid_id", "Mã thiết bị không hợp lệ — báo IT.")]
    [InlineData("device.not_seen", "Trạm chưa nhận diện được — thử lại sau.")]
    [InlineData("scan.empty_payload", "Payload quét rỗng — quét lại.")]
    [InlineData("wo.if_match_required", "Phiên dữ liệu hết hạn — quét lại WO.")]
    [InlineData("wo.idempotency_key_required", "Yêu cầu thiếu khóa idempotency — báo IT.")]
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
