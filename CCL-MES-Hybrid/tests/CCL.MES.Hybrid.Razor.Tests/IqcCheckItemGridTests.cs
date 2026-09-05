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

    // ══ P13 bước 4b — ô đếm lỗi, ô đo, kết luận máy chấm, ghi đè ══════════

    private static IqcCheckItemDto Defect(long id, string key, int? count = null,
        string? auto = null, string? reason = null, int? seq = null) => new()
    {
        Id = id, ItemKey = key, Seq = 1, Section = 2, Kind = "DefectCount",
        GroupCode = "NQ", GroupLabelVi = "Ngoại quan", GroupLabelEn = "Appearance",
        LabelVi = "Nhăn / Hằn", LabelEn = "Wrinkle", AcceptanceVi = "Không có",
        DefectCount = count, AutoVerdict = auto, AutoVerdictReason = reason,
        AutoVerdictOffendingSeq = seq,
    };

    private static IqcCheckItemDto Measure(long id, string key, int n,
        double? low = null, double? up = null, string? unit = null,
        bool tearIsPass = false, List<double?>? values = null,
        string? auto = null, string? reason = null, int? seq = null) => new()
    {
        Id = id, ItemKey = key, Seq = 1, Section = 3, Kind = "Measure",
        GroupCode = "KT", GroupLabelVi = "Kích thước", GroupLabelEn = "Dimension",
        LabelVi = "Chiều rộng", LabelEn = "Width", AcceptanceVi = "220 ± 2 mm",
        MeasureCount = n, LimitLow = low, LimitUp = up, LimitUnit = unit,
        TearIsPass = tearIsPass, Measurements = values ?? new List<double?>(),
        AutoVerdict = auto, AutoVerdictReason = reason, AutoVerdictOffendingSeq = seq,
    };

    // ── hình dạng ô nhập đi theo Kind ────────────────────────────────────

    [Fact]
    public void Hang_muc_dem_loi_co_MOT_o_so()
    {
        var cut = Render(section: 2, items: [Defect(9, "RD-01")]);

        Assert.Single(cut.FindAll("[data-testid='iqc-sec2-item-9-defect']"));
        Assert.Single(cut.FindAll("[data-testid='iqc-sec2-item-9-defect-save']"));
    }

    [Fact]
    public void Hang_muc_do_5_lan_co_DUNG_5_o()
    {
        // File master ghi 5 phép đo cho kích thước. Dựng 3 ô rồi chấm là kết
        // luận trên dữ liệu không tồn tại.
        var cut = Render(section: 3, items: [Measure(9, "KT-03", 5, 218, 222, "mm")]);

        for (var k = 1; k <= 5; k++)
            Assert.Single(cut.FindAll($"[data-testid='iqc-sec3-item-9-m{k}']"));
        Assert.Empty(cut.FindAll("[data-testid='iqc-sec3-item-9-m6']"));
    }

    [Fact]
    public void O_tick_rach_CHI_hien_khi_tieu_chuan_ghi_or_tear()
    {
        var co = Render(section: 3, items: [Measure(9, "BD-01", 1, 10.0, null, "N/25mm", tearIsPass: true)]);
        Assert.Single(co.FindAll("[data-testid='iqc-sec3-item-9-tear']"));

        var khong = Render(section: 3, items: [Measure(9, "KT-03", 5, 218, 222, "mm")]);
        Assert.Empty(khong.FindAll("[data-testid='iqc-sec3-item-9-tear']"));
    }

    [Fact]
    public void Muc_toan_hang_muc_nguoi_bam_KHONG_dung_cot_ghi_nhan()
    {
        // Một cột trống suốt màn hình là chỗ mắt phải lướt qua mà không thu
        // được gì.
        var cut = Render(section: 2);
        Assert.Empty(cut.FindAll("th.ipqc-col-result"));
        Assert.Contains("ipqc-no-result", cut.Markup);
    }

    [Fact]
    public void Muc_co_hang_muc_nhap_so_thi_dung_cot_ghi_nhan()
    {
        var cut = Render(section: 2, items:
        [
            It(1, "NQ-01", "NQ", "Ngoại quan", "Tem nhãn", section: 2),
            Defect(9, "RD-01"),
        ]);
        Assert.Single(cut.FindAll("th.ipqc-col-result"));
        Assert.DoesNotContain("ipqc-no-result", cut.Markup);
    }

    // ── gửi lên server đúng thứ ──────────────────────────────────────────

    [Fact]
    public void Luu_so_loi_gui_DefectCount_va_de_may_quyet()
    {
        var cut = Render(section: 2, items: [Defect(9, "RD-01")]);
        cut.Find("[data-testid='iqc-sec2-item-9-defect']").Input("3");
        cut.Find("[data-testid='iqc-sec2-item-9-defect-save']").Click();

        var (_, itemId, body) = Assert.Single(_api.SetIqcTicketItemCalls);
        Assert.Equal(9L, itemId);
        Assert.Equal(3, body.DefectCount);
        // Người kiểm đã đếm; tiêu chuẩn nói con số đó nghĩa là gì. Client KHÔNG
        // tự chấm hộ — Pass để null cho server quyết.
        Assert.Null(body.Pass);
    }

    [Fact]
    public void O_de_TRONG_gui_null_chu_KHONG_gui_0()
    {
        // Ép ô trống về 0 là tuyên bố "đã đếm, không có lỗi" thay cho người
        // chưa làm việc.
        var cut = Render(section: 2, items: [Defect(9, "RD-01")]);
        cut.Find("[data-testid='iqc-sec2-item-9-defect-save']").Click();

        var (_, _, body) = Assert.Single(_api.SetIqcTicketItemCalls);
        Assert.Null(body.DefectCount);
    }

    [Fact]
    public void Luu_phep_do_gui_du_5_gia_tri_o_trong_thanh_null()
    {
        var cut = Render(section: 3, items: [Measure(9, "KT-03", 5, 218, 222, "mm")]);
        cut.Find("[data-testid='iqc-sec3-item-9-m1']").Input("220");
        cut.Find("[data-testid='iqc-sec3-item-9-m2']").Input("219.5");
        cut.Find("[data-testid='iqc-sec3-item-9-measures-save']").Click();

        var (_, _, body) = Assert.Single(_api.SetIqcTicketItemCalls);
        Assert.NotNull(body.Measurements);
        // Đủ 5 phần tử: server đối chiếu số lượng với MeasureCount, và ba ô
        // chưa đo phải đi lên dưới dạng null chứ không bị nuốt mất.
        Assert.Equal(5, body.Measurements!.Count);
        Assert.Equal(220d, body.Measurements[0]);
        Assert.Equal(219.5d, body.Measurements[1]);
        Assert.Null(body.Measurements[2]);
        Assert.Null(body.Measurements[4]);
    }

    [Fact]
    public void Tick_rach_di_kem_len_server()
    {
        var cut = Render(section: 3, items:
            [Measure(9, "BD-01", 1, 10.0, null, "N/25mm", tearIsPass: true)]);
        cut.Find("[data-testid='iqc-sec3-item-9-m1']").Input("6.4");
        cut.Find("[data-testid='iqc-sec3-item-9-tear']").Change(true);
        cut.Find("[data-testid='iqc-sec3-item-9-measures-save']").Click();

        var (_, _, body) = Assert.Single(_api.SetIqcTicketItemCalls);
        Assert.Equal(6.4d, body.Measurements![0]);
        Assert.True(body.TearObserved);
    }

    // ── kết luận máy chấm hiện ra ────────────────────────────────────────

    [Fact]
    public void Hien_may_noi_gi_VA_vi_sao()
    {
        // Chỉ hiện "Fail" mà không nói lý do thì người kiểm phải tự dò lại 5 số.
        var cut = Render(section: 3, items:
            [Measure(9, "KT-03", 5, 218, 222, "mm",
                auto: "Fail", reason: "iqc.judge.below_low", seq: 3)]);

        var pill = cut.Find("[data-testid='iqc-sec3-item-9-auto']");
        Assert.Contains("iqc-auto-ng", pill.GetAttribute("class"));
        Assert.Contains("3", pill.TextContent);          // đúng ô gây trượt
    }

    [Fact]
    public void Chua_quyet_duoc_KHONG_duoc_nhin_nhu_dat()
    {
        var cut = Render(section: 2, items:
            [Defect(9, "RD-01", auto: "Undecidable", reason: "iqc.judge.defect_incomplete")]);

        var pill = cut.Find("[data-testid='iqc-sec2-item-9-auto']");
        Assert.Contains("iqc-auto-unknown", pill.GetAttribute("class"));
        Assert.DoesNotContain("iqc-auto-ok", pill.GetAttribute("class"));
    }

    [Fact]
    public void Hang_muc_nguoi_bam_KHONG_treo_nhan_may_cham()
    {
        // Máy im lặng ở đây là đúng; treo thêm "máy chưa quyết được" chỉ làm ồn.
        var cut = Render(section: 2, items:
        [
            new IqcCheckItemDto
            {
                Id = 9, ItemKey = "NQ-01", Seq = 1, Section = 2, Kind = "Verdict",
                GroupCode = "NQ", GroupLabelVi = "Ngoại quan", LabelVi = "Tem nhãn",
                AutoVerdict = "Undecidable", AutoVerdictReason = "iqc.judge.human_only",
            },
        ]);
        Assert.Empty(cut.FindAll("[data-testid='iqc-sec2-item-9-auto']"));
    }

    [Fact]
    public void Hien_NGUONG_SO_da_dong_bang_chu_khong_chi_chuoi_tieu_chi()
    {
        // Chuỗi tiêu chí gốc có thể mơ hồ ("FTM 2"); con số máy thật sự so
        // thì không, nên nó phải hiện ra.
        var cut = Render(section: 3, items: [Measure(9, "KT-03", 5, 218, 222, "mm")]);
        var chip = cut.Find("[data-testid='iqc-sec3-item-9-limit']");
        Assert.Contains("218", chip.TextContent);
        Assert.Contains("222", chip.TextContent);
        Assert.Contains("mm", chip.TextContent);
    }

    [Fact]
    public void Chi_co_can_duoi_thi_hien_dau_lon_hon_bang()
    {
        var cut = Render(section: 3, items:
            [Measure(9, "BD-01", 1, 10.0, null, "N/25mm")]);
        Assert.Contains("≥ 10", cut.Find("[data-testid='iqc-sec3-item-9-limit']").TextContent);
    }

    [Fact]
    public void Khong_co_nguong_so_thi_KHONG_hien_chip()
    {
        var cut = Render(section: 2, items: [Defect(9, "RD-01")]);
        Assert.Empty(cut.FindAll("[data-testid='iqc-sec2-item-9-limit']"));
    }

    // ── ghi đè kèm lý do ─────────────────────────────────────────────────

    [Fact]
    public void Server_doi_ly_do_thi_MO_o_ly_do_chu_khong_bao_loi_do()
    {
        _api.SetIqcTicketItemThrows = new ApiException(422,
            new CCL.MES.Shared.Envelopes.ApiError
            {
                Code = "iqc.verdict_override_reason_required",
                MessageEn = "reason required",
            });

        var cut = Render(section: 2, items: [Defect(9, "RD-01", auto: "Fail",
            reason: "iqc.judge.defect_found")]);
        cut.Find("[data-testid='iqc-sec2-item-9-ok']").Click();

        // Không phải lỗi — là một câu hỏi.
        Assert.Single(cut.FindAll("[data-testid='iqc-sec2-item-9-override-row']"));
        Assert.Single(cut.FindAll("[data-testid='iqc-sec2-item-9-reason']"));
        Assert.Empty(cut.FindAll("[data-testid='iqc-sec2-error']"));
    }

    [Fact]
    public void Luu_lai_kem_ly_do_gui_DUNG_phan_dinh_vua_bam()
    {
        _api.SetIqcTicketItemThrows = new ApiException(422,
            new CCL.MES.Shared.Envelopes.ApiError
            {
                Code = "iqc.verdict_override_reason_required",
                MessageEn = "reason required",
            });

        var cut = Render(section: 2, items: [Defect(9, "RD-01", auto: "Fail",
            reason: "iqc.judge.defect_found")]);
        cut.Find("[data-testid='iqc-sec2-item-9-ok']").Click();

        _api.SetIqcTicketItemThrows = null;   // lần này server nhận
        cut.Find("[data-testid='iqc-sec2-item-9-reason']").Input("mép cắt bỏ, QA đã duyệt");
        cut.Find("[data-testid='iqc-sec2-item-9-reason-save']").Click();

        var last = _api.SetIqcTicketItemCalls[^1];
        // Phán định phải là cái người kiểm đã bấm lúc đầu, không phải đoán lại.
        Assert.True(last.Body.Pass);
        Assert.Equal("mép cắt bỏ, QA đã duyệt", last.Body.OverrideReason);
    }

    [Fact]
    public void Chua_go_ly_do_thi_nut_luu_bi_khoa()
    {
        _api.SetIqcTicketItemThrows = new ApiException(422,
            new CCL.MES.Shared.Envelopes.ApiError
            {
                Code = "iqc.verdict_override_reason_required",
                MessageEn = "reason required",
            });

        var cut = Render(section: 2, items: [Defect(9, "RD-01", auto: "Fail",
            reason: "iqc.judge.defect_found")]);
        cut.Find("[data-testid='iqc-sec2-item-9-ok']").Click();

        Assert.True(cut.Find("[data-testid='iqc-sec2-item-9-reason-save']").HasAttribute("disabled"));
    }

    [Fact]
    public void Bo_thi_dong_o_ly_do_lai()
    {
        _api.SetIqcTicketItemThrows = new ApiException(422,
            new CCL.MES.Shared.Envelopes.ApiError
            {
                Code = "iqc.verdict_override_reason_required",
                MessageEn = "reason required",
            });

        var cut = Render(section: 2, items: [Defect(9, "RD-01", auto: "Fail",
            reason: "iqc.judge.defect_found")]);
        cut.Find("[data-testid='iqc-sec2-item-9-ok']").Click();
        cut.Find("[data-testid='iqc-sec2-item-9-reason-cancel']").Click();

        Assert.Empty(cut.FindAll("[data-testid='iqc-sec2-item-9-override-row']"));
    }

    [Fact]
    public void Loi_khac_van_hien_nhu_cu_chu_khong_mo_o_ly_do()
    {
        _api.SetIqcTicketItemThrows = new ApiException(404,
            new CCL.MES.Shared.Envelopes.ApiError
            {
                Code = "iqc.item_not_found", MessageEn = "gone",
            });

        var cut = Render(section: 2, items: [Defect(9, "RD-01")]);
        cut.Find("[data-testid='iqc-sec2-item-9-defect-save']").Click();

        Assert.Empty(cut.FindAll("[data-testid='iqc-sec2-item-9-override-row']"));
        Assert.Single(cut.FindAll("[data-testid='iqc-sec2-error']"));
    }

    [Fact]
    public void Ai_da_de_len_may_thi_hien_ra_tren_dong()
    {
        // Sáu tháng sau auditor hỏi "ai cho qua cái này?" — câu trả lời phải
        // nằm ngay trên dòng, không phải trong bảng audit.
        var cut = Render(section: 2, items: [new IqcCheckItemDto
        {
            Id = 9, ItemKey = "RD-01", Seq = 1, Section = 2, Kind = "DefectCount",
            GroupCode = "NQ", GroupLabelVi = "Ngoại quan", LabelVi = "Nhăn / Hằn",
            Pass = true, DefectCount = 3, AutoVerdict = "Fail",
            AutoVerdictReason = "iqc.judge.defect_found",
            OverrideReason = "mép cắt bỏ", OverriddenBy = "thiepdt",
        }]);

        var note = cut.Find("[data-testid='iqc-sec2-item-9-override']");
        Assert.Contains("thiepdt", note.TextContent);
        Assert.Contains("mép cắt bỏ", note.TextContent);
    }
}
