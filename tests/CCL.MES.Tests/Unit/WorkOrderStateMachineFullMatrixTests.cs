using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7a-1.4 + P10.7e-1 Q1 — full 13×13 transition-matrix coverage
/// per <c>docs/P10.7-WO-STATE-CONTRACT.md</c> §3.1. 7e-1 expanded the
/// grid from 12 × 12 = 144 cells to 13 × 13 = 169 cells by adding
/// the SHIPPED terminal phase (Q1). 25 new cells classified:
///   • 13 cells from SHIPPED-row: ALL Blocked (terminal source).
///   • 12 cells from SHIPPED-column: NEW→SHIPPED through DONE→SHIPPED
///     all Blocked EXCEPT OQC_PENDING→SHIPPED (RequiresSignoff per
///     Q1 + Q5 3-sig terminal pass).
///   • The pre-existing DONE row is REINTERPRETED — DONE narrowed
///     from fully terminal to transient: DONE→FQC_PENDING becomes
///     RequiresCondition (Q1 cascade); DONE→CANCELLED becomes
///     RecoveryOnly (was Blocked); other DONE outgoing cells stay
///     Blocked.
///   • OQC_PENDING→DONE shifts from RequiresSignoff (7d) to
///     RecoveryOnly (7e-1 admin/sys bypass per Q1 contract).
///
/// The 7a-1.1 <see cref="WorkOrderStateMachineCanonicalTests"/>
/// covers a happy-path subset; this file locks every cell against
/// the contract table.
///
/// Authority on each cell's classification:
///   - "Blocked" / "Allowed" / "RequiresCondition" / "RequiresSignoff"
///     / "RecoveryOnly" all come from the contract's §3.1 grid.
///   - The diagonal (from == to) is ALWAYS blocked per §3.1 footer.
///   - Terminal-source rule: SHIPPED + CANCELLED never produce a
///     non-blocked outgoing edge (DONE is transient per Q1).
///   - Any non-terminal source → CANCELLED is recovery-only per §3.2
///     "any → CANCELLED (non-terminal source)".
/// </summary>
public sealed class WorkOrderStateMachineFullMatrixTests
{
    [Theory]
    [MemberData(nameof(AllCells))]
    public void Every_cell_matches_contract_classification(
        MesPhase from, MesPhase to, MesTransitionKind expected)
    {
        var actual = WorkOrderStateMachine.ClassifyTransition(from, to);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Matrix_covers_every_pair_exactly_once()
    {
        // Sanity guard: 13 × 13 = 169 cells (P10.7e-1 Q1 — added SHIPPED).
        var phases = Enum.GetValues<MesPhase>();
        Assert.Equal(13, phases.Length);

        var seen = new HashSet<(MesPhase, MesPhase)>();
        foreach (var row in AllCellsData())
        {
            var from = (MesPhase)row[0];
            var to   = (MesPhase)row[1];
            Assert.True(seen.Add((from, to)), $"Duplicate cell ({from}, {to})");
        }
        Assert.Equal(169, seen.Count);
    }

    [Fact]
    public void Allowed_and_requires_cells_total_matches_contract_count()
    {
        // Contract §3.1 footer: 31 non-blocked cells (Allowed +
        // RequiresCondition + RequiresSignoff + RecoveryOnly) on the
        // 169-cell grid. Excludes diagonals + terminal-source rows
        // (SHIPPED + CANCELLED) + the blocked default fall-through.
        var nonBlocked = AllCellsData()
            .Where(r => (MesTransitionKind)r[2] != MesTransitionKind.Blocked)
            .ToList();
        var allowed   = nonBlocked.Count(r => (MesTransitionKind)r[2] == MesTransitionKind.Allowed);
        var condition = nonBlocked.Count(r => (MesTransitionKind)r[2] == MesTransitionKind.RequiresCondition);
        var signoff   = nonBlocked.Count(r => (MesTransitionKind)r[2] == MesTransitionKind.RequiresSignoff);
        var recovery  = nonBlocked.Count(r => (MesTransitionKind)r[2] == MesTransitionKind.RecoveryOnly);

        Assert.Equal(4, allowed);       // 4 unconditional happy edges (unchanged from 7d)
        // P10.7c-1 Q6 — adds PAUSED → FQC_PENDING as condition.
        // P10.7e-1 Q1 — adds DONE → FQC_PENDING as condition (transient DONE).
        Assert.Equal(5, condition);     // PREPRESS→SETTING + RUNNING→FQC + RUNNING→PAUSED + PAUSED→FQC + DONE→FQC
        // P10.7e-1 Q1 — replaces OQC_PENDING → DONE (was signoff) with
        // OQC_PENDING → SHIPPED. Count stays at 9 (target shifted only).
        Assert.Equal(9, signoff);       // IPQC × 3, QA × 2, FQC × 2, OQC × 2 (SHIPPED + FQC re-loop)
        // P10.7e-1 Q1 — recovery count grows: (a) DONE is no longer
        // terminal-blocked, so DONE → CANCELLED becomes RecoveryOnly
        // (+1); (b) OQC_PENDING → DONE retained as RecoveryOnly for
        // legacy admin/sys bypass (was signoff in 7d) (+1). Total
        // non-terminal sources → CANCELLED = 11 (incl. DONE). Plus
        // SETTING → PREPRESS = 1. Plus OQC_PENDING → DONE = 1. Total
        // recovery: 13.
        Assert.Equal(13, recovery);
        Assert.Equal(31, nonBlocked.Count);
    }

    public static IEnumerable<object[]> AllCells => AllCellsData();

    private static IEnumerable<object[]> AllCellsData()
    {
        foreach (var from in Enum.GetValues<MesPhase>())
        {
            foreach (var to in Enum.GetValues<MesPhase>())
            {
                yield return new object[] { from, to, Expected(from, to) };
            }
        }
    }

    /// <summary>
    /// Authoritative cell classification, mirrored from the contract
    /// §3.1 grid. ANY edit to this method must be paired with a
    /// matching edit to the contract doc.
    /// </summary>
    private static MesTransitionKind Expected(MesPhase from, MesPhase to)
    {
        // Diagonal — always blocked.
        if (from == to) return MesTransitionKind.Blocked;

        // P10.7e-1 Q1 — terminal-source rule narrowed: SHIPPED and
        // CANCELLED never produce a non-blocked outgoing edge. DONE
        // narrowed to transient (Q1) so its single non-blocked
        // outgoing edge (DONE → FQC_PENDING) is in the switch below.
        if (from is MesPhase.CANCELLED or MesPhase.SHIPPED) return MesTransitionKind.Blocked;

        // Any non-terminal source → CANCELLED is recovery-only.
        if (to == MesPhase.CANCELLED) return MesTransitionKind.RecoveryOnly;

        // Explicit happy-path edges from contract §3.1.
        return (from, to) switch
        {
            (MesPhase.NEW, MesPhase.PREPRESS) => MesTransitionKind.Allowed,

            (MesPhase.PREPRESS, MesPhase.SETTING) => MesTransitionKind.RequiresCondition,

            (MesPhase.SETTING, MesPhase.IPQC_WAIT) => MesTransitionKind.Allowed,
            (MesPhase.SETTING, MesPhase.PREPRESS) => MesTransitionKind.RecoveryOnly,

            (MesPhase.IPQC_WAIT, MesPhase.IPQC_APPROVED) => MesTransitionKind.RequiresSignoff,
            (MesPhase.IPQC_WAIT, MesPhase.QA_PENDING)    => MesTransitionKind.RequiresSignoff,
            (MesPhase.IPQC_WAIT, MesPhase.PREPRESS)      => MesTransitionKind.RequiresSignoff,

            (MesPhase.QA_PENDING, MesPhase.IPQC_APPROVED) => MesTransitionKind.RequiresSignoff,
            (MesPhase.QA_PENDING, MesPhase.PREPRESS)      => MesTransitionKind.RequiresSignoff,

            (MesPhase.IPQC_APPROVED, MesPhase.RUNNING) => MesTransitionKind.Allowed,

            (MesPhase.RUNNING, MesPhase.PAUSED)       => MesTransitionKind.RequiresCondition,
            (MesPhase.RUNNING, MesPhase.FQC_PENDING)  => MesTransitionKind.RequiresCondition,

            (MesPhase.PAUSED, MesPhase.RUNNING) => MesTransitionKind.Allowed,
            // P10.7c-1 amendment Q6 — Finish from PAUSED.
            (MesPhase.PAUSED, MesPhase.FQC_PENDING) => MesTransitionKind.RequiresCondition,

            // P10.7e-1 Q1 — DONE narrowed to transient. Controller's
            // Finish handler stamps DONE then auto-cascades to
            // FQC_PENDING in the same SaveChanges.
            (MesPhase.DONE, MesPhase.FQC_PENDING) => MesTransitionKind.RequiresCondition,

            (MesPhase.FQC_PENDING, MesPhase.OQC_PENDING) => MesTransitionKind.RequiresSignoff,
            (MesPhase.FQC_PENDING, MesPhase.PREPRESS)    => MesTransitionKind.RequiresSignoff,

            // P10.7e-1 Q1 — OQC Pass NOW advances to SHIPPED (3-sig
            // gate). Legacy OQC_PENDING → DONE retained as RecoveryOnly
            // for admin/sys bypass.
            (MesPhase.OQC_PENDING, MesPhase.SHIPPED)      => MesTransitionKind.RequiresSignoff,
            (MesPhase.OQC_PENDING, MesPhase.DONE)         => MesTransitionKind.RecoveryOnly,
            // Signed-§10 answer: OQC_REJECT → FQC_PENDING (not full rework).
            (MesPhase.OQC_PENDING, MesPhase.FQC_PENDING)  => MesTransitionKind.RequiresSignoff,

            _ => MesTransitionKind.Blocked,
        };
    }
}
