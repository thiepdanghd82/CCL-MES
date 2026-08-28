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
/// P12 bước 2b — màn soạn tiêu chuẩn kiểm theo mã nguyên liệu.
///
/// <para>Khoá bốn điều: (a) hợp đồng <c>cmes-add-new-inline</c> — nút thêm nằm
/// NGAY DƯỚI hàng cuối, bấm ra form INLINE; (b) <b>RBAC-by-omission</b> —
/// QC/Operator không thấy nút thêm/gỡ; (c) mã chưa có spec nói rõ đang kiểm
/// theo ma trận mặc định; (d) gỡ đi qua RowContextMenu (L35), không cột Actions.</para>
/// </summary>
public sealed class IqcSpecEditorTests : TestContext
{
    private readonly RecordingApi _api = new();
    private readonly StubAuthSession _session = new();

    public IqcSpecEditorTests()
    {
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddI18n();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("eng-1");
        _session.SetUser("eng-1", "Engineer");
    }

    private static IqcSpecItemDto Row(long id, string itemId, string label, bool active = true) => new()
    {
        Id = id, ItemId = itemId, Seq = 1,
        GroupCode = "NQ", GroupLabelVi = "Ngoại quan", GroupLabelEn = "Visual",
        LabelVi = label, LabelEn = label + " (EN)",
        AcceptanceVi = "tiêu chí " + itemId, MethodVi = "Soi mắt",
        SourceFrequency = "All lot", Active = active,
    };

    private static IqcLibraryOptionDto Opt(string itemId, string vi) => new()
    {
        ItemId = itemId, GroupCode = "NQ", GroupLabelVi = "Ngoại quan",
        ItemVi = vi, ItemEn = vi + " (EN)",
        DefaultAcceptanceVi = "mặc định " + itemId, DefaultMethodVi = "cách đo mặc định",
    };

    private void Serve(IqcSpecEditResponse r) => _api.IqcSpecImpl = (_, _) => Task.FromResult(r);

    private static IqcSpecEditResponse WithSpec(bool local = false) => new()
    {
        MaterialCode = "336-H1a", SpecNo = local ? "MES-SPEC-0001" : "CCL-SPEC-QC229",
        SpecActive = true, IsLocalSpec = local,
        Items = [Row(11, "NQ-01", "Tem nhãn")],
        Library = [Opt("NQ-01", "Tem nhãn"), Opt("NQ-02", "Màu sắc")],
    };

    private static IqcSpecEditResponse NoSpec() => new()
    {
        MaterialCode = "TWP5050",
        Library = [Opt("NQ-01", "Tem nhãn")],
    };

    private IRenderedComponent<IqcSpecEditor> Render(string? code = "336-H1a") =>
        RenderComponent<IqcSpecEditor>(p => p.Add(x => x.MaterialCode, code));

    // ── (c) mã chưa có spec ──────────────────────────────────────────────

