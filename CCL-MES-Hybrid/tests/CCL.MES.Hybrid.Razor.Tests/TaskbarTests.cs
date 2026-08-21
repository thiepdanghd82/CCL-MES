using Bunit;
using CCL.MES.Hybrid.Client.Windows;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P2-PR1 taskbar chips. N windows → N chips (stable open order). Click = focus/
/// restore · double-click = maximize↔restore · middle-click / × = close. Chip
/// carries is-min when minimized + is-active for the focused window.
/// </summary>
public sealed class TaskbarTests : TestContext
{
    private readonly WindowManager _wm = new();

    public TaskbarTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddI18n();
        Services.AddSingleton<IWindowManager>(_wm);
    }

    private IRenderedComponent<Taskbar> Render() => RenderComponent<Taskbar>();

    [Fact]
    public void N_windows_render_N_chips_in_open_order()
    {
        _wm.Open("k1", "Alpha", null, typeof(CounterProbe));
        _wm.Open("k2", "Bravo", null, typeof(CounterProbe));
        _wm.Open("k3", "Charlie", null, typeof(CounterProbe));

        var cut = Render();
        var chips = cut.FindAll(".tb-chip");
        Assert.Equal(3, chips.Count);
        Assert.Equal("Alpha", chips[0].QuerySelector(".tb-chip-title")!.TextContent);
        Assert.Equal("Bravo", chips[1].QuerySelector(".tb-chip-title")!.TextContent);
        Assert.Equal("Charlie", chips[2].QuerySelector(".tb-chip-title")!.TextContent);
    }

    [Fact]
    public void No_windows_renders_no_taskbar()
    {
        var cut = Render();
        Assert.Empty(cut.FindAll(".taskbar"));
    }

    [Fact]
    public void Click_focuses_and_marks_active()
    {
        _wm.Open("k1", "Alpha", null, typeof(CounterProbe));
        _wm.Open("k2", "Bravo", null, typeof(CounterProbe)); // Bravo active now

        var cut = Render();
        Assert.Contains("is-active", cut.FindAll(".tb-chip")[1].GetAttribute("class")!);

        cut.FindAll(".tb-chip")[0].Click();   // focus Alpha

        Assert.Contains("is-active", cut.FindAll(".tb-chip")[0].GetAttribute("class")!);
        Assert.DoesNotContain("is-active", cut.FindAll(".tb-chip")[1].GetAttribute("class")!);
        Assert.Equal("k1", _wm.Active!.Key);
    }

    [Fact]
    public void Double_click_toggles_maximize_then_restore()
    {
        var w = _wm.Open("k1", "Alpha", null, typeof(CounterProbe))!;
        var cut = Render();

        cut.FindAll(".tb-chip")[0].DoubleClick();
        Assert.Equal(WindowState.Maximized, w.State);

        cut.FindAll(".tb-chip")[0].DoubleClick();
        Assert.Equal(WindowState.Normal, w.State);
    }

    [Fact]
    public void Close_glyph_closes_the_window()
    {
        _wm.Open("k1", "Alpha", null, typeof(CounterProbe));
        _wm.Open("k2", "Bravo", null, typeof(CounterProbe));

        var cut = Render();
        cut.FindAll(".tb-chip-x")[0].Click();

        Assert.Single(_wm.Windows);
        Assert.Equal("k2", _wm.Windows[0].Key);
        Assert.Single(cut.FindAll(".tb-chip"));
    }

    [Fact]
    public void Middle_click_closes_the_window()
    {
        _wm.Open("k1", "Alpha", null, typeof(CounterProbe));
        var cut = Render();

        cut.FindAll(".tb-chip")[0].MouseUp(new MouseEventArgs { Button = 1 });

        Assert.Empty(_wm.Windows);
    }

    [Fact]
    public void Minimized_chip_carries_is_min()
    {
        var w = _wm.Open("k1", "Alpha", null, typeof(CounterProbe))!;
        var cut = Render();

        _wm.Minimize(w.Id);

        Assert.Contains("is-min", cut.FindAll(".tb-chip")[0].GetAttribute("class")!);
    }
}
