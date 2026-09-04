using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Razor.Shared.Iqc;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Localization;
using CCL.MES.Shared.Quality;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P12 bước 3 — lưới hạng mục kiểm của phiếu IQC: MỘT bảng phẳng, KHÔNG tab
/// nhóm (Henry chốt 2026-09-03 — mục stepper đã chia hạng mục rồi).
///
/// <para>Khoá bốn điều: (a) mỗi MỤC chỉ hiện hạng mục của mình, TẤT CẢ trong
/// một bảng; (b) nhóm thành CỘT và chỉ in ở dòng đầu mỗi nhóm; (c)
/// <c>Pass=null</c> hiện <b>Chưa kiểm</b> chứ KHÔNG phải NG; (d) tiêu chuẩn
/// còn <c>XXX</c> khoá nút ĐẠT nhưng vẫn cho chấm NG.</para>
/// </summary>
public sealed class IqcCheckItemGridTests : TestContext
{
    private readonly RecordingApi _api = new();

    public IqcCheckItemGridTests()
    {
        Services.AddSingleton<ICclApiClient>(_api);
        var session = new StubAuthSession();
        session.SetUser("qc-1", "QC");
        Services.AddSingleton<IAuthSession>(session);
        Services.AddI18n();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("qc-1");
    }

    private static IqcCheckItemDto It(
        long id, string key, string group, string groupLabel, string label,
        int section, bool? pass = null, bool unspecified = false) => new()
    {
        Id = id, ItemKey = key, Seq = 1, Section = section,
        GroupCode = group, GroupLabelVi = groupLabel, GroupLabelEn = groupLabel + " (EN)",
        LabelVi = label, LabelEn = label + " (EN)",
        AcceptanceVi = unspecified ? "FTM:  XXX" : "tiêu chí " + key,
        AcceptanceEn = unspecified ? "FTM:  XXX" : "spec " + key,
        MethodVi = "Soi mắt", MethodEn = "Visual",
        Pass = pass, AcceptanceUnspecified = unspecified,
    };

    /// <summary>Bộ mẫu trải cả mục 2 và mục 3, có sẵn một dòng ĐẠT, một dòng
    /// tiêu chuẩn còn placeholder.</summary>
    private static List<IqcCheckItemDto> Sample() =>
    [
        It(1, "NL-01", "NL", "Nguyên liệu", "Nhận dạng", section: 2),
        It(2, "NQ-01", "NQ", "Ngoại quan", "Tem nhãn", section: 2, pass: true),
        It(3, "KT-01", "KT", "Kích thước", "Kích thước tiêu chuẩn", section: 3),
        It(4, "CU-01", "CU", "Độ cứng", "Độ cứng bút chì", section: 3, unspecified: true),
    ];

    private IRenderedComponent<IqcCheckItemGrid> Render(
        int section, List<IqcCheckItemDto>? items = null,
        string? specNo = "CCL-SPEC-QC229", bool matrix = false) =>
        RenderComponent<IqcCheckItemGrid>(p => p
            .Add(x => x.TicketId, 42L)
            .Add(x => x.Items, items ?? Sample())
            .Add(x => x.Section, section)
            .Add(x => x.SpecNo, specNo)
            .Add(x => x.FromDefaultMatrix, matrix)
            .Add(x => x.TestIdPrefix, $"iqc-sec{section}"));

    // ── (a) mỗi mục chỉ hiện hạng mục của mình ───────────────────────────

    [Fact]
    public void Muc_2_hien_TAT_CA_hang_muc_cua_no_trong_MOT_bang()
    {
        var cut = Render(section: 2);

        // Cả hai hạng mục của mục 2 cùng lúc — KHÔNG phải bấm tab mới thấy.
        Assert.Contains("iqc-sec2-item-1", cut.Markup);          // NL-01
        Assert.Contains("iqc-sec2-item-2", cut.Markup);          // NQ-01
        Assert.DoesNotContain("iqc-sec2-item-3", cut.Markup);    // KT-01 thuộc mục 3
        Assert.DoesNotContain("iqc-sec2-item-4", cut.Markup);    // CU-01 thuộc mục 3
    }

    [Fact]
    public void KHONG_con_tab_nhom_nao()
    {
        // Mục stepper đã chia hạng mục; thêm tab là chia hai lần cùng một tập.
        var cut = Render(section: 2);

        Assert.DoesNotContain("ipqc-tabs", cut.Markup);
        Assert.DoesNotContain("ipqc-tab-chip", cut.Markup);
        Assert.Empty(cut.FindAll("[role=tablist]"));
    }

    [Fact]
    public void Nhom_thanh_COT_va_chi_in_o_DONG_DAU_moi_nhom()
    {
        // Lặp lại cùng một tên nhóm ở mọi dòng là nhiễu; nhưng bỏ hẳn thì mất
        // thông tin. In ở dòng đầu nhóm là chỗ giữa.
        var cut = Render(section: 2, items:
        [
            It(1, "NQ-01", "NQ", "Ngoại quan", "Tem nhãn", section: 2),
            It(2, "NQ-02", "NQ", "Ngoại quan", "Màu sắc", section: 2),
            It(3, "NL-01", "NL", "Nguyên liệu", "Nhận dạng", section: 2),
        ]);

        Assert.Equal("Ngoại quan", cut.Find("[data-testid='iqc-sec2-item-1-group']").TextContent.Trim());
        Assert.Equal("", cut.Find("[data-testid='iqc-sec2-item-2-group']").TextContent.Trim());
        Assert.Equal("Nguyên liệu", cut.Find("[data-testid='iqc-sec2-item-3-group']").TextContent.Trim());
    }

