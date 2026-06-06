using CCL.MES.Domain.StateMachine;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// P10.7a-1.4 — full 12×12 transition-matrix coverage per
/// <c>docs/P10.7-WO-STATE-CONTRACT.md</c> §3.1. The 7a-1.1
/// <see cref="WorkOrderStateMachineCanonicalTests"/> covers a
/// happy-path subset; this file locks every one of the 144 cells
/// (including the 124 blocked cells that the subset doesn't visit)
/// against the contract table.
///
/// The data source is a single <see cref="ExpectedMatrix"/> dictionary
/// so a future contract amendment edits one place and the test
/// regenerates the full grid automatically.
///
/// Authority on each cell's classification:
///   - "Blocked" / "Allowed" / "RequiresCondition" / "RequiresSignoff"
///     / "RecoveryOnly" all come from the contract's §3.1 grid.
///   - The diagonal (from == to) is ALWAYS blocked per §3.1 footer.
///   - Terminal-source rule: DONE and CANCELLED never produce a
///     non-blocked outgoing edge.
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
        // Sanity guard: 12 × 12 = 144 cells.
        var phases = Enum.GetValues<MesPhase>();
        Assert.Equal(12, phases.Length);

        var seen = new HashSet<(MesPhase, MesPhase)>();
        foreach (var row in AllCellsData())
        {
            var from = (MesPhase)row[0];
            var to   = (MesPhase)row[1];
            Assert.True(seen.Add((from, to)), $"Duplicate cell ({from}, {to})");
        }
        Assert.Equal(144, seen.Count);
    }

    [Fact]
    public void Allowed_and_requires_cells_total_matches_contract_count()
    {
        // Contract §3.1 footer: 20 non-blocked cells (Allowed +
        // RequiresCondition + RequiresSignoff + RecoveryOnly).
        // Excludes diagonals + terminal-source rows.
        var nonBlocked = AllCellsData()
            .Where(r => (MesTransitionKind)r[2] != MesTransitionKind.Blocked)
            .ToList();
        // Adjust if the contract is amended: 20 happy + 10 recovery
        // (any non-terminal → CANCELLED) + 1 SETTING → PREPRESS abort
        // = 31 recovery cells; happy edges from the matrix subset = 18
        // (8 Allowed + 3 RequiresCondition + 9 RequiresSignoff). Total
        // computed here for self-documenting failure messages.
        var allowed   = nonBlocked.Count(r => (MesTransitionKind)r[2] == MesTransitionKind.Allowed);
        var condition = nonBlocked.Count(r => (MesTransitionKind)r[2] == MesTransitionKind.RequiresCondition);
        var signoff   = nonBlocked.Count(r => (MesTransitionKind)r[2] == MesTransitionKind.RequiresSignoff);
        var recovery  = nonBlocked.Count(r => (MesTransitionKind)r[2] == MesTransitionKind.RecoveryOnly);

        Assert.Equal(4, allowed);       // 4 unconditional happy edges
        // P10.7c-1 amendment Q6 — adds PAUSED→FQC_PENDING as condition.
        Assert.Equal(4, condition);     // PREPRESS→SETTING + RUNNING→FQC + RUNNING→PAUSED + PAUSED→FQC
        Assert.Equal(9, signoff);       // IPQC × 3, QA × 2, FQC × 2, OQC × 2
        Assert.Equal(11, recovery);     // 10 × (non-terminal → CANCELLED) + 1 SETTING → PREPRESS
        Assert.Equal(28, nonBlocked.Count);
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

        // Terminal-source rule.
        if (from is MesPhase.DONE or MesPhase.CANCELLED) return MesTransitionKind.Blocked;

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
            // P10.7c-1 amendment Q6 — Finish from PAUSED. Controller
            // stamps active WoPauseEvent.EndedAt = now before transition
            // so OEE math stays consistent.
            (MesPhase.PAUSED, MesPhase.FQC_PENDING) => MesTransitionKind.RequiresCondition,

            (MesPhase.FQC_PENDING, MesPhase.OQC_PENDING) => MesTransitionKind.RequiresSignoff,
            (MesPhase.FQC_PENDING, MesPhase.PREPRESS)    => MesTransitionKind.RequiresSignoff,

            // Signed-§10 answer: OQC_REJECT → FQC_PENDING (not full rework).
            (MesPhase.OQC_PENDING, MesPhase.DONE)         => MesTransitionKind.RequiresSignoff,
            (MesPhase.OQC_PENDING, MesPhase.FQC_PENDING)  => MesTransitionKind.RequiresSignoff,

            _ => MesTransitionKind.Blocked,
        };
    }
}
