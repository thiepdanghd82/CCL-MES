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

        Assert.Equal(4, nums.Count);          // Total / Running / Setup / Idle
        var markup = cut.Markup;
        Assert.Contains("Tổng máy", markup);
        Assert.Contains("Đang chạy", markup);
        Assert.Equal(1, _api.MachineDashboardCalls);
    }

    [Fact]
    public void Renders_a_row_per_machine_with_status_pills()
    {
        _api.MachineDashboard = Board();

        var cut = RenderComponent<MachineDashboard>();

        Assert.Equal(3, cut.FindAll(".md-table tbody tr").Count);
        Assert.Single(cut.FindAll(".md-pill-running"));
        Assert.Single(cut.FindAll(".md-pill-setup"));
        Assert.Single(cut.FindAll(".md-pill-idle"));

        var markup = cut.Markup;
        Assert.Contains("FBL01", markup);
        Assert.Contains("WO-26-0001", markup);
        Assert.Contains("250/1000 (25%)", markup);   // progress formatting
    }

    [Fact]
    public void Empty_board_shows_placeholder()
    {
        _api.MachineDashboard = new MachineDashboardDto();

        var cut = RenderComponent<MachineDashboard>();

        Assert.Contains("Chưa có trung tâm sản xuất", cut.Markup);
        Assert.Empty(cut.FindAll(".md-table tbody tr"));
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

        // Click the "Đang chạy" status chip.
        cut.FindAll(".md-chip").First(b => b.TextContent.Trim() == "Đang chạy").Click();

        var rows = cut.FindAll(".md-table tbody tr");
        Assert.Single(rows);                       // only the Running machine
        Assert.Contains("FBL01", cut.Markup);
        Assert.DoesNotContain("ACNC3", cut.Markup); // Setup machine filtered out
    }

    [Fact]
    public void Area_chip_filters_to_one_area()
    {
        _api.MachineDashboard = Board();
        var cut = RenderComponent<MachineDashboard>();

        cut.FindAll(".md-chip").First(b => b.TextContent.Trim() == "CNC").Click();

        Assert.Single(cut.FindAll(".md-area"));
        Assert.Single(cut.FindAll(".md-table tbody tr"));
        Assert.Contains("ACNC3", cut.Markup);
        Assert.DoesNotContain("FBL01", cut.Markup);
    }

    [Fact]
    public void Search_filters_by_code()
    {
        _api.MachineDashboard = Board();
        var cut = RenderComponent<MachineDashboard>();

        cut.Find(".md-search").Input("FBL");

        var rows = cut.FindAll(".md-table tbody tr");
        Assert.Single(rows);
        Assert.Contains("FBL01", cut.Markup);
    }

    [Fact]
    public void Filter_with_no_match_shows_placeholder()
    {
        _api.MachineDashboard = Board();
        var cut = RenderComponent<MachineDashboard>();

        cut.Find(".md-search").Input("zzz-nope");

        Assert.Contains("Không có máy nào khớp", cut.Markup);
        Assert.Empty(cut.FindAll(".md-table tbody tr"));
    }
}
