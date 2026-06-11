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
/// P10.9 — bUnit render tests for the QMS Inspection Queue page: 3 stage
/// tabs (IPQC/FQC/OQC) with counts + the per-stage worklist + tab switch.
/// </summary>
public sealed class QmsQueueTests : TestContext
{
    private readonly RecordingApi _api;

    public QmsQueueTests()
    {
        _api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(_api);
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    private static QmsQueueDto Queue() => new()
    {
        IpqcCount = 1, FqcCount = 1, OqcCount = 0,
        Ipqc = new[] { new QmsQueueRow { WoId = 1, WoNo = "WO-IP-1", ProductName = "PA", MachineCode = "FBL01", TargetQty = 1000, QtyDone = 0 } },
        Fqc = new[] { new QmsQueueRow { WoId = 2, WoNo = "WO-FQ-2", ProductName = "PB", MachineCode = "ACNC3", TargetQty = 500, QtyDone = 500 } },
        Oqc = System.Array.Empty<QmsQueueRow>(),
    };

    [Fact]
    public void Renders_three_stage_tabs_with_counts_and_ipqc_by_default()
    {
        _api.QmsQueue = Queue();

        var cut = RenderComponent<QmsQueue>();
        var markup = cut.Markup;

        Assert.Equal(3, cut.FindAll(".qms-tabs .md-chip").Count);
        Assert.Contains("IPQC (1)", markup);
        Assert.Contains("FQC (1)", markup);
        Assert.Contains("OQC (0)", markup);
        // IPQC stage shown first.
        Assert.Contains("WO-IP-1", markup);
        Assert.DoesNotContain("WO-FQ-2", markup);
        Assert.Equal(1, _api.QmsQueueCalls);
    }

    [Fact]
    public void Switching_to_fqc_tab_shows_fqc_rows()
    {
        _api.QmsQueue = Queue();
        var cut = RenderComponent<QmsQueue>();

        cut.FindAll(".qms-tabs .md-chip").First(b => b.TextContent.Contains("FQC")).Click();

        var markup = cut.Markup;
        Assert.Contains("WO-FQ-2", markup);
        Assert.DoesNotContain("WO-IP-1", markup);
    }

    [Fact]
    public void Empty_stage_shows_placeholder()
    {
        _api.QmsQueue = Queue();
        var cut = RenderComponent<QmsQueue>();

        // OQC bucket is empty.
        cut.FindAll(".qms-tabs .md-chip").First(b => b.TextContent.Contains("OQC")).Click();

        Assert.Contains("Không có WO nào đang chờ OQC", cut.Markup);
        Assert.Empty(cut.FindAll(".md-table tbody tr"));
    }
}
