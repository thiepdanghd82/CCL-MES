using Bunit;
using CCL.MES.Hybrid.Client.Localization;
using CCL.MES.Hybrid.Client.Windows;
using CCL.MES.Hybrid.Razor.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// The reusable <see cref="FloatingWindow"/> chrome + <c>Modal</c> float mode.
/// Chrome parity for the Traceability showcard is covered by
/// <see cref="QualityTraceabilityTests"/>; here we lock the component contract
/// so any new showcard that wraps it inherits drag/resize/traffic-lights.
/// </summary>
public sealed class FloatingWindowTests : TestContext
{
    public FloatingWindowTests()
    {
        // cclMesFloat.* interop has no engine in the test host.
        JSInterop.Mode = JSRuntimeMode.Loose;
        // i18n Phase-2: FloatingWindow tooltips route through ITranslator.
        Services.AddSingleton<ILanguageService, InMemoryLanguageService>();
        Services.AddSingleton<ITranslationCatalog, TranslationCatalog>();
        Services.AddSingleton<ITranslator, Translator>();
    }

    private IRenderedComponent<FloatingWindow> RenderWin(bool closed = false, System.Action? onClose = null)
        => RenderComponent<FloatingWindow>(p => p
            .Add(x => x.Title, "WO-9")
            .Add(x => x.AriaLabel, "Traceability WO-9")
            .Add(x => x.OnClose, () => onClose?.Invoke())
            .AddChildContent("<div class=\"probe\">body</div>"));

    [Fact]
    public void Renders_full_window_chrome()
    {
        var cut = RenderWin();
        Assert.Single(cut.FindAll(".trace-win"));
        Assert.Equal("dialog", cut.Find(".trace-win").GetAttribute("role"));
        Assert.Equal("Traceability WO-9", cut.Find(".trace-win").GetAttribute("aria-label"));
        Assert.Equal(8, cut.FindAll(".fw-handle").Count);                 // 8-way resize
        Assert.Single(cut.FindAll(".fw-light-min"));
        Assert.Single(cut.FindAll(".fw-light-max"));
        Assert.Single(cut.FindAll(".fw-light-close"));
        Assert.Equal("WO-9", cut.Find(".trace-win-title").TextContent.Trim());
        Assert.Single(cut.FindAll(".probe"));                            // ChildContent body
    }

    [Fact]
    public void Close_button_raises_OnClose()
    {
        var closed = false;
        var cut = RenderWin(onClose: () => closed = true);
        cut.Find(".fw-light-close").Click();
        Assert.True(closed);
    }

    [Fact]
    public void Rect_callback_reports_rect_and_toggles_maximize_icon()
    {
        WindowRect? got = null;
        var cut = RenderComponent<FloatingWindow>(p => p
            .Add(x => x.Title, "WO-9")
            .Add(x => x.OnRectChanged, r => got = r)
            .AddChildContent("<span/>"));

        // Not maximized initially → maximize glyph shown (no is-max class).
        Assert.DoesNotContain("is-max", cut.Find(".fw-light-max").GetAttribute("class"));

        var rect = new WindowRect { X = 10, Y = 20, W = 800, H = 500, Maximized = true };
        cut.InvokeAsync(() => cut.Instance.OnRectChanged_JS(rect)).Wait();

        Assert.Equal(rect, got);                                          // forwarded to parent
        Assert.Contains("is-max", cut.Find(".fw-light-max").GetAttribute("class"));   // restore icon
    }

    [Fact]
    public void Minimize_and_maximize_toggles_can_be_hidden()
    {
        var cut = RenderComponent<FloatingWindow>(p => p
            .Add(x => x.Title, "x")
            .Add(x => x.ShowMinimize, false)
            .Add(x => x.ShowMaximize, false)
            .AddChildContent("<span/>"));
        Assert.Empty(cut.FindAll(".fw-light-min"));
        Assert.Empty(cut.FindAll(".fw-light-max"));
        Assert.Single(cut.FindAll(".fw-light-close"));                   // close always present
    }

    // ── Modal float opt-in ──
    [Fact]
    public void Modal_default_is_a_centred_scrim_not_a_floating_window()
    {
        var cut = RenderComponent<Modal>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<div class=\"mbody\"/>"))));
        Assert.Single(cut.FindAll(".modal-scrim"));
        Assert.Empty(cut.FindAll(".trace-win"));
    }

    [Fact]
    public void Modal_float_mode_renders_the_floating_window_chrome()
    {
        var cut = RenderComponent<Modal>(p => p
            .Add(x => x.Open, true)
            .Add(x => x.Float, true)
            .Add(x => x.AriaLabel, "Showcard")
            .Add(x => x.Header, (RenderFragment)(b => b.AddMarkupContent(0, "Title")))
            .Add(x => x.Body, (RenderFragment)(b => b.AddMarkupContent(0, "<div class=\"mbody\"/>"))));
        Assert.Empty(cut.FindAll(".modal-scrim"));       // no centred scrim
        Assert.Single(cut.FindAll(".trace-win"));        // FloatingWindow chrome
        Assert.Equal(8, cut.FindAll(".fw-handle").Count);
        Assert.Single(cut.FindAll(".mbody"));            // body preserved
    }
}
