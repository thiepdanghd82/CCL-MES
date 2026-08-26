using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.IpqcReview;
using CCL.MES.Shared.Localization;
using CCL.MES.Shared.ReasonCodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// i18n bảng IPQC — mắt xích cuối: bấm cờ EN thì BẢNG có đổi chữ thật không.
///
/// <para>Bệnh cũ: <c>IpqcLibraryMaterializer</c> chọn <c>ItemVi ?? ItemEn</c> rồi
/// ĐÓNG BĂNG lựa chọn đó, nên bốn cột dữ liệu QC (nhãn hạng mục · nhóm ·
/// phương pháp · tiêu chí) vĩnh viễn là tiếng Việt bất kể cờ ngôn ngữ. Chrome
/// quanh bảng thì dịch đúng vì nó đi qua <c>T()</c>, khiến lỗi trông như "chưa
/// dịch xong" chứ không như một mắt xích đứt — và không test nào bắt được, vì
/// bảng vẫn đầy chữ.</para>
///
/// <para>Khoá ba điều: (a) EN hiện ra khi có bản dịch; (b) rơi về VI khi thiếu,
/// KHÔNG để ô trống; (c) đổi ngôn ngữ giữa chừng thì bảng vẽ lại — và tab đang
/// mở KHÔNG bị văng, vì khoá tab vẫn là chuỗi VI.</para>
/// </summary>
public sealed class IpqcDashboardLanguageTests : TestContext
{
    private readonly RecordingApi _api = new();
    private readonly StubAuthSession _session = new();

    public IpqcDashboardLanguageTests()
    {
        Services.AddSingleton<ICclApiClient>(_api);
        _session.SetUser("qc-user", "QC");
        Services.AddSingleton<IAuthSession>(_session);
        Services.AddI18n();
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        Services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);
        this.AddTestAuthorization().SetAuthorized("qc-user");
    }

    private static IReadOnlyList<ReasonCodeOption> Scraps() => Array.Empty<ReasonCodeOption>();

    /// <summary>Hai hạng mục: một CÓ đủ bản EN, một THIẾU hoàn toàn (đúng trạng
    /// thái của mọi dòng materialize trước tính năng này, và của các dòng
    /// PRESS_CNC không còn nguồn trong thư viện v5).</summary>
    private static IpqcView View() => new()
    {
        WoId = 77, WoNo = "WO-I18N-1", MesPhase = "IPQC_WAIT", ETag = "v1",
        ResolvedLines = "LABEL",
        Items = new[]
        {
            new IpqcViewItem
            {
                ItemKey = "LBL-A1", ProcessLine = "LABEL", Status = "Pending",
                GroupLabel = "A·Ngoại quan",  GroupLabelEn = "A·Appearance",
                Label = "Đúng nội dung in",   LabelEn = "Print content correct",
                Method = "Soi mắt",           MethodEn = "Visual inspection",
                AcceptanceCriteria = "Đúng so spec", AcceptanceCriteriaEn = "Matches the spec",
            },
            new IpqcViewItem
            {
                // Không có cột *En nào — phải rơi về VI ở CẢ hai ngôn ngữ.
                ItemKey = "PNC-A1", ProcessLine = "LABEL", Status = "Pending",
                GroupLabel = "A·Ngoại quan",
                Label = "Gãy / nứt (Crack)",
                Method = "Soi mắt nghiêng",
                AcceptanceCriteria = "Không gãy, không nứt",
            },
        },
    };

    private IRenderedComponent<IpqcDashboard> Render()
    {
        _api.IpqcViewImpl = (_, _) => Task.FromResult(View());
        return RenderComponent<IpqcDashboard>(p => p
            .Add(d => d.WorkOrderId, 77L)
            .Add(d => d.ScrapReasons, Scraps()));
    }

    private void SetLanguage(LanguageCode code) =>
        Services.GetRequiredService<ILanguageService>().Set(code);

    // ── (a) EN hiện ra khi có bản dịch ───────────────────────────────────

    [Fact]
    public void Chon_EN_thi_nhan_method_spec_va_tab_doi_sang_tieng_Anh()
    {
        SetLanguage(LanguageCode.English);
        var cut = Render();

        cut.WaitForAssertion(() =>
        {
            var html = cut.Markup;
            Assert.Contains("Print content correct", html);
            Assert.Contains("Visual inspection", html);
            Assert.Contains("Matches the spec", html);
            Assert.Contains("A·Appearance", html);

            // Bản VI của CHÍNH hạng mục đó phải biến mất — nếu còn thì UI đang
            // vẽ cả hai, tức là chọn ngôn ngữ không có tác dụng.
            Assert.DoesNotContain("Đúng nội dung in", html);
            Assert.DoesNotContain("Đúng so spec", html);
        });
    }

    [Fact]
    public void Mac_dinh_tieng_Viet_van_giu_nguyen_hanh_vi_cu()
    {
        var cut = Render();

        cut.WaitForAssertion(() =>
        {
            var html = cut.Markup;
            Assert.Contains("Đúng nội dung in", html);
            Assert.Contains("Soi mắt", html);
            Assert.Contains("A·Ngoại quan", html);
            Assert.DoesNotContain("Print content correct", html);
        });
    }

    // ── (b) thiếu EN thì rơi về VI, không để ô trống ─────────────────────

    [Fact]
    public void Hang_muc_khong_co_ban_EN_thi_van_hien_tieng_Viet_chu_khong_trong()
    {
        SetLanguage(LanguageCode.English);
        var cut = Render();

        cut.WaitForAssertion(() =>
        {
            var html = cut.Markup;
            // PNC-A1 không có bản dịch ⇒ giữ nguyên tiếng Việt. Ô trống ở đây
            // nghĩa là người vận hành mất tiêu chí chấp nhận — nguy hiểm hơn
            // hẳn so với việc thấy một dòng chưa dịch.
            Assert.Contains("Gãy / nứt (Crack)", html);
            Assert.Contains("Soi mắt nghiêng", html);
            Assert.Contains("Không gãy, không nứt", html);
        });
    }

    // ── (c) đổi ngôn ngữ giữa chừng ──────────────────────────────────────

    [Fact]
    public void Doi_ngon_ngu_giua_chung_thi_bang_ve_lai_va_tab_dang_mo_khong_bi_vang()
    {
        var cut = Render();
        cut.WaitForAssertion(() => Assert.Contains("Đúng nội dung in", cut.Markup));

        SetLanguage(LanguageCode.English);

        cut.WaitForAssertion(() =>
        {
            var html = cut.Markup;
            Assert.Contains("Print content correct", html);
            Assert.DoesNotContain("Đúng nội dung in", html);

            // Tab vẫn hiện (khoá tab là chuỗi VI nên state _activeTab sống sót;
            // dịch cả khoá sẽ làm tab đang mở không khớp và bảng trắng trơn).
            Assert.Contains("A·Appearance", html);
            Assert.Contains("Gãy / nứt (Crack)", html);
        });

        // …và quay lại VI cũng phải chạy, không kẹt một chiều.
        SetLanguage(LanguageCode.Vietnamese);
        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Đúng nội dung in", cut.Markup);
            Assert.DoesNotContain("Print content correct", cut.Markup);
        });
    }
}