    [Fact]
    public void Muc_3_chi_hien_hang_muc_cua_muc_3()
    {
        var cut = Render(section: 3);

        Assert.Contains("iqc-sec3-item-3", cut.Markup);
        Assert.DoesNotContain("iqc-sec3-item-1", cut.Markup);
    }




    // ── (b) đổi ngôn ngữ: đổi NHÃN, không văng tab ───────────────────────

    [Fact]
    public void Doi_sang_EN_thi_nhan_nhom_va_nhan_hang_muc_doi_theo()
    {
        var cut = Render(section: 2);
        cut.WaitForAssertion(() => Assert.Contains("Nhận dạng", cut.Markup));

        Services.GetRequiredService<ILanguageService>().Set(LanguageCode.English);

        cut.WaitForAssertion(() =>
        {
            var html = cut.Markup;
            Assert.Contains("Nguyên liệu (EN)", html);   // nhãn NHÓM (cột) đã dịch
            Assert.Contains("Nhận dạng (EN)", html);     // nhãn hạng mục đã dịch
            Assert.Contains("iqc-sec2-item-1", html);    // không mất dòng nào
        });
    }

    // ── (c) CHƯA KIỂM ≠ NG ───────────────────────────────────────────────

    [Fact]
    public void Pass_null_hien_CHUA_KIEM_chu_khong_phai_khong_dat()
    {
        // Trước P12, Pass là bool không nullable ⇒ hạng mục vừa dựng đều hiện NG,
        // tuyên bố cả lô không đạt mà không ai bấm gì.
        var cut = Render(section: 2);

        cut.WaitForAssertion(() =>
        {
            var pill = cut.Find("[data-testid='iqc-sec2-item-1-status']");
            Assert.Equal("Chưa kiểm", pill.TextContent.Trim());
            Assert.Contains("ipqc-status-pending", pill.GetAttribute("class"));
        });
    }

    [Fact]
    public void Hang_muc_da_cham_DAT_hien_dung_trang_thai()
    {
        var cut = Render(section: 2);

        Assert.Equal("Đạt", cut.Find("[data-testid='iqc-sec2-item-2-status']").TextContent.Trim());
    }

    // ── (d) tiêu chuẩn còn XXX ───────────────────────────────────────────

    [Fact]
    public void Tieu_chuan_con_XXX_thi_khoa_nut_DAT_nhung_van_cham_NG_duoc()
    {
        var cut = Render(section: 3);

        cut.WaitForAssertion(() =>
        {
            Assert.True(cut.Find("[data-testid='iqc-sec3-item-4-ok']").HasAttribute("disabled"));
            Assert.False(cut.Find("[data-testid='iqc-sec3-item-4-ng']").HasAttribute("disabled"));
            // Hạng mục vẫn HIỆN — chỉ đánh dấu, không ẩn.
            Assert.Contains("iqc-sec3-item-4-unspecified", cut.Markup);
        });
    }

    // ── ghi phán định ────────────────────────────────────────────────────

    [Fact]
    public void Bam_DAT_thi_goi_dung_phieu_dung_hang_muc()
    {
        var cut = Render(section: 2);
        cut.Find("[data-testid='iqc-sec2-item-1-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(_api.SetIqcTicketItemCalls);
            Assert.Equal(42L, call.TicketId);
            Assert.Equal(1L, call.ItemId);
            Assert.True(call.Body.Pass);
        });
    }

    [Fact]
    public void Server_tu_choi_thi_HIEN_loi_chu_khong_nuot()
    {
        _api.SetIqcTicketItemThrows = new InvalidOperationException("iqc.acceptance_unspecified");
        var cut = Render(section: 2);
        cut.Find("[data-testid='iqc-sec2-item-1-ok']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("iqc-sec2-error", cut.Markup);
            // Ô giữ nguyên CHƯA KIỂM — không ghi lạc quan khi server từ chối.
            Assert.Equal("Chưa kiểm",
                cut.Find("[data-testid='iqc-sec2-item-1-status']").TextContent.Trim());
        });
    }

    // ── băng nhắc ma trận mặc định ───────────────────────────────────────

    [Fact]
    public void Bo_hang_muc_tu_MA_TRAN_thi_co_bang_nhac()
    {
        // Sáu tháng sau, câu hỏi đầu của auditor là "hồ sơ này kiểm theo tiêu
        // chuẩn nào?". Không có băng này thì không ai trả lời được.
        var cut = Render(section: 2, specNo: null, matrix: true);

        Assert.Contains("iqc-sec2-matrix-banner", cut.Markup);
        Assert.DoesNotContain("iqc-sec2-spec", cut.Markup);
    }

    [Fact]
    public void Bo_hang_muc_theo_SPEC_thi_hien_so_spec_chu_khong_hien_bang_nhac()
    {
        var cut = Render(section: 2);

        Assert.Contains("CCL-SPEC-QC229", cut.Markup);
        Assert.DoesNotContain("iqc-sec2-matrix-banner", cut.Markup);
    }

    [Fact]
    public void Phieu_khong_co_hang_muc_nao_thi_noi_thang_chu_khong_de_luoi_rong()
    {
        var cut = Render(section: 2, items: new List<IqcCheckItemDto>());

        Assert.Contains("iqc-sec2-empty", cut.Markup);
        Assert.DoesNotContain("iqc-sec2-table", cut.Markup);
    }
}
