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
/// P10.8 — bUnit render tests for the Machine Dashboard (slice 1: plant
/// KPI strip + flat machine table). Drives the page against a stubbed
/// <see cref="ICclApiClient"/> so the status→pill mapping + KPI strip +
/// empty state are locked without booting MAUI or the API.
/// </summary>
public sealed class MachineDashboardTests : TestContext
{
    private readonly RecordingApi _api;

    public MachineDashboardTests()
    {
        _api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddI18n();
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    private static MachineDashboardDto Board() => new()
    {
        Total = 3,
        Running = 1,
        Setup = 1,
        Idle = 1,
        Machines = new[]
        {
            new MachineDashboardItem
            {
                WorkCenterId = 1, Code = "FBL01", Description = "Flexo 1", Area = "FLEXO",
                Status = "Running", ActiveWoNo = "WO-26-0001", ActiveMesPhase = "RUNNING",
                TargetQty = 1000, QtyDone = 250, QtyNg = 3,
            },
            new MachineDashboardItem
            {
                WorkCenterId = 2, Code = "ACNC3", Description = "CNC 3", Area = "CNC",
                Status = "Setup", ActiveWoNo = "WO-26-0002", ActiveMesPhase = "SETTING",
                TargetQty = 500,
            },
            new MachineDashboardItem
            {
                WorkCenterId = 3, Code = "AOI01", Description = "Inspect 1", Area = "INSPECTION",
                Status = "Idle",
            },
        },
    };

    [Fact]
    public void Renders_kpi_strip_from_board()
    {
        _api.MachineDashboard = Board();

        var cut = RenderComponent<MachineDashboard>();
        var nums = cut.FindAll(".md-kpi-num");

        Assert.Equal(6, nums.Count);          // Running / Idle / Setup / Down / Maintenance / Plant Quality
        var markup = cut.Markup;
        Assert.Contains("Chất lượng xưởng", markup);
        Assert.Contains("Running", markup);
        Assert.Equal(1, _api.MachineDashboardCalls);
    }

    [Fact]
    public void Renders_a_card_per_machine_with_status_pills()
    {
        _api.MachineDashboard = Board();

        var cut = RenderComponent<MachineDashboard>();

        Assert.Equal(3, cut.FindAll(".md-mc-card").Count);
        Assert.Single(cut.FindAll(".md-pill-running"));
        Assert.Single(cut.FindAll(".md-pill-setup"));
        Assert.Single(cut.FindAll(".md-pill-idle"));

        var markup = cut.Markup;
        Assert.Contains("FBL01", markup);
        Assert.Contains("WO-26-0001", markup);
        Assert.Contains("25%", markup);              // progress pct
        Assert.Contains("Hiệu suất 98.8%", markup);      // real quality metric
    }

    [Fact]
    public void Empty_board_shows_placeholder()
    {
        _api.MachineDashboard = new MachineDashboardDto();

        var cut = RenderComponent<MachineDashboard>();

        Assert.Contains("Không có trung tâm sản xuất.", cut.Markup);
        Assert.Empty(cut.FindAll(".md-mc-card"));
    }

    [Fact]
    public void Groups_machines_by_area()
    {
        _api.MachineDashboard = Board();

        var cut = RenderComponent<MachineDashboard>();
        var sections = cut.FindAll(".md-area");

        Assert.Equal(3, sections.Count);   // FLEXO / CNC / INSPECTION
        var markup = cut.Markup;
        Assert.Contains("FLEXO", markup);
        Assert.Contains("CNC", markup);
        Assert.Contains("INSPECTION", markup);
    }

    [Fact]
    public void Status_chip_filters_rows()
    {
        _api.MachineDashboard = Board();
        var cut = RenderComponent<MachineDashboard>();

        // Click the "Running" status chip (label is "▶ Running").
        cut.FindAll(".md-chip").First(b => b.TextContent.Contains("Running")).Click();

        // Cards reflect the filter (the right-hand activity sidebar is
        // intentionally plant-wide, so assert on cards, not whole markup).
        var cards = cut.FindAll(".md-mc-card");
        Assert.Single(cards);                       // only the Running machine
        Assert.Contains("FBL01", cards[0].TextContent);
        Assert.DoesNotContain(cards, c => c.TextContent.Contains("ACNC3"));
    }

    [Fact]
    public void Area_chip_filters_to_one_area()
    {
        _api.MachineDashboard = Board();
        var cut = RenderComponent<MachineDashboard>();

        cut.FindAll(".md-chip").First(b => b.TextContent.Trim() == "CNC").Click();

        Assert.Single(cut.FindAll(".md-area"));
        var cards = cut.FindAll(".md-mc-card");
        Assert.Single(cards);
        Assert.Contains("ACNC3", cards[0].TextContent);
        Assert.DoesNotContain(cards, c => c.TextContent.Contains("FBL01"));
    }

    [Fact]
    public void Search_filters_by_code()
    {
        _api.MachineDashboard = Board();
        var cut = RenderComponent<MachineDashboard>();

        cut.Find(".md-search").Input("FBL");

        var cards = cut.FindAll(".md-mc-card");
        Assert.Single(cards);
        Assert.Contains("FBL01", cut.Markup);
    }

    [Fact]
    public void Filter_with_no_match_shows_placeholder()
    {
        _api.MachineDashboard = Board();
        var cut = RenderComponent<MachineDashboard>();

        cut.Find(".md-search").Input("zzz-nope");

        Assert.Contains("Không có máy nào khớp bộ lọc.", cut.Markup);
        Assert.Empty(cut.FindAll(".md-mc-card"));
    }

    [Fact]
    public void Clicking_a_row_opens_the_detail_drawer()
    {
        _api.MachineDashboard = Board();
        _api.MachineDetail = new MachineDetailDto
        {
            WorkCenterId = 1, Code = "FBL01", Description = "Flexo 1", Area = "FLEXO",
            Status = "Running",
            ActiveWo = new MachineWoRow { WoNo = "WO-26-0001", MesPhase = "RUNNING", TargetQty = 1000, QtyDone = 250 },
            TodayWoCount = 2, TodayGood = 800, TodayNg = 6,
            RecentWos = new[]
            {
                new MachineWoRow { WoNo = "WO-26-0001", MesPhase = "RUNNING", TargetQty = 1000, QtyDone = 250 },
            },
        };
        var cut = RenderComponent<MachineDashboard>();

        Assert.Empty(cut.FindAll(".md-drawer"));     // closed initially

        cut.FindAll(".md-row").First().Click();

        Assert.Single(cut.FindAll(".md-drawer"));
        Assert.NotEmpty(_api.MachineDetailCalls);   // detail fetched for the clicked row
        var markup = cut.Markup;
        Assert.Contains("Lệnh SX đang chạy", markup);
        Assert.Contains("WO-26-0001", markup);
        Assert.Contains("800", markup);              // today good
    }

    [Fact]
    public void Closing_the_drawer_hides_it()
    {
        _api.MachineDashboard = Board();
        _api.MachineDetail = new MachineDetailDto { WorkCenterId = 1, Code = "FBL01", Status = "Running" };
        var cut = RenderComponent<MachineDashboard>();

        cut.FindAll(".md-row").First().Click();
        Assert.Single(cut.FindAll(".md-drawer"));

        cut.Find(".md-drawer-close").Click();
        Assert.Empty(cut.FindAll(".md-drawer"));
    }
}
