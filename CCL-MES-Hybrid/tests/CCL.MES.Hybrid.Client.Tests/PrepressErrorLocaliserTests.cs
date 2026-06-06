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
    [InlineData("wo.not_found", "Không tìm thấy WO trên máy chủ.")]
    [InlineData("wo.material_row_not_found", "Không tìm thấy dòng vật tư — tải lại checklist.")]
    [InlineData("wo.invalid_phase", "WO không ở giai đoạn PREPRESS — không thể ghi check.")]
    [InlineData("wo.if_match_required", "Phiên dữ liệu hết hạn — tải lại checklist.")]
    [InlineData("wo.idempotency_key_required", "Yêu cầu thiếu khóa idempotency — báo IT.")]
    [InlineData("prepress.invalid_status", "Trạng thái không hợp lệ — chỉ chấp nhận Pending / OK / NG.")]
    [InlineData("prepress.invalid_reason_code", "Mã lỗi NG không có trong danh mục Scrap — chọn mã hợp lệ.")]
    [InlineData("prepress.invalid_ng_note", "Ghi chú NG bắt buộc khi đặt NG (1-500 ký tự).")]
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
        Assert.Contains("không thể ghi check", msg);
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
    [InlineData("wo.state_conflict", "Một thao tác khác đã cập nhật checklist này. Đang tải lại trạng thái mới nhất — thử ghi lại.")]
    [InlineData("wo.if_match_required", "Phiên dữ liệu chưa được tải lại — quét lại WO.")]
    [InlineData("wo.idempotency_key_required", "Yêu cầu thiếu khóa idempotency — báo IT.")]
    [InlineData("http.empty_body", "Máy chủ trả về phản hồi rỗng — báo IT.")]
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
