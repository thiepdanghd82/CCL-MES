using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Qms;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P10.9 — bUnit render tests for the QC History page: pass/reject KPI +
/// forensic table + kind/judgment/search filters (which re-query).
/// </summary>
public sealed class QcHistoryTests : TestContext
{
    private readonly RecordingApi _api;

    public QcHistoryTests()
    {
        _api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddI18n();
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    private static QcHistoryDto History() => new()
    {
        Total = 2, Pass = 1, Reject = 1, PassRatePct = 50,
        Rows = new[]
        {
            new QcHistoryRow { CheckId = 1, WoNo = "WO-26-7001", QcKind = "FQC", Judgment = "Pass", InspectedBy = "qc1" },
            new QcHistoryRow { CheckId = 2, WoNo = "WO-26-7002", QcKind = "OQC", Judgment = "Reject", InspectedBy = "qc1", ApprovedBy = "qa1" },
        },
    };

    [Fact]
    public void Renders_kpis_and_rows()
    {
        _api.QcHistory = History();

        var cut = RenderComponent<QcHistory>();

        Assert.Equal(5, cut.FindAll(".md-kpi-num").Count);
        Assert.Equal(2, cut.FindAll(".md-table tbody tr").Count);
        var markup = cut.Markup;
        Assert.Contains("WO-26-7001", markup);
        Assert.Contains("50%", markup);
        Assert.Single(cut.FindAll(".soh-status.done"));      // the Pass row
        Assert.Single(cut.FindAll(".soh-status.stopped"));   // the Reject row
        Assert.Contains((null, (string?)null, (string?)null), _api.QcHistoryCalls);
    }

    [Fact]
    public void Kind_chip_requeries_with_kind()
    {
        _api.QcHistory = History();
        var cut = RenderComponent<QcHistory>();

        cut.FindAll(".md-chip").First(b => b.TextContent.Trim() == "OQC").Click();

        Assert.Contains(_api.QcHistoryCalls, c => c.Kind == "OQC");
    }

    [Fact]
    public void Judgment_chip_requeries_with_judgment()
    {
        _api.QcHistory = History();
        var cut = RenderComponent<QcHistory>();

        cut.FindAll(".md-chip").First(b => b.TextContent.Contains("Loại")).Click();

        Assert.Contains(_api.QcHistoryCalls, c => c.Judgment == "reject");
    }

    [Fact]
    public void Empty_history_shows_placeholder()
    {
        _api.QcHistory = new QcHistoryDto();

        var cut = RenderComponent<QcHistory>();

        Assert.Contains("Không có lượt kiểm QC nào khớp bộ lọc.", cut.Markup);
        Assert.Empty(cut.FindAll(".md-table tbody tr"));
    }
}
