using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Qms;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// QMS Inspection Queue routes. The hub (/qms) keeps its 3-tab behaviour; the
/// FQC stage route (/qms/fqc) locks to a single stage and hides the switcher.
/// (IPQC/OQC are now full inspection modules, not stage-locked queue views.)
/// </summary>
public sealed class QmsQueueStageRouteTests : TestContext
{
    private readonly RecordingApi _api;

    public QmsQueueStageRouteTests()
    {
        _api = new RecordingApi();
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddI18n();
        this.AddTestAuthorization().SetAuthorized("test-user");
    }

    private static QmsQueueDto Queue() => new()
    {
        IpqcCount = 1, FqcCount = 1, OqcCount = 0,
        Ipqc = new[] { new QmsQueueRow { WoId = 1, WoNo = "WO-IP-1", ProductName = "PA", MachineCode = "FBL01", TargetQty = 1000, QtyDone = 0 } },
        Fqc = new[] { new QmsQueueRow { WoId = 2, WoNo = "WO-FQ-2", ProductName = "PB", MachineCode = "ACNC3", TargetQty = 500, QtyDone = 500 } },
        Oqc = System.Array.Empty<QmsQueueRow>(),
    };

    private IRenderedComponent<QmsQueue> RenderAt(string relativeUrl)
    {
        _api.QmsQueue = Queue();
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo(relativeUrl);
        return RenderComponent<QmsQueue>();
    }

    [Fact]
    public void Default_qms_keeps_three_tabs()   // regression
    {
        var cut = RenderAt("/qms");
        Assert.Equal(3, cut.FindAll(".qms-tabs button.md-chip").Count);
        Assert.Contains("WO-IP-1", cut.Markup);   // IPQC default
    }

    [Fact]
    public void Fqc_route_locks_to_fqc_and_hides_switcher()
    {
        var cut = RenderAt("/qms/fqc");

        Assert.Empty(cut.FindAll(".qms-tabs button.md-chip"));
        Assert.Contains("FQC (1)", cut.Markup);
        Assert.Contains("WO-FQ-2", cut.Markup);
        Assert.DoesNotContain("WO-IP-1", cut.Markup);
    }
}
