using CCL.MES.Domain.Entities;
using CCL.MES.Domain.Routing;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P11-1 — cell fork <c>PREPRESS → SPLIT</c> + join <c>SPLIT →
/// FQC_PENDING</c> qua <see cref="WorkOrderStateMachine.CanTransition"/>.
/// WO 1-leg KHÔNG bị ảnh hưởng (parity lock ở các test khác).
/// </summary>
public sealed class MesPhaseSplitTransitionTests
{
    private static WoLeg Leg(long id, LegKind kind, LegPhase phase) => new()
    {
        Id = id, LegKind = kind.ToString(), InputSource = nameof(InputSource.IN_LINE), LegPhase = phase.ToString(),
    };

    // ── Fork PREPRESS → SPLIT ──────────────────────────────────────

    [Fact]
    public void Fork_allowed_when_two_legs_and_valid_dag()
    {
        var wo = new WorkOrder
        {
            Legs = { Leg(1, LegKind.PRINT, LegPhase.PREPRESS), Leg(2, LegKind.CUT, LegPhase.PREPRESS) },
        };

        var r = WorkOrderStateMachine.CanTransition(wo, MesPhase.PREPRESS, MesPhase.SPLIT);

        Assert.True(r.Allowed);
    }

    [Fact]
    public void Fork_rejected_when_fewer_than_two_legs()
    {
        var wo = new WorkOrder { Legs = { Leg(1, LegKind.PRINT_CUT, LegPhase.PREPRESS) } };

        var r = WorkOrderStateMachine.CanTransition(wo, MesPhase.PREPRESS, MesPhase.SPLIT);

        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.InvalidRoutingDag, r.Error);
    }

    [Fact]
    public void Fork_rejected_when_dag_invalid()
    {
        // 2 leg nhưng ASSEMBLY thiếu input → DAG invalid.
        var wo = new WorkOrder
        {
            Legs = { Leg(1, LegKind.PRINT, LegPhase.PREPRESS), Leg(2, LegKind.ASSEMBLY, LegPhase.PREPRESS) },
            LegEdges = { new WoLegDependency { LegId = 2, DependsOnLegId = 1 } }, // assembly chỉ có PRINT
        };

        var r = WorkOrderStateMachine.CanTransition(wo, MesPhase.PREPRESS, MesPhase.SPLIT);

        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.InvalidRoutingDag, r.Error);
    }

    // ── Join SPLIT → FQC_PENDING ───────────────────────────────────

    [Fact]
    public void Join_allowed_when_all_terminal_legs_done()
    {
        // 2 leg song song (đều terminal) đều LEG_DONE.
        var wo = new WorkOrder
        {
            Legs = { Leg(1, LegKind.PRINT, LegPhase.LEG_DONE), Leg(2, LegKind.CUT, LegPhase.LEG_DONE) },
        };

        var r = WorkOrderStateMachine.CanTransition(wo, MesPhase.SPLIT, MesPhase.FQC_PENDING);

        Assert.True(r.Allowed);
    }

    [Fact]
    public void Join_rejected_when_a_terminal_leg_not_done()
    {
        var wo = new WorkOrder
        {
            Legs = { Leg(1, LegKind.PRINT, LegPhase.LEG_DONE), Leg(2, LegKind.CUT, LegPhase.RUNNING) },
        };

        var r = WorkOrderStateMachine.CanTransition(wo, MesPhase.SPLIT, MesPhase.FQC_PENDING);

        Assert.False(r.Allowed);
        Assert.Equal(WoErrorCode.LegsNotAllDone, r.Error);
    }

    [Fact]
    public void Join_ignores_non_terminal_legs_done_state()
    {
        // T3: PRINT(1) ∥ TAPE(2) → ASSEMBLY(3) → CUT(4, terminal).
        // PRINT/TAPE/ASSEMBLY không phải terminal; chỉ CUT quyết định join.
        var wo = new WorkOrder
        {
            Legs =
            {
                Leg(1, LegKind.PRINT, LegPhase.LEG_DONE),
                Leg(2, LegKind.TAPE, LegPhase.LEG_DONE),
                Leg(3, LegKind.ASSEMBLY, LegPhase.LEG_DONE),
                Leg(4, LegKind.CUT, LegPhase.LEG_DONE),
            },
            LegEdges =
            {
                new WoLegDependency { LegId = 3, DependsOnLegId = 1 },
                new WoLegDependency { LegId = 3, DependsOnLegId = 2 },
                new WoLegDependency { LegId = 4, DependsOnLegId = 3 },
            },
        };

        var r = WorkOrderStateMachine.CanTransition(wo, MesPhase.SPLIT, MesPhase.FQC_PENDING);

        Assert.True(r.Allowed);
    }

    [Fact]
    public void IsTerminalLeg_true_only_for_leaf()
    {
        var print = Leg(1, LegKind.PRINT, LegPhase.LEG_DONE);
        var cut = Leg(2, LegKind.CUT, LegPhase.RUNNING);
        var wo = new WorkOrder
        {
            Legs = { print, cut },
            LegEdges = { new WoLegDependency { LegId = 2, DependsOnLegId = 1 } },
        };

        Assert.False(WorkOrderStateMachine.IsTerminalLeg(wo, print)); // print có successor cut
        Assert.True(WorkOrderStateMachine.IsTerminalLeg(wo, cut));    // cut là leaf
    }
}
