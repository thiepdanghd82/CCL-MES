using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7a-2.2 — exhaustive coverage of <see cref="WorkOrderStateMachine.IsForceablePhase"/>
/// against the 144-cell §3.1 grid. The forceable set (11 cells) is
/// derived directly from §3.1's "recovery-only" cells:
///   1. SETTING → PREPRESS                    (the §8.1 archetype)
///   2. NEW           → CANCELLED
///   3. PREPRESS      → CANCELLED
///   4. SETTING       → CANCELLED
///   5. IPQC_WAIT     → CANCELLED
///   6. QA_PENDING    → CANCELLED
///   7. IPQC_APPROVED → CANCELLED
///   8. RUNNING       → CANCELLED
///   9. PAUSED        → CANCELLED
///   10. FQC_PENDING  → CANCELLED
///   11. OQC_PENDING  → CANCELLED
/// Every other cell (133 of 144) is non-forceable. Henry's adj #4
/// specifically requires explicit assertion for:
///   * DONE → * (12 cells): terminal source per §2.2
///   * CANCELLED → * (12 cells): terminal source per §2.2
///   * * → DONE (12 cells): no recovery-only path to DONE
///   * * → NEW (12 cells): NEW is system-only entry per §2.2
///   * diagonal (12 cells): same-state force per Q4a → 422 same_state
/// </summary>
public sealed class WorkOrderStateMachineIsForceableTests
{
    private static readonly MesPhase[] AllPhases =
    {
        MesPhase.NEW, MesPhase.PREPRESS, MesPhase.SETTING, MesPhase.IPQC_WAIT,
        MesPhase.QA_PENDING, MesPhase.IPQC_APPROVED, MesPhase.RUNNING, MesPhase.PAUSED,
        MesPhase.FQC_PENDING, MesPhase.OQC_PENDING, MesPhase.DONE, MesPhase.CANCELLED,
    };

    private static readonly (MesPhase From, MesPhase To)[] ForceableCells = new[]
    {
        (MesPhase.SETTING,       MesPhase.PREPRESS),
        (MesPhase.NEW,           MesPhase.CANCELLED),
        (MesPhase.PREPRESS,      MesPhase.CANCELLED),
        (MesPhase.SETTING,       MesPhase.CANCELLED),
        (MesPhase.IPQC_WAIT,     MesPhase.CANCELLED),
        (MesPhase.QA_PENDING,    MesPhase.CANCELLED),
        (MesPhase.IPQC_APPROVED, MesPhase.CANCELLED),
        (MesPhase.RUNNING,       MesPhase.CANCELLED),
        (MesPhase.PAUSED,        MesPhase.CANCELLED),
        (MesPhase.FQC_PENDING,   MesPhase.CANCELLED),
        (MesPhase.OQC_PENDING,   MesPhase.CANCELLED),
    };

    public static IEnumerable<object[]> All144Cells()
    {
        foreach (var from in AllPhases)
            foreach (var to in AllPhases)
                yield return new object[] { from, to };
    }

    [Theory]
    [MemberData(nameof(All144Cells))]
    public void IsForceablePhase_classifies_every_cell_per_contract(MesPhase from, MesPhase to)
    {
        var expected = ForceableCells.Contains((from, to));
        var actual = WorkOrderStateMachine.IsForceablePhase(from, to);
        Assert.Equal(expected, actual);
    }

    // ── Meta: matrix coverage + count totals ──────────────────────

    [Fact]
    public void Matrix_covers_exactly_144_cells_once()
    {
        var seen = new HashSet<(MesPhase, MesPhase)>();
        foreach (var pair in All144Cells())
        {
            var key = ((MesPhase)pair[0], (MesPhase)pair[1]);
            Assert.True(seen.Add(key), $"Duplicate cell in matrix: {key}");
        }
        Assert.Equal(144, seen.Count);
    }

    [Fact]
    public void Forceable_set_has_exactly_eleven_cells_and_zero_overlap_with_blocked()
    {
        var forceable = 0;
        foreach (var pair in All144Cells())
        {
            var from = (MesPhase)pair[0];
            var to = (MesPhase)pair[1];
            if (WorkOrderStateMachine.IsForceablePhase(from, to))
                forceable++;
        }
        Assert.Equal(11, forceable);
        Assert.Equal(ForceableCells.Length, forceable);
    }

    // ── Henry's adj #4 targeted asserts ────────────────────────────

    [Theory]
    [InlineData(MesPhase.NEW)]
    [InlineData(MesPhase.PREPRESS)]
    [InlineData(MesPhase.SETTING)]
    [InlineData(MesPhase.IPQC_WAIT)]
    [InlineData(MesPhase.QA_PENDING)]
    [InlineData(MesPhase.IPQC_APPROVED)]
    [InlineData(MesPhase.RUNNING)]
    [InlineData(MesPhase.PAUSED)]
    [InlineData(MesPhase.FQC_PENDING)]
    [InlineData(MesPhase.OQC_PENDING)]
    [InlineData(MesPhase.DONE)]
    [InlineData(MesPhase.CANCELLED)]
    public void From_DONE_is_never_forceable_regardless_of_target(MesPhase to)
        => Assert.False(WorkOrderStateMachine.IsForceablePhase(MesPhase.DONE, to));

