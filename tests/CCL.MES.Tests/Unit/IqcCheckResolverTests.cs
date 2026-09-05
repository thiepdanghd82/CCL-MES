using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P12 bước 2a — dựng bộ hạng mục kiểm cho lô NVL từ mã nguyên liệu.
///
/// <para>Khoá bốn luật đã chốt với Henry 2026-08-28:</para>
/// <list type="number">
/// <item>Khoá nối là <c>MotherCode</c>, KHÔNG phải mã IFS (đo được: 0 khớp).</item>
/// <item>Có spec ⇒ tiêu chuẩn RIÊNG của mã đó, tuyệt đối không dùng giá trị chung.</item>
/// <item>Chưa có spec ⇒ ma trận 13 hạng mục, ĐÁNH DẤU rõ là mặc định.</item>
/// <item>Tiêu chuẩn dạng <c>XXX</c> vẫn hiện nhưng gắn cờ "chưa xác định".</item>
/// </list>
/// </summary>
public sealed class IqcCheckResolverTests
{
    private static IqcCheckItemLibrary L(
        string id, string group, string vi, int sort,
        bool matrix = false, string? defAcc = null) => new()
    {
        ItemId = id, GroupCode = group, GroupLabelVi = group + "·nhóm",
        GroupLabelEn = group + "·group", ItemVi = vi, ItemEn = vi + " (EN)",
        InDefaultMatrix = matrix, DefaultAcceptanceVi = defAcc,
        DefaultMethodVi = "phương pháp mặc định",
        Sort = sort, Active = true,
    };

    /// <summary>Thư viện thu nhỏ đúng hình dạng thật: vài hạng mục trong ma
    /// trận, vài hạng mục chỉ áp cho vật liệu cụ thể.</summary>
    private static List<IqcCheckItemLibrary> Lib() =>
    [
        L("NL-01", "NL", "Nhận dạng vật liệu", 10, matrix: true, defAcc: "Theo mẫu chuẩn"),
        L("NQ-01", "NQ", "Tem nhãn", 20, matrix: true, defAcc: "Đúng thông tin"),
        L("CU-01", "CU", "Độ cứng bút chì", 30, matrix: true, defAcc: "Loại Bút, Qủa nặng:  XXX"),
        L("KT-02", "KT", "Chiều dài", 40),          // ngoài ma trận
        L("BD-02", "BD", "Cross-cut test", 50),     // ngoài ma trận
    ];

    private static List<IqcMaterialSpec> Specs() =>
    [
        new() { SpecNo = "CCL-SPEC-QC229", MaterialCode = "336-H1a", MaterialCodeIfs = null, Active = true },
        new() { SpecNo = "CCL-SPEC-QC060", MaterialCode = "TESA 4982", MaterialCodeIfs = "70000076", Active = true },
    ];

    private static IqcSpecItem SI(string spec, string item, string acc, int seq = 1) => new()
    {
        SpecNo = spec, ItemId = item, Seq = seq,
        AcceptanceVi = acc, AcceptanceEn = acc + " (EN)",
        MethodVi = "soi mắt", SourceFrequency = "All lot", Active = true,
    };

    private static List<IqcSpecItem> SpecItems() =>
    [
        SI("CCL-SPEC-QC229", "NL-01", "TIÊU CHUẨN RIÊNG của 336-H1a"),
        SI("CCL-SPEC-QC229", "KT-02", "dài 500M ± 5"),
        SI("CCL-SPEC-QC229", "NQ-01", "không rách", 1),
        SI("CCL-SPEC-QC229", "NQ-01", "không ẩm", 2),   // nhiều tiêu chí cùng mã
    ];

    private static IqcCheckResolver.Result Run(string? code) =>
        IqcCheckResolver.Resolve(code, IqcMaterialCategory.Any, Specs(), SpecItems(), Lib());

    // ── (1) khoá nối là MotherCode ───────────────────────────────────────

    [Fact]
    public void Khop_theo_MotherCode()
    {
        var r = Run("336-H1a");

        Assert.Equal("CCL-SPEC-QC229", r.SpecNo);
        Assert.False(r.FromDefaultMatrix);
    }

    [Theory]
    [InlineData("336-h1a")]
    [InlineData("  336-H1a  ")]
    public void Khop_khong_phan_biet_hoa_thuong_va_da_trim(string code)
        => Assert.Equal("CCL-SPEC-QC229", Run(code).SpecNo);

    [Fact]
    public void KHONG_khop_theo_ma_IFS()
    {
        // Đo trên live: PartNo (300xxxxx) khớp MaterialCodeIfs (7xxxxxxx) đúng
        // 0 dòng — hai hệ đánh số khác hẳn. Ai đó nối nhầm ở đây sẽ dựng bộ
        // hạng mục của một nguyên liệu KHÁC cho lô đang cầm.
        var r = Run("70000076");

        Assert.Null(r.SpecNo);
        Assert.True(r.FromDefaultMatrix);
    }

    // ── (2) có spec ⇒ tiêu chuẩn RIÊNG ───────────────────────────────────

