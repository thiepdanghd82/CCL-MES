using CCL.MES.Hybrid.Client.Routing;
using CCL.MES.Shared.Envelopes;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// P11-3 — lock mọi banner VN operator thấy trên LegsDashboard. Đổi 1
/// chuỗi → test fail → màn hình không drift âm thầm.
/// </summary>
public sealed class RoutingErrorLocaliserTests
{
    [Theory]
    [InlineData("wo.not_found", "Không tìm thấy WO trên máy chủ.")]
    [InlineData("leg.not_found", "Không tìm thấy công đoạn (leg) này — tải lại danh sách.")]
    [InlineData("leg.inputs_not_ready", "Chưa thể chạy: bán thành phẩm (in/tape) chưa xong hoặc thiếu số lượng.")]
    [InlineData("leg.invalid_phase", "Không thể chuyển công đoạn sang trạng thái này — tải lại.")]
    [InlineData("leg.invalid_reason", "Cần nhập lý do rework (1-500 ký tự).")]
    [InlineData("routing.unmapped", "Có công đoạn trong routing chưa map được — cần người duyệt cấu hình, KHÔNG tự đoán.")]
    public void Locked_VN_banner_for_api_error(string code, string expected)
    {
        var msg = RoutingErrorLocaliser.LocaliseApiError(422, new ApiError { Code = code, MessageEn = "x" });
        Assert.Equal(expected, msg);
    }

    [Theory]
    [InlineData("wo.state_conflict", "Công đoạn này vừa được cập nhật bởi thao tác khác. Đang tải trạng thái mới — thử lại.")]
    [InlineData("http.empty_body", "Máy chủ trả về rỗng — báo IT.")]
    public void Locked_VN_banner_for_set_error(string code, string expected)
        => Assert.Equal(expected, RoutingErrorLocaliser.LocaliseSetError(code));

    [Fact]
    public void Unknown_code_falls_through_with_diagnostic()
    {
        var msg = RoutingErrorLocaliser.LocaliseApiError(500, new ApiError { Code = "x.y", MessageEn = "boom" });
        Assert.Contains("500", msg);
        Assert.Contains("x.y", msg);
    }
}
