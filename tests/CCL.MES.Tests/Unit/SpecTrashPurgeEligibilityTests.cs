using CCL.MES.Web.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Phase 9 T1 → T2a — Pure unit tests for the value types declared at
/// the bottom of <see cref="SpecTrashPurgeService"/>:
/// <see cref="PurgeOneOutcome"/> + <see cref="PurgeCycleStats"/>.
///
/// <para>
/// The Rule #1 ELIGIBILITY date-boundary semantics that lived here as
/// a documenting mirror in T1 were promoted to T2a integration —
/// <c>SpecTrashPurgeServiceTests</c> exercises the real EF predicate
/// against an isolated /tmp SQLite with seeded TrashedAt at -29d / -31d /
/// -45d. No mirror remains; the prod predicate is the only source.
/// </para>
/// </summary>
public class SpecTrashPurgeEligibilityTests
{
    // ── PurgeOneOutcome factory methods — all return Skipped = true ───

    [Fact]
    public void AlreadyGone_factory_marks_outcome_as_skipped()
    {
        var o = PurgeOneOutcome.AlreadyGone();
        Assert.True(o.Skipped);
        Assert.Equal(0, o.BlobsRemoved);
        Assert.Equal(0, o.BlobsFailed);
    }

    [Fact]
    public void Restored_factory_marks_outcome_as_skipped()
    {
        var o = PurgeOneOutcome.Restored();
        Assert.True(o.Skipped);
        Assert.Equal(0, o.BlobsRemoved);
        Assert.Equal(0, o.BlobsFailed);
    }

    [Fact]
    public void WasSkipped_factory_marks_outcome_as_skipped_for_wo_blocker_path()
    {
        // Used when active-WO defence-in-depth fires (Rule #2). Audit row
        // is written with skipped=true before this returns.
        var o = PurgeOneOutcome.WasSkipped();
        Assert.True(o.Skipped);
    }

    // ── PurgeCycleStats defaults — accumulator starts at zero ─────────

    [Fact]
    public void PurgeCycleStats_starts_with_all_counters_at_zero()
    {
        var s = new PurgeCycleStats();
        Assert.Equal(0, s.EligibleCount);
        Assert.Equal(0, s.PurgedCount);
        Assert.Equal(0, s.SkippedCount);
        Assert.Equal(0, s.FailedCount);
        Assert.Equal(0, s.BlobsRemoved);
        Assert.Equal(0, s.BlobsFailed);
        Assert.Equal(default(DateTime), s.CutoffUtc);
    }

    [Fact]
    public void PurgeCycleStats_counters_are_independently_mutable()
    {
        var s = new PurgeCycleStats
        {
            CutoffUtc      = new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc),
            EligibleCount  = 5,
            PurgedCount    = 3,
            SkippedCount   = 1,
            FailedCount    = 1,
            BlobsRemoved   = 7,
            BlobsFailed    = 0,
        };
        Assert.Equal(5, s.EligibleCount);
        Assert.Equal(3, s.PurgedCount);
        Assert.Equal(1, s.SkippedCount);
        Assert.Equal(1, s.FailedCount);
        Assert.Equal(3 + 1 + 1, s.PurgedCount + s.SkippedCount + s.FailedCount);
        Assert.Equal(7, s.BlobsRemoved);
    }
}
