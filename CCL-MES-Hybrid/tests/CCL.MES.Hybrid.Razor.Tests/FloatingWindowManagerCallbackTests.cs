using System.Linq;
using Bunit;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Razor.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P2-PR1 — the additive WindowManager callbacks on <see cref="FloatingWindow"/>
/// must not regress the legacy standalone chrome. When OnMinimizeRequested /
/// OnMaximizeRequested / OnFocusRequested are wired, the traffic-lights bubble
/// the intent so the manager owns WindowState; when unset, the component behaves
/// exactly as before (no exceptions, chrome intact).
/// </summary>
public sealed class FloatingWindowManagerCallbackTests : TestContext
{
    public FloatingWindowManagerCallbackTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddI18n();
    }

    [Fact]
    public void Minimize_bubbles_to_callback_when_wired()
    {
        var minimized = false;
        var cut = RenderComponent<FloatingWindow>(p => p
            .Add(x => x.Title, "WO-9")
            .Add(x => x.OnMinimizeRequested, () => minimized = true)
            .AddChildContent("<span/>"));

        cut.Find(".fw-light-min").Click();
        Assert.True(minimized);
    }

    [Fact]
    public void Maximize_bubbles_to_callback_when_wired()
    {
        var maximized = false;
        var cut = RenderComponent<FloatingWindow>(p => p
            .Add(x => x.Title, "WO-9")
            .Add(x => x.OnMaximizeRequested, () => maximized = true)
            .AddChildContent("<span/>"));

        cut.Find(".fw-light-max").Click();
        Assert.True(maximized);
    }

    [Fact]
    public void Focus_bubbles_on_pointerdown_when_wired()
    {
        var focused = false;
        var cut = RenderComponent<FloatingWindow>(p => p
            .Add(x => x.Title, "WO-9")
            .Add(x => x.OnFocusRequested, () => focused = true)
            .AddChildContent("<span/>"));

        cut.Find(".trace-win").PointerDown();
        Assert.True(focused);
    }

    [Fact]
    public void Activating_brings_the_fixed_window_to_front()
    {
        // P2 fix — taskbar chip click marks the window active via IsActive but
        // does NOT fire its pointerdown; the real z-index of the position:fixed
        // .trace-win must still be raised (host wrapper z-index can't restack it).
        var cut = RenderComponent<FloatingWindow>(p => p
            .Add(x => x.Title, "WO-9")
            .Add(x => x.IsActive, false)
            .AddChildContent("<span/>"));

        Assert.Equal(0, JSInterop.Invocations.Count(i => i.Identifier == "cclMesFloat.bringToFront"));

        cut.SetParametersAndRender(p => p.Add(x => x.IsActive, true));   // host focus
        JSInterop.VerifyInvoke("cclMesFloat.bringToFront");
    }

    [Fact]
    public void A_window_that_mounts_active_is_brought_to_front()
    {
        RenderComponent<FloatingWindow>(p => p
            .Add(x => x.Title, "WO-9")
            .Add(x => x.IsActive, true)
            .AddChildContent("<span/>"));

        JSInterop.VerifyInvoke("cclMesFloat.bringToFront");
    }

    [Fact]
    public void Legacy_chrome_intact_without_callbacks()
    {
        // No callbacks wired — full chrome renders, traffic-lights safe to click.
        var cut = RenderComponent<FloatingWindow>(p => p
            .Add(x => x.Title, "WO-9")
            .AddChildContent("<span class=\"probe\"/>"));

        Assert.Single(cut.FindAll(".trace-win"));
        Assert.Equal(8, cut.FindAll(".fw-handle").Count);
        cut.Find(".fw-light-min").Click();   // no throw (JS loose)
        cut.Find(".fw-light-max").Click();
        cut.Find(".trace-win").PointerDown(); // no-op, no throw
        Assert.Single(cut.FindAll(".probe"));
    }
}