    [Fact]
    public void Co_spec_thi_lay_tieu_chuan_RIENG_chu_khong_lay_gia_tri_chung()
    {
        // Đây là bất biến quan trọng nhất của P12. Lấy DefaultAcceptanceVi cho
        // mã ĐÃ CÓ spec là sai mà vô hình — màn hình vẫn đầy chữ, chỉ là chữ sai.
        var it = Assert.Single(Run("336-H1a").Items.Where(i => i.ItemKey == "NL-01"));

        Assert.Equal("TIÊU CHUẨN RIÊNG của 336-H1a", it.AcceptanceVi);
        Assert.NotEqual("Theo mẫu chuẩn", it.AcceptanceVi);
        Assert.False(it.FromDefaultMatrix);
    }

    [Fact]
    public void Co_spec_thi_lay_dung_hang_muc_cua_spec_do_ke_ca_ngoai_ma_tran()
    {
        var keys = Run("336-H1a").Items.Select(i => i.ItemKey).ToList();

        Assert.Contains("KT-02", keys);              // ngoài ma trận nhưng spec có
        Assert.DoesNotContain("CU-01", keys);        // trong ma trận nhưng spec KHÔNG có
    }

    [Fact]
    public void Nhieu_tieu_chi_cung_ma_deu_duoc_giu()
    {
        var nq = Run("336-H1a").Items.Where(i => i.ItemKey == "NQ-01").OrderBy(i => i.Seq).ToList();

        Assert.Equal(2, nq.Count);
        Assert.Equal(new[] { "không rách", "không ẩm" }, nq.Select(i => i.AcceptanceVi));
    }

    // ── (3) chưa có spec ⇒ ma trận, ĐÁNH DẤU rõ ──────────────────────────

    [Fact]
    public void Chua_co_spec_thi_dung_ma_tran_13_hang_muc()
    {
        var r = Run("TWP5050");   // 1 trong 590 mã chưa có spec

        Assert.Null(r.SpecNo);
        Assert.True(r.FromDefaultMatrix);
        Assert.Equal(new[] { "NL-01", "NQ-01", "CU-01" }, r.Items.Select(i => i.ItemKey));
        Assert.All(r.Items, i => Assert.True(i.FromDefaultMatrix));
    }

    [Fact]
    public void Hang_muc_ma_tran_mang_co_phan_biet_de_khong_ai_nham_voi_spec_that()
    {
        // Không có cờ này thì sáu tháng sau không ai phân biệt được hồ sơ nào
        // kiểm theo spec thật, hồ sơ nào theo mặc định — câu hỏi đầu của auditor.
        Assert.All(Run("TWP5050").Items, i => Assert.True(i.FromDefaultMatrix));
        Assert.All(Run("336-H1a").Items, i => Assert.False(i.FromDefaultMatrix));
    }

    [Fact]
    public void Spec_ton_tai_nhung_khong_co_dong_chi_tiet_thi_van_lui_ve_ma_tran()
    {
        // Thà bộ mặc định còn hơn màn hình trắng.
        var r = IqcCheckResolver.Resolve("TESA 4982", IqcMaterialCategory.Any, Specs(), SpecItems(), Lib());

        Assert.True(r.FromDefaultMatrix);
        Assert.NotEmpty(r.Items);
    }

    // ── (4) tiêu chuẩn XXX ───────────────────────────────────────────────

    [Fact]
    public void Tieu_chuan_dang_XXX_bi_danh_dau_chua_xac_dinh()
    {
        // 521/5 961 dòng thư viện còn placeholder; CU-01 tới 96%. Người kiểm
        // KHÔNG được hỏi "đạt hay không so với XXX?" rồi bắt ký.
        var cu = Assert.Single(Run("TWP5050").Items.Where(i => i.ItemKey == "CU-01"));

        Assert.True(cu.AcceptanceUnspecified);
        // Hạng mục vẫn HIỆN (Henry chốt) — chỉ đánh dấu, không ẩn.
        Assert.Contains("XXX", cu.AcceptanceVi);
    }

    [Fact]
    public void Tieu_chuan_binh_thuong_KHONG_bi_danh_dau_nham()
    {
        Assert.All(Run("TWP5050").Items.Where(i => i.ItemKey != "CU-01"),
            i => Assert.False(i.AcceptanceUnspecified));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Không có bụi bẩn", false)]
    [InlineData("FTM:  XXX", true)]
    [InlineData("Loại Bút, Qủa nặng:  XXX", true)]
    public void Nhan_dien_placeholder(string? acc, bool mong_doi)
        => Assert.Equal(mong_doi, IqcCheckResolver.IsUnspecified(acc));

    // ── biên ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ma_nguyen_lieu_rong_thi_tra_RONG_chu_khong_doan_bua(string? code)
    {
        // Dựng sai bộ hạng mục còn tệ hơn không dựng.
        var r = IqcCheckResolver.Resolve(code, IqcMaterialCategory.Any, Specs(), SpecItems(), Lib());
        Assert.Empty(r.Items);
        Assert.False(r.FromDefaultMatrix);
    }

    [Fact]
    public void Thu_vien_rong_thi_tra_rong()
        => Assert.Empty(IqcCheckResolver.Resolve("336-H1a", IqcMaterialCategory.Any, Specs(), SpecItems(), null).Items);

    [Fact]
    public void Dong_Inactive_bi_loai()
    {
        var lib = Lib();
        lib.First(x => x.ItemId == "NQ-01").Active = false;
        var r = IqcCheckResolver.Resolve("TWP5050", IqcMaterialCategory.Any, Specs(), SpecItems(), lib);

        Assert.DoesNotContain("NQ-01", r.Items.Select(i => i.ItemKey));
    }
}
