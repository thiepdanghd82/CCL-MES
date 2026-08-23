using CCL.MES.Api.Policies;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.Routing;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Api.Tests.Unit;

/// <summary>
/// Luật thuần của Routing DAG tách khỏi <c>RoutingController</c>. Phần lớn luật
/// routing đã ở Domain từ P11; đây khoá 3 mẩu controller tự giữ: parse ToPhase,
/// kiểm reason rework, và tính soft/hard cho picker. <see cref="DependencyStatus"/>
/// là mẩu có logic thật nhất — trước đây chỉ kiểm được gián tiếp qua GET picker
/// dựng cả WebApplicationFactory; nay là hàm thuần trên đồ thị leg dựng tay.
/// </summary>
public sealed class RoutingPolicyTests
{
    // ── ParseAdvanceToPhase ──────────────────────────────────────────────

    [Theory]
    [InlineData("RUNNING", LegPhase.RUNNING)]
    [InlineData("running", LegPhase.RUNNING)]          // case-insensitive
    [InlineData("LEG_DONE", LegPhase.LEG_DONE)]
    [InlineData("PREPRESS", LegPhase.PREPRESS)]
    public void ParseAdvanceToPhase_valid_returns_phase(string raw, LegPhase expected)
    {
        var p = RoutingPolicy.ParseAdvanceToPhase(raw);
        Assert.True(p.IsValid);
        Assert.Equal(expected, p.Phase);
        Assert.Null(p.ErrorCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bogus")]
    public void ParseAdvanceToPhase_invalid_yields_leg_invalid_phase(string? raw)
    {
        var p = RoutingPolicy.ParseAdvanceToPhase(raw);
        Assert.False(p.IsValid);
        Assert.Equal("leg.invalid_phase", p.ErrorCode);
        Assert.Equal("ToPhase không hợp lệ.", p.ErrorMessage);
    }

    // ── ValidateReworkReason ─────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateReworkReason_blank_yields_leg_invalid_reason(string? reason)
    {
        var err = RoutingPolicy.ValidateReworkReason(reason);
        Assert.NotNull(err);
        Assert.Equal("leg.invalid_reason", err!.Value.ErrorCode);
        Assert.Equal("Reason bắt buộc (1-500 ký tự).", err.Value.Message);
    }

    [Fact]
    public void ValidateReworkReason_over_500_yields_leg_invalid_reason()
    {
        var err = RoutingPolicy.ValidateReworkReason(new string('x', 501));
        Assert.Equal("leg.invalid_reason", err!.Value.ErrorCode);
    }

    [Fact]
    public void ValidateReworkReason_valid_returns_null()
    {
        Assert.Null(RoutingPolicy.ValidateReworkReason("misprint"));
        Assert.Null(RoutingPolicy.ValidateReworkReason(new string('x', 500)));  // boundary
    }

    // ── DependencyStatus ─────────────────────────────────────────────────

    private static WoLeg Leg(long id, LegPhase phase, int qtyDone = 0) =>
        new() { Id = id, LegPhase = phase.ToString(), QtyDoneCached = qtyDone };

    private static WoLegDependency Edge(long legId, long dependsOn, DependencyGate gate, int requiredQty = 0) =>
        new() { LegId = legId, DependsOnLegId = dependsOn, DependencyGate = gate.ToString(), RequiredQty = requiredQty };

    [Theory]
    [InlineData(LegPhase.RUNNING)]
    [InlineData(LegPhase.LEG_DONE)]
    public void DependencyStatus_running_or_done_leg_is_never_blocked(LegPhase phase)
    {
        var leg = Leg(2, phase);
        var wo = new WorkOrder { Legs = { Leg(1, LegPhase.PREPRESS), leg }, LegEdges = { Edge(2, 1, DependencyGate.HARD) } };
        Assert.Equal((false, false), RoutingPolicy.DependencyStatus(wo, leg));
    }

    [Fact]
    public void DependencyStatus_no_edges_is_clear()
    {
        var leg = Leg(1, LegPhase.PREPRESS);
        var wo = new WorkOrder { Legs = { leg } };
        Assert.Equal((false, false), RoutingPolicy.DependencyStatus(wo, leg));
    }

    [Fact]
    public void DependencyStatus_hard_predecessor_not_done_blocks_hard()
    {
        var pred = Leg(1, LegPhase.RUNNING);
        var leg = Leg(2, LegPhase.PREPRESS);
        var wo = new WorkOrder { Legs = { pred, leg }, LegEdges = { Edge(2, 1, DependencyGate.HARD) } };
        Assert.Equal((false, true), RoutingPolicy.DependencyStatus(wo, leg));
    }

    [Fact]
    public void DependencyStatus_soft_predecessor_not_done_flags_soft_only()
    {
        var pred = Leg(1, LegPhase.RUNNING);
        var leg = Leg(2, LegPhase.PREPRESS);
        var wo = new WorkOrder { Legs = { pred, leg }, LegEdges = { Edge(2, 1, DependencyGate.SOFT) } };
        Assert.Equal((true, false), RoutingPolicy.DependencyStatus(wo, leg));
    }

    [Fact]
    public void DependencyStatus_hard_predecessor_done_no_qty_gate_is_clear()
    {
        var pred = Leg(1, LegPhase.LEG_DONE);
        var leg = Leg(2, LegPhase.PREPRESS);
        var wo = new WorkOrder { Legs = { pred, leg }, LegEdges = { Edge(2, 1, DependencyGate.HARD) } };
        Assert.Equal((false, false), RoutingPolicy.DependencyStatus(wo, leg));
    }

    [Fact]
    public void DependencyStatus_hard_predecessor_done_but_insufficient_qty_still_blocks()
    {
        var pred = Leg(1, LegPhase.LEG_DONE, qtyDone: 50);
        var leg = Leg(2, LegPhase.PREPRESS);
        var wo = new WorkOrder { Legs = { pred, leg }, LegEdges = { Edge(2, 1, DependencyGate.HARD, requiredQty: 100) } };
        Assert.Equal((false, true), RoutingPolicy.DependencyStatus(wo, leg));
    }

    [Fact]
    public void DependencyStatus_hard_predecessor_done_with_enough_qty_is_clear()
    {
        var pred = Leg(1, LegPhase.LEG_DONE, qtyDone: 100);
        var leg = Leg(2, LegPhase.PREPRESS);
        var wo = new WorkOrder { Legs = { pred, leg }, LegEdges = { Edge(2, 1, DependencyGate.HARD, requiredQty: 100) } };
        Assert.Equal((false, false), RoutingPolicy.DependencyStatus(wo, leg));
    }

    [Fact]
    public void DependencyStatus_mixed_undone_hard_and_soft_flags_both()
    {
        var hardPred = Leg(1, LegPhase.RUNNING);
        var softPred = Leg(2, LegPhase.RUNNING);
        var leg = Leg(3, LegPhase.PREPRESS);
        var wo = new WorkOrder
        {
            Legs = { hardPred, softPred, leg },
            LegEdges = { Edge(3, 1, DependencyGate.HARD), Edge(3, 2, DependencyGate.SOFT) },
        };
        Assert.Equal((true, true), RoutingPolicy.DependencyStatus(wo, leg));
    }
}
