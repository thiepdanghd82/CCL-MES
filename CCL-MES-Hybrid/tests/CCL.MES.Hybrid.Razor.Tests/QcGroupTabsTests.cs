using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Localization;
using CCL.MES.Shared.ReasonCodes;
using CCL.MES.Shared.WoQcReview;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// Nguyên tắc Henry 2026-08-28: <b>IPQC · FQC · OQC cùng bộ hạng mục, cùng cấu
/// trúc</b>; khác biệt duy nhất là FQC/OQC không kiểm lại bảng vật tư.
///
/// <para>Trước đó FQC/OQC là một lưới THẺ phẳng, không tab, hiển thị mã khoá
/// thô (<c>color_match</c>) và trộn metadata đầu form + ô chữ ký vào cùng danh
/// sách OK/NG. Nay là <b>tab nhóm A·B·C·D·E → bảng 5 cột</b>, cùng khuôn IPQC.</para>
///
/// <para>Khoá ba điều: (a) tab dựng đúng từ <c>GroupLabel</c> và chỉ hiện hạng
/// mục của tab đang mở; (b) <b>khoá tab là chuỗi VI</b> nên đổi ngôn ngữ chỉ
/// đổi NHÃN, không văng tab; (c) nhóm E·RoHS chỉ mọc ở OQC.</para>
/// </summary>
public sealed class QcGroupTabsTests : TestContext
{
    private readonly RecordingApi _api = new();

    public QcGroupTabsTests()
    {
        Services.AddSingleton<ICclApiClient>(_api);
        // OqcDashboard tiêm IAuthSession cho chốt dual-sig Q5 (Approver phải
        // khác Inspector). Không đăng ký thì component không dựng được.
        var session = new StubAuthSession();
        session.SetUser("inspector-1", "QC");
        Services.AddSingleton<IAuthSession>(session);
        Services.AddI18n();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("inspector-1");
    }

    private static WoQcViewItem It(string key, string group, string label, string? en = null,
                                   string status = "Pending") => new()
    {
        ItemKey = key, Status = status,
        GroupLabel = group, GroupLabelEn = group + " (EN)",
        Label = label, LabelEn = en,
        Spec = "tiêu chí " + key, Method = "Soi mắt",
    };

    private static WoQcView View(string kind, params WoQcViewItem[] items) => new()
    {
        WoId = 42, WoNo = "WO-26-3683",
        MesPhase = kind == "OQC" ? "OQC_PENDING" : "FQC_PENDING",
        ETag = "v1", QcKind = kind,
        Items = items.ToList(),
    };

    private static WoQcViewItem[] FourGroups() =>
    [
        It("LBL-A1", "A·Ngoại quan", "Ngoại quan", "Appearance"),
        It("LBL-A2", "A·Ngoại quan", "Xước", "Scratch"),
        It("LBL-B1", "B·Kích thước", "Kích thước", "Dimension"),
        It("LBL-C1", "C·Màu sắc", "Màu", "Colour"),
        It("LBL-D1", "D·Chức năng", "Bám dính", "Adhesion"),
    ];

    private void Serve(WoQcView v) => _api.WoQcViewImpl = (_, _, _) => Task.FromResult(v);

