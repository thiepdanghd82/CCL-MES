using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7a-2.2 + P10.7e-1 Q1 — exhaustive coverage of
/// <see cref="WorkOrderStateMachine.IsForceablePhase"/> against the
/// 169-cell §3.1 grid (was 144 pre-7e). The forceable set grew from
/// 11 to 13 cells:
///   1. SETTING       → PREPRESS                (the §8.1 archetype)
///   2. NEW           → CANCELLED
///   3. PREPRESS      → CANCELLED
///   4. SETTING       → CANCELLED
///   5. IPQC_WAIT     → CANCELLED
///   6. QA_PENDING    → CANCELLED
///   7. IPQC_APPROVED → CANCELLED
///   8. RUNNING       → CANCELLED
///   9. PAUSED        → CANCELLED
///  10. FQC_PENDING   → CANCELLED
///  11. OQC_PENDING   → CANCELLED
///  12. DONE          → CANCELLED              (Q1 NEW — DONE narrowed
///                                              to transient; admin can
///                                              still cancel a stuck DONE row)
///  13. OQC_PENDING   → DONE                   (Q1 NEW — legacy admin
///                                              bypass when closing a
///                                              stuck-at-OQC row without
///                                              the 3-sig SHIPPED path)
/// Every other cell (156 of 169) is non-forceable. Henry's adj #4 +
/// 7e-1 Q1 require explicit assertion for the narrowed terminal-source
/// rule (SHIPPED + CANCELLED only; DONE is transient).
/// </summary>
public sealed class WorkOrderStateMachineIsForceableTests
{
    private static readonly MesPhase[] AllPhases =
    {
        MesPhase.NEW, MesPhase.PREPRESS, MesPhase.SETTING, MesPhase.IPQC_WAIT,
        MesPhase.QA_PENDING, MesPhase.IPQC_APPROVED, MesPhase.RUNNING, MesPhase.PAUSED,
        MesPhase.FQC_PENDING, MesPhase.OQC_PENDING, MesPhase.DONE, MesPhase.CANCELLED,
        MesPhase.SHIPPED,  // P10.7e-1 Q1
        MesPhase.SPLIT,    // P11-1 — fork umbrella (non-terminal → force-cancel-able)
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
        // P10.7e-1 Q1 — DONE narrowed to transient: DONE → CANCELLED
        // becomes RecoveryOnly so admin can cancel a stuck-at-DONE row.
        (MesPhase.DONE,          MesPhase.CANCELLED),
        // P10.7e-1 Q1 — OQC_PENDING → DONE shifts from RequiresSignoff
        // (7d) to RecoveryOnly (7e-1) for legacy admin bypass.
        (MesPhase.OQC_PENDING,   MesPhase.DONE),
        // P11-1 — SPLIT is non-terminal → force-cancel-able (admin can
        // cancel a stuck-mid-fork WO).
        (MesPhase.SPLIT,         MesPhase.CANCELLED),
    };

    public static IEnumerable<object[]> All169Cells()
    {
        foreach (var from in AllPhases)
            foreach (var to in AllPhases)
                yield return new object[] { from, to };
    }

    [Theory]
    [MemberData(nameof(All169Cells))]
    public void IsForceablePhase_classifies_every_cell_per_contract(MesPhase from, MesPhase to)
    {
        var expected = ForceableCells.Contains((from, to));
        var actual = WorkOrderStateMachine.IsForceablePhase(from, to);
        Assert.Equal(expected, actual);
    }

    // ── Meta: matrix coverage + count totals ──────────────────────

    [Fact]
    public void Matrix_covers_exactly_196_cells_once()
    {
        // P11-1 — 14 × 14 = 196 (was 13 × 13 = 169 in 7e-1 after SHIPPED).
        var seen = new HashSet<(MesPhase, MesPhase)>();
        foreach (var pair in All169Cells())
        {
            var key = ((MesPhase)pair[0], (MesPhase)pair[1]);
            Assert.True(seen.Add(key), $"Duplicate cell in matrix: {key}");
        }
        Assert.Equal(196, seen.Count);
    }

