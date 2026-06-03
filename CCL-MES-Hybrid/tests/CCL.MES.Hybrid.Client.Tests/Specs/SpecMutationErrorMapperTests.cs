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
    [InlineData("duplicate_spec_code", "Mã Spec đã tồn tại — chọn mã khác.")]
    [InlineData("not_found",           "Không tìm thấy Spec (có thể đã bị xoá).")]
    [InlineData("trashed",             "Spec đang ở Thùng rác — khôi phục trước khi thực hiện.")]
    [InlineData("reason_required",     "Lý do Revise phải có ít nhất 5 ký tự.")]
    [InlineData("confirm_mismatch",    "Mã Spec xác nhận chưa đúng — gõ lại chính xác mã hiện tại.")]
    [InlineData("already_trashed",     "Spec đã ở Thùng rác.")]
    [InlineData("not_trashed",         "Spec không ở Thùng rác — không cần khôi phục.")]
    public void Simple_codes_map_to_VN_strings(string code, string expected)
    {
        Assert.Equal(expected, SpecMutationErrorMapper.ToVietnameseMessage(code));
    }

    [Fact]
    public void Validation_with_message_passes_through_messageEn()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("validation", messageEn: "ProductId required");
        Assert.Contains("ProductId required", msg);
        Assert.StartsWith("Dữ liệu chưa hợp lệ", msg);
    }

    [Fact]
    public void Validation_with_blank_messageEn_uses_generic_VN()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("validation");
        Assert.Equal("Dữ liệu chưa hợp lệ.", msg);
    }

    [Theory]
    [InlineData("immutable_status", "Approved",  "Chỉ Bản nháp mới được sửa. (Hiện tại: Approved)")]
    [InlineData("immutable_status", null,        "Chỉ Bản nháp mới được sửa.")]
    [InlineData("invalid_source_status", "Draft",  "Chỉ Spec đã Duyệt hoặc Phát hành mới có thể tạo Revise. (Hiện tại: Draft)")]
    [InlineData("invalid_status",        "Released",  "Chỉ Spec đã Duyệt hoặc Phát hành mới có thể đánh dấu Thay thế. (Hiện tại: Released)")]
    public void Status_aware_codes_append_currentStatus(string code, string? status, string expected)
    {
        Assert.Equal(expected, SpecMutationErrorMapper.ToVietnameseMessage(code, currentStatus: status));
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
        Assert.Contains("vẫn còn Work Order", msg);
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
        Assert.StartsWith("Phiên đăng nhập", SpecMutationErrorMapper.ToVietnameseMessage("auth.invalid_credentials"));
        Assert.StartsWith("Phiên đăng nhập", SpecMutationErrorMapper.ToVietnameseMessage("auth.bad_claim"));
    }

    [Fact]
    public void Http_non_success_includes_status_text()
    {
        var msg = SpecMutationErrorMapper.ToVietnameseMessage("http.non_success", messageEn: "HTTP 503");
        Assert.Contains("HTTP 503", msg);
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
        Assert.Equal("Mã Spec đã tồn tại — chọn mã khác.", msg);
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
}