    private IRenderedFragment RenderFqc()
        => RenderComponent<FqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.ScrapReasons, Array.Empty<ReasonCodeOption>()));

    private IRenderedFragment RenderOqc()
        => RenderComponent<OqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 42L)
            .Add(d => d.ScrapReasons, Array.Empty<ReasonCodeOption>()));

    // ── (a) tab dựng đúng, lọc đúng ──────────────────────────────────────

    [Fact]
    public void FQC_dung_tab_nhom_chu_khong_phai_luoi_the_phang()
    {
        Serve(View("FQC", FourGroups()));
        var cut = RenderFqc();

        cut.WaitForAssertion(() =>
        {
            var html = cut.Markup;
            Assert.Contains("fqc-tabs", html);
            Assert.Contains("fqc-item-table", html);
            foreach (var g in new[] { "A·Ngoại quan", "B·Kích thước", "C·Màu sắc", "D·Chức năng" })
                Assert.Contains(g, html);
        });
    }

    [Fact]
    public void Chi_hien_hang_muc_cua_tab_dang_mo()
    {
        Serve(View("FQC", FourGroups()));
        var cut = RenderFqc();

        cut.WaitForAssertion(() =>
        {
            var html = cut.Markup;
            // Tab đầu (A·Ngoại quan) mở sẵn ⇒ 2 hạng mục của nó có mặt…
            Assert.Contains("fqc-item-LBL-A1", html);
            Assert.Contains("fqc-item-LBL-A2", html);
            // …còn hạng mục nhóm khác thì KHÔNG. Nếu dòng nào cũng hiện thì tab
            // chỉ là trang trí và bảng vẫn dài như lưới phẳng cũ.
            Assert.DoesNotContain("fqc-item-LBL-B1", html);
            Assert.DoesNotContain("fqc-item-LBL-C1", html);
        });
    }

    [Fact]
    public void Bam_tab_khac_thi_bang_doi_theo()
    {
        Serve(View("FQC", FourGroups()));
        var cut = RenderFqc();
        cut.WaitForAssertion(() => Assert.Contains("fqc-item-LBL-A1", cut.Markup));

        cut.Find("[data-testid='fqc-tab-B·Kích thước']").Click();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("fqc-item-LBL-B1", cut.Markup);
            Assert.DoesNotContain("fqc-item-LBL-A1", cut.Markup);
        });
    }

    // ── (b) đổi ngôn ngữ: đổi NHÃN, không văng tab ───────────────────────

    [Fact]
    public void Doi_sang_EN_thi_nhan_tab_doi_ma_tab_dang_mo_khong_vang()
    {
        Serve(View("FQC", FourGroups()));
        var cut = RenderFqc();
        cut.Find("[data-testid='fqc-tab-B·Kích thước']").Click();
        cut.WaitForAssertion(() => Assert.Contains("fqc-item-LBL-B1", cut.Markup));

        Services.GetRequiredService<ILanguageService>().Set(LanguageCode.English);

        cut.WaitForAssertion(() =>
        {
            var html = cut.Markup;
            Assert.Contains("B·Kích thước (EN)", html);          // nhãn đã dịch
            Assert.Contains("fqc-tab-B·Kích thước", html);       // KHOÁ vẫn là VI
            Assert.Contains("fqc-item-LBL-B1", html);            // tab không văng
            Assert.Contains("Dimension", html);                  // nhãn hạng mục EN
        });
    }

    // ── (c) nhóm E·RoHS chỉ ở OQC ────────────────────────────────────────

    [Fact]
    public void OQC_co_them_tab_E_RoHS()
    {
        var items = FourGroups()
            .Append(It("pb_ppm", "E·RoHS & Halogen", "Pb (Lead)", "Pb (Lead)"))
            .ToArray();
        Serve(View("OQC", items));
        var cut = RenderOqc();

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("oqc-tabs", cut.Markup);
            // Razor HTML-encode dấu & ⇒ markup mang "E·RoHS &amp; Halogen".
            // Khớp theo TESTID của tab thì vừa chắc vừa không phụ thuộc encode.
            Assert.NotNull(cut.Find("[data-testid='oqc-tab-E·RoHS & Halogen']"));
            Assert.Contains("Halogen", cut.Markup);
        });
    }

    [Fact]
    public void Hang_muc_khong_co_nhom_roi_vao_tab_Khac_chu_khong_bien_mat()
    {
        // Hạng mục đóng băng trước thay đổi này không có GroupLabel. Chúng
        // PHẢI vẫn hiện — mất một hạng mục kiểm nguy hiểm hơn hẳn một tab xấu.
        Serve(View("FQC", It("legacy_key", "", "Hạng mục cũ")));
        var cut = RenderFqc();

        cut.WaitForAssertion(() => Assert.Contains("fqc-item-legacy_key", cut.Markup));
    }
}
