using System.Linq;
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
/// Sidebar revamp (Phương án A) — accordion + icon-parity contract. Precedent:
/// IBM Carbon SideNavMenu (collapsible section header + chevron + aria-expanded)
/// over the Siemens iX rail. Proves: (1) EVERY top-level item carries an inline
/// <svg> icon (so the 64px collapsed rail is legible); (2) the group holding the
/// active route auto-opens; (3) toggling a section header flips aria-expanded and
/// hides/shows the group body; (4) href/route/testids unchanged (no nav lost).
/// </summary>
public sealed class NavMenuAccordionTests : TestContext
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
    }

    // Every top-level nav item that is NOT a collapsible section header must
    // carry an icon. The 64px collapsed rail shows icon-only, so a missing icon
    // = a blank row a gloved operator can't identify.
    [Fact]
    public void Every_top_level_item_has_an_svg_icon()
    {
        Wire("Admin");
        var cut = RenderComponent<NavMenu>();

        var items = cut.FindAll("a.app-nav-link, button.app-nav-link");
        Assert.NotEmpty(items);
        foreach (var item in items)
        {
            var id = item.GetAttribute("data-testid") ?? item.GetAttribute("href") ?? "(?)";
            Assert.True(item.QuerySelectorAll("svg.nav-ico").Length == 1,
                $"nav item '{id}' is missing its .nav-ico icon");
        }
    }

    // The NPI group owns /npi/* — landing there auto-opens that group even if a
    // prior session had collapsed it (route wins over persisted state).
    [Fact]
    public void Group_owning_active_route_auto_opens()
    {
        Wire("Admin");
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        nav.NavigateTo("/npi/structure");

        var cut = RenderComponent<NavMenu>();

        var header = cut.Find("[data-testid='nav-grphdr-npi']");
        Assert.Equal("true", header.GetAttribute("aria-expanded"));
        var body = cut.Find("[data-testid='nav-grpbody-npi']");
        Assert.False(body.HasAttribute("hidden"));   // body visible
    }

    // A group NOT owning the active route still renders its header; default open
    // (no persisted collapse) so its body is visible.
    [Fact]
    public void Groups_default_open_when_no_persisted_state()
    {
        Wire("Admin");
        var cut = RenderComponent<NavMenu>();

        foreach (var g in new[] { "npi", "production", "monitoring", "qms" })
        {
            var header = cut.Find($"[data-testid='nav-grphdr-{g}']");
            Assert.Equal("true", header.GetAttribute("aria-expanded"));
            Assert.False(cut.Find($"[data-testid='nav-grpbody-{g}']").HasAttribute("hidden"));
        }
    }

    // Clicking a section header collapses the group: aria-expanded flips to
    // false and the body gains the [hidden] attribute. Clicking again re-opens.
    [Fact]
    public void Toggling_section_header_flips_aria_expanded_and_hides_body()
    {
        Wire("Admin");
        var cut = RenderComponent<NavMenu>();

        var headerSel = "[data-testid='nav-grphdr-monitoring']";
        var bodySel = "[data-testid='nav-grpbody-monitoring']";

        Assert.Equal("true", cut.Find(headerSel).GetAttribute("aria-expanded"));
        Assert.False(cut.Find(bodySel).HasAttribute("hidden"));

        cut.Find(headerSel).Click();   // collapse

        Assert.Equal("false", cut.Find(headerSel).GetAttribute("aria-expanded"));
        Assert.True(cut.Find(bodySel).HasAttribute("hidden"));

        cut.Find(headerSel).Click();   // re-open

        Assert.Equal("true", cut.Find(headerSel).GetAttribute("aria-expanded"));
        Assert.False(cut.Find(bodySel).HasAttribute("hidden"));
    }

    // Collapsing a group must NOT remove its items from the DOM contract — the
    // window-open buttons keep their testids (route/RBAC unchanged), they are
    // only visually hidden by the [hidden] body wrapper.
    [Fact]
    public void Collapsed_group_keeps_item_testids_in_dom()
    {
        Wire("Admin");
        var cut = RenderComponent<NavMenu>();

        cut.Find("[data-testid='nav-grphdr-monitoring']").Click();   // collapse

        Assert.Single(cut.FindAll("[data-testid='nav-win-machines']"));
        Assert.Single(cut.FindAll("[data-testid='nav-win-shoporders']"));
    }
}
