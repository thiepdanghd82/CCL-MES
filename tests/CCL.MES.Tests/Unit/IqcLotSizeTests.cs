using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P13 bước 4 — cỡ lô cho AQL là SỐ ĐƠN VỊ, không phải con số trong ô "số lượng".
///
/// <para>Khoá cái bẫy đắt nhất của bước này: một lô 5.000 m² thật ra là 3 cuộn.
/// Đưa 5.000 vào bảng AQL thì app đòi lấy 200 mẫu từ 3 cuộn — và người kiểm sẽ
/// học cách bỏ qua mọi con số app đề xuất.</para>
/// </summary>
public sealed class IqcLotSizeTests
{
    [Theory]
    [InlineData("pcs", true)]
    [InlineData("PCS", true)]
    [InlineData("rolls", true)]
    [InlineData("Roll", true)]
    [InlineData("cuộn", true)]
    [InlineData("sheet", true)]
    [InlineData("can", true)]
    [InlineData("m", false)]
    [InlineData("m2", false)]
    [InlineData("m²", false)]
    [InlineData("kg", false)]
    [InlineData("L", false)]
    public void Nhan_dien_don_vi_dem_duoc(string uom, bool countable)
        => Assert.Equal(countable, IqcLotSize.IsCountable(uom));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("blister")]
    [InlineData("xyz")]
    public void Don_vi_la_thi_noi_KHONG_BIET_chu_khong_doan(string? uom)
    {
        // Đoán "liên tục" thì mất đề xuất một cách im lặng; đoán "đếm được" thì
        // đề xuất một con số dựa trên đơn vị mình không hiểu. Cả hai đều tệ hơn
        // null.
        Assert.Null(IqcLotSize.IsCountable(uom));
        Assert.Null(IqcLotSize.For(100, uom));
        Assert.Null(IqcLotSize.SuggestSampleSize(100, uom));
    }

    [Fact]
    public void Don_vi_lien_tuc_KHONG_de_xuat_co_mau()
    {
        // 5.000 m² là 3 cuộn chứ không phải 5.000 đơn vị. Bảng AQL không có gì
        // để nói ở đây, và im lặng là câu trả lời đúng.
        Assert.Null(IqcLotSize.For(5000, "m2"));
        Assert.Null(IqcLotSize.SuggestSampleSize(5000, "m2"));
        Assert.Null(IqcLotSize.SuggestSampleSize(250.5, "kg"));
        Assert.Null(IqcLotSize.SuggestSampleSize(50, "L"));
    }

    [Fact]
    public void Lam_tron_XUONG_vi_khong_kiem_duoc_nua_cuon()
    {
        Assert.Equal(10L, IqcLotSize.For(10.7, "rolls"));
        Assert.Equal(4L, IqcLotSize.For(4.0, "rolls"));
        Assert.Null(IqcLotSize.For(0.5, "rolls"));   // nửa cuộn không phải một cỡ lô
        Assert.Null(IqcLotSize.For(0, "pcs"));
        Assert.Null(IqcLotSize.For(-3, "pcs"));
    }

    [Fact]
    public void Trung_voi_con_so_NGUOI_KIEM_da_ghi_tay_tren_live()
    {
        // Phiếu thật trên live: pcs, lô 100, người kiểm ghi cỡ mẫu 20.
        // Bậc 6 của bảng (91–150) đúng là 20 — luật khớp hành vi có sẵn, không
        // phải luật mới áp lên người ta.
        Assert.Equal(20, IqcLotSize.SuggestSampleSize(100, "pcs"));
    }

    [Fact]
    public void Cat_ngon_theo_co_lo_that()
    {
        // Lô 4 cuộn: bậc 1 cho 2 mẫu, và 2 < 4 nên lấy 2.
        Assert.Equal(2, IqcLotSize.SuggestSampleSize(4, "rolls"));
        // Lô 1 cuộn: bảng nói 2, nhưng không ai lấy 2 cuộn ra khỏi lô 1 cuộn.
        Assert.Equal(1, IqcLotSize.SuggestSampleSize(1, "rolls"));
    }

    // ── luật đòi lý do ───────────────────────────────────────────────────

    [Fact]
    public void Doi_khac_de_xuat_thi_phai_ghi_ly_do()
    {
        Assert.True(IqcLotSize.NeedsReason(20, 10));   // nới — giảm bảo đảm
        Assert.True(IqcLotSize.NeedsReason(20, 32));   // siết — tốn công xưởng
        Assert.False(IqcLotSize.NeedsReason(20, 20));
    }

    [Fact]
    public void KHONG_de_xuat_duoc_thi_KHONG_doi_ly_do()
    {
        // Bắt giải trình cho sai lệch so với con số app chưa từng đưa ra là vô
        // nghĩa — và nó sẽ dạy người dùng gõ bừa vào ô lý do.
        Assert.False(IqcLotSize.NeedsReason(null, 7));
        Assert.False(IqcLotSize.NeedsReason(null, 0));
    }
}
