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
    public void Tra_PartNo_thi_hien_bang_via_mother_va_luu_vao_ma_me()
    {
        Serve(new IqcSpecEditResponse
        {
            MaterialCode = "336-H1a",
            QueriedCode = "30030146",
            ResolvedViaMother = true,
            SpecNo = "CCL-SPEC-QC229",
            SpecActive = true,
            Items = [Row(11, "NQ-01", "Tem nhãn")],
            Library = [Opt("NQ-01", "Tem nhãn"), Opt("NQ-02", "Màu sắc")],
        });
        var cut = Render("30030146");

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("iqc-spec-via-mother", cut.Markup);
            Assert.Contains("30030146", cut.Markup);
            Assert.Contains("336-H1a", cut.Markup);
            Assert.Contains("CCL-SPEC-QC229", cut.Markup);
        });

        cut.Find("[data-testid='iqc-spec-addrow']").Click();
        cut.Find("[data-testid='iqc-spec-addnew-name']").Change("NQ-02");
        cut.Find("[data-testid='iqc-spec-addnew-save']").Click();

        cut.WaitForAssertion(() =>
        {
            var call = Assert.Single(_api.AddIqcSpecItemCalls);
            Assert.Equal("336-H1a", call.Code);
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

    [Fact]
    public void Ma_hang_muc_nam_o_DONG_RIENG_khong_dinh_vao_nhan()
    {
        // Henry thấy trên máy: "Material identificationNL-01 · #1" — nhãn và mã
        // dính liền vì .qms-cell-sub nằm trong <span> (inline). Class không có
        // display nên phụ thuộc thẻ bọc; nay class tự đủ + markup dùng <div>.
        Serve(WithSpec());
        var cut = Render();

        cut.WaitForAssertion(() =>
        {
            var sub = cut.Find("[data-testid='iqc-spec-item-11'] .qms-cell-sub");
            Assert.Equal("div", sub.TagName, ignoreCase: true);
            Assert.Contains("NQ-01", sub.TextContent);
        });
    }

    // ── phạm vi: mã mẹ này áp cho những PartNo nào ───────────────────────

    [Fact]
    public void Man_hinh_noi_ro_ma_me_va_cac_PartNo_bi_ap()
    {
        // Sửa một dòng tiêu chuẩn ở đây là sửa cho CẢ họ vật liệu. Người soạn
        // phải đọc được phạm vi trước khi bấm, không phải đoán từ mã.
        Serve(new IqcSpecEditResponse
        {
            MaterialCode = "336-H1a",
            QueriedCode = "30030146",
            ResolvedViaMother = true,
            SpecNo = "CCL-SPEC-QC229", SpecActive = true,
            Items = [Row(11, "NQ-01", "Tem nhãn")],
            Library = [Opt("NQ-01", "Tem nhãn")],
            AppliesTo =
            [
                new IqcSpecAppliesToDto { PartNo = "30030146", WidthMm = 46 },
                new IqcSpecAppliesToDto { PartNo = "30030176", WidthMm = 76 },
            ],
            AppliesToTotal = 5,
        });
        var cut = Render("30030146");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("336-H1a",
                cut.Find("[data-testid='iqc-spec-family-mother']").TextContent.Trim());
            Assert.Contains("5", cut.Find("[data-testid='iqc-spec-family-count']").TextContent);
            // Mã vừa tra được đánh dấu để người soạn biết mình đứng ở đâu.
            Assert.Contains("iqc-spec-family-cur",
                cut.Find("[data-testid='iqc-spec-family-part-30030146']").GetAttribute("class"));
            Assert.DoesNotContain("iqc-spec-family-cur",
                cut.Find("[data-testid='iqc-spec-family-part-30030176']").GetAttribute("class"));
            // Cắt danh sách thì phải nói còn bao nhiêu, không im lặng giấu.
            Assert.Contains("3", cut.Find("[data-testid='iqc-spec-family-more']").TextContent);
        });
    }

    [Fact]
    public void Ma_khong_co_PartNo_con_thi_khong_ve_khoi_pham_vi()
    {
        Serve(WithSpec());
        var cut = Render();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("iqc-spec-table", cut.Markup);
            Assert.DoesNotContain("iqc-spec-family", cut.Markup);
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

    // ── P13: một mã có NHIỀU bộ tiêu chuẩn ───────────────────────────────

    private static IqcSpecEditResponse WithManySets() => new()
    {
        MaterialCode = "SFG-APB2M000102",
        SpecNo = "CCL-SPEC-QC552", SpecActive = true,
        Specs =
        [
            new IqcSpecHeaderDto { SpecNo = "CCL-SPEC-QC552", Active = true, Approval = "Approved" },
            new IqcSpecHeaderDto { SpecNo = "IQC26-A1B2C3D4", Active = true, Approval = "PendingQc",
                                   ImportSource = "iqc-report-2026" },
        ],
        Items =
        [
            new IqcSpecItemDto { Id = 1, SpecNo = "CCL-SPEC-QC552", ItemId = "KT-04", Seq = 1,
                                 LabelVi = "Độ dày", AcceptanceVi = "0.16±0.016", Active = true },
            new IqcSpecItemDto { Id = 2, SpecNo = "IQC26-A1B2C3D4", ItemId = "KT-04", Seq = 1,
                                 LabelVi = "Độ dày", AcceptanceVi = "0.20±0.02", Active = true },
        ],
        Library = [Opt("NQ-01", "Tem nhãn")],
    };

    [Fact]
    public void Ma_co_nhieu_bo_thi_GOP_lai_va_noi_ro_co_bao_nhieu_bo()
    {
        // Trước đây màn hình chỉ hiện MỘT bộ, trong khi resolver gộp TẤT CẢ vào
        // phiếu — người soạn tiêu chuẩn không nhìn thấy thứ người kiểm phải làm.
        Serve(WithManySets());
        var cut = Render("SFG-APB2M000102");

        Assert.NotNull(cut.Find("[data-testid=iqc-spec-multi]"));
        Assert.NotNull(cut.Find("[data-testid='iqc-spec-set-CCL-SPEC-QC552']"));
        Assert.NotNull(cut.Find("[data-testid='iqc-spec-set-IQC26-A1B2C3D4']"));
        // Cả HAI chỉ tiêu của cùng hạng mục KT-04 đều hiện — đó chính là điều
        // người dùng cần thấy để quyết định gỡ bộ nào.
        var body = cut.Find("[data-testid=iqc-spec-table]").TextContent;
        Assert.Contains("0.16±0.016", body);
        Assert.Contains("0.20±0.02", body);
    }

    [Fact]
    public void Gop_nhieu_bo_thi_moi_dong_phai_noi_ro_no_thuoc_bo_nao()
    {
        // Hai chỉ tiêu khác nhau cho cùng một hạng mục hiện cạnh nhau mà không
        // ghi bộ nào là tệ hơn cả việc giấu bớt.
        Serve(WithManySets());
        var cut = Render("SFG-APB2M000102");

        var body = cut.Find("[data-testid=iqc-spec-table]").TextContent;
        Assert.Contains("CCL-SPEC-QC552", body);
        Assert.Contains("IQC26-A1B2C3D4", body);
    }

    [Fact]
    public void Bo_nhap_tu_file_master_mang_nhan_CHO_QC_DUYET()
    {
        Serve(WithManySets());
        var cut = Render("SFG-APB2M000102");

        Assert.Contains("chờ QC duyệt",
            cut.Find("[data-testid='iqc-spec-set-IQC26-A1B2C3D4']").TextContent);
    }

    [Fact]
    public void Engineer_go_duoc_MOT_BO_tieu_chuan()
    {
        Serve(WithManySets());
        var cut = Render("SFG-APB2M000102");

        cut.Find("[data-testid='iqc-spec-set-toggle-IQC26-A1B2C3D4']").Click();

        var call = Assert.Single(_api.SetIqcSpecActiveCalls);
        Assert.Equal("IQC26-A1B2C3D4", call.SpecNo);
        Assert.False(call.Active);   // gỡ = tắt, KHÔNG xoá cứng
    }

    [Fact]
    public void Chi_co_MOT_bo_thi_khong_bay_ra_bang_liet_ke_thua()
    {
        // 1124/1131 mã chỉ có một bộ — bày thêm một khối chỉ để nói "có 1 bộ"
        // là rác trên màn hình của gần như mọi mã.
        var r = WithSpec();
        r.Specs = [new IqcSpecHeaderDto { SpecNo = r.SpecNo!, Active = true, Approval = "Approved" }];
        Serve(r);
        var cut = Render();

        Assert.Empty(cut.FindAll("[data-testid=iqc-spec-sets]"));
        Assert.Empty(cut.FindAll("[data-testid=iqc-spec-multi]"));
    }

    [Fact]
    public void Nguoi_KHONG_du_quyen_thi_khong_thay_nut_go_bo()
    {
        _session.SetUser("qc-1", "QC");     // QC xem được, không sửa được
        Serve(WithManySets());
        var cut = Render("SFG-APB2M000102");

        Assert.NotNull(cut.Find("[data-testid=iqc-spec-multi]"));   // vẫn THẤY
        Assert.Empty(cut.FindAll("[data-testid^=iqc-spec-set-toggle]"));  // nhưng không sửa
    }

    // ── Ô tra mã: rộng + × + gợi ý ────────────────────────────────────

    private IRenderedComponent<IqcSpecEditor> RenderBlank() =>
        RenderComponent<IqcSpecEditor>(p => p.Add(x => x.DebounceMs, 0));

    [Fact]
    public void Go_336_hien_goi_y_ma_co_ky_tu_do()
    {
        _api.SearchIqcMaterialImpl = (desc, _, _) => Task.FromResult(new IqcMaterialSearchResponse
        {
            TooShort = false,
            Items =
            [
                new IqcMaterialSearchItem { CodeIfs = "336T", PartDescription = "Acrylic tape" },
                new IqcMaterialSearchItem { CodeIfs = "336-H1a", PartDescription = "PET liner" },
            ],
        });

        var cut = RenderBlank();
        cut.Find("[data-testid=iqc-spec-search]").Input("336");

        cut.WaitForAssertion(() =>
        {
            Assert.Equal(2, cut.FindAll("[data-testid=iqc-spec-suggest-row]").Count);
            Assert.Contains("336T", cut.Markup);
            Assert.Contains("336-H1a", cut.Markup);
        });
        Assert.Single(_api.SearchIqcMaterialCalls);
        Assert.Equal("336", _api.SearchIqcMaterialCalls[0].Desc);
    }

    [Fact]
    public void Go_PET_hien_goi_y_theo_mo_ta()
    {
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(new IqcMaterialSearchResponse
        {
            TooShort = false,
            Items = [new IqcMaterialSearchItem { CodeIfs = "TWP5050", PartDescription = "PET film 50um" }],
        });

        var cut = RenderBlank();
        cut.Find("[data-testid=iqc-spec-search]").Input("PET");

        cut.WaitForAssertion(() =>
        {
            var row = Assert.Single(cut.FindAll("[data-testid=iqc-spec-suggest-row]"));
            Assert.Equal("TWP5050", row.GetAttribute("data-code"));
            Assert.Contains("PET film", cut.Markup);
        });
    }

    [Fact]
    public void Bam_goi_y_thi_nap_tieu_chuan_cua_ma_do()
    {
        Serve(NoSpec());
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(new IqcMaterialSearchResponse
        {
            TooShort = false,
            Items = [new IqcMaterialSearchItem { CodeIfs = "TWP5050", PartDescription = "PET film" }],
        });

        var cut = RenderBlank();
        cut.Find("[data-testid=iqc-spec-search]").Input("PET");
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("[data-testid=iqc-spec-suggest-row]")));

        cut.Find("[data-testid=iqc-spec-suggest-row]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("iqc-spec-nospec", cut.Markup);
            Assert.Empty(cut.FindAll("[data-testid=iqc-spec-suggest]"));
        });
        Assert.Contains(_api.IqcSpecCalls, c => c.Code == "TWP5050");
    }

    [Fact]
    public void Nut_x_xoa_ky_tu_va_dong_goi_y()
    {
        _api.SearchIqcMaterialImpl = (_, _, _) => Task.FromResult(new IqcMaterialSearchResponse
        {
            TooShort = false,
            Items = [new IqcMaterialSearchItem { CodeIfs = "336T" }],
        });

        var cut = RenderBlank();
        cut.Find("[data-testid=iqc-spec-search]").Input("336");
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find("[data-testid=iqc-spec-search-clear]")));

        cut.Find("[data-testid=iqc-spec-search-clear]").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Equal("", cut.Find("[data-testid=iqc-spec-search]").GetAttribute("value") ?? "");
            Assert.Empty(cut.FindAll("[data-testid=iqc-spec-suggest]"));
            Assert.Empty(cut.FindAll("[data-testid=iqc-spec-search-clear]"));
        });
    }
}
