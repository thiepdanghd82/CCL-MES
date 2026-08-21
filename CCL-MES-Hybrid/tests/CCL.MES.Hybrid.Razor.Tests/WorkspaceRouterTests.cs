using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Connectivity;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Hybrid.Client.RecentScans;
using CCL.MES.Hybrid.Client.Windows;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P2-PR2 — @Body empty-workspace swap + WorkspaceRouter deep-link. When the URL
/// resolves to a registered window key the real MainLayout must (1) open that
/// window and (2) render the WorkspaceHome background in place of @Body — the
/// routed page renders ONCE inside its window, never a second time in @Body
/// (no double-render). A non-window URL keeps @Body untouched.
/// </summary>
public sealed class WorkspaceRouterTests : TestContext
{
    private readonly WindowManager _wm = new();

    public WorkspaceRouterTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddI18n();
        Services.AddSingleton<IWindowManager>(_wm);
        Services.AddSingleton<IWindowRegistry, WindowRegistry>();
        Services.AddSingleton<IFloatingWindowStore, InMemoryFloatingWindowStore>();
        Services.AddSingleton<IAuthSession>(new StubAuthSession());
        Services.AddSingleton<IConnectivityMonitor, AlwaysOnlineConnectivityMonitor>();
        Services.AddSingleton<IRecentScansService, InMemoryRecentScansService>();
        // The PR2 window pages (e.g. QmsDashboard) inject ICclApiClient — the
        // recording fake defaults to a non-null queue so the page renders clean.
        Services.AddSingleton<ICclApiClient>(new RecordingApi());
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

    [Fact]
    public void Deep_link_to_a_window_route_opens_the_window_and_shows_workspace_home()
    {
        // Deep-link straight to a registered window key (any-auth QMS Dashboard).
        NavTo("/qms/dashboard");

        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, RouteBody("A")));

        // WorkspaceRouter opened the window for the URL key...
        Assert.Single(_wm.Windows);
        Assert.Equal(WindowRegistryKeys.QmsDashboard, _wm.Windows[0].Key);

        // ...and @Body is the empty-workspace background, NOT the routed page —
        // so the page renders exactly once (inside the window), no double-render.
        Assert.Single(cut.FindAll("[data-testid='workspace-home']"));
        Assert.Empty(cut.FindAll(".route-body"));
        Assert.Single(cut.FindAll(".window-host"));
    }

    [Fact]
    public void Non_window_route_keeps_body_and_no_workspace_home()
    {
        NavTo("/_not_a_window_");

        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, RouteBody("A")));

        // No registry hit → no window, @Body renders the route surface as before.
        Assert.Empty(_wm.Windows);
        Assert.Empty(cut.FindAll("[data-testid='workspace-home']"));
        Assert.Single(cut.FindAll(".route-body"));
    }

    [Fact]
    public void Root_route_is_non_window_home_dashboard_renders_full_page_in_body()
    {
        // FIX 1 — "/" is NOT a registered window key: it is the default full-page
        // landing. So opening the app / tapping Home must keep @Body (the Home
        // dashboard) and NOT open a window or show the empty WorkspaceHome.
        NavTo("/");

        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, RouteBody("home")));

        Assert.Empty(_wm.Windows);
        Assert.Empty(cut.FindAll("[data-testid='workspace-home']"));
        Assert.Single(cut.FindAll(".route-body"));
    }

    [Fact]
    public void Navigating_from_window_route_to_non_window_route_restores_body()
    {
        NavTo("/qms/dashboard");
        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, RouteBody("A")));
        Assert.Single(cut.FindAll("[data-testid='workspace-home']"));

        // Navigate to a deferred (non-window) route: @Body comes back, the window
        // stays open (session-only) but the shell surface is the route again.
        NavTo("/settings");
        cut.Render();

        Assert.Empty(cut.FindAll("[data-testid='workspace-home']"));
        Assert.Single(cut.FindAll(".route-body"));
        Assert.Single(_wm.Windows);   // window persists across navigation
    }

    [Fact]
    public void Deep_link_reload_dedupes_does_not_stack_duplicate_windows()
    {
        NavTo("/qms/dashboard");
        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, RouteBody("A")));
        Assert.Single(_wm.Windows);

        // A re-navigation to the SAME key (reload / re-deep-link) focuses the
        // existing window rather than opening a duplicate.
        NavTo("/qms/dashboard");
        cut.Render();

        Assert.Single(_wm.Windows);
    }
}
