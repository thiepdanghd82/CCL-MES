using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Connectivity;
using CCL.MES.Hybrid.Client.Hardware;
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
/// P2-PR1 keep-alive — the load-bearing proof. Renders the REAL
/// <see cref="MainLayout"/> host with an <see cref="IWindowManager"/> holding
/// two windows whose body is a stateful counter. Incrementing the counter then
/// swapping the layout's @Body (simulating a route change) must NOT reset the
/// counter: @key = window id + stable OPEN order keeps the component instance
/// alive across the re-render. Also proves the cascaded WindowContext.IsActive
/// flows down (false when minimized) so a polling body can pause.
/// </summary>
public sealed class WindowLayerKeepAliveTests : TestContext
{
    private readonly WindowManager _wm = new();

    public WindowLayerKeepAliveTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddI18n();
        Services.AddSingleton<IWindowManager>(_wm);
        Services.AddSingleton<IWindowRegistry, WindowRegistry>();
        Services.AddSingleton<IFloatingWindowStore, InMemoryFloatingWindowStore>();
        Services.AddSingleton<IAuthSession>(new StubAuthSession());
        Services.AddSingleton<IConnectivityMonitor, AlwaysOnlineConnectivityMonitor>();
        Services.AddSingleton<IRecentScansService, InMemoryRecentScansService>();
        Services.AddSingleton<IOptions<HardwareOptions>>(
            Options.Create(new HardwareOptions { ScanEnabled = true }));
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    private static RenderFragment Body(string marker) => builder =>
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "class", "route-body");
        builder.AddAttribute(2, "data-marker", marker);
        builder.CloseElement();
    };

    [Fact]
    public void Window_body_state_survives_a_route_body_swap()
    {
        // Two windows before the host renders → the layer @foreach picks them up.
        _wm.Open("k1", "One", null, typeof(CounterProbe));
        _wm.Open("k2", "Two", null, typeof(CounterProbe));

        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, Body("route-A")));

        // Two hosted windows, each with its own counter probe (starts at 0).
        Assert.Equal(2, cut.FindAll(".window-host").Count);
        var probes = cut.FindComponents<CounterProbe>();
        Assert.Equal(2, probes.Count);

        // Bump the first window's counter twice.
        var firstInc = cut.FindAll(".cp-inc")[0];
        firstInc.Click();
        firstInc.Click();
        Assert.Equal(2, cut.FindComponents<CounterProbe>()[0].Instance.Count);

        // "Navigate" — swap the layout body. The window instances must survive.
        cut.SetParametersAndRender(p => p.Add(x => x.Body, Body("route-B")));
        Assert.Equal("route-B", cut.Find(".route-body").GetAttribute("data-marker"));

        // SAME instance + state intact (keep-alive): still 2, not reset to 0.
        var after = cut.FindComponents<CounterProbe>();
        Assert.Equal(2, after.Count);
        Assert.Equal(2, after[0].Instance.Count);
        Assert.Same(probes[0].Instance, after[0].Instance);
    }

    [Fact]
    public void Minimized_window_stays_mounted_but_hidden_and_cascades_inactive()
    {
        var w = _wm.Open("k1", "One", null, typeof(CounterProbe))!;
        _wm.Open("k2", "Two", null, typeof(CounterProbe));

        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, Body("A")));

        // Active window cascades IsActive=true down to its body.
        Assert.Contains("True", cut.FindComponents<CounterProbe>()[0].Markup);

        _wm.Minimize(w.Id);
        cut.Render();

        // Still mounted (keep-alive) but the host is display:none via .is-min...
        var host = cut.FindAll(".window-host")[0];
        Assert.Contains("is-min", host.GetAttribute("class")!);
        Assert.Equal(2, cut.FindComponents<CounterProbe>().Count);
        // ...and the cascade flipped to inactive so a polling body can pause.
        Assert.Contains("False", cut.FindComponents<CounterProbe>()[0].Markup);
    }

    [Fact]
    public void Empty_workspace_renders_no_window_hosts_and_no_taskbar()
    {
        var cut = RenderComponent<MainLayout>(p => p.Add(x => x.Body, Body("A")));
        Assert.Empty(cut.FindAll(".window-host"));
        Assert.Empty(cut.FindAll(".taskbar"));
        Assert.Single(cut.FindAll(".route-body"));   // route surface untouched
    }
}
