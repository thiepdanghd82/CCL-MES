using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Cỡ mẫu FAI của IPQC lấy theo SỐ CAVITY của một shot (Thiệp chốt 2026-09-11),
/// KHÔNG theo cỡ lô kiểu ISO 2859-1 như IQC.
///
/// <para>Mỗi ca dưới đây dựng từ một hình dạng CÓ THẬT trong dữ liệu — con số
/// trích ở doc-comment của <see cref="IpqcCavityPlan"/>, không phải ca tưởng
/// tượng.</para>
/// </summary>
public class IpqcCavityPlanTests
{
    // ── Công đoạn IN ────────────────────────────────────────────────────────

    [Fact]
    public void In_co_cavity_thi_lay_dung_so_va_ghi_nguon()
    {
        var r = IpqcCavityPlan.FromPrint(12);
        Assert.Equal(12, r.Count);
        Assert.Equal(IpqcCavityPlan.SourcePrint, r.Source);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-3)]
    public void In_chua_dien_cavity_thi_KHONG_mac_dinh_1(int? cavity)
    {
        // Mặc định 1 sẽ biến "kiểm đủ cavity" thành "kiểm một con", mà hồ sơ
        // vẫn trông như đã kiểm đủ. Đo được: 12% spec in chưa điền cavity.
        var r = IpqcCavityPlan.FromPrint(cavity);
        Assert.Null(r.Count);                                    // ← đỏ nếu ai đó cho mặc định
        Assert.Equal(IpqcCavityPlan.SourceMissing, r.Source);
    }

    // ── Công đoạn CẮT ───────────────────────────────────────────────────────

    [Fact]
    public void Cat_moi_dong_cung_mot_gia_tri_thi_lay_gia_tri_do()
    {
        // Hình dạng thật: 179/238 spec chỉ có đúng một giá trị cavity cắt.
        var r = IpqcCavityPlan.FromCut(new int?[] { 5, 5, 5 });
        Assert.Equal(5, r.Count);
        Assert.Equal(IpqcCavityPlan.SourceCut, r.Source);
    }

    [Fact]
    public void Cat_NHIEU_gia_tri_khac_nhau_thi_KHONG_chon_ho()
    {
        // Hình dạng thật: một spec có DRILL HOLE cavity 5, MANUAL TAPPING
        // cavity 2, PRESS vừa 5 vừa 2 — nên ngay cả cột Process cũng không
        // tách được. Chọn bừa 2 thì người kiểm soi 2 con trong khi khuôn có 5,
        // và hồ sơ ghi là đã kiểm đủ. 59/238 spec rơi vào đây.
        var r = IpqcCavityPlan.FromCut(new int?[] { 5, 2, 5, 2 });
        Assert.Null(r.Count);                                    // ← đỏ nếu ai đó lấy Max/First
        Assert.Equal(IpqcCavityPlan.SourceAmbiguous, r.Source);
    }

    [Fact]
    public void Cat_bo_qua_gia_tri_rong_va_khong_duong()
    {
        // Chỉ một giá trị dương thật ⇒ vẫn là rõ ràng, không phải mơ hồ.
        var r = IpqcCavityPlan.FromCut(new int?[] { null, 0, 8, null });
        Assert.Equal(8, r.Count);
        Assert.Equal(IpqcCavityPlan.SourceCut, r.Source);
    }

    [Fact]
    public void Cat_khong_co_dong_nao_thi_Missing()
    {
        var r = IpqcCavityPlan.FromCut(Array.Empty<int?>());
        Assert.Null(r.Count);
        Assert.Equal(IpqcCavityPlan.SourceMissing, r.Source);
    }

    [Fact]
    public void Cat_co_dong_nhung_toan_rong_thi_Missing()
    {
        var r = IpqcCavityPlan.FromCut(new int?[] { null, 0 });
        Assert.Null(r.Count);
        Assert.Equal(IpqcCavityPlan.SourceMissing, r.Source);
    }

    [Fact]
    public void Truyen_null_ca_danh_sach_thi_Missing_chu_khong_no()
    {
        var r = IpqcCavityPlan.FromCut(null);
        Assert.Null(r.Count);
        Assert.Equal(IpqcCavityPlan.SourceMissing, r.Source);
    }

    // ── Chọn đúng công đoạn cho từng hạng mục ───────────────────────────────

    [Theory]
    [InlineData("LABEL")]
    [InlineData("DIGITAL")]
    [InlineData("SILK")]
    public void Hang_muc_dong_IN_lay_cavity_IN(string line)
    {
        var plan = new IpqcCavityPlan.Plan(
            IpqcCavityPlan.FromPrint(12),
            IpqcCavityPlan.FromCut(new int?[] { 5 }));

        var r = IpqcCavityPlan.For(plan, line);
        Assert.Equal(12, r.Count);                               // 12 chứ không phải 5
        Assert.Equal(IpqcCavityPlan.SourcePrint, r.Source);
    }

    [Theory]
    [InlineData("PRESS_CNC")]
    [InlineData("FINISHING")]
    public void Hang_muc_dong_CAT_lay_cavity_CAT(string line)
    {
        var plan = new IpqcCavityPlan.Plan(
            IpqcCavityPlan.FromPrint(12),
            IpqcCavityPlan.FromCut(new int?[] { 5 }));

        var r = IpqcCavityPlan.For(plan, line);
        Assert.Equal(5, r.Count);                                // 5 chứ không phải 12
        Assert.Equal(IpqcCavityPlan.SourceCut, r.Source);
    }

    [Fact]
    public void Hang_muc_KHONG_thuoc_in_lan_cat_thi_khong_gan_cavity_bua()
    {
        var plan = new IpqcCavityPlan.Plan(
            IpqcCavityPlan.FromPrint(12),
            IpqcCavityPlan.FromCut(new int?[] { 5 }));

        var r = IpqcCavityPlan.For(plan, "NONE");
        Assert.Null(r.Count);
        Assert.Equal(IpqcCavityPlan.SourceNotApplicable, r.Source);
    }

    [Fact]
    public void Line_lay_tu_CO_tick_box_van_duoc_dong_dau_dung_cong_doan()
    {
        // PRESS_CNC không có dòng thư viện riêng — nó mượn hạng mục của LABEL
        // qua cờ SheetCut. Nhưng QcLineLibrarySelector đóng dấu ProcessLine =
        // LINE ĐÃ RESOLVE (PRESS_CNC), nên cavity phải lấy bên CẮT.
        var plan = new IpqcCavityPlan.Plan(
            IpqcCavityPlan.FromPrint(12),
            IpqcCavityPlan.FromCut(new int?[] { 5 }));

        Assert.Equal(5, IpqcCavityPlan.For(plan, "PRESS_CNC").Count);
    }

    // ── Phân loại công đoạn ─────────────────────────────────────────────────

    [Theory]
    [InlineData("label", QcProcessKind.Print)]
    [InlineData("  SILK  ", QcProcessKind.Print)]
    [InlineData("press_cnc", QcProcessKind.Cut)]
    [InlineData("", QcProcessKind.Other)]
    [InlineData(null, QcProcessKind.Other)]
    [InlineData("KHONG-BIET", QcProcessKind.Other)]
    public void Phan_loai_cong_doan_khong_phan_biet_hoa_thuong_va_khong_doan(
        string? line, string expected)
        => Assert.Equal(expected, QcProcessKind.Of(line));
}
