using CCL.MES.Web.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Phase 9 T1 — Pure-testable surface of <see cref="SpecTrashPurgeService"/>:
/// the <see cref="PurgeOneOutcome"/> + <see cref="PurgeCycleStats"/> value
/// types, plus a documenting test for the Rule #1 ELIGIBILITY date-boundary
/// contract. EF query + audit emit + blob cleanup paths land in T2
/// integration once <c>IsolatedDbFixture</c> is in place.
///
/// <para>
/// <b>Why eligibility predicate is documenting-only at T1</b>: the prod
/// predicate runs inline as a SQL WHERE clause inside an EF query —
/// <c>SpecTrashPurgeService.cs:130-134</c>. Extracting it for direct
/// unit-test would be a prod refactor "for testability" which Henry's
/// hard constraint forbids. The boundary semantics are instead pinned
/// here in a test-local mirror; T2 will assert prod uses this exact
/// formula against a fresh /tmp SQLite.
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
        // Sanity — these are independent accumulators, not derived.
        Assert.Equal(5, s.EligibleCount);
        Assert.Equal(3, s.PurgedCount);
        Assert.Equal(1, s.SkippedCount);
        Assert.Equal(1, s.FailedCount);
        Assert.Equal(3 + 1 + 1, s.PurgedCount + s.SkippedCount + s.FailedCount);
        Assert.Equal(7, s.BlobsRemoved);
    }

    // ── Rule #1 ELIGIBILITY — strict-`<` boundary documenting test ────

    /// <summary>
    /// Mirror of the inline predicate at <c>SpecTrashPurgeService.cs:127-134</c>:
    /// <code>
    ///   var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);
    ///   eligible = row.IsTrashed
    ///           && row.TrashedAt.HasValue
    ///           && row.TrashedAt.Value &lt; cutoff;
    /// </code>
    /// Lives in the test project so a future prod change to <c>&lt;=</c>
    /// won't accidentally pass these assertions. T2 will assert prod uses
    /// THIS formula via an EF round-trip.
    /// </summary>
    private static bool IsEligibleMirror(bool isTrashed, DateTime? trashedAt, DateTime nowUtc, int retentionDays)
    {
        if (!isTrashed) return false;
        if (!trashedAt.HasValue) return false;
        var cutoff = nowUtc.AddDays(-retentionDays);
        return trashedAt.Value < cutoff;
    }

    [Fact]
    public void Eligibility_excludes_non_trashed_rows()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(IsEligibleMirror(
            isTrashed: false,
            trashedAt: now.AddDays(-100),     // very old, but not trashed
            nowUtc: now, retentionDays: 30));
    }

    [Fact]
    public void Eligibility_excludes_rows_with_null_trashedAt()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(IsEligibleMirror(
            isTrashed: true,
            trashedAt: null,                  // null → can't compute age
            nowUtc: now, retentionDays: 30));
    }

    [Fact]
    public void Eligibility_keeps_row_29_days_old_under_30_day_retention()
    {
        // 29-day-old row is YOUNGER than cutoff → cutoff > trashedAt → false
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(IsEligibleMirror(
            isTrashed: true,
            trashedAt: now.AddDays(-29),
            nowUtc: now, retentionDays: 30));
    }

    [Fact]
    public void Eligibility_keeps_row_exactly_30_days_old_strict_lt()
    {
        // EXACT cutoff boundary — strict `<` means "exactly N days" KEEPS.
        // This is the documented Henry rule ("Exactly-30-day boundary
        // keeps the row").
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.False(IsEligibleMirror(
            isTrashed: true,
            trashedAt: now.AddDays(-30),
            nowUtc: now, retentionDays: 30));
    }

    [Fact]
    public void Eligibility_purges_row_31_days_old_under_30_day_retention()
    {
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(IsEligibleMirror(
            isTrashed: true,
            trashedAt: now.AddDays(-31),
            nowUtc: now, retentionDays: 30));
    }

    [Fact]
    public void Eligibility_with_1_day_retention_purges_2_day_old_row()
    {
        // Floor scenario — retention=1 is the env-overridable minimum
        // (SpecTrashPurgeService ctor floors to 1).
        var now = new DateTime(2026, 6, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(IsEligibleMirror(
            isTrashed: true,
            trashedAt: now.AddDays(-2),
            nowUtc: now, retentionDays: 1));
    }
}
