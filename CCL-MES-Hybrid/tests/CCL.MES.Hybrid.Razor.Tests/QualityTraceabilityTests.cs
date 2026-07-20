using System.Linq;
using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Client.Hardware;
using CCL.MES.Hybrid.Client.Realtime;
using CCL.MES.Hybrid.Razor.Pages;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Quality;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// bUnit tests for the real-time frozen-snapshot Traceability list + detail
/// dialog: list renders + double-click opens the dialog, a hub "change"
/// signal re-pulls the list (debounced), the Live/Offline badge reflects the
/// connection, the generic renderer shows header + variant columns, and a
/// not-frozen phase shows the empty-state.
/// </summary>
public sealed class QualityTraceabilityTests : TestContext
{
    private readonly RecordingApi _api;
    private readonly StubShopfloorLive _live;

    public QualityTraceabilityTests()
    {
        _api = new RecordingApi();
        _live = new StubShopfloorLive();
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton<IShopfloorLiveService>(_live);
        Services.AddSingleton<IBarcodeScannerService>(new StubScannerService());
        Services.AddSingleton(Options.Create(new HardwareOptions { ScanEnabled = false }));
        this.AddTestAuthorization().SetAuthorized("qc-user");
    }

    private static NpiPagedRaw<TraceListRow> OnePage(TraceListRow row)
        => new() { Items = new[] { row }, Total = 1, Page = 1, PageSize = 50 };

    private static TraceListRow Row() => new()
    {
        WoId = 7, WoNo = "WO-TR-1", ProductName = "Widget", ProductCode = "PC-1", Customer = "Acme",
        CurrentMesPhase = "RUNNING", LastScannedAtUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        LatestFrozenAtUtc = null, FrozenPhases = new() { TracePhase.Product },
    };

    private static TraceabilityDetailDto Detail() => new()
    {
        WoNo = "WO-TR-1", ProductName = "Widget",
        Product = new TracePhaseDto
        {
            Version = 1, SchemaVersion = 1, FrozenAtUtc = DateTime.UtcNow, FrozenBy = "op",
            Payload = new TracePayload
            {
                Phase = TracePhase.Product, WoNo = "WO-TR-1", Variant = "PC-1",
                Header = new() { new TraceKv { Label = "Product code", Value = "80640004" } },
                Items = new()
                {
                    new TraceItem { Key = "m0", Label = "1. AAA", Status = "Ok", Extra = new() { ["lotNo"] = "L1", ["qpaM2"] = "0.5" } },
                    new TraceItem { Key = "m1", Label = "2. BBB", Status = "Ng", NgReason = "SC-1", Extra = new() { ["lotNo"] = "L2", ["scrapFactor"] = "2" } },
                },
            },
        },
        Ipqc = null, Fqc = null, Oqc = null,
    };

    [Fact]
    public void List_renders_and_double_click_opens_dialog()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row()));
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());

        var cut = RenderComponent<QualityTraceability>();
        Assert.Single(cut.FindAll("tr.trace-row"));
        Assert.Contains("WO-TR-1", cut.Markup);
        Assert.Contains("RUNNING", cut.Markup);
        Assert.Empty(cut.FindAll(".trace-modal"));

        cut.Find("tr.trace-row").TriggerEvent("ondblclick", new MouseEventArgs());

        Assert.Single(cut.FindAll(".trace-modal"));
        Assert.Equal("WO-TR-1", cut.Find(".trace-modal-title").TextContent.Trim());
    }

    [Fact]
    public void Live_signal_repulls_the_list()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row()));
        var cut = RenderComponent<QualityTraceability>();
        var initial = _api.TraceabilityCalls.Count;   // 1 from OnInitialized

        _live.RaiseChanged("trace_updated:WO-TR-1");   // hub push → debounced re-pull

        cut.WaitForAssertion(() => Assert.True(_api.TraceabilityCalls.Count > initial));
    }

    [Fact]
    public void Live_offline_badge_reflects_connection()
    {
        _api.TraceabilityImpl = (s, p, ps, ct) => Task.FromResult(OnePage(Row()));
        var cut = RenderComponent<QualityTraceability>();

        // Stub starts disconnected → Offline.
        Assert.Contains("Offline", cut.Find(".trace-live").TextContent);

        _live.SetConnected(true);
        cut.WaitForAssertion(() => Assert.Contains("Live", cut.Find(".trace-live").TextContent));
    }

    [Fact]
    public void Dialog_generic_renderer_shows_header_and_variant_columns()
    {
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());
        var cut = RenderComponent<TraceabilityDetailDialog>(p => p
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.OnClose, () => { }));

        Assert.Equal(4, cut.FindAll(".trace-tab").Count);
        var m = cut.Markup;
        Assert.Contains("Product code", m);
        Assert.Contains("80640004", m);
        Assert.Contains("lotNo", m);
        Assert.Contains("qpaM2", m);
        Assert.Contains("scrapFactor", m);
        Assert.Equal(2, cut.FindAll(".trace-items tbody tr").Count);
    }

    [Fact]
    public void Not_frozen_tab_shows_empty_state_not_error()
    {
        _api.TraceabilityDetailImpl = (wo, ct) => Task.FromResult(Detail());
        var cut = RenderComponent<TraceabilityDetailDialog>(p => p
            .Add(x => x.WoNo, "WO-TR-1").Add(x => x.OnClose, () => { }));

        Assert.Empty(cut.FindAll(".trace-empty"));
        var ipqcTab = cut.FindAll(".trace-tab").First(b => b.TextContent.Contains("IPQC"));
        ipqcTab.Click();
        Assert.Single(cut.FindAll(".trace-empty"));
        Assert.Contains("not frozen yet", cut.Markup);
    }
}
