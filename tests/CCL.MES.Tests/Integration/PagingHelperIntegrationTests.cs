using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using CCL.MES.Tests.Integration._Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Phase 9 T2b — Integration tests for <see cref="PagingHelper.PageAsync{T}"/>
/// against a real EF queryable on isolated /tmp SQLite. Locks in the clamp
/// rules + Skip/Take/Count semantics that ship to every paged endpoint
/// (NPI tabs, Spec list, audit log, etc.).
///
/// <para>
/// Seeds 100 deterministic <see cref="WorkCenter"/> rows
/// (<c>WC-000</c>..<c>WC-099</c>) so page-sequence assertions are
/// reproducible without touching prod data. Uses
/// <see cref="IsolatedDbFixture"/> per-test (xUnit ctor-per-test) so each
/// fact has its own DB — no order dependence.
/// </para>
/// </summary>
public sealed class PagingHelperIntegrationTests : IDisposable
{
    private readonly IsolatedDbFixture _fx;

    public PagingHelperIntegrationTests()
    {
        _fx = new IsolatedDbFixture();
        SeedWorkCenters(100);
    }

    public void Dispose() => _fx.Dispose();

    // ── Happy path — first page ────────────────────────────────────────

    [Fact]
    public async Task First_page_returns_first_pageSize_items_in_order()
    {
        using var db = _fx.NewContext();
        var q = db.WorkCenters.AsNoTracking().OrderBy(w => w.Code);

        var result = await PagingHelper.PageAsync(q, page: 1, pageSize: 10);

        Assert.Equal(100, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal("WC-000", result.Items[0].Code);
        Assert.Equal("WC-009", result.Items[9].Code);
        Assert.Equal(10, result.TotalPages);
    }

    // ── Happy path — middle page (Skip/Take offset) ────────────────────

    [Fact]
    public async Task Middle_page_skips_correct_offset()
    {
        using var db = _fx.NewContext();
        var q = db.WorkCenters.AsNoTracking().OrderBy(w => w.Code);

        var result = await PagingHelper.PageAsync(q, page: 3, pageSize: 10);

        Assert.Equal(10, result.Items.Count);
        Assert.Equal("WC-020", result.Items[0].Code);
        Assert.Equal("WC-029", result.Items[9].Code);
    }

    // ── Happy path — last partial page ────────────────────────────────

    [Fact]
    public async Task Last_partial_page_returns_remainder()
    {
        using var db = _fx.NewContext();
        var q = db.WorkCenters.AsNoTracking().OrderBy(w => w.Code);

        var result = await PagingHelper.PageAsync(q, page: 4, pageSize: 30);

        Assert.Equal(100, result.Total);
        Assert.Equal(4, result.Page);
        Assert.Equal(30, result.PageSize);
        Assert.Equal(10, result.Items.Count);                  // 100 - 3*30 = 10
        Assert.Equal("WC-090", result.Items[0].Code);
        Assert.Equal("WC-099", result.Items[9].Code);
    }

    // ── Page out of bounds — beyond last → empty items but Total + Page returned ─

    [Fact]
    public async Task Page_beyond_last_returns_empty_items_with_total_still_correct()
    {
        using var db = _fx.NewContext();
        var q = db.WorkCenters.AsNoTracking().OrderBy(w => w.Code);

        var result = await PagingHelper.PageAsync(q, page: 999, pageSize: 50);

        Assert.Equal(100, result.Total);
        Assert.Equal(999, result.Page);                        // page is NOT clamped on upper bound
        Assert.Equal(50, result.PageSize);
        Assert.Empty(result.Items);
        Assert.Equal(2, result.TotalPages);
    }

    // ── Clamp — page < 1 ──────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task Page_below_one_is_clamped_to_one(int badPage)
    {
        using var db = _fx.NewContext();
        var q = db.WorkCenters.AsNoTracking().OrderBy(w => w.Code);

        var result = await PagingHelper.PageAsync(q, page: badPage, pageSize: 10);

        Assert.Equal(1, result.Page);                          // clamped
        Assert.Equal(10, result.Items.Count);
        Assert.Equal("WC-000", result.Items[0].Code);          // returns first page
    }

    // ── Clamp — pageSize < 1 → default 50 ─────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PageSize_below_one_is_clamped_to_default_50(int badSize)
    {
        using var db = _fx.NewContext();
        var q = db.WorkCenters.AsNoTracking().OrderBy(w => w.Code);

        var result = await PagingHelper.PageAsync(q, page: 1, pageSize: badSize);

        Assert.Equal(50, result.PageSize);                     // clamped
        Assert.Equal(50, result.Items.Count);
    }

    // ── Clamp — pageSize > 500 → default 50 ───────────────────────────

    [Theory]
    [InlineData(501)]
    [InlineData(1000)]
    [InlineData(int.MaxValue)]
    public async Task PageSize_above_max_is_clamped_to_default_50(int hugeSize)
    {
        using var db = _fx.NewContext();
        var q = db.WorkCenters.AsNoTracking().OrderBy(w => w.Code);

        var result = await PagingHelper.PageAsync(q, page: 1, pageSize: hugeSize);

        Assert.Equal(50, result.PageSize);                     // clamped
        Assert.Equal(50, result.Items.Count);
    }

    [Fact]
    public async Task PageSize_at_max_boundary_500_passes_through()
    {
        using var db = _fx.NewContext();
        var q = db.WorkCenters.AsNoTracking().OrderBy(w => w.Code);

        var result = await PagingHelper.PageAsync(q, page: 1, pageSize: 500);

        // Boundary: < 1 OR > 500 → clamp. Exactly 500 passes.
        Assert.Equal(500, result.PageSize);
        Assert.Equal(100, result.Items.Count);                 // only 100 rows exist
    }

    // ── Empty set ─────────────────────────────────────────────────────

    [Fact]
    public async Task Empty_set_returns_zero_total_zero_pages_empty_items()
    {
        using var db = _fx.NewContext();
        // Filter to a non-matching code.
        var q = db.WorkCenters.AsNoTracking().Where(w => w.Code == "WC-DOES-NOT-EXIST");

        var result = await PagingHelper.PageAsync(q, page: 1, pageSize: 10);

        Assert.Equal(0, result.Total);
        Assert.Equal(1, result.Page);
        Assert.Equal(10, result.PageSize);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalPages);
    }

    // ── EF Where filter compose ──────────────────────────────────────

    [Fact]
    public async Task Filter_applied_before_pagination_reflects_in_total_and_items()
    {
        using var db = _fx.NewContext();
        // Filter half the set.
        var q = db.WorkCenters.AsNoTracking()
            .Where(w => string.Compare(w.Code, "WC-050") < 0)  // codes < WC-050 → 50 rows
            .OrderBy(w => w.Code);

        var result = await PagingHelper.PageAsync(q, page: 1, pageSize: 20);

        Assert.Equal(50, result.Total);                        // pre-filter total
        Assert.Equal(20, result.Items.Count);
        Assert.Equal("WC-000", result.Items[0].Code);
        Assert.Equal("WC-019", result.Items[19].Code);
        Assert.Equal(3, result.TotalPages);                    // ceil(50/20)
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private void SeedWorkCenters(int count)
    {
        using var db = _fx.NewContext();
        // Skip the IsolatedDbFixture's existing seed rows by using a code
        // namespace ("WC-NNN") that won't collide with prod-seeded WCs.
        var batch = Enumerable.Range(0, count)
            .Select(i => new WorkCenter { Code = $"WC-{i:D3}", Description = $"WC #{i}" })
            .ToList();
        db.WorkCenters.AddRange(batch);
        db.SaveChanges();
    }
}
