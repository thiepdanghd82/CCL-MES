using Bunit;
using Bunit.TestDoubles;
using CCL.MES.Hybrid.Client;
using CCL.MES.Hybrid.Razor.Shared;
using CCL.MES.Hybrid.Razor.Tests._Support;
using CCL.MES.Shared.Routing;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Hybrid.Razor.Tests;

/// <summary>
/// P11-3 — bUnit render tests cho <see cref="LegsDashboard"/> (fork-join
/// leg surface). Rule 4: plain button/input. Rule 7.3 wire-mirror: mỗi
/// probe có server test cặp trong RoutingControllerTests.
/// </summary>
public sealed class LegsDashboardTests : TestContext
{
    private readonly RecordingApi _api = new();

    public LegsDashboardTests()
    {
        Services.AddSingleton<ICclApiClient>(_api);
        Services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        this.AddTestAuthorization().SetAuthorized("op");
    }

    private static LegRow Leg(int seq, string kind, string phase, bool terminal = false, bool hard = false, bool soft = false) => new()
    {
        LegId = 100 + seq, Sequence = seq, LegKind = kind, Method = kind + "-m",
        ProcessLine = "SILK", LegPhase = phase, LegETag = $"etag{seq}",
        IsTerminal = terminal, HardBlocked = hard, SoftWaiting = soft,
    };

    // T3: PRINT ∥ TAPE → ASSEMBLY → CUT(terminal).
    private static LegsView T3View(
        string printPhase = "PREPRESS", string tapePhase = "PREPRESS",
        string asmPhase = "PREPRESS", bool asmHard = false) => new()
    {
        WoId = 42, WoNo = "WO-P11-T3", MesPhase = "SPLIT", WoETag = "wo==", TargetQty = 1000,
        Legs =
        {
            Leg(0, "PRINT", printPhase),
            Leg(1, "TAPE", tapePhase),
            Leg(2, "ASSEMBLY", asmPhase, hard: asmHard),
            Leg(3, "CUT", "PREPRESS", terminal: true),
        },
    };

    [Fact]
    public void Renders_all_legs_with_kind_labels_and_terminal_flag()
    {
        _api.LegsViewImpl = (_, _) => Task.FromResult(T3View());
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));

        Assert.Equal(4, cut.FindAll("[data-testid^='leg-card-']").Count);
        Assert.Contains("IN", cut.Find("[data-testid='leg-kind-0']").TextContent);
        Assert.Contains("CẮT TAPE", cut.Find("[data-testid='leg-kind-1']").TextContent);
        Assert.Contains("DÁN", cut.Find("[data-testid='leg-kind-2']").TextContent);
        // CUT là terminal duy nhất.
        Assert.Single(cut.FindAll(".legs-terminal"));
    }

    [Fact]
    public void Hard_blocked_leg_shows_gate_and_disables_running_advance()
    {
        _api.LegsViewImpl = (_, _) => Task.FromResult(T3View(asmPhase: "IPQC_APPROVED", asmHard: true));
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));

        Assert.NotNull(cut.Find("[data-testid='leg-hard-2']"));
        var advanceBtn = cut.Find("[data-testid='leg-advance-2']");
        Assert.True(advanceBtn.HasAttribute("disabled")); // ASSEMBLY→RUNNING chặn
    }

    [Fact]
    public void Soft_waiting_leg_shows_advisory_banner()
    {
        var v = T3View();
        v.Legs[3] = Leg(3, "CUT", "IPQC_APPROVED", terminal: true, soft: true);
        _api.LegsViewImpl = (_, _) => Task.FromResult(v);
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));
        Assert.NotNull(cut.Find("[data-testid='leg-soft-3']"));
    }

    [Fact]
    public void Advance_click_calls_api_with_leg_etag_and_next_phase()
    {
        _api.LegsViewImpl = (_, _) => Task.FromResult(T3View());
        _api.AdvanceLegImpl = (_, _, _, _, _) => Task.FromResult(new LegSetResponse { Ok = true, LegPhase = "SETTING", LegETag = "new" });
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));

        cut.Find("[data-testid='leg-advance-0']").Click();

        var call = Assert.Single(_api.AdvanceLegCalls);
        Assert.Equal(100L, call.LegId);       // PRINT leg id
        Assert.Equal("etag0", call.ETag);      // per-leg If-Match
        Assert.Equal("SETTING", call.ToPhase); // next after PREPRESS
    }

    [Fact]
    public void Join_response_bubbles_OnPhaseChanged()
    {
        _api.LegsViewImpl = (_, _) => Task.FromResult(T3View());
        _api.AdvanceLegImpl = (_, _, _, _, _) => Task.FromResult(new LegSetResponse { Ok = true, Joined = true, WoMesPhase = "FQC_PENDING" });
        var bubbled = false;
        var cut = RenderComponent<LegsDashboard>(p => p
            .Add(x => x.WorkOrderId, 42)
            .Add(x => x.OnPhaseChanged, EventCallback.Factory.Create(this, () => bubbled = true)));

        cut.Find("[data-testid='leg-advance-0']").Click();

        Assert.True(bubbled); // L21 — parent re-fetch → FqcDashboard
    }

    [Fact]
    public void Rework_flow_requires_reason_then_calls_api()
    {
        var v = T3View(printPhase: "IPQC_WAIT");
        _api.LegsViewImpl = (_, _) => Task.FromResult(v);
        _api.ReworkLegImpl = (_, _, _, _, _) => Task.FromResult(new LegSetResponse { Ok = true, LegPhase = "PREPRESS" });
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));

        cut.Find("[data-testid='leg-rework-0']").Click();               // open modal
        Assert.NotNull(cut.Find("[data-testid='legs-rework-modal']"));
        // confirm disabled khi chưa nhập lý do
        Assert.True(cut.Find("[data-testid='legs-rework-confirm']").HasAttribute("disabled"));
        cut.Find("[data-testid='legs-rework-reason']").Input("NG lệch màu");
        cut.Find("[data-testid='legs-rework-confirm']").Click();

        var call = Assert.Single(_api.ReworkLegCalls);
        Assert.Equal(100L, call.LegId);
        Assert.Equal("NG lệch màu", call.Reason);
    }

    [Fact]
    public void Initial_load_error_shows_banner()
    {
        _api.LegsViewImpl = (_, _) => throw new InvalidOperationException("boom");
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));
        Assert.NotNull(cut.Find("[data-testid='legs-initial-error']"));
    }
}
