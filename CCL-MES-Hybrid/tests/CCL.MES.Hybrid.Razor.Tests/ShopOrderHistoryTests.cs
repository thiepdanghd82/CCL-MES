using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Machines;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P10.8 — bUnit render tests for the Shop Order History page. Drives it
/// against a stubbed <see cref="ICclApiClient"/> so the KPI tiles, the
/// forensic table, and the period/search filters (which re-query the
/// server) are locked without booting MAUI or the API.
/// </summary>
public sealed class ShopOrderHistoryTests : TestContext
{
    private readonly RecordingApi _api;

    public ShopOrderHistoryTests()
    {
        _api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(_api);
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    private static ShopOrderHistoryDto History() => new()
    {
        TotalWos = 2,
        Output = 1500,
        Reject = 15,
        YieldPct = 99,
        Rows = new[]
        {
            new ShopOrderHistoryRow
            {
                WoId = 1, WoNo = "WO-26-9001", CustomerName = "Acme", ProductName = "Label A",
                MachineCode = "FBL01", MesPhase = "SHIPPED", TargetQty = 1000, QtyDone = 1000,
                QtyNg = 10, YieldPct = 99,
            },
            new ShopOrderHistoryRow
            {
                WoId = 2, WoNo = "WO-26-9002", CustomerName = "Beta", ProductName = "Label B",
                MachineCode = "ACNC3", MesPhase = "CANCELLED", TargetQty = 500, QtyDone = 500,
                QtyNg = 5, YieldPct = 99,
            },
        },
    };

    [Fact]
    public void Renders_kpis_and_rows()
    {
        _api.ShopOrderHistory = History();

        var cut = RenderComponent<ShopOrderHistory>();

        Assert.Equal(4, cut.FindAll(".md-kpi-num").Count);
        Assert.Equal(2, cut.FindAll(".md-table tbody tr").Count);
        var markup = cut.Markup;
        Assert.Contains("WO-26-9001", markup);
        Assert.Contains("Acme", markup);
        Assert.Contains("99%", markup);
        // First call on init carries no filters.
        Assert.Contains((null, (string?)null), _api.ShopOrderHistoryCalls);
    }

    [Fact]
    public void Period_chip_requeries_with_period()
    {
        _api.ShopOrderHistory = History();
        var cut = RenderComponent<ShopOrderHistory>();

        cut.FindAll(".md-chip").First(b => b.TextContent.Trim() == "7d").Click();

        Assert.Contains(_api.ShopOrderHistoryCalls, c => c.Period == "7d");
    }

    [Fact]
    public void Search_requeries_with_term()
    {
        _api.ShopOrderHistory = History();
        var cut = RenderComponent<ShopOrderHistory>();

        cut.Find(".md-search").Change("Acme");

        Assert.Contains(_api.ShopOrderHistoryCalls, c => c.Search == "Acme");
    }

    [Fact]
    public void Empty_history_shows_placeholder()
    {
        _api.ShopOrderHistory = new ShopOrderHistoryDto();

        var cut = RenderComponent<ShopOrderHistory>();

        Assert.Contains("No shop orders", cut.Markup);
        Assert.Empty(cut.FindAll(".md-table tbody tr"));
    }
}
