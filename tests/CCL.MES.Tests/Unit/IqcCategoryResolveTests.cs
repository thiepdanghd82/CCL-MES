using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P13 bước 4 — bộ chuẩn theo NHÓM và hình dạng hạng mục.
///
/// <para>Khoá cái lỗ đã ĐO ĐƯỢC trên live 2026-09-05: 0/7.212 dòng tiêu chuẩn
/// kê ô đếm lỗi, và không ô nào nằm trong ma trận mặc định — 30 hạng mục đếm
/// lỗi nhập ở bước 1 không có đường nào tới phiếu. Test này khoá đường thứ hai
/// (theo nhóm) và bảo đảm nó KHÔNG rò sang nhóm khác.</para>
/// </summary>
public sealed class IqcCategoryResolveTests
{
    private static IqcCheckItemLibrary L(
        string id, string vi, int sort, IqcMaterialCategory cat, IqcCheckKind kind,
        int measure = 0, bool matrix = false) => new()
    {
        ItemId = id, GroupCode = "NQ", GroupLabelVi = "Ngoại quan", GroupLabelEn = "Appearance",
        ItemVi = vi, ItemEn = vi + " (EN)", Sort = sort, Active = true,
        Category = cat, Kind = kind, MeasureCount = measure, InDefaultMatrix = matrix,
    };

    /// <summary>Thư viện thu nhỏ giữ đúng HÌNH DẠNG của live: hạng mục theo-mã
    /// mang <c>Any</c>, hạng mục theo-nhóm mang nhóm của nó và Sort lớn hơn hẳn
    /// (live: Any 10–210 · theo nhóm 1010–1300).</summary>
    private static List<IqcCheckItemLibrary> Lib() =>
    [
        L("NL-01", "Nhận dạng vật liệu", 10,  IqcMaterialCategory.Any,  IqcCheckKind.Verdict, matrix: true),
        L("KT-03", "Chiều rộng",         30,  IqcMaterialCategory.Any,  IqcCheckKind.Measure, measure: 5),
        L("KT-04", "Độ dày",             40,  IqcMaterialCategory.Any,  IqcCheckKind.Measure, measure: 5),
        L("BD-01", "Độ bám dính",        50,  IqcMaterialCategory.Any,  IqcCheckKind.Measure, measure: 1),
        L("MT-01", "Hồ sơ HSF",          60,  IqcMaterialCategory.Any,  IqcCheckKind.Document),
        L("RD-01", "Nhăn / Hằn",       1010,  IqcMaterialCategory.Roll, IqcCheckKind.DefectCount),
        L("RD-02", "Xước",             1020,  IqcMaterialCategory.Roll, IqcCheckKind.DefectCount),
        L("PD-01", "Nhăn",             1140,  IqcMaterialCategory.Pcs,  IqcCheckKind.DefectCount),
        L("CD-01", "Không rách",       1280,  IqcMaterialCategory.Chem, IqcCheckKind.Verdict),
    ];

    private static List<IqcMaterialSpec> Specs() =>
    [
        new() { SpecNo = "S-ROLL", MaterialCode = "336-H1a", Active = true },
    ];

    private static List<IqcSpecItem> SpecItems() =>
    [
        new() { SpecNo = "S-ROLL", ItemId = "NL-01", Seq = 1, Active = true,
                AcceptanceVi = "Theo mẫu chuẩn" },
        new() { SpecNo = "S-ROLL", ItemId = "KT-03", Seq = 1, Active = true,
                AcceptanceVi = "220 ± 2 mm", LimitLow = 218, LimitUp = 222,
                LimitUnit = "mm", LimitParsed = true },
        new() { SpecNo = "S-ROLL", ItemId = "BD-01", Seq = 1, Active = true,
                AcceptanceVi = "≥ 10.0 N/25mm or tear", LimitLow = 10.0,
                LimitUnit = "N/25mm", LimitLabel = "Face", TearIsPass = true,
                LimitParsed = true },
        new() { SpecNo = "S-ROLL", ItemId = "MT-01", Seq = 1, Active = true,
                AcceptanceVi = "Có hồ sơ HSF còn hạn" },
    ];

    private static IqcCheckResolver.Result Run(IqcMaterialCategory cat, string code = "336-H1a")
        => IqcCheckResolver.Resolve(code, cat, Specs(), SpecItems(), Lib());

    // ── bộ chuẩn theo nhóm phải tới được phiếu ───────────────────────────

