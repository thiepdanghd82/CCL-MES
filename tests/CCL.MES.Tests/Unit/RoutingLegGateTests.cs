using CCL.MES.Domain.Entities;
using CCL.MES.Domain.Routing;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>P11-2 — luật tiến phase leg + gate phụ thuộc (pure).</summary>
public sealed class RoutingLegGateTests
{
    private static WoLeg Leg(long id, LegKind kind, LegPhase phase, int qtyDone = 0) => new()
    {
        Id = id, LegKind = kind.ToString(), LegPhase = phase.ToString(),
        InputSource = nameof(InputSource.IN_LINE), QtyDoneCached = qtyDone,
    };

    [Fact]
    public void Next_follows_leg_flow()
    {
        Assert.Equal(LegPhase.SETTING, RoutingLegGate.Next(LegPhase.PREPRESS));
        Assert.Equal(LegPhase.LEG_DONE, RoutingLegGate.Next(LegPhase.RUNNING));
        Assert.Null(RoutingLegGate.Next(LegPhase.LEG_DONE));
    }

    [Fact]
    public void CanEnter_allows_single_forward_step()
    {
        var wo = new WorkOrder { Legs = { Leg(1, LegKind.PRINT, LegPhase.PREPRESS) } };
        var r = RoutingLegGate.CanEnter(wo, wo.Legs[0], LegPhase.SETTING);
        Assert.True(r.Allowed);
    }

    [Fact]
    public void CanEnter_rejects_skipping_phases()
    {
        var wo = new WorkOrder { Legs = { Leg(1, LegKind.PRINT, LegPhase.PREPRESS) } };
        var r = RoutingLegGate.CanEnter(wo, wo.Legs[0], LegPhase.RUNNING); // skip
        Assert.False(r.Allowed);
    }

    [Fact]
    public void CanEnter_hard_gate_blocks_running_until_inputs_done()
    {
        // ASSEMBLY(3) HARD dep PRINT(1)+TAPE(2) chưa done → chặn vào RUNNING.
        var wo = new WorkOrder
        {
            Legs = { Leg(1, LegKind.PRINT, LegPhase.PREPRESS), Leg(2, LegKind.TAPE, LegPhase.PREPRESS), Leg(3, LegKind.ASSEMBLY, LegPhase.IPQC_APPROVED) },
            LegEdges =
            {
                new WoLegDependency { LegId = 3, DependsOnLegId = 1, DependencyGate = "HARD", RequiredQty = 100 },
                new WoLegDependency { LegId = 3, DependsOnLegId = 2, DependencyGate = "HARD", RequiredQty = 100 },
            },
        };
        var r = RoutingLegGate.CanEnter(wo, wo.Legs[2], LegPhase.RUNNING);
        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.AssemblyInputsMissing, r.Error);
    }

    [Fact]
    public void CanEnter_hard_gate_opens_when_inputs_done_and_qty_met()
    {
        var wo = new WorkOrder
        {
            Legs = { Leg(1, LegKind.PRINT, LegPhase.LEG_DONE, 100), Leg(2, LegKind.TAPE, LegPhase.LEG_DONE, 100), Leg(3, LegKind.ASSEMBLY, LegPhase.IPQC_APPROVED) },
            LegEdges =
            {
                new WoLegDependency { LegId = 3, DependsOnLegId = 1, DependencyGate = "HARD", RequiredQty = 100 },
                new WoLegDependency { LegId = 3, DependsOnLegId = 2, DependencyGate = "HARD", RequiredQty = 100 },
            },
        };
        var r = RoutingLegGate.CanEnter(wo, wo.Legs[2], LegPhase.RUNNING);
        Assert.True(r.Allowed);
    }

    [Fact]
    public void CanEnter_soft_gate_allows_but_flags_waiting()
    {
        // CUT(2) SOFT dep PRINT(1) chưa done → cho vào RUNNING nhưng SoftWaiting.
        var wo = new WorkOrder
        {
            Legs = { Leg(1, LegKind.PRINT, LegPhase.RUNNING), Leg(2, LegKind.CUT, LegPhase.IPQC_APPROVED) },
            LegEdges = { new WoLegDependency { LegId = 2, DependsOnLegId = 1, DependencyGate = "SOFT" } },
        };
        var r = RoutingLegGate.CanEnter(wo, wo.Legs[1], LegPhase.RUNNING);
        Assert.True(r.Allowed);
        Assert.True(r.SoftWaiting);
    }
}
