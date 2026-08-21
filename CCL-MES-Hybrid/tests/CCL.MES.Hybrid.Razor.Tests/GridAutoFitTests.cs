using System.Linq;
using Bunit;
using CCL.MES.Hybrid.Razor.Shared;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P2 — multi-window grid fit. GridAutoFit must render an inert anchor whose id
/// is the SAME id it hands to cclMesGrid.register, so the JS side can scope the
/// .grid-scroll lookup to the floating window ('.trace-win') the grid lives in.
/// Without this, every registered grid measured whichever .grid-scroll was first
/// in the document → all windows fit to the first window's height. bUnit can't
/// run the JS DOM scoping, so these lock the CONTRACT the JS relies on (anchor
/// id === register id, selector passed through).
/// </summary>
public sealed class GridAutoFitTests : TestContext
{
    public GridAutoFitTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void Renders_an_inert_anchor_span_carrying_an_id()
    {
        var cut = RenderComponent<GridAutoFit>();

        var anchor = cut.Find("span.gaf-anchor");
        Assert.False(string.IsNullOrWhiteSpace(anchor.Id));
        Assert.True(anchor.HasAttribute("hidden"));
        // Startup RowPx default present; anchor renders nothing visible.
        Assert.StartsWith("gaf-", anchor.Id);
    }

    [Fact]
    public void Anchor_id_matches_the_id_passed_to_register()
    {
        var cut = RenderComponent<GridAutoFit>();

        var anchor = cut.Find("span.gaf-anchor");
        var reg = JSInterop.Invocations.Single(i => i.Identifier == "cclMesGrid.register");
        // register(id, ref, selector, rowPx) — first arg is the id the JS uses
        // to getElementById(anchor) and closest('.trace-win').
        Assert.Equal(anchor.Id, reg.Arguments[0]);
    }

    [Fact]
    public void Passes_the_scroll_selector_through_to_register()
    {
        var cut = RenderComponent<GridAutoFit>(p => p.Add(x => x.ScrollSelector, ".iqc-scroll"));

        var reg = JSInterop.Invocations.Single(i => i.Identifier == "cclMesGrid.register");
        Assert.Equal(".iqc-scroll", reg.Arguments[2]);
    }

    [Fact]
    public void Default_scroll_selector_is_grid_scroll()
    {
        RenderComponent<GridAutoFit>();

        var reg = JSInterop.Invocations.Single(i => i.Identifier == "cclMesGrid.register");
        Assert.Equal(".grid-scroll", reg.Arguments[2]);
    }

    [Fact]
    public void Dispose_unregisters_with_the_same_id()
    {
        var cut = RenderComponent<GridAutoFit>();
        var anchor = cut.Find("span.gaf-anchor");
        var registeredId = anchor.Id;

        DisposeComponents();

        var unreg = JSInterop.Invocations.Single(i => i.Identifier == "cclMesGrid.unregister");
        Assert.Equal(registeredId, unreg.Arguments[0]);
    }
}
