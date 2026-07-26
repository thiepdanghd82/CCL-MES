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
        Services.AddI18n();
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

    // ── P11.5-3 semi-stock reserve/consume (FROM_STOCK assembly) ───

    // T3 view nhưng ASSEMBLY leg là FROM_STOCK (xuất kho bán thành phẩm).
    private static LegsView T3FromStockView(string asmPhase = "IPQC_APPROVED")
    {
        var v = T3View(asmPhase: asmPhase);
        v.Legs[2] = new LegRow
        {
            LegId = 102, Sequence = 2, LegKind = "ASSEMBLY", Method = "ASSEMBLY-m",
            ProcessLine = "SILK", LegPhase = asmPhase, LegETag = "etag2",
            InputSource = "FROM_STOCK",
        };
        return v;
    }

    [Fact]
    public void From_stock_assembly_shows_semi_actions_in_line_legs_do_not()
    {
        _api.LegsViewImpl = (_, _) => Task.FromResult(T3FromStockView());
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));

        // ASSEMBLY (seq 2) FROM_STOCK → có nút giữ/consume.
        Assert.NotNull(cut.Find("[data-testid='leg-semi-reserve-2']"));
        Assert.NotNull(cut.Find("[data-testid='leg-semi-consume-2']"));
        // PRINT (seq 0, IN_LINE mặc định) → KHÔNG có semi block.
        Assert.Empty(cut.FindAll("[data-testid='leg-semi-0']"));
    }

    [Fact]
    public void Reserve_modal_defaults_qty_to_target_and_calls_api_fefo()
    {
        _api.LegsViewImpl = (_, _) => Task.FromResult(T3FromStockView());
        _api.ReserveSemiImpl = (_, _, _, _) => Task.FromResult(new SemiSetResponse { Ok = true, Allocated = 1000 });
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));

        cut.Find("[data-testid='leg-semi-reserve-2']").Click();          // open modal
        Assert.NotNull(cut.Find("[data-testid='legs-semi-modal']"));
        // LotNo trống → FEFO auto. Confirm ngay (qty default = TargetQty 1000).
        cut.Find("[data-testid='legs-semi-confirm']").Click();

        var call = Assert.Single(_api.ReserveSemiCalls);
        Assert.Equal(102L, call.LegId);
        Assert.Equal(1000, call.Req.Qty);
        Assert.Equal("PRINTED_SEMI", call.Req.SemiKind);
        Assert.Null(call.Req.LotNo);
    }

    [Fact]
    public void Reserve_insufficient_stock_shows_localised_banner()
    {
        _api.LegsViewImpl = (_, _) => Task.FromResult(T3FromStockView());
        _api.ReserveSemiImpl = (_, _, _, _) => throw new ApiException(422,
            new CCL.MES.Shared.Envelopes.ApiError { Code = "semi.insufficient_stock", MessageEn = "no stock" });
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));

        cut.Find("[data-testid='leg-semi-reserve-2']").Click();
        cut.Find("[data-testid='legs-semi-confirm']").Click();

        Assert.Contains("Kho không đủ", cut.Find("[data-testid='legs-banner']").TextContent);
    }

    [Fact]
    public void Consume_click_calls_api_for_leg()
    {
        _api.LegsViewImpl = (_, _) => Task.FromResult(T3FromStockView());
        _api.ConsumeSemiImpl = (_, _, _) => Task.FromResult(new SemiSetResponse { Ok = true, Allocated = 1000 });
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));

        cut.Find("[data-testid='leg-semi-consume-2']").Click();

        var call = Assert.Single(_api.ConsumeSemiCalls);
        Assert.Equal(102L, call.LegId);
    }

    // ── P11-3 redesign — visual DAG lane + gate alert ──

    [Fact]
    public void Dag_lanes_group_print_tape_branch_then_converge_then_terminal()
    {
        _api.LegsViewImpl = (_, _) => Task.FromResult(T3View());
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));

        // T3 → 3 chặng: nhánh song song (PRINT∥TAPE) → hội tụ (ASSEMBLY) → terminal (CUT).
        var branch = cut.Find(".legs-stage-branch");
        Assert.Contains("leg-card-0", branch.InnerHtml);   // PRINT
        Assert.Contains("leg-card-1", branch.InnerHtml);   // TAPE
        var converge = cut.Find(".legs-stage-converge");
        Assert.Contains("leg-card-2", converge.InnerHtml); // ASSEMBLY
        var terminal = cut.Find(".legs-stage-terminal");
        Assert.Contains("leg-card-3", terminal.InnerHtml); // CUT
        // 3 chặng → 2 mũi tên nối hội tụ.
        Assert.Equal(2, cut.FindAll(".legs-connector").Count);
    }

    [Fact]
    public void Ipqc_wait_leg_shows_per_leg_ipqc_drill_in_toggle()
    {
        // ASSEMBLY leg (seq 2) at IPQC_WAIT → the leg card offers the per-leg
        // IPQC drill-in toggle; a PREPRESS leg (seq 0) does not.
        var v = T3View(asmPhase: "IPQC_WAIT");
        _api.LegsViewImpl = (_, _) => Task.FromResult(v);
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));

        Assert.NotNull(cut.Find("[data-testid='leg-ipqc-toggle-2']"));
        Assert.Empty(cut.FindAll("[data-testid='leg-ipqc-toggle-0']"));
        // Panel is collapsed until toggled.
        Assert.Empty(cut.FindAll("[data-testid='leg-ipqc-panel-2']"));
    }

    [Fact]
    public void Per_leg_readiness_chips_show_ipqc_and_materials_counts()
    {
        var v = T3View();
        v.Legs[0] = new LegRow
        {
            LegId = 100, Sequence = 0, LegKind = "PRINT", Method = "m", ProcessLine = "SILK",
            LegPhase = "PREPRESS", LegETag = "e0",
            MaterialsTotal = 6, MaterialsOk = 6, IpqcItemsTotal = 25, IpqcItemsOk = 10,
        };
        _api.LegsViewImpl = (_, _) => Task.FromResult(v);
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));

        var badge = cut.Find("[data-testid='leg-readiness-0']");
        Assert.Contains("IPQC 10/25", badge.TextContent);
        Assert.Contains("Vật tư 6/6", badge.TextContent);   // vi-default
        // A leg with no materialised surface shows no readiness chip.
        Assert.Empty(cut.FindAll("[data-testid='leg-readiness-1']"));
    }

    [Fact]
    public void Hard_gate_shows_red_alert_banner_with_title_and_disables_advance()
    {
        _api.LegsViewImpl = (_, _) => Task.FromResult(T3View(asmPhase: "IPQC_APPROVED", asmHard: true));
        var cut = RenderComponent<LegsDashboard>(p => p.Add(x => x.WorkOrderId, 42));

        var gate = cut.Find("[data-testid='leg-hard-2']");
        Assert.Contains("legs-gate-hard", gate.ClassList);          // token đỏ (var(--ng))
        Assert.Contains("Chưa chạy được", gate.TextContent);        // tiêu đề gate (vi-default)
        Assert.True(cut.Find("[data-testid='leg-advance-2']").HasAttribute("disabled"));
    }
}