    [Fact]
    public void Phieu_cuon_nhan_du_o_dem_loi_du_KHONG_spec_nao_ke()
    {
        var r = Run(IqcMaterialCategory.Roll);
        var keys = r.Items.Select(i => i.ItemKey).ToList();

        // Không dòng spec nào kê RD-01/RD-02 — chúng phải tới bằng đường NHÓM.
        Assert.DoesNotContain(SpecItems(), x => x.ItemId.StartsWith("RD-"));
        Assert.Contains("RD-01", keys);
        Assert.Contains("RD-02", keys);
        Assert.All(r.Items.Where(i => i.ItemKey.StartsWith("RD-")),
            i => Assert.True(i.FromCategoryStandard));
    }

    [Fact]
    public void O_dem_loi_KHONG_ro_sang_nhom_khac()
    {
        var roll = Run(IqcMaterialCategory.Roll).Items.Select(i => i.ItemKey).ToList();
        var chem = Run(IqcMaterialCategory.Chem).Items.Select(i => i.ItemKey).ToList();

        Assert.DoesNotContain("PD-01", roll);
        Assert.DoesNotContain("CD-01", roll);
        Assert.DoesNotContain("RD-01", chem);
        Assert.Contains("CD-01", chem);
    }

    [Fact]
    public void Khong_suy_duoc_nhom_thi_KHONG_dung_bua_bo_nao()
    {
        // Any = KHÔNG BIẾT. Dựng bừa 13 ô đếm lỗi cho một can mực bắt người kiểm
        // bấm qua từng ô vô nghĩa; im lặng ở đây là câu trả lời đúng.
        var r = Run(IqcMaterialCategory.Any);
        Assert.DoesNotContain(r.Items, i => i.FromCategoryStandard);
        Assert.Contains(r.Items, i => i.ItemKey == "NL-01");   // phần theo-mã vẫn còn
    }

    [Fact]
    public void Bo_chuan_nhom_xep_SAU_hang_muc_theo_ma()
    {
        var r = Run(IqcMaterialCategory.Roll);
        var firstStd = r.Items.First(i => i.FromCategoryStandard).Sort;
        Assert.All(r.Items.Where(i => !i.FromCategoryStandard),
            i => Assert.True(i.Sort < firstStd,
                $"{i.ItemKey} sort {i.Sort} phải nhỏ hơn {firstStd}"));
    }

    // ── hình dạng + ngưỡng phải đi theo hạng mục ─────────────────────────

    [Fact]
    public void Hang_muc_do_mang_so_lan_do_va_nguong_cua_SPEC()
    {
        var kt = Run(IqcMaterialCategory.Roll).Items.Single(i => i.ItemKey == "KT-03");
        Assert.Equal(IqcCheckKind.Measure, kt.Kind);
        Assert.Equal(5, kt.MeasureCount);
        Assert.Equal(218, kt.LimitLow);
        Assert.Equal(222, kt.LimitUp);
        Assert.Equal("mm", kt.LimitUnit);
    }

    [Fact]
    public void Nguong_or_tear_va_nhan_lop_do_duoc_giu_nguyen()
    {
        var bd = Run(IqcMaterialCategory.Roll).Items.Single(i => i.ItemKey == "BD-01");
        Assert.Equal(10.0, bd.LimitLow);
        Assert.Null(bd.LimitUp);              // "≥" chỉ có cận dưới
        Assert.True(bd.TearIsPass);
        Assert.Equal("Face", bd.LimitLabel);  // đo lớp nào — phân biệt Face/Adhesive
    }

    [Fact]
    public void Hang_muc_theo_nhom_KHONG_co_nguong_so()
    {
        // Không có dòng spec ⇒ không có con số nào ⇒ máy phải nhường người chấm.
        // Bịa ra một cận ở đây là bịa ra tiêu chuẩn.
        var rd = Run(IqcMaterialCategory.Roll).Items.Single(i => i.ItemKey == "RD-01");
        Assert.Equal(IqcCheckKind.DefectCount, rd.Kind);
        Assert.Null(rd.LimitLow);
        Assert.Null(rd.LimitUp);
        Assert.Equal(0, rd.MeasureCount);
    }

    [Fact]
    public void So_lan_do_chi_co_nghia_voi_hang_muc_kieu_Measure()
    {
        var mt = Run(IqcMaterialCategory.Roll).Items.Single(i => i.ItemKey == "MT-01");
        Assert.Equal(IqcCheckKind.Document, mt.Kind);
        Assert.Equal(0, mt.MeasureCount);
    }

