using CCL.MES.Domain.Entities;
using CCL.MES.Domain.Routing;
using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P11-1 — 3 luật DAG (no-cycle · assembly-inputs · terminal-reaches-FQC)
/// + toàn vẹn cạnh. Fail-closed với mã lỗi cụ thể.
/// </summary>
public sealed class RoutingDagValidatorTests
{
    private static WoLeg Leg(long id, LegKind kind, InputSource src = InputSource.IN_LINE) => new()
    {
        Id = id, LegKind = kind.ToString(), InputSource = src.ToString(), LegPhase = nameof(LegPhase.PREPRESS),
    };

    private static WoLegDependency Edge(long leg, long dependsOn) => new()
    {
        LegId = leg, DependsOnLegId = dependsOn,
    };

    [Fact]
    public void Valid_T3_dag_passes()
    {
        // PRINT(1) ∥ TAPE(2) → ASSEMBLY(3) → CUT(4)
        var wo = new WorkOrder
        {
            Legs = { Leg(1, LegKind.PRINT), Leg(2, LegKind.TAPE), Leg(3, LegKind.ASSEMBLY), Leg(4, LegKind.CUT) },
            LegEdges = { Edge(3, 1), Edge(3, 2), Edge(4, 3) },
        };

        var (ok, err) = RoutingDagValidator.IsValid(wo);

        Assert.True(ok);
        Assert.Null(err);
    }

    [Fact]
    public void Valid_two_parallel_terminal_legs_pass()
    {
        // PRINT(1) ∥ CUT(2), không cạnh — cả hai terminal, join tại FQC.
        var wo = new WorkOrder { Legs = { Leg(1, LegKind.PRINT), Leg(2, LegKind.CUT) } };
        var (ok, _) = RoutingDagValidator.IsValid(wo);
        Assert.True(ok);
    }

    [Fact]
    public void Cycle_is_rejected()
    {
        var wo = new WorkOrder
        {
            Legs = { Leg(1, LegKind.PRINT), Leg(2, LegKind.CUT) },
            LegEdges = { Edge(2, 1), Edge(1, 2) },   // 1↔2 vòng
        };

        var (ok, err) = RoutingDagValidator.IsValid(wo);

        Assert.False(ok);
        Assert.Equal(WoErrorCode.InvalidRoutingDag, err);
    }

    [Fact]
    public void Assembly_missing_tape_input_is_rejected()
    {
        // ASSEMBLY(3) chỉ có dep PRINT(1), thiếu TAPE.
        var wo = new WorkOrder
        {
            Legs = { Leg(1, LegKind.PRINT), Leg(3, LegKind.ASSEMBLY), Leg(4, LegKind.CUT) },
            LegEdges = { Edge(3, 1), Edge(4, 3) },
        };

        var (ok, err) = RoutingDagValidator.IsValid(wo);

        Assert.False(ok);
        Assert.Equal(WoErrorCode.AssemblyInputsMissing, err);
    }

    [Fact]
    public void Edge_to_nonexistent_leg_is_rejected()
    {
        var wo = new WorkOrder
        {
            Legs = { Leg(1, LegKind.PRINT), Leg(2, LegKind.CUT) },
            LegEdges = { Edge(2, 99) },   // 99 không tồn tại
        };

        var (ok, err) = RoutingDagValidator.IsValid(wo);

        Assert.False(ok);
        Assert.Equal(WoErrorCode.InvalidRoutingDag, err);
    }

    [Fact]
    public void Empty_legs_is_rejected()
    {
        var (ok, err) = RoutingDagValidator.IsValid(new WorkOrder());
        Assert.False(ok);
        Assert.Equal(WoErrorCode.InvalidRoutingDag, err);
    }
}
