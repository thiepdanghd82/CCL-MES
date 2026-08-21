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
/// Sidebar revamp — Phương án B (advanced navigation). Precedent: Cloudscape /
/// IBM Carbon side-nav quick-filter + VS Code / JetBrains "recent". Proves the
/// three new affordances layered on top of A WITHOUT breaking the accordion:
///   (1) QUICK FIND filters items by label (diacritics + case insensitive),
///       force-opens groups holding a hit, hides non-matching items/groups, and
///       shows a "no results" row on an empty match; clearing restores the
///       normal accordion.
///   (2) PIN toggles an item into a top PINNED group + persists the CSV via
///       cclMesDensity.navPinsSet.
///   (3) RECENT records visited routes (MRU) via cclMesDensity.navRecentSet and
///       surfaces them (minus current page + minus pinned) in a RECENT group.
/// A-era testids (nav-grphdr-*, nav-win-*, nav-home) stay intact — nothing lost.
/// </summary>
public sealed class NavMenuAdvancedNavTests : TestContext
{
    private void Wire(string role = "Admin", string? pinsCsv = null, string? recentCsv = null)
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        // Persist reads (OnAfterRenderAsync). Loose mode would return null; give
        // explicit values so pin/recent seeding is deterministic.
        JSInterop.Setup<string>("cclMesDensity.navGroupsGet").SetResult(string.Empty);
        JSInterop.Setup<string>("cclMesDensity.navPinsGet").SetResult(pinsCsv ?? string.Empty);
        JSInterop.Setup<string>("cclMesDensity.navRecentGet").SetResult(recentCsv ?? string.Empty);
        JSInterop.SetupVoid("cclMesDensity.navGroupsSet", _ => true);
        JSInterop.SetupVoid("cclMesDensity.navPinsSet", _ => true);
        JSInterop.SetupVoid("cclMesDensity.navRecentSet", _ => true);