    [Fact]
    public void Forceable_set_has_exactly_fourteen_cells_and_zero_overlap_with_blocked()
    {
        // P10.7e-1 Q1 — 11 → 13 forceable cells (added DONE → CANCELLED
        // + OQC_PENDING → DONE). P11-1 — 13 → 14 (added SPLIT → CANCELLED).
        var forceable = 0;
        foreach (var pair in All169Cells())
        {
            var from = (MesPhase)pair[0];
            var to = (MesPhase)pair[1];
            if (WorkOrderStateMachine.IsForceablePhase(from, to))
                forceable++;
        }
        Assert.Equal(14, forceable);
        Assert.Equal(ForceableCells.Length, forceable);
    }

    // ── Henry's adj #4 targeted asserts ────────────────────────────

    // P10.7e-1 Q1 — DONE narrowed to TRANSIENT (no longer fully
    // terminal). DONE → CANCELLED is now RecoveryOnly + forceable,
    // so this assertion narrows to "DONE → anything-except-CANCELLED".
    // SHIPPED is the new fully-terminal source.
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
    [InlineData(MesPhase.SHIPPED)]
    public void From_DONE_is_never_forceable_except_to_CANCELLED(MesPhase to)
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
    [InlineData(MesPhase.SHIPPED)]
    public void From_SHIPPED_is_never_forceable_regardless_of_target(MesPhase to)
        => Assert.False(WorkOrderStateMachine.IsForceablePhase(MesPhase.SHIPPED, to));

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

    // P10.7e-1 Q1 — OQC_PENDING → DONE is now RecoveryOnly + forceable
    // (was RequiresSignoff in 7d). The "→ DONE never forceable" rule
    // narrows to "→ DONE never forceable EXCEPT from OQC_PENDING".
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
    [InlineData(MesPhase.DONE)]
    [InlineData(MesPhase.CANCELLED)]
    [InlineData(MesPhase.SHIPPED)]
    public void Target_DONE_is_never_forceable_except_from_OQC_PENDING(MesPhase from)
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
    [InlineData(MesPhase.SHIPPED)]
    public void Target_SHIPPED_is_never_forceable_regardless_of_source(MesPhase from)
        => Assert.False(WorkOrderStateMachine.IsForceablePhase(from, MesPhase.SHIPPED));

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

    // ── §8.1 archetype + 7e-1 Q1 expansion: 2 non-CANCEL forceable cells ──

    [Fact]
    public void Setting_to_prepress_and_oqc_to_done_are_the_only_non_cancel_forceable_cells()
    {
        // Sanity: the §8.1 "operator A left mid-shift" example is forceable.
        Assert.True(WorkOrderStateMachine.IsForceablePhase(MesPhase.SETTING, MesPhase.PREPRESS));
        // P10.7e-1 Q1 — legacy OQC bypass to DONE is forceable (admin
        // can close a stuck-at-OQC row without the 3-sig SHIPPED path).
        Assert.True(WorkOrderStateMachine.IsForceablePhase(MesPhase.OQC_PENDING, MesPhase.DONE));

        // And: no other non-cancel target is forceable from any source.
        foreach (var from in AllPhases)
        {
            foreach (var to in AllPhases)
            {
                if (to == MesPhase.CANCELLED) continue;
                if (from == MesPhase.SETTING && to == MesPhase.PREPRESS) continue;
                if (from == MesPhase.OQC_PENDING && to == MesPhase.DONE) continue;
                Assert.False(WorkOrderStateMachine.IsForceablePhase(from, to),
                    $"Non-CANCEL forceable cell found outside SETTING→PREPRESS / OQC_PENDING→DONE: {from} → {to}");
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
    // P10.7e-1 Q1 — DONE is now non-terminal (transient), so it joins
    // the set of sources that can force-cancel a stuck row.
    [InlineData(MesPhase.DONE)]
    public void Every_non_terminal_source_can_force_cancel(MesPhase from)
        => Assert.True(WorkOrderStateMachine.IsForceablePhase(from, MesPhase.CANCELLED));
}
