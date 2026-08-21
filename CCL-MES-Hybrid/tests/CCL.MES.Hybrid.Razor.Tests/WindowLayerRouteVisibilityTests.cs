using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Connectivity;
using CCL.MES.Hybrid.Client.Files;
using CCL.MES.Hybrid.Client.Grid;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Hybrid.Client.Printing;
using CCL.MES.Hybrid.Client.RecentScans;
using CCL.MES.Hybrid.Client.Windows;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// Henry hardware-verify fix — full-page-mode vs workspace-mode. The
/// <c>.window-layer</c> (z-index 90, position:fixed) with a maximized-to-
/// workspace window used to cover @Body, so tapping a genuine full-page tab
/// (Home <c>/</c>, <c>/qms/iqc</c>, <c>/quality/traceability</c>, <c>/workorders</c>,
/// <c>/settings</c>…) showed the stale window and looked like "no navigation".
///
/// Hướng A: on a window route the layer is visible; on a real full-page route
/// the layer gets <c>.is-hidden</c> (display:none) so the page shows through —
/// but the windows stay MOUNTED (keep-alive). A taskbar chip single-click routes
/// through <c>MainLayout.OnChipActivate</c> to reveal + focus the window again.
/// </summary>
public sealed class WindowLayerRouteVisibilityTests : TestContext
{
    private readonly WindowManager _wm = new();

    public WindowLayerRouteVisibilityTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddI18n();
        Services.AddSingleton<IWindowManager>(_wm);
        Services.AddSingleton<IWindowRegistry, WindowRegistry>();
        Services.AddSingleton<IFloatingWindowStore, InMemoryFloatingWindowStore>();
        Services.AddSingleton<IAuthSession>(new StubAuthSession());
        Services.AddSingleton<IConnectivityMonitor, AlwaysOnlineConnectivityMonitor>();
        Services.AddSingleton<IRecentScansService, InMemoryRecentScansService>();
        Services.AddSingleton<ICclApiClient>(new RecordingApi());
        Services.AddSingleton<IGridPreferenceStore>(new InMemoryGridPreferenceStore());
        Services.AddSingleton<IFileOpener>(new StubFileOpener());
        Services.AddSingleton<IFileSaver>(new StubFileSaver());
        Services.AddSingleton<IFilePickerService>(new StubFilePickerService());
        Services.AddSingleton<IPrintService>(new StubPrintService());
        Services.AddSingleton<IOptions<HardwareOptions>>(
            Options.Create(new HardwareOptions { ScanEnabled = true }));
        this.AddTestAuthorization().SetAuthorized("qc-user");
    }

    private static RenderFragment RouteBody(string marker) => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "route-body");
        builder.AddAttribute(2, "data-marker", marker);
        builder.CloseElement();
    };

    private void NavTo(string relative) =>
        Services.GetRequiredService<FakeNavigationManager>()
            .NavigateTo("http://localhost/" + relative.TrimStart('/'));

    // (a) window route → window-layer is VISIBLE (no .is-hidden).
    [Fact]
    public void Window_route_shows_window_layer_without_is_hidden()
    {
        NavTo("/qms/dashboard");

        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, RouteBody("A")));

        var layer = cut.Find(".window-layer");
        Assert.DoesNotContain("is-hidden", layer.GetAttribute("class") ?? "");
        // @Body swapped to the workspace background (single-render of the page).
        Assert.Single(cut.FindAll("[data-testid='workspace-home']"));
    }

    // (b) full-page route (/qms/iqc) → window-layer is HIDDEN + @Body renders the
    // page (NOT WorkspaceHome). /qms/iqc is a genuine full-page tab, not a
    // registry window key.
    [Fact]
    public void Full_page_route_qms_iqc_hides_window_layer_and_renders_body()
    {
        NavTo("/qms/iqc");

        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, RouteBody("iqc")));

        var layer = cut.Find(".window-layer");
        Assert.Contains("is-hidden", layer.GetAttribute("class") ?? "");
        Assert.Empty(cut.FindAll("[data-testid='workspace-home']"));
        Assert.Single(cut.FindAll(".route-body"));
        Assert.Empty(_wm.Windows);   // no window opened for a full-page tab
    }

    // (b-2) Home "/" is also a full-page route → layer hidden + @Body renders.
    [Fact]
    public void Full_page_route_root_home_hides_window_layer_and_renders_body()
    {
        NavTo("/");

        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, RouteBody("home")));

        var layer = cut.Find(".window-layer");
        Assert.Contains("is-hidden", layer.GetAttribute("class") ?? "");
        Assert.Empty(cut.FindAll("[data-testid='workspace-home']"));
        Assert.Single(cut.FindAll(".route-body"));
    }

    // (c) chip single-click → OnChipActivate: WM.Focus + (route-key) Nav to key
    // → SyncWorkspaceRoute reveals the layer again.
    [Fact]
    public void Chip_single_click_on_route_window_reveals_layer_and_navigates_to_key()
    {
        // Land on a full-page route → layer starts hidden even though a window
        // (opened earlier in the session) is still mounted behind it.
        NavTo("/qms/iqc");
        var win = _wm.Open(WindowRegistryKeys.QmsDashboard, "Dash", null, typeof(QmsDashboardStub))!;

        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, RouteBody("iqc")));

        // Layer hidden on the full-page route; the window is still mounted (keep-alive).
        Assert.Contains("is-hidden", cut.Find(".window-layer").GetAttribute("class") ?? "");
        Assert.Single(cut.FindAll(".window-host"));

        // Single-click the taskbar chip → reveal + route to the window key.
        var chip = cut.Find("[data-testid='tb-chip']");
        chip.Click();

        // WM.Focus ran (this window is the active one)...
        Assert.True(_wm.Windows[0].IsActive);
        // ...and the layer is revealed again (URL now = the window key).
        Assert.DoesNotContain("is-hidden", cut.Find(".window-layer").GetAttribute("class") ?? "");
        Assert.Single(cut.FindAll("[data-testid='workspace-home']"));
        Assert.EndsWith(WindowRegistryKeys.QmsDashboard,
            Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    // (c-2) chip single-click on a NON-route window (spec:{id}) → reveal directly
    // (no Nav), the layer becomes visible without a matching registry key.
    [Fact]
    public void Chip_single_click_on_non_route_window_reveals_layer_directly()
    {
        NavTo("/qms/iqc");
        _wm.Open("spec:909", "Spec #909", null, typeof(QmsDashboardStub));

        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, RouteBody("iqc")));
        Assert.Contains("is-hidden", cut.Find(".window-layer").GetAttribute("class") ?? "");

        cut.Find("[data-testid='tb-chip']").Click();

        // Non-route key → reveal directly; URL stays on the full-page route.
        Assert.DoesNotContain("is-hidden", cut.Find(".window-layer").GetAttribute("class") ?? "");
        Assert.EndsWith("/qms/iqc",
            Services.GetRequiredService<FakeNavigationManager>().Uri);
    }

    // (d) keep-alive — a window opened while on a full-page route (layer hidden)
    // stays MOUNTED; navigating between full-page routes does not unmount it.
    [Fact]
    public void Window_stays_mounted_while_layer_is_hidden_on_full_page_route()
    {
        NavTo("/qms/iqc");
        _wm.Open(WindowRegistryKeys.QmsDashboard, "Dash", null, typeof(QmsDashboardStub));

        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, RouteBody("iqc")));

        // Hidden layer, but the host is in the DOM (display:none via CSS).
        Assert.Contains("is-hidden", cut.Find(".window-layer").GetAttribute("class") ?? "");
        Assert.Single(cut.FindAll(".window-host"));

        // Move to another full-page route → still hidden, still mounted (keep-alive).
        NavTo("/settings");
        cut.Render();

        Assert.Contains("is-hidden", cut.Find(".window-layer").GetAttribute("class") ?? "");
        Assert.Single(cut.FindAll(".window-host"));
        Assert.Single(_wm.Windows);
    }

    /// <summary>Inert body component so the taskbar chip renders without pulling
    /// a real page's DI graph into these visibility-focused tests.</summary>
    private sealed class QmsDashboardStub : ComponentBase { }
}
