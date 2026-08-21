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
/// P2-PR3 — QmsQueue window Mode param. Inside a floating window there is no
/// per-window URL to read, so the host passes the stage lock through the
/// [Parameter] Mode. Mode="fqc" must lock to the FQC stage (hide the 3-tab
/// switcher) EXACTLY like the /qms/fqc route; Mode=null must behave like the
/// /qms hub (3 tabs). The parameter WINS over Nav.Uri so a window hosted at the
/// shell URL still locks correctly.
/// </summary>
public sealed class QmsQueueModeParamTests : TestContext
{
    private readonly RecordingApi _api;

    public QmsQueueModeParamTests()
    {
        _api = new RecordingApi { QmsQueue = Queue() };
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddI18n();
        this.AddTestAuthorization().SetAuthorized("qc-user");
    }

    private static QmsQueueDto Queue() => new()
    {
        IpqcCount = 1, FqcCount = 1, OqcCount = 0,
        Ipqc = new[] { new QmsQueueRow { WoId = 1, WoNo = "WO-IP-1", ProductName = "PA", MachineCode = "FBL01", TargetQty = 1000, QtyDone = 0 } },
        Fqc = new[] { new QmsQueueRow { WoId = 2, WoNo = "WO-FQ-2", ProductName = "PB", MachineCode = "ACNC3", TargetQty = 500, QtyDone = 500 } },
        Oqc = System.Array.Empty<QmsQueueRow>(),
    };

    [Fact]
    public void Mode_null_renders_the_three_tab_hub()
    {
        // Host URL is the shell root — the WINDOW carries no Mode, so it is the hub.
        var cut = RenderComponent<QmsQueue>();

        Assert.Equal(3, cut.FindAll(".qms-tabs button.md-chip").Count);
        Assert.Contains("WO-IP-1", cut.Markup);   // IPQC default tab
    }

    [Fact]
    public void Mode_fqc_locks_to_fqc_and_hides_switcher()
    {
        var cut = RenderComponent<QmsQueue>(p => p.Add(x => x.Mode, "fqc"));

        Assert.Empty(cut.FindAll(".qms-tabs button.md-chip"));   // switcher hidden
        Assert.Contains("FQC (1)", cut.Markup);
        Assert.Contains("WO-FQ-2", cut.Markup);
        Assert.DoesNotContain("WO-IP-1", cut.Markup);
    }

    [Fact]
    public void Mode_param_wins_over_url_when_both_disagree()
    {
        // Navigate the shell to the hub URL, but pass Mode="fqc" as the window
        // param — the param must win so the window is FQC-locked, not the hub.
        var nav = Services.GetRequiredService<NavigationManager>();
        nav.NavigateTo("/qms");

        var cut = RenderComponent<QmsQueue>(p => p.Add(x => x.Mode, "fqc"));

        Assert.Empty(cut.FindAll(".qms-tabs button.md-chip"));
        Assert.Contains("WO-FQ-2", cut.Markup);
    }
}
