using System.Linq;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Client.RecentScans;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// "QC QMS Data" sidebar group — sidebar revamp (Phương án A): the 5 primary
/// modules now render as iX/Carbon SINGLE-LINE items (icon + short label, the
/// former uppercase subtitle carried into title/aria-label, NOT a rendered
/// second line). Proves: (1) the 5 primary modules render in the mock order
/// Dashboard/IQC/IPQC/OQC/iCRA for QC-policy roles, each with an <svg> icon and
/// a title/aria-label from its subtitle; (2) the deeper sub-items keep the rest
/// of the QC surfaces reachable; (3) RBAC-by-omission — a role outside
/// Admin/Supervisor/QC never sees the group; (4) QC History / Library moved OUT
/// of MONITORING; (5) the new labels flip EN/VI live; (6) the 2-line variant is
/// gone (no .nav-title / .nav-sub second line rendered).
/// </summary>
public sealed class NavCclQmsGroupTests : TestContext
{
    private readonly InMemoryLanguageService _lang = new();

    // 5 primary single-line modules, in mock order. P2-PR2 — 4 of them
    // (Dashboard/IPQC/OQC/iCRA) open as floating WINDOWS (<button>); only
    // /qms/iqc stays a NavLink <a> (IqcModule self-hosts its own FloatingWindow
    // showcards → window-in-window migration deferred to PR3). Order preserved
    // regardless of anchor-vs-button.
    private static readonly (string Testid, bool IsWindowButton)[] Primary =
    {
        ("nav-win-qms-dashboard", true),
        ("nav-qms-iqc",           false),
        ("nav-win-qms-ipqc",      true),
        ("nav-win-qms-oqc",       true),
        ("nav-win-qms-icra",      true),
    };

    private void Wire(string role)
    {
        var session = new StubAuthSession();
        session.SetUser("u", role);
        Services.AddSingleton<IAuthSession>(session);
        Services.AddSingleton<ILanguageService>(_lang);
        Services.AddSingleton<ITranslationCatalog, TranslationCatalog>();
        Services.AddSingleton<ITranslator, Translator>();
        Services.AddSingleton<IRecentScansService, InMemoryRecentScansService>();
        // P2-PR1 — NavMenu injects the window manager + registry.
        Services.AddSingleton<CCL.MES.Hybrid.Client.Windows.IWindowManager,
            CCL.MES.Hybrid.Client.Windows.WindowManager>();
        Services.AddSingleton<CCL.MES.Hybrid.Client.Windows.IWindowRegistry,
            CCL.MES.Hybrid.Client.Windows.WindowRegistry>();
        Services.AddSingleton<IOptions<HardwareOptions>>(
            Options.Create(new HardwareOptions { ScanEnabled = true }));
        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("u");
        auth.SetRoles(role);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("Supervisor")]
    [InlineData("QC")]
    public void Qms_group_renders_five_single_line_modules_for_qc_roles(string role)
    {
        Wire(role);
        var cut = RenderComponent<NavMenu>();

        Assert.Contains("QC QMS Data", cut.Markup);   // section header

        // Each primary module is a SINGLE-line item (icon + label) rendered as a
        // window <button> (PR2) or the surviving IQC NavLink <a>. The former
        // subtitle is carried into title/aria-label, not a second rendered line.
        foreach (var (testid, isWindowButton) in Primary)
        {
            var tag = isWindowButton ? "button" : "a";
            var el = cut.FindAll($"{tag}.app-nav-link[data-testid='{testid}']");
            Assert.Single(el);
            Assert.Single(el[0].QuerySelectorAll("svg.nav-ico"));   // icon on every module
            Assert.False(string.IsNullOrEmpty(el[0].GetAttribute("title")));       // subtitle → tooltip
            Assert.False(string.IsNullOrEmpty(el[0].GetAttribute("aria-label")));  // subtitle → aria
            Assert.Empty(el[0].QuerySelectorAll(".nav-sub"));       // NO second line
        }

        // The 2-line variant is gone entirely.
        Assert.Empty(cut.FindAll(".app-nav-link-2line"));

        // P2-PR1 — QC History + QC Library now open as floating windows (buttons).
        Assert.Single(cut.FindAll("[data-testid='nav-win-qchistory']"));
        Assert.Single(cut.FindAll("[data-testid='nav-win-qclibrary']"));

        // P2-PR3 — the Inspection Queue hub + FQC stage are window buttons now.
        Assert.Single(cut.FindAll("[data-testid='nav-win-qms-queue']"));
        Assert.Single(cut.FindAll("[data-testid='nav-win-qms-fqc']"));
        Assert.Empty(cut.FindAll("a[href='/qms']"));
        Assert.Empty(cut.FindAll("a[href='/qms/fqc']"));

        // P2 showcard-migration — Traceability is a window button, no longer a NavLink.
        Assert.Single(cut.FindAll("[data-testid='nav-win-traceability']"));
        Assert.Empty(cut.FindAll("a[href='/quality/traceability']"));
    }

