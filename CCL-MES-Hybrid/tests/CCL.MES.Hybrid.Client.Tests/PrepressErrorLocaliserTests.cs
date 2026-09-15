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
    [InlineData("wo.not_found", "Không thấy lệnh sản xuất này trên máy chủ — quét lại mã WO.")]
    [InlineData("wo.material_row_not_found", "Không thấy dòng vật tư này — nạp lại danh sách.")]
    [InlineData("wo.invalid_phase", "Lệnh không còn ở bước chuẩn bị nên không ghi kiểm được — nạp lại màn hình.")]
    [InlineData("wo.if_match_required", "Dữ liệu trên màn hình đã cũ — nạp lại danh sách rồi làm lại.")]
    [InlineData("wo.idempotency_key_required", "Yêu cầu thiếu khoá chống trùng — báo IT.")]
    [InlineData("prepress.invalid_status", "Trạng thái không hợp lệ — chỉ nhận Chờ / OK / NG.")]
    [InlineData("prepress.invalid_reason_code", "Mã lỗi NG không có trong danh mục — chọn một mã trong danh sách.")]
    [InlineData("prepress.invalid_ng_note", "Đánh NG thì bắt buộc ghi mô tả (1–500 ký tự).")]
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
        Assert.Contains("bước chuẩn bị", msg);
        Assert.Contains("không ghi kiểm được", msg);
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
    [InlineData("wo.state_conflict", "Danh sách vừa được cập nhật ở nơi khác. Màn hình đang lấy lại trạng thái mới — ghi lại.")]
    [InlineData("wo.if_match_required", "Data session has not been reloaded — scan the WO again.")]
    [InlineData("wo.idempotency_key_required", "Yêu cầu thiếu khoá chống trùng — báo IT.")]
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
