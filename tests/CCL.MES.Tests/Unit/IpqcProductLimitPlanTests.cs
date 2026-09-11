using CCL.MES.Application.Services;
using CCL.MES.Domain;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Ngưỡng số theo SẢN PHẨM bơm vào hạng mục thư viện theo DÒNG SẢN XUẤT
/// (Thiệp chốt 2026-09-11).
///
/// <para>Luật khớp phải đủ hai vế: cùng <c>LibraryItemKey</c> VÀ stage của kế
/// hoạch hợp công đoạn của hạng mục. Mỗi ca dưới đây khoá một cách mà việc
/// khớp có thể sai mà vẫn "ra số" — tức sai mà không ai nhìn thấy.</para>
/// </summary>
public class IpqcProductLimitPlanTests
{
    private static IpqcProductLimitPlan.Criterion C(
        string key, QcStage stage, double? low = 19.5, double? up = 20.5,
        long windowId = 7, double? nom = 20, string? unit = "mm")
        => new(key, windowId, stage, low, up, nom, unit);

    // ── Khớp đúng ───────────────────────────────────────────────────────────

    [Fact]
    public void Khop_dung_ma_va_dung_cong_doan_thi_lay_nguong()
    {
        var r = IpqcProductLimitPlan.For(
            new[] { C("LBL-B1", QcStage.IpqcPrint) }, "LBL-B1", "LABEL");

        Assert.True(r.HasBound);
        Assert.Equal(19.5, r.Low!.Value, 3);
        Assert.Equal(20.5, r.Up!.Value, 3);
        Assert.Equal(20, r.Nominal!.Value, 3);
        Assert.Equal("mm", r.Unit);
        Assert.Equal(7, r.SourceWindowId);       // truy ngược được về kế hoạch nào
    }

    [Fact]
    public void Khong_phan_biet_hoa_thuong_va_khoang_trang_thua()
    {
        var r = IpqcProductLimitPlan.For(
            new[] { C("  lbl-b1 ", QcStage.IpqcPrint) }, "LBL-B1", "LABEL");
        Assert.True(r.HasBound);
    }

    [Theory]
    [InlineData(QcStage.IpqcCut, "PRESS_CNC")]
    [InlineData(QcStage.IpqcCut, "FINISHING")]
    [InlineData(QcStage.IpqcPrint, "DIGITAL")]
    [InlineData(QcStage.IpqcPrint, "SILK")]
    public void Stage_cua_ke_hoach_phai_hop_cong_doan_cua_hang_muc(QcStage st, string line)
    {
        var r = IpqcProductLimitPlan.For(new[] { C("X-1", st) }, "X-1", line);
        Assert.True(r.HasBound);
    }

    // ── KHÔNG khớp — mỗi ca là một cách sai mà vẫn "ra số" ──────────────────

    [Fact]
    public void Ke_hoach_khau_IN_KHONG_bom_nguong_sang_hang_muc_khau_CAT()
    {
        // Cùng tên hạng mục nhưng khác dung sai: bế 5 cavity và in 12 cavity
        // không dùng chung một khoảng. Thiếu vế stage là bơm nhầm mà vẫn ra số.
        var r = IpqcProductLimitPlan.For(
            new[] { C("LBL-B1", QcStage.IpqcPrint) }, "LBL-B1", "PRESS_CNC");
        Assert.False(r.HasBound);                // ← đỏ nếu bỏ phép kiểm stage
    }

    [Theory]
    [InlineData(QcStage.Fqc)]
    [InlineData(QcStage.Oqc)]
    public void Ke_hoach_FQC_OQC_KHONG_ap_cho_khau_trong_chuyen(QcStage st)
    {
        var r = IpqcProductLimitPlan.For(new[] { C("LBL-B1", st) }, "LBL-B1", "LABEL");
        Assert.False(r.HasBound);
    }

    [Fact]
    public void Khac_ma_thi_khong_dinh_vao_nhau()
    {
        var r = IpqcProductLimitPlan.For(
            new[] { C("LBL-B1", QcStage.IpqcPrint) }, "LBL-B4", "LABEL");
        Assert.False(r.HasBound);
    }

    [Fact]
    public void Tieu_chi_KHONG_gan_hang_muc_thu_vien_thi_bi_bo_qua()
    {
        // Hợp lệ: kỹ sư ghi một tiêu chí riêng của sản phẩm chưa có trong thư
        // viện. Nó chỉ không bơm ngưỡng vào đâu được.
        var r = IpqcProductLimitPlan.For(
            new[] { C(null!, QcStage.IpqcPrint) }, "LBL-B1", "LABEL");
        Assert.False(r.HasBound);
    }

    [Fact]
    public void Tieu_chi_khong_co_can_nao_thi_khong_tinh_la_nguong()
    {
        var r = IpqcProductLimitPlan.For(
            new[] { C("LBL-B1", QcStage.IpqcPrint, low: null, up: null) }, "LBL-B1", "LABEL");
        Assert.False(r.HasBound);
        Assert.Null(r.SourceWindowId);
    }

    // ── Mâu thuẫn trong spec ────────────────────────────────────────────────

    [Fact]
    public void HAI_tieu_chi_cung_tro_ve_mot_hang_muc_thi_KHONG_lay_cai_nao()
    {
        // Hai kế hoạch cùng nói về "kích thước tổng thể" với hai dung sai khác
        // nhau là mâu thuẫn trong chính spec. Chọn bừa một cái là giấu mâu
        // thuẫn đi rồi đóng dấu nó vào hồ sơ đã ký.
        var r = IpqcProductLimitPlan.For(new[]
        {
            C("LBL-B1", QcStage.IpqcPrint, 19.5, 20.5, windowId: 7),
            C("LBL-B1", QcStage.IpqcPrint, 19.0, 21.0, windowId: 9),
        }, "LBL-B1", "LABEL");

        Assert.False(r.HasBound);                // ← đỏ nếu ai đó lấy First()
    }

    [Fact]
    public void Trung_ma_nhung_KHAC_cong_doan_thi_van_ro_rang()
    {
        // Không phải mâu thuẫn: một tiêu chí cho khâu in, một cho khâu cắt.
        var set = new[]
        {
            C("X-1", QcStage.IpqcPrint, 19.5, 20.5, windowId: 7),
            C("X-1", QcStage.IpqcCut,   4.5,  5.5,  windowId: 8),
        };
        Assert.Equal(20.5, IpqcProductLimitPlan.For(set, "X-1", "LABEL").Up!.Value, 3);
        Assert.Equal(5.5,  IpqcProductLimitPlan.For(set, "X-1", "PRESS_CNC").Up!.Value, 3);
    }

    // ── Đầu vào rỗng ────────────────────────────────────────────────────────

    [Fact]
    public void Chua_ai_soan_ke_hoach_nao_thi_tra_None_chu_khong_no()
    {
        Assert.False(IpqcProductLimitPlan.For(null, "LBL-B1", "LABEL").HasBound);
        Assert.False(IpqcProductLimitPlan.For(
            Array.Empty<IpqcProductLimitPlan.Criterion>(), "LBL-B1", "LABEL").HasBound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Hang_muc_khong_co_ma_thi_khong_khop_bua(string? itemKey)
    {
        var r = IpqcProductLimitPlan.For(
            new[] { C("LBL-B1", QcStage.IpqcPrint) }, itemKey, "LABEL");
        Assert.False(r.HasBound);
    }
}
