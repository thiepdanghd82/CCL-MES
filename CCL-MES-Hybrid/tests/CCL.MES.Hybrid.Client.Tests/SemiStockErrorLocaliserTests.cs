using CCL.MES.Hybrid.Client.Routing;
using CCL.MES.Shared.Envelopes;
using Xunit;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// P11.5-3 — lock mọi banner VN operator thấy trên kho bán thành phẩm +
/// reserve/consume. Đổi 1 chuỗi → test fail → UI không drift âm thầm.
/// Wire-mirror: mỗi code khớp 1 nhánh SemiStockController.
/// </summary>
public sealed class SemiStockErrorLocaliserTests
{
    [Theory]
    [InlineData("semi.invalid_lot_no", "Mã lô (LotNo) bắt buộc.")]
    [InlineData("semi.invalid_kind", "Loại bán thành phẩm phải là PRINTED_SEMI hoặc TAPE_SEMI.")]
    [InlineData("semi.invalid_qty", "Số lượng phải lớn hơn 0.")]
    [InlineData("semi.not_assembly", "Chỉ công đoạn DÁN (assembly) mới xuất kho bán thành phẩm.")]
    [InlineData("semi.leg_in_line", "Công đoạn này là IN_LINE — dùng bán thành phẩm cùng WO, không xuất kho.")]
    [InlineData("semi.lot_not_found", "Không tìm thấy lô này trong kho — kiểm tra lại mã lô.")]
    [InlineData("semi.insufficient_stock", "Kho không đủ bán thành phẩm — cần nhập thêm lô (không giữ một phần).")]
    [InlineData("semi.nothing_reserved", "Chưa giữ lô nào cho công đoạn này — reserve trước khi hoàn tất.")]
    public void Locked_VN_banner_for_api_error(string code, string expected)
    {
        var msg = SemiStockErrorLocaliser.LocaliseApiError(422, new ApiError { Code = code, MessageEn = "x" });
        Assert.Equal(expected, msg);
    }

    [Theory]
    [InlineData("semi.lot_exists", "Mã lô này đã tồn tại trong kho.")]
    [InlineData("semi.lot_conflict", "Lô vừa bị cập nhật bởi thao tác khác. Đang tải lại kho — thử lại.")]
    [InlineData("http.empty_body", "Máy chủ trả về rỗng — báo IT.")]
    public void Locked_VN_banner_for_set_error(string code, string expected)
        => Assert.Equal(expected, SemiStockErrorLocaliser.LocaliseSetError(code));

    [Fact]
    public void Unknown_api_error_falls_back_to_diagnostic()
    {
        var msg = SemiStockErrorLocaliser.LocaliseApiError(500, new ApiError { Code = "x.y", MessageEn = "boom" });
        Assert.Contains("500", msg);
        Assert.Contains("x.y", msg);
    }
}
