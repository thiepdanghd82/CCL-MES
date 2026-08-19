using System.Linq;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// QA / mini-QMS module pages (scaffold, static mock). Each module renders the
/// shell (topbar + stepper + tables) matching the reference layout; the OK/NG
/// toggle + lot decision drive local state (IPQC lock banner, OQC iCRA trigger).
/// </summary>
public sealed class QmsModulePagesTests : TestContext
{
    private readonly InMemoryLanguageService _lang = new();

    public QmsModulePagesTests()
    {
        Services.AddSingleton<ILanguageService>(_lang);
        Services.AddSingleton<ITranslationCatalog, TranslationCatalog>();
        Services.AddSingleton<ITranslator, Translator>();
        // feat/iqc-ticket — IqcModule now injects ICclApiClient + IAuthSession
        // (Add-ticket form). Chrome/scaffold tests don't open the form so the
        // defaults never fire; wiring them keeps DI resolution happy.
        Services.AddSingleton<CCL.MES.Hybrid.Client.ICclApiClient>(new _Support.RecordingApi());
        Services.AddSingleton<CCL.MES.Hybrid.Client.Auth.IAuthSession>(new _Support.StubAuthSession());
        // feat/iqc-module-tabs — IqcModule hosts MaterialsInspectionForm showcards
        // (L34), so it injects IFloatingWindowStore even when the form isn't opened.
        Services.AddSingleton<CCL.MES.Hybrid.Client.Windows.IFloatingWindowStore>(
            new CCL.MES.Hybrid.Client.Windows.InMemoryFloatingWindowStore());
        this.AddTestAuthorization().SetAuthorized("qc-user");
    }

    // ── IQC (feat/iqc-module-tabs — 3 sub-tab shell) ──────────────────────
    [Fact]
    public void Iqc_renders_subtabs_directly_without_module_strip()
    {
        var cut = RenderComponent<IqcModule>();

        // feat/qms-topbar-breadcrumb: the per-module strip (+ duplicate user
        // badge) is gone — module identity now lives in the main topbar
        // breadcrumb. Sub-tabs sit directly under the main topbar.
        Assert.Empty(cut.FindAll("[data-testid='qms-module-topbar']"));
        Assert.Empty(cut.FindAll("[data-testid='qms-user-badge']"));
        // Redesigned shell: Dashboard · IQC Data · New Ticket sub-tabs.
        Assert.Single(cut.FindAll("[data-testid='iqc-subtab-dashboard']"));
        Assert.Single(cut.FindAll("[data-testid='iqc-subtab-data']"));
        Assert.Single(cut.FindAll("[data-testid='iqc-subtab-newticket']"));
        // Dashboard active by default.
        Assert.Single(cut.FindAll("[data-testid='iqc-dash']"));
    }

    [Fact]
    public void Iqc_subtab_click_switches_active_surface()
    {
        var cut = RenderComponent<IqcModule>();
        Assert.Single(cut.FindAll("[data-testid='iqc-dash']"));

        cut.Find("[data-testid='iqc-subtab-newticket']").Click();

        Assert.Single(cut.FindAll("[data-testid='iqc-newticket']"));
        Assert.Empty(cut.FindAll("[data-testid='iqc-dash']"));
    }

    // ── IPQC ─────────────────────────────────────────────────────────────
    [Fact]
    public void Ipqc_renders_material_table_lock_banner_and_check_table()
    {
        var cut = RenderComponent<IpqcModule>();

        Assert.Single(cut.FindAll("[data-testid='qms-ipqc-material-table']"));
        Assert.Equal(3, cut.FindAll("[data-testid='qms-stepper'] .qms-step").Count);
        Assert.Single(cut.FindAll("[data-testid='qms-inspection-table']"));   // shared check table
        // A material is seeded NG → the setup-lock banner shows.
        Assert.Single(cut.FindAll("[data-testid='qms-ipqc-lock-banner']"));
    }

    [Fact]
    public void Ipqc_toggle_ng_to_ok_clears_lock_banner_when_all_ok()
    {
        var cut = RenderComponent<IpqcModule>();
        Assert.Single(cut.FindAll("[data-testid='qms-ipqc-lock-banner']"));

        // Flip every material's OK button → no NG left → banner disappears.
        foreach (var toggle in cut.FindAll("[data-testid='qms-okng']").ToList())
            toggle.QuerySelector(".qms-okng-ok")!.Click();

        Assert.Empty(cut.FindAll("[data-testid='qms-ipqc-lock-banner']"));
    }

    // ── OQC ──────────────────────────────────────────────────────────────
    [Fact]
    public void Oqc_renders_trace_check_and_decision_panel()
    {
        var cut = RenderComponent<OqcModule>();

        Assert.Single(cut.FindAll("[data-testid='qms-oqc-trace']"));
        Assert.Single(cut.FindAll("[data-testid='qms-inspection-table']"));
        Assert.Single(cut.FindAll("[data-testid='qms-decision-panel']"));
        Assert.Contains("SP-KX120", cut.Markup);
    }

    [Fact]
    public void Oqc_reject_shows_icra_trigger_accept_hides_it()
    {
        var cut = RenderComponent<OqcModule>();
        // Mock defaults to Reject → iCRA trigger visible.
        Assert.Single(cut.FindAll("[data-testid='qms-icra-trigger']"));

        cut.Find("[data-testid='qms-decision-accept']").Click();
        Assert.Empty(cut.FindAll("[data-testid='qms-icra-trigger']"));

        cut.Find("[data-testid='qms-decision-reject']").Click();
        Assert.Single(cut.FindAll("[data-testid='qms-icra-trigger']"));
    }

    // ── iCRA ─────────────────────────────────────────────────────────────
    [Fact]
    public void Icra_renders_request_list()
    {
        var cut = RenderComponent<IcraModule>();
        Assert.Single(cut.FindAll("[data-testid='qms-icra-table']"));
        Assert.Contains("ICRA-260809-01", cut.Markup);
    }

    // ── i18n live flip ───────────────────────────────────────────────────
    [Fact]
    public void Module_chrome_flips_en_vi_live()
    {
        // Module title moved to the main-topbar breadcrumb / <PageTitle> (not in
        // this component's body markup) — assert on the sub-tab chrome instead.
        var cut = RenderComponent<IqcModule>();
        Assert.Contains("Bảng điều khiển", cut.Markup);                    // VI sub-tab

        _lang.Set(LanguageCode.English);

        cut.WaitForAssertion(() =>
            Assert.Contains("Dashboard", cut.Markup));                     // EN sub-tab
    }
}
