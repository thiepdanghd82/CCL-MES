using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Ledger Excel ghi ngày nhập kho ở hai dạng: Roll đủ <c>dd/MM/yyyy</c>, PCS
/// rút gọn <c>d-MMM</c>. Cả hai phải ra cùng một mốc để hạn dùng (+365) đúng.
/// </summary>
public class IqcWarehouseInDateTests
{
    private static readonly DateTime Received = new(2026, 1, 5);

    [Theory]
    [InlineData("18/08/2026", 2026, 8, 18)]
    [InlineData("2/1/2026", 2026, 1, 2)]
    [InlineData("2026-01-02", 2026, 1, 2)]
    public void Doc_duoc_dang_du_ngay_thang_nam(string raw, int y, int m, int d)
        => Assert.Equal(new DateTime(y, m, d), IqcWarehouseInDate.Parse(raw, Received));

    [Fact]
    public void Dang_rut_gon_PCS_muon_nam_cua_ngay_ve()
        => Assert.Equal(new DateTime(2026, 1, 5), IqcWarehouseInDate.Parse("5-Jan", Received));

    [Fact]
    public void Dang_rut_gon_cuoi_nam_chon_nam_gan_ngay_ve_nhat()
    {
        // Nhập kho 31-Dec, kiểm 02-Jan năm sau — năm phải lùi lại một, không
        // được cộng thêm 363 ngày vào hạn dùng.
        var received = new DateTime(2026, 1, 2);
        Assert.Equal(new DateTime(2025, 12, 31), IqcWarehouseInDate.Parse("31-Dec", received));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("n/a")]
    public void O_trong_hoac_rac_thi_null_khong_doan(string? raw)
        => Assert.Null(IqcWarehouseInDate.Parse(raw, Received));

    [Theory]
    [InlineData("01/01/1900")]   // ô trống → Excel serial 0
    [InlineData("18/01/1900")]
    public void Moc_serial_1900_cua_Excel_khong_phai_ngay_that(string raw)
        => Assert.Null(IqcWarehouseInDate.Parse(raw, new DateTime(2026, 6, 3)));

    [Fact]
    public void Han_dung_bang_ngay_nhap_kho_cong_365()
    {
        Assert.Equal(new DateTime(2027, 1, 5), IqcWarehouseInDate.Expiry(new DateTime(2026, 1, 5)));
        Assert.Null(IqcWarehouseInDate.Expiry(null));
    }
}
