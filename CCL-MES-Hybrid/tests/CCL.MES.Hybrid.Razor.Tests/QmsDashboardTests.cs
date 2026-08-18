using System.Linq;
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
/// QMS Dashboard (/qms/dashboard) — KPI-by-stage overview reusing the existing
/// Inspection Queue endpoint. Proves the counts render + the stage cards link
/// down to their stage-scoped queues (no new DTO/API).
/// </summary>
public sealed class QmsDashboardTests : TestContext
{
    private readonly RecordingApi _api;

    public QmsDashboardTests()
    {
        _api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddI18n();
        this.AddTestAuthorization().SetAuthorized("qc-user");
    }

    [Fact]
    public void Renders_stage_counts_and_total()
    {
        _api.QmsQueue = new QmsQueueDto
        {
            IpqcCount = 3, FqcCount = 2, OqcCount = 1,
            Ipqc = System.Array.Empty<QmsQueueRow>(),
            Fqc = System.Array.Empty<QmsQueueRow>(),
            Oqc = System.Array.Empty<QmsQueueRow>(),
        };

        var cut = RenderComponent<QmsDashboard>();

        var ipqc = cut.Find("[data-testid='qms-dash-ipqc']");
        Assert.Contains("3", ipqc.TextContent);
        var total = cut.Find("[data-testid='qms-dash-total']");
        Assert.Contains("6", total.TextContent);        // 3 + 2 + 1

        Assert.Equal(1, _api.QmsQueueCalls);
    }

    [Fact]
    public void Stage_cards_link_to_stage_scoped_queues()
    {
        _api.QmsQueue = new QmsQueueDto();
        var cut = RenderComponent<QmsDashboard>();

        Assert.Equal("/qms/ipqc", cut.Find("[data-testid='qms-dash-ipqc']").GetAttribute("href"));
        Assert.Equal("/qms/fqc", cut.Find("[data-testid='qms-dash-fqc']").GetAttribute("href"));
        Assert.Equal("/qms/oqc", cut.Find("[data-testid='qms-dash-oqc']").GetAttribute("href"));
        Assert.Equal("/qms", cut.Find("[data-testid='qms-dash-queue']").GetAttribute("href"));
    }

    [Fact]
    public void Shows_dash_when_data_missing()
    {
        _api.QmsQueue = null;   // GetQmsQueueAsync → null
        var cut = RenderComponent<QmsDashboard>();

        Assert.Contains("—", cut.Find("[data-testid='qms-dash-total']").TextContent);
    }
}
