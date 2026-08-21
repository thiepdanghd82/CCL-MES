using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Hybrid.Client.RecentScans;
using CCL.MES.Hybrid.Client.Windows;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P2-PR2 — the remaining SAFE sidebar tabs (Home, 4 NPI grids, Semi store, and
/// the QMS Dashboard/IPQC/OQC/iCRA modules) now open floating WINDOWS via
/// WM.Open instead of navigating the whole shell, keeping AuthorizeView
/// RBAC-by-omission. Mirrors NavMenuWindowOpenTests (PR1) but for the PR2 subset.
/// The deferred routes (/npi/specs, /workorders, /qms/iqc, /qms, /qms/fqc,
/// /quality/traceability, /settings) stay NavLinks — asserted here too.
/// </summary>
public sealed class NavMenuWindowOpenPr2Tests : TestContext
{
    private readonly WindowManager _wm = new();
    private readonly WindowRegistry _registry = new();

    private void Wire(string role)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        var session = new StubAuthSession();
        session.SetUser("u", role);
        Services.AddSingleton<IAuthSession>(session);
        Services.AddI18n();
        Services.AddSingleton<IRecentScansService, InMemoryRecentScansService>();
        Services.AddSingleton<IWindowManager>(_wm);
        Services.AddSingleton<IWindowRegistry>(_registry);
        Services.AddSingleton<IOptions<HardwareOptions>>(
            Options.Create(new HardwareOptions { ScanEnabled = true }));
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("u");
        auth.SetRoles(role);

        // Bind the PR2 subset keys to a param-free stand-in so the test needs no
        // page DI (the real MainLayout binds the concrete page types).
        foreach (var key in new[]
        {
            WindowRegistryKeys.NpiStructure, WindowRegistryKeys.NpiRoutine,
            WindowRegistryKeys.NpiRawMaterials, WindowRegistryKeys.NpiWorkCenters,
            WindowRegistryKeys.SemiProducts,
            WindowRegistryKeys.QmsDashboard, WindowRegistryKeys.QmsIpqc,
            WindowRegistryKeys.QmsOqc, WindowRegistryKeys.QmsIcra,
        })
        {
            _registry.Register(new WindowRegistryEntry(
                key, typeof(CounterProbe), "windows.qc_history.title"));
        }
    }

    [Theory]
    [InlineData("nav-win-npi-structure", "/npi/structure")]
    [InlineData("nav-win-npi-routine", "/npi/routine")]
    [InlineData("nav-win-npi-rawmaterials", "/npi/rawmaterials")]
    [InlineData("nav-win-npi-workcenters", "/npi/workcenters")]
    [InlineData("nav-win-semiproducts", "/warehouse/semi-products")]
    [InlineData("nav-win-qms-dashboard", "/qms/dashboard")]
    [InlineData("nav-win-qms-ipqc", "/qms/ipqc")]
    [InlineData("nav-win-qms-oqc", "/qms/oqc")]
    [InlineData("nav-win-qms-icra", "/qms/icra")]
    public void Safe_tab_opens_a_window_for_its_key(string testid, string expectedKey)
    {
        Wire("Admin");
        var cut = RenderComponent<NavMenu>();

        cut.Find($"[data-testid='{testid}']").Click();

        Assert.Single(_wm.Windows);
        Assert.Equal(expectedKey, _wm.Windows[0].Key);
    }

    [Fact]
    public void Reopening_same_pr2_tab_dedupes_focuses_existing()
    {
        Wire("Admin");
        var cut = RenderComponent<NavMenu>();

        cut.Find("[data-testid='nav-win-qms-dashboard']").Click();
        cut.Find("[data-testid='nav-win-qms-dashboard']").Click();

        Assert.Single(_wm.Windows);   // dedupe by key — no duplicate window
    }

    [Fact]
    public void Home_is_a_full_page_navlink_not_a_window()
    {
        // FIX 1 — Home is the default full-page landing: an <a href="/"> NavLink,
        // NOT a window <button>. Tapping it navigates the shell to "/" so the Home
        // dashboard renders in @Body (KPI + modules), never the empty workspace.
        Wire("Admin");
        var cut = RenderComponent<NavMenu>();

        var home = cut.Find("[data-testid='nav-home']");
        Assert.Equal("a", home.TagName.ToLowerInvariant());
        Assert.Equal("/", home.GetAttribute("href"));

        // The old window button is gone (no key opens a Home window anymore).
        Assert.Empty(cut.FindAll("[data-testid='nav-win-home']"));
        Assert.Empty(_wm.Windows);
    }

    [Fact]
    public void Deferred_routes_remain_navlinks_not_windows()
    {
        Wire("Admin");
        var cut = RenderComponent<NavMenu>();

        // Deferred surfaces keep their href anchor (no window button).
        Assert.NotNull(cut.Find("a[href='/npi/specs']"));
        Assert.NotNull(cut.Find("a[href='/workorders']"));
        Assert.NotNull(cut.Find("a[href='/qms/iqc']"));
        Assert.NotNull(cut.Find("a[href='/qms']"));
        Assert.NotNull(cut.Find("a[href='/qms/fqc']"));
        Assert.NotNull(cut.Find("a[href='/quality/traceability']"));
        Assert.NotNull(cut.Find("a[href='/settings']"));
    }

    [Fact]
    public void Npi_window_tabs_hidden_for_operator_rbac_by_omission()
    {
        Wire("Operator");
        var cut = RenderComponent<NavMenu>();

        // The 4 NPI grids sit under a role-gated AuthorizeView (Admin/Sup/Eng/QC).
        Assert.Empty(cut.FindAll("[data-testid='nav-win-npi-structure']"));
        Assert.Empty(cut.FindAll("[data-testid='nav-win-qms-dashboard']"));
    }
}
