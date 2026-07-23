using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P11.5-3 — bUnit render tests cho <see cref="SemiStockDashboard"/> (kho
/// bán thành phẩm). Rule 4: plain button/select/input. Wire-mirror:
/// SemiStockControllerTests khớp từng probe server-side.
/// </summary>
public sealed class SemiStockDashboardTests : TestContext
{
    private readonly RecordingApi _api = new();

    public SemiStockDashboardTests()
    {
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        this.AddTestAuthorization().SetAuthorized("op");
    }

    private static SemiLotRow Lot(long id, string lotNo, int avail, int reserved = 0,
        string status = "AVAILABLE", bool expiring = false) => new()
    {
        Id = id, LotNo = lotNo, SemiKind = "PRINTED_SEMI", SourceWorkOrderId = 1,
        QtyProduced = avail + reserved, QtyAvailable = avail, QtyReserved = reserved,
        Status = status, Expiring = expiring,
    };

    private static SemiStockView View(params SemiLotRow[] lots) => new()
    {
        Lots = lots.ToList(),
        TotalAvailable = lots.Sum(l => l.QtyAvailable),
        TotalReserved = lots.Sum(l => l.QtyReserved),
    };

    [Fact]
    public void Renders_lots_and_totals()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(Lot(1, "SEMI-A", 400), Lot(2, "SEMI-B", 400, 100)));
        var cut = RenderComponent<SemiStockDashboard>();

        Assert.Equal(2, cut.FindAll("[data-testid^='semi-row-']").Count);
        var totals = cut.Find("[data-testid='semi-totals']").TextContent;
        Assert.Contains("800", totals);   // available
        Assert.Contains("100", totals);   // reserved
    }

    [Fact]
    public void Expiring_lot_raises_fefo_banner_and_tag()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(Lot(1, "SEMI-EARLY", 400, expiring: true)));
        var cut = RenderComponent<SemiStockDashboard>();

        Assert.NotNull(cut.Find("[data-testid='semi-expiry-banner']"));
        Assert.NotNull(cut.Find("[data-testid='semi-expiring-1']"));
    }

    [Fact]
    public void Empty_stock_shows_empty_state()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View());
        var cut = RenderComponent<SemiStockDashboard>();
        Assert.NotNull(cut.Find("[data-testid='semi-empty']"));
    }

    [Fact]
    public void Filter_kind_change_reloads_with_kind_param()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(Lot(1, "SEMI-A", 400)));
        var cut = RenderComponent<SemiStockDashboard>();

        cut.Find("[data-testid='semi-filter-kind']").Change("TAPE_SEMI");

        // 1st call = initial (null kind), 2nd = after filter.
        Assert.Equal(2, _api.SemiLotsCalls.Count);
        Assert.Equal("TAPE_SEMI", _api.SemiLotsCalls[^1].Kind);
    }

    [Fact]
    public void Post_lot_submits_with_entered_fields_then_reloads()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(Lot(1, "SEMI-A", 400)));
        PostSemiLotRequest? sent = null;
        _api.PostSemiLotImpl = (req, _) => { sent = req; return Task.FromResult(new SemiSetResponse { Ok = true, SemiLotId = 9 }); };
        var cut = RenderComponent<SemiStockDashboard>();

        cut.Find("[data-testid='semi-add-toggle']").Click();
        cut.Find("[data-testid='semi-add-lotno']").Input("SEMI-NEW-1");
        cut.Find("[data-testid='semi-add-qty']").Change("250");
        cut.Find("[data-testid='semi-add-submit']").Click();

        Assert.NotNull(sent);
        Assert.Equal("SEMI-NEW-1", sent!.LotNo);
        Assert.Equal(250, sent.Qty);
        Assert.Equal("PRINTED_SEMI", sent.SemiKind);
        // reloaded after post (initial + reload).
        Assert.Equal(2, _api.SemiLotsCalls.Count);
    }

    [Fact]
    public void Post_lot_duplicate_shows_localised_banner()
    {
        _api.SemiLotsImpl = (_, _, _, _) => Task.FromResult(View(Lot(1, "SEMI-A", 400)));
        _api.PostSemiLotImpl = (_, _) => Task.FromResult(new SemiSetResponse { Ok = false, ErrorCode = "semi.lot_exists" });
        var cut = RenderComponent<SemiStockDashboard>();

        cut.Find("[data-testid='semi-add-toggle']").Click();
        cut.Find("[data-testid='semi-add-lotno']").Input("SEMI-A");
        cut.Find("[data-testid='semi-add-qty']").Change("10");
        cut.Find("[data-testid='semi-add-submit']").Click();

        Assert.Contains("đã tồn tại", cut.Find("[data-testid='semi-banner']").TextContent);
    }

    [Fact]
    public void Initial_load_error_shows_banner()
    {
        _api.SemiLotsImpl = (_, _, _, _) => throw new InvalidOperationException("boom");
        var cut = RenderComponent<SemiStockDashboard>();
        Assert.NotNull(cut.Find("[data-testid='semi-initial-error']"));
    }
}