    // ── khử trùng: tiêu chuẩn theo-MÃ thắng bộ chuẩn nhóm ────────────────

    [Fact]
    public void Spec_ke_trung_hang_muc_nhom_thi_ban_theo_MA_thang()
    {
        var specItems = SpecItems().Concat([
            new IqcSpecItem { SpecNo = "S-ROLL", ItemId = "RD-01", Seq = 1, Active = true,
                              AcceptanceVi = "TIÊU CHUẨN RIÊNG cho nhăn" }]).ToList();

        var r = IqcCheckResolver.Resolve(
            "336-H1a", IqcMaterialCategory.Roll, Specs(), specItems, Lib());

        var rd = r.Items.Where(i => i.ItemKey == "RD-01").ToList();
        Assert.Single(rd);                                   // không nhân đôi
        Assert.False(rd[0].FromCategoryStandard);            // bản theo-mã
        Assert.Equal("TIÊU CHUẨN RIÊNG cho nhăn", rd[0].AcceptanceVi);
    }

    // ── mã rỗng: vẫn còn bộ chuẩn của nhóm ───────────────────────────────

    [Fact]
    public void Ma_rong_van_con_bo_chuan_cua_nhom()
    {
        // Không resolve được mã thì không có tiêu chuẩn theo-mã — nhưng lô cuộn
        // vẫn là lô cuộn, và 13 ô đếm lỗi của sheet Roll vẫn áp.
        var r = IqcCheckResolver.Resolve(
            "", IqcMaterialCategory.Roll, Specs(), SpecItems(), Lib());
        Assert.All(r.Items, i => Assert.True(i.FromCategoryStandard));
        Assert.Equal(2, r.Items.Count);
        Assert.Null(r.SpecNo);
    }

    // ── luật suy nhóm ────────────────────────────────────────────────────

    [Theory]
    [InlineData("m2",    IqcMaterialCategory.Roll)]
    [InlineData("M2",    IqcMaterialCategory.Roll)]
    [InlineData("m²",    IqcMaterialCategory.Roll)]
    [InlineData("pcs",   IqcMaterialCategory.Pcs)]
    [InlineData("Sheet", IqcMaterialCategory.Pcs)]
    [InlineData("kg",    IqcMaterialCategory.Chem)]
    [InlineData("",      IqcMaterialCategory.Any)]
    [InlineData(null,    IqcMaterialCategory.Any)]
    [InlineData("box",   IqcMaterialCategory.Any)]
    public void Suy_nhom_tu_don_vi_ton_kho(string? uom, IqcMaterialCategory expect)
        => Assert.Equal(expect, IqcCategoryRule.FromInventoryUom(uom));

    [Fact]
    public void Nhom_phieu_Chemical_va_Tools_thang_don_vi_ton_kho()
    {
        // Phiếu đã nói rõ là hoá chất thì đơn vị "m2" không lật ngược được.
        Assert.Equal(IqcMaterialCategory.Chem,
            IqcCategoryRule.Resolve(IqcGroup.Chemical, "m2"));
        Assert.Equal(IqcMaterialCategory.Tool,
            IqcCategoryRule.Resolve(IqcGroup.Tools, "m2"));
        // "Materials" gộp cả cuộn lẫn tấm ⇒ tự nó không quyết được gì.
        Assert.Equal(IqcMaterialCategory.Roll,
            IqcCategoryRule.Resolve(IqcGroup.Materials, "m2"));
        Assert.Equal(IqcMaterialCategory.Pcs,
            IqcCategoryRule.Resolve(IqcGroup.Materials, "pcs"));
    }

    [Fact]
    public void Moi_hang_muc_theo_nhom_deu_KHONG_mang_Any()
    {
        // Luật IsCategoryStandard dựa đúng vào điều này; nếu ai đó thêm một
        // hạng mục theo-nhóm mà để Category=Any thì nó sẽ rò sang mọi phiếu.
        Assert.True(IqcCategoryRule.IsCategoryStandard(IqcMaterialCategory.Roll));
        Assert.True(IqcCategoryRule.IsCategoryStandard(IqcMaterialCategory.Tool));
        Assert.False(IqcCategoryRule.IsCategoryStandard(IqcMaterialCategory.Any));
    }
}