    [Fact]
    public void Primary_modules_render_in_mock_order()
    {
        Wire("QC");
        var cut = RenderComponent<NavMenu>();

        // Order holds across the anchor/button mix (P2-PR2): the 5 primary
        // modules by testid, in document order.
        var wanted = Primary.Select(p => p.Testid).ToArray();
        var order = cut.FindAll("[data-testid^='nav-']")
                       .Select(a => a.GetAttribute("data-testid"))
                       .Where(id => wanted.Contains(id))
                       .ToArray();
        Assert.Equal(new[] { "nav-win-qms-dashboard", "nav-qms-iqc", "nav-win-qms-ipqc", "nav-win-qms-oqc", "nav-win-qms-icra" }, order);
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Engineer")]
    public void Qms_group_hidden_for_non_qc_roles_rbac_by_omission(string role)
    {
        Wire(role);
        var cut = RenderComponent<NavMenu>();

        Assert.DoesNotContain("QC QMS Data", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid='nav-win-qms-dashboard']"));
        Assert.Empty(cut.FindAll("[data-testid='nav-win-qms-icra']"));
        foreach (var href in new[] { "/qms/dashboard", "/qms/iqc", "/qms/icra", "/qms/history", "/qc/library" })
            Assert.DoesNotContain(cut.FindAll("a"), a => a.GetAttribute("href") == href);

        // MONITORING (ungated) still renders → the QC links MOVED, not hidden.
        // P2-PR1 — the monitoring items are now window-opening buttons.
        Assert.Single(cut.FindAll("[data-testid='nav-win-machines']"));
        Assert.Single(cut.FindAll("[data-testid='nav-win-shoporders']"));
    }

    [Fact]
    public void Qc_history_and_library_are_not_in_monitoring_section()
    {
        // Operator sees MONITORING but NOT the QC links → they left MONITORING.
        Wire("Operator");
        var cut = RenderComponent<NavMenu>();

        Assert.Single(cut.FindAll("[data-testid='nav-win-machines']"));   // MONITORING present
        Assert.DoesNotContain("QC History", cut.Markup);
        Assert.DoesNotContain("QC Library", cut.Markup);
        Assert.DoesNotContain("Lịch sử QC", cut.Markup);
        Assert.DoesNotContain("Thư viện QC", cut.Markup);
    }

    [Fact]
    public void New_qms_labels_flip_en_vi_live()
    {
        Wire("QC");
        var cut = RenderComponent<NavMenu>();

        // Default Vietnamese — title + subtitle.
        Assert.Contains("Tổng quan", cut.Markup);       // nav.qms.m.dashboard VI
        Assert.Contains("Kiểm đầu vào", cut.Markup);     // nav.qms.m.iqc.sub VI

        _lang.Set(LanguageCode.English);                 // live flip

        cut.WaitForAssertion(() =>
        {
            Assert.Contains("Overview", cut.Markup);           // dashboard title EN
            Assert.Contains("Incoming quality", cut.Markup);   // iqc subtitle EN
            Assert.Contains("QC QMS Data", cut.Markup);        // brand identical both langs
        });
    }
}