    [Fact]
    public void Ma_CHUA_co_spec_thi_noi_ro_dang_kiem_theo_ma_tran_mac_dinh()
    {
        // 1 trong 590 mã. Hiện bảng trống không nói được gì; người soạn cần biết
        // lô đang về được kiểm bằng cái gì.
        Serve(NoSpec());
        var cut = Render("TWP5050");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("iqc-spec-nospec", cut.Markup);
            Assert.Contains("MA TRẬN MẶC ĐỊNH", cut.Markup);
            Assert.DoesNotContain("iqc-spec-specno", cut.Markup);
        });
    }

    [Fact]
    public void Bo_tu_FILE_MASTER_duoc_danh_dau_de_biet_import_sau_se_ghi_de()
    {
        Serve(WithSpec(local: false));
        var cut = Render();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("iqc-spec-frommaster", cut.Markup);
            Assert.Contains("CCL-SPEC-QC229", cut.Markup);
        });
    }

    [Fact]
    public void Spec_do_nguoi_dung_soan_KHONG_bi_danh_dau_nham_la_file_master()
    {
        Serve(WithSpec(local: true));
        var cut = Render();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("MES-SPEC-0001", cut.Markup);
            Assert.DoesNotContain("iqc-spec-frommaster", cut.Markup);
        });
    }

    // ── (a) hợp đồng add-new-inline ──────────────────────────────────────

    [Fact]
    public void Nut_them_nam_duoi_hang_cuoi_va_bam_ra_form_INLINE()
    {
        Serve(WithSpec());
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Contains("iqc-spec-addrow", cut.Markup));

        // Trước khi bấm: chưa có form.
        Assert.DoesNotContain("iqc-spec-addnew-form", cut.Markup);

        cut.Find("[data-testid='iqc-spec-addrow']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("iqc-spec-addnew-form", cut.Markup);
            Assert.NotNull(cut.Find("[data-testid='iqc-spec-addnew-name']"));
            Assert.NotNull(cut.Find("[data-testid='iqc-spec-addnew-save']"));
            Assert.NotNull(cut.Find("[data-testid='iqc-spec-addnew-cancel']"));
        });
    }

    [Fact]
    public void Chua_chon_hang_muc_thi_nut_luu_bi_khoa()
    {
        Serve(WithSpec());
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Contains("iqc-spec-addrow", cut.Markup));
        cut.Find("[data-testid='iqc-spec-addrow']").Click();

        cut.WaitForAssertion(() =>
            Assert.True(cut.Find("[data-testid='iqc-spec-addnew-save']").HasAttribute("disabled")));
    }

    [Fact]
    public void Chon_hang_muc_thi_MOI_san_tieu_chuan_mac_dinh_cua_thu_vien()
    {
        // Người soạn sửa lại cho đúng mã này thay vì gõ từ đầu — 590 mã mà bắt
        // gõ tay từng ô thì không ai soạn hết.
        Serve(WithSpec());
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Contains("iqc-spec-addrow", cut.Markup));
        cut.Find("[data-testid='iqc-spec-addrow']").Click();
        cut.Find("[data-testid='iqc-spec-addnew-name']").Change("NQ-02");

        cut.WaitForAssertion(() =>
            Assert.Equal("mặc định NQ-02",
                cut.Find("[data-testid='iqc-spec-addnew-acc']").GetAttribute("value")));
    }

    [Fact]
    public void Luu_thi_goi_dung_ma_va_dung_hang_muc()
    {
        Serve(WithSpec());
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Contains("iqc-spec-addrow", cut.Markup));
        cut.Find("[data-testid='iqc-spec-addrow']").Click();
        cut.Find("[data-testid='iqc-spec-addnew-name']").Change("NQ-02");
        cut.Find("[data-testid='iqc-spec-addnew-save']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(_api.AddIqcSpecItemCalls);
            Assert.Equal("336-H1a", call.Code);
            Assert.Equal("NQ-02", call.Body.ItemId);
            Assert.Equal("mặc định NQ-02", call.Body.AcceptanceVi);
        });
    }

    [Fact]
    public void Huy_thi_dong_form_va_KHONG_goi_server()
    {
        Serve(WithSpec());
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Contains("iqc-spec-addrow", cut.Markup));
        cut.Find("[data-testid='iqc-spec-addrow']").Click();
        cut.Find("[data-testid='iqc-spec-addnew-cancel']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("iqc-spec-addnew-form", cut.Markup);
            Assert.Empty(_api.AddIqcSpecItemCalls);
        });
    }

    [Fact]
    public void Server_tu_choi_thi_HIEN_loi_chu_khong_nuot()
    {
        _api.IqcSpecWriteThrows = new InvalidOperationException("iqc.item_not_in_library");
        Serve(WithSpec());
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Contains("iqc-spec-addrow", cut.Markup));
        cut.Find("[data-testid='iqc-spec-addrow']").Click();
        cut.Find("[data-testid='iqc-spec-addnew-name']").Change("NQ-02");
        cut.Find("[data-testid='iqc-spec-addnew-save']").Click();

        cut.WaitForAssertion(() => Assert.Contains("iqc-spec-error", cut.Markup));
    }

    // ── (b) RBAC-by-omission ─────────────────────────────────────────────

    [Theory]
    [InlineData("Admin")]
    [InlineData("Supervisor")]
    [InlineData("Engineer")]
    public void Engineer_tro_len_THAY_nut_them(string role)
    {
        _session.SetUser("u", role);
        Serve(WithSpec());
        var cut = Render();

        cut.WaitForAssertion(() => Assert.Contains("iqc-spec-addrow", cut.Markup));
    }

    [Theory]
    [InlineData("QC")]
    [InlineData("Operator")]
    public void QC_va_Operator_KHONG_thay_nut_them(string role)
    {
        // Ẩn nút KHÔNG phải là phân quyền — server vẫn 403 độc lập (đã khoá ở
        // IqcSpecEditTests). Đây chỉ là không dựng affordance vô nghĩa.
        _session.SetUser("u", role);
        Serve(WithSpec());
        var cut = Render();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("iqc-spec-table", cut.Markup);       // vẫn XEM được
            Assert.DoesNotContain("iqc-spec-addrow", cut.Markup);
        });
    }

    // ── (d) gỡ đi qua RowContextMenu ─────────────────────────────────────

    [Fact]
    public void KHONG_de_ra_cot_Actions_tren_luoi()
    {
        // L35 — hành động trên dòng đi qua chuột-phải / long-press / kebab dùng
        // chung, không phải một cột nút inline.
        Serve(WithSpec());
        var cut = Render();

        cut.WaitForAssertion(() =>
        {
            var head = cut.Find("[data-testid='iqc-spec-table'] thead").TextContent;
            Assert.DoesNotContain("Actions", head);
            Assert.DoesNotContain("Hành động", head);
        });
    }

    [Fact]
    public void Chuot_phai_vao_dong_dang_dung_thi_hien_lenh_GO()
    {
        Serve(WithSpec());
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Contains("iqc-spec-item-11", cut.Markup));

        cut.Find("[data-testid='iqc-spec-item-11']").ContextMenu();

        cut.WaitForAssertion(() => Assert.Contains("Gỡ hạng mục", cut.Markup));
    }

    [Fact]
    public void Chuot_phai_vao_dong_DA_GO_thi_hien_lenh_KHOI_PHUC()
    {
        Serve(new IqcSpecEditResponse
        {
            MaterialCode = "336-H1a", SpecNo = "CCL-SPEC-QC229", SpecActive = true,
            Items = [Row(11, "NQ-01", "Tem nhãn", active: false)],
            Library = [Opt("NQ-01", "Tem nhãn")],
        });
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Contains("iqc-spec-item-11", cut.Markup));

        cut.Find("[data-testid='iqc-spec-item-11']").ContextMenu();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Khôi phục", cut.Markup);
            Assert.DoesNotContain("Gỡ hạng mục", cut.Markup);
        });
    }

    [Fact]
    public void QC_chuot_phai_thi_KHONG_hien_menu()
    {
        _session.SetUser("u", "QC");
        Serve(WithSpec());
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Contains("iqc-spec-item-11", cut.Markup));

        cut.Find("[data-testid='iqc-spec-item-11']").ContextMenu();

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain("Gỡ hạng mục", cut.Markup);
            Assert.DoesNotContain("Khôi phục", cut.Markup);
        });
    }

    [Fact]
    public void Dong_da_GO_van_doc_duoc_chu_khong_bien_mat()
    {
        // Người soạn cần thấy mình đã gỡ gì — xoá khỏi mắt là mất dấu vết.
        Serve(new IqcSpecEditResponse
        {
            MaterialCode = "336-H1a", SpecNo = "CCL-SPEC-QC229", SpecActive = true,
            Items = [Row(11, "NQ-01", "Tem nhãn", active: false)],
            Library = [],
        });
        var cut = Render();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("iqc-spec-row-off", cut.Markup);
            Assert.Contains("Tem nhãn", cut.Markup);
            Assert.Equal("Đã gỡ",
                cut.Find("[data-testid='iqc-spec-item-11-status']").TextContent.Trim());
        });
    }

    // ── i18n ─────────────────────────────────────────────────────────────

    [Fact]
    public void Doi_sang_EN_thi_nhan_hang_muc_va_nhom_doi_theo()
    {
        Serve(WithSpec());
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Contains("Tem nhãn", cut.Markup));

        Services.GetRequiredService<ILanguageService>().Set(LanguageCode.English);

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Tem nhãn (EN)", cut.Markup);
            Assert.Contains("Visual", cut.Markup);
        });
    }
}
