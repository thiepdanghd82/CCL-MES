using CCL.MES.Hybrid.Client.RecentScans;
using CCL.MES.Shared.RecentScans;

namespace CCL.MES.Hybrid.Client.Tests.RecentScans;

/// <summary>
/// P10.6f — ring-buffer + dedupe + Changed-event contract for the
/// in-memory <see cref="IRecentScansService"/> impl. The Preferences-
/// backed MAUI impl reuses the same <see cref="InMemoryRecentScansService.ApplyRecord"/>
/// helper so these tests cover both code paths.
///
/// Eight guards:
///   1. Empty store returns empty list.
///   2. Single record returns 1 entry.
///   3. Order is newest-first.
///   4. Same (Code, Context) dedupes — count stays equal.
///   5. Dedupe promotes the dupe to the head (newest scan wins).
///   6. Different Context for same Code keeps both rows.
///   7. Capacity (MaxCapacity=25) caps the buffer.
///   8. Clear empties + fires Changed.
/// </summary>
public sealed class InMemoryRecentScansServiceTests
{
    private static RecentScanEntry Entry(string code, string context = "wo-accept",
        DateTime? at = null, bool resolved = true) =>
        new()
        {
            Code = code,
            Format = "Code128",
            Context = context,
            ScannedAt = at ?? DateTime.UtcNow,
            ResolvedDisplay = code,
            ResolvedSubtitle = resolved ? "PRD · CUST" : null,
            Resolved = resolved,
        };

    [Fact]
    public void Empty_store_returns_empty_list()
    {
        var sut = new InMemoryRecentScansService();
        Assert.Empty(sut.GetRecent());
    }

    [Fact]
    public void Single_record_returns_one_entry()
    {
        var sut = new InMemoryRecentScansService();
        sut.Record(Entry("WO-1"));
        Assert.Single(sut.GetRecent());
    }

    [Fact]
    public void Order_is_newest_first()
    {
        var sut = new InMemoryRecentScansService();
        sut.Record(Entry("WO-1"));
        sut.Record(Entry("WO-2"));
        sut.Record(Entry("WO-3"));

        var recent = sut.GetRecent();
        Assert.Equal(new[] { "WO-3", "WO-2", "WO-1" }, recent.Select(e => e.Code));
    }

    [Fact]
    public void Duplicate_code_and_context_collapses_to_one_row()
    {
        var sut = new InMemoryRecentScansService();
        var t0 = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc);
        var t1 = t0.AddMinutes(1);
        var t2 = t1.AddMinutes(1);
        sut.Record(Entry("WO-1", at: t0));
        sut.Record(Entry("WO-2", at: t1));
        sut.Record(Entry("WO-1", at: t2));  // dupe of first

        var recent = sut.GetRecent();
        Assert.Equal(2, recent.Count);
        // Dupe must promote to head with the LATEST timestamp.
        Assert.Equal("WO-1", recent[0].Code);
        Assert.Equal(t2, recent[0].ScannedAt);
        Assert.Equal("WO-2", recent[1].Code);
        Assert.Equal(t1, recent[1].ScannedAt);
    }

    [Fact]
    public void Same_code_different_context_keeps_both_rows()
    {
        var sut = new InMemoryRecentScansService();
        sut.Record(Entry("WO-1", context: "wo-accept"));
        sut.Record(Entry("WO-1", context: "qc-capture"));

        var recent = sut.GetRecent();
        Assert.Equal(2, recent.Count);
        Assert.Contains(recent, e => e.Context == "wo-accept");
        Assert.Contains(recent, e => e.Context == "qc-capture");
    }

    [Fact]
    public void Capacity_cap_drops_oldest_when_exceeded()
    {
        var sut = new InMemoryRecentScansService();
        // Push MaxCapacity + 3 distinct entries; expect head is the
        // latest + the 3 oldest dropped.
        const int extra = 3;
        for (var i = 0; i < IRecentScansService.MaxCapacity + extra; i++)
            sut.Record(Entry($"WO-{i:000}"));

        var recent = sut.GetRecent(IRecentScansService.MaxCapacity);
        Assert.Equal(IRecentScansService.MaxCapacity, recent.Count);
        // Newest first — WO-{cap+extra-1} at index 0.
        Assert.Equal($"WO-{IRecentScansService.MaxCapacity + extra - 1:000}", recent[0].Code);
        // The {extra} smallest indices got evicted — no row with i < extra.
        for (var i = 0; i < extra; i++)
            Assert.DoesNotContain(recent, e => e.Code == $"WO-{i:000}");
    }

    [Fact]
    public void Clear_empties_the_store()
    {
        var sut = new InMemoryRecentScansService();
        sut.Record(Entry("WO-1"));
        sut.Record(Entry("WO-2"));
        sut.Clear();
        Assert.Empty(sut.GetRecent());
    }

    [Fact]
    public void Record_and_clear_both_fire_Changed_event()
    {
        var sut = new InMemoryRecentScansService();
        var hits = 0;
        sut.Changed += (_, _) => hits++;

        sut.Record(Entry("WO-1"));
        Assert.Equal(1, hits);

        sut.Record(Entry("WO-2"));
        Assert.Equal(2, hits);

        sut.Clear();
        Assert.Equal(3, hits);
    }

    [Fact]
    public void Clear_on_empty_store_does_not_fire_Changed()
    {
        var sut = new InMemoryRecentScansService();
        var hits = 0;
        sut.Changed += (_, _) => hits++;

        sut.Clear();
        Assert.Equal(0, hits);
    }

    [Fact]
    public void GetRecent_with_smaller_count_returns_only_that_many()
    {
        var sut = new InMemoryRecentScansService();
        for (var i = 0; i < 10; i++) sut.Record(Entry($"WO-{i}"));

        Assert.Equal(3, sut.GetRecent(3).Count);
        Assert.Equal(IRecentScansService.DefaultDisplayCount, sut.GetRecent().Count);
    }

    [Fact]
    public void Record_with_blank_code_is_a_noop()
    {
        var sut = new InMemoryRecentScansService();
        sut.Record(Entry("   "));
        Assert.Empty(sut.GetRecent());
    }
}
