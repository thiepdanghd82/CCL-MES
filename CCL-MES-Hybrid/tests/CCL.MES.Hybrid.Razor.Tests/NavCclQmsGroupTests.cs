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
/// "QC QMS Data" sidebar group — refactored to the QA mini-QMS mock. Proves:
/// (1) the 5 primary modules render as 2-line items (title + subtitle) in the
/// mock order Dashboard/IQC/IPQC/OQC/iCRA for QC-policy roles; (2) the compact
/// secondary links keep the rest of the QC surfaces; (3) RBAC-by-omission — a
/// role outside Admin/Supervisor/QC never sees the group; (4) QC History /
/// Library moved OUT of MONITORING; (5) the new labels flip EN/VI live.
/// </summary>
public sealed class NavCclQmsGroupTests : TestContext
{
    private readonly InMemoryLanguageService _lang = new();

    // 5 primary 2-line modules, in mock order.
    private static readonly (string Href, string Testid)[] Primary =
    {
        ("/qms/dashboard", "nav-qms-dashboard"),
        ("/qms/iqc",       "nav-qms-iqc"),
        ("/qms/ipqc",      "nav-qms-ipqc"),
        ("/qms/oqc",       "nav-qms-oqc"),
        ("/qms/icra",      "nav-qms-icra"),
    };

    // Compact secondary links that must stay reachable as NavLinks (nothing
    // lost). P2-PR1 moved /qms/history + /qc/library OUT of this anchor list —
    // they now open as floating WINDOWS (buttons), asserted separately below.
    private static readonly string[] Secondary =
        { "/qms", "/qms/fqc", "/quality/traceability" };

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
    public void Qms_group_renders_five_two_line_modules_for_qc_roles(string role)
    {
        Wire(role);
        var cut = RenderComponent<NavMenu>();

        Assert.Contains("QC QMS Data", cut.Markup);   // section header

        // Each primary module is a 2-line item with a title + subtitle span.
        foreach (var (href, testid) in Primary)
        {
            var link = cut.FindAll($"a.app-nav-link-2line[data-testid='{testid}']");
            Assert.Single(link);
            Assert.Equal(href, link[0].GetAttribute("href"));
            Assert.Single(link[0].QuerySelectorAll(".nav-title"));
            Assert.Single(link[0].QuerySelectorAll(".nav-sub"));
        }

        // Secondary QC surfaces stay reachable.
        foreach (var href in Secondary)
            Assert.Contains(cut.FindAll("a.app-nav-link-sub"),
                a => a.GetAttribute("href") == href);

        // P2-PR1 — QC History + QC Library now open as floating windows (buttons).
        Assert.Single(cut.FindAll("[data-testid='nav-win-qchistory']"));
        Assert.Single(cut.FindAll("[data-testid='nav-win-qclibrary']"));
    }

    [Fact]
    public void Primary_modules_render_in_mock_order()
    {
        Wire("QC");
        var cut = RenderComponent<NavMenu>();

        var order = cut.FindAll("a.app-nav-link-2line")
                       .Select(a => a.GetAttribute("data-testid"))
                       .ToArray();
        Assert.Equal(new[] { "nav-qms-dashboard", "nav-qms-iqc", "nav-qms-ipqc", "nav-qms-oqc", "nav-qms-icra" }, order);
    }

    [Theory]
    [InlineData("Operator")]
    [InlineData("Engineer")]
    public void Qms_group_hidden_for_non_qc_roles_rbac_by_omission(string role)
    {
        Wire(role);
        var cut = RenderComponent<NavMenu>();

        Assert.DoesNotContain("QC QMS Data", cut.Markup);
        Assert.Empty(cut.FindAll("a.app-nav-link-2line"));
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