    [Theory]
    [InlineData(MesPhase.NEW)]
    [InlineData(MesPhase.PREPRESS)]
    [InlineData(MesPhase.SETTING)]
    [InlineData(MesPhase.IPQC_WAIT)]
    [InlineData(MesPhase.QA_PENDING)]
    [InlineData(MesPhase.IPQC_APPROVED)]
    [InlineData(MesPhase.RUNNING)]
    [InlineData(MesPhase.PAUSED)]
    [InlineData(MesPhase.FQC_PENDING)]
    [InlineData(MesPhase.OQC_PENDING)]
    [InlineData(MesPhase.DONE)]
    [InlineData(MesPhase.CANCELLED)]
    public void From_CANCELLED_is_never_forceable_regardless_of_target(MesPhase to)
        => Assert.False(WorkOrderStateMachine.IsForceablePhase(MesPhase.CANCELLED, to));

    [Theory]
    [InlineData(MesPhase.NEW)]
    [InlineData(MesPhase.PREPRESS)]
    [InlineData(MesPhase.SETTING)]
    [InlineData(MesPhase.IPQC_WAIT)]
    [InlineData(MesPhase.QA_PENDING)]
    [InlineData(MesPhase.IPQC_APPROVED)]
    [InlineData(MesPhase.RUNNING)]
    [InlineData(MesPhase.PAUSED)]
    [InlineData(MesPhase.FQC_PENDING)]
    [InlineData(MesPhase.OQC_PENDING)]
    [InlineData(MesPhase.DONE)]
    [InlineData(MesPhase.CANCELLED)]
    public void Target_DONE_is_never_forceable_regardless_of_source(MesPhase from)
        => Assert.False(WorkOrderStateMachine.IsForceablePhase(from, MesPhase.DONE));

    [Theory]
    [InlineData(MesPhase.NEW)]
    [InlineData(MesPhase.PREPRESS)]
    [InlineData(MesPhase.SETTING)]
    [InlineData(MesPhase.IPQC_WAIT)]
    [InlineData(MesPhase.QA_PENDING)]
    [InlineData(MesPhase.IPQC_APPROVED)]
    [InlineData(MesPhase.RUNNING)]
    [InlineData(MesPhase.PAUSED)]
    [InlineData(MesPhase.FQC_PENDING)]
    [InlineData(MesPhase.OQC_PENDING)]
    [InlineData(MesPhase.DONE)]
    [InlineData(MesPhase.CANCELLED)]
    public void Target_NEW_is_never_forceable_regardless_of_source(MesPhase from)
        => Assert.False(WorkOrderStateMachine.IsForceablePhase(from, MesPhase.NEW));

    [Theory]
    [InlineData(MesPhase.NEW)]
    [InlineData(MesPhase.PREPRESS)]
    [InlineData(MesPhase.SETTING)]
    [InlineData(MesPhase.IPQC_WAIT)]
    [InlineData(MesPhase.QA_PENDING)]
    [InlineData(MesPhase.IPQC_APPROVED)]
    [InlineData(MesPhase.RUNNING)]
    [InlineData(MesPhase.PAUSED)]
    [InlineData(MesPhase.FQC_PENDING)]
    [InlineData(MesPhase.OQC_PENDING)]
    [InlineData(MesPhase.DONE)]
    [InlineData(MesPhase.CANCELLED)]
    public void Self_loops_are_never_forceable(MesPhase phase)
        => Assert.False(WorkOrderStateMachine.IsForceablePhase(phase, phase));

    // ── §8.1 archetype: SETTING → PREPRESS is the only non-CANCEL forceable cell ──

    [Fact]
    public void Setting_to_prepress_is_the_only_non_cancel_forceable_cell()
    {
        // Sanity: the §8.1 "operator A left mid-shift" example is forceable.
        Assert.True(WorkOrderStateMachine.IsForceablePhase(MesPhase.SETTING, MesPhase.PREPRESS));

        // And: no other non-cancel target is forceable from any source.
        foreach (var from in AllPhases)
        {
            foreach (var to in AllPhases)
            {
                if (to == MesPhase.CANCELLED) continue;
                if (from == MesPhase.SETTING && to == MesPhase.PREPRESS) continue;
                Assert.False(WorkOrderStateMachine.IsForceablePhase(from, to),
                    $"Non-CANCEL forceable cell found outside SETTING→PREPRESS: {from} → {to}");
            }
        }
    }

    // ── Counterpart check: every non-terminal source CAN force-cancel ──

    [Theory]
    [InlineData(MesPhase.NEW)]
    [InlineData(MesPhase.PREPRESS)]
    [InlineData(MesPhase.SETTING)]
    [InlineData(MesPhase.IPQC_WAIT)]
    [InlineData(MesPhase.QA_PENDING)]
    [InlineData(MesPhase.IPQC_APPROVED)]
    [InlineData(MesPhase.RUNNING)]
    [InlineData(MesPhase.PAUSED)]
    [InlineData(MesPhase.FQC_PENDING)]
    [InlineData(MesPhase.OQC_PENDING)]
    public void Every_non_terminal_source_can_force_cancel(MesPhase from)
        => Assert.True(WorkOrderStateMachine.IsForceablePhase(from, MesPhase.CANCELLED));
}