        var session = new StubAuthSession();
        session.SetUser("u", role);
        Services.AddSingleton<IAuthSession>(session);
        Services.AddI18n();
        Services.AddSingleton<IRecentScansService, InMemoryRecentScansService>();
        Services.AddSingleton<IWindowManager>(new WindowManager());
        Services.AddSingleton<IWindowRegistry>(new WindowRegistry());
        Services.AddSingleton<IOptions<HardwareOptions>>(
            Options.Create(new HardwareOptions { ScanEnabled = true }));
        var auth = this.AddTestAuthorization();
        auth.SetAuthorized("u");
        auth.SetRoles(role);
    }

    // ── (1) FILTER ─────────────────────────────────────────────────────────────

    // Typing a needle that matches only the Machine Dashboard label hides the
    // non-matching items (data-nav-hit="false") and marks the matching one a hit.
    [Fact]
    public void Typing_filter_marks_matching_hit_and_others_miss()
    {
        Wire();
        var cut = RenderComponent<NavMenu>();   // VI: /machines = "Bảng điều khiển máy"

        cut.Find("[data-testid='nav-find-input']").Input("dieu khien");

        // Matching item (Machine Dashboard = /machines) is a hit ("điều khiển").
        var machine = cut.Find("[data-nav-key='/machines']");
        Assert.Equal("true", machine.GetAttribute("data-nav-hit"));

        // A clearly non-matching item (Work Orders = "Lệnh SX — Quét") is a miss.
        var wo = cut.Find("[data-nav-key='/workorders']");
        Assert.Equal("false", wo.GetAttribute("data-nav-hit"));

        // The <nav> carries the filtering class so CSS hides the misses.
        Assert.Contains("nav-filtering", cut.Find("nav").GetAttribute("class"));
    }

    // Diacritics + case are folded: "cong" matches "Công đoạn" (Routings VI).
    [Fact]
    public void Filter_folds_diacritics_and_case()
    {
        Wire();
        var cut = RenderComponent<NavMenu>();   // defaults to VI

        cut.Find("[data-testid='nav-find-input']").Input("CONG");

        // "/npi/routine" label VI = "Công đoạn" → folded "cong doan" contains "cong".
        Assert.Equal("true", cut.Find("[data-nav-key='/npi/routine']").GetAttribute("data-nav-hit"));
    }

    // A group whose only hits live inside it is force-open (body not [hidden])
    // even if it started collapsed; a group with NO hit is hidden entirely.
    [Fact]
    public void Filter_force_opens_group_with_hit_and_hides_group_without()
    {
        Wire();
        var cut = RenderComponent<NavMenu>();

        // Collapse MONITORING first (so we can prove the filter re-opens it).
        cut.Find("[data-testid='nav-grphdr-monitoring']").Click();
        Assert.True(cut.Find("[data-testid='nav-grpbody-monitoring']").HasAttribute("hidden"));

        cut.Find("[data-testid='nav-find-input']").Input("dieu khien");

        // MONITORING owns /machines → force-open (body visible), group not hidden.
        Assert.False(cut.Find("[data-testid='nav-grpbody-monitoring']").HasAttribute("hidden"));
        Assert.False(cut.Find("[data-testid='nav-grp-monitoring']").HasAttribute("hidden"));

        // NPI holds no "machine" hit → the whole group is hidden.
        Assert.True(cut.Find("[data-testid='nav-grp-npi']").HasAttribute("hidden"));
    }

    // An empty match shows the "no results" row.
    [Fact]
    public void Filter_with_no_match_shows_no_results_row()
    {
        Wire();
        var cut = RenderComponent<NavMenu>();

        cut.Find("[data-testid='nav-find-input']").Input("zzzznotamenu");

        Assert.Single(cut.FindAll("[data-testid='nav-find-none']"));
    }

    // Clearing the filter restores the normal accordion (no filtering class, no
    // no-results row, all items back to hit="true").
    [Fact]
    public void Clearing_filter_restores_accordion()
    {
        Wire();
        var cut = RenderComponent<NavMenu>();

        cut.Find("[data-testid='nav-find-input']").Input("machine");
        Assert.Contains("nav-filtering", cut.Find("nav").GetAttribute("class"));

        cut.Find("[data-testid='nav-find-clear']").Click();

        Assert.DoesNotContain("nav-filtering", cut.Find("nav").GetAttribute("class") ?? "");
        Assert.Empty(cut.FindAll("[data-testid='nav-find-none']"));
        Assert.Equal("true", cut.Find("[data-nav-key='/workorders']").GetAttribute("data-nav-hit"));
    }

    // ── (2) PIN ────────────────────────────────────────────────────────────────

    // Pinning an item creates the PINNED group + persists the CSV via
    // cclMesDensity.navPinsSet.
    [Fact]
    public void Pinning_item_shows_pinned_group_and_persists()
    {
        Wire();
        var cut = RenderComponent<NavMenu>();

        // No PINNED group before any pin.
        Assert.Empty(cut.FindAll("[data-testid='nav-grp-pinned']"));

        // Click the pin affordance on the Machine Dashboard item.
        cut.Find("[data-testid='nav-pin-/machines']").Click();

        // PINNED group appears with the pinned item rendered.
        Assert.Single(cut.FindAll("[data-testid='nav-grp-pinned']"));
        Assert.Single(cut.FindAll("[data-testid='nav-pin-item-/machines']"));

        // Persisted the CSV (value = "/machines").
        var call = Assert.Single(JSInterop.Invocations["cclMesDensity.navPinsSet"]);
        Assert.Equal("/machines", call.Arguments[0]);
    }

    // A persisted pin is loaded on init → the PINNED group renders it up front.
    [Fact]
    public void Persisted_pin_renders_pinned_group_on_load()
    {
        Wire(pinsCsv: "/workorders");
        var cut = RenderComponent<NavMenu>();

        Assert.Single(cut.FindAll("[data-testid='nav-grp-pinned']"));
        Assert.Single(cut.FindAll("[data-testid='nav-pin-item-/workorders']"));
    }

    // RBAC-by-omission: a persisted pin for a QMS item the user's role can't see
    // is NOT surfaced (operator role can't reach the QMS group).
    [Fact]
    public void Persisted_pin_for_unauthorized_item_is_hidden()
    {
        Wire(role: "Operator", pinsCsv: "/qms/ipqc");
        var cut = RenderComponent<NavMenu>();

        // /qms/ipqc requires Admin/Supervisor/QC → hidden for Operator, so the
        // PINNED group has no visible pin → the group itself does not render.
        Assert.Empty(cut.FindAll("[data-testid='nav-grp-pinned']"));
    }

    // ── (3) RECENT ───────────────────────────────────────────────────────────────

    // Navigating records the destination into the MRU via navRecentSet, and a
    // previously-recorded route surfaces in the RECENT group (minus current page).
    [Fact]
    public void Navigation_records_recent_and_surfaces_group()
    {
        Wire();
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        var cut = RenderComponent<NavMenu>();

        // Visit /machines, then move to /workorders. /machines should now be a
        // "recent" (not the current page) and appear in the RECENT group.
        nav.NavigateTo("/machines");
        nav.NavigateTo("/workorders");

        cut.WaitForAssertion(() =>
        {
            Assert.Single(cut.FindAll("[data-testid='nav-grp-recent']"));
            Assert.Single(cut.FindAll("[data-testid='nav-recent-item-/machines']"));
            // The current page (/workorders) is recorded but NOT rendered in RECENT.
            Assert.Empty(cut.FindAll("[data-testid='nav-recent-item-/workorders']"));
        });

        // navRecentSet was invoked at least once (persist ran).
        Assert.NotEmpty(JSInterop.Invocations["cclMesDensity.navRecentSet"]);
    }

    // A recent route that is ALSO pinned is not duplicated in RECENT (pinned wins).
    [Fact]
    public void Pinned_route_is_excluded_from_recent()
    {
        Wire(pinsCsv: "/machines", recentCsv: "/machines,/workorders");
        var nav = Services.GetRequiredService<FakeNavigationManager>();
        var cut = RenderComponent<NavMenu>();

        // Move to a neutral page so neither /machines nor /workorders is current.
        nav.NavigateTo("/settings");

        cut.WaitForAssertion(() =>
        {
            // /machines is pinned → present in PINNED, absent from RECENT.
            Assert.Single(cut.FindAll("[data-testid='nav-pin-item-/machines']"));
            Assert.Empty(cut.FindAll("[data-testid='nav-recent-item-/machines']"));
            // /workorders is only recent → shows in RECENT.
            Assert.Single(cut.FindAll("[data-testid='nav-recent-item-/workorders']"));
        });
    }

    // ── (4) NO REGRESSION ON A ───────────────────────────────────────────────────

    // The A-era accordion + item testids are all still present (nothing lost).
    [Fact]
    public void A_era_testids_intact()
    {
        Wire();
        var cut = RenderComponent<NavMenu>();

        foreach (var g in new[] { "npi", "production", "monitoring", "qms" })
            Assert.Single(cut.FindAll($"[data-testid='nav-grphdr-{g}']"));

        Assert.Single(cut.FindAll("[data-testid='nav-home']"));
        Assert.Single(cut.FindAll("[data-testid='nav-win-machines']"));
        Assert.Single(cut.FindAll("[data-testid='nav-win-workorders']"));
        Assert.Single(cut.FindAll("[data-testid='nav-qms-iqc']"));
    }
}
