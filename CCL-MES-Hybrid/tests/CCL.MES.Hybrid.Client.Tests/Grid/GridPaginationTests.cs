using CCL.MES.Hybrid.Client.Grid;

namespace CCL.MES.Hybrid.Client.Tests.Grid;

/// <summary>
/// P10.5a — pure pagination math coverage. Pages disabling Next/Prev
/// buttons + Clamp routing depend on this; regressing any of it would
/// surface immediately as a stuck pager on Catalyst.
/// </summary>
public sealed class GridPaginationTests
{
    [Theory]
    [InlineData(0, 20, 1)]        // empty → 1 page (never 0 — UX clarity)
    [InlineData(1, 20, 1)]        // single row → 1 page
    [InlineData(20, 20, 1)]       // exact one page
    [InlineData(21, 20, 2)]       // overflow → 2 pages
    [InlineData(100, 20, 5)]      // 5 even pages
    [InlineData(101, 20, 6)]      // 5 + remainder
    [InlineData(5000, 50, 100)]   // typical RawMaterials volume
    public void TotalPages_returns_expected(int total, int pageSize, int expected)
    {
        Assert.Equal(expected, GridPagination.TotalPages(total, pageSize));
    }

    [Theory]
    [InlineData(100, 0)]      // zero pageSize → fallback to 1, returns total
    [InlineData(100, -5)]     // negative pageSize → fallback to 1
    public void TotalPages_handles_invalid_pageSize_without_divide_by_zero(int total, int pageSize)
    {
        // Should not throw; should return something >= 1.
        var result = GridPagination.TotalPages(total, pageSize);
        Assert.True(result >= 1);
    }

    [Theory]
    [InlineData(0, 5, 1)]       // below 1 → clamp to 1
    [InlineData(-3, 5, 1)]      // negative → clamp to 1
    [InlineData(1, 5, 1)]       // in range
    [InlineData(3, 5, 3)]       // in range
    [InlineData(5, 5, 5)]       // upper bound
    [InlineData(6, 5, 5)]       // above max → clamp to max
    [InlineData(100, 5, 5)]     // way above max → clamp to max
    public void Clamp_pins_to_valid_range(int requested, int totalPages, int expected)
    {
        Assert.Equal(expected, GridPagination.Clamp(requested, totalPages));
    }

    [Fact]
    public void Clamp_zero_totalPages_returns_one()
    {
        // Defensive — totalPages 0 shouldn't happen (we floor to 1) but
        // if a caller passes it we should still return a sane page.
        Assert.Equal(1, GridPagination.Clamp(5, 0));
        Assert.Equal(1, GridPagination.Clamp(0, 0));
    }

    [Theory]
    [InlineData(1, 5, false)]    // not last
    [InlineData(4, 5, false)]    // not last
    [InlineData(5, 5, true)]     // last
    [InlineData(6, 5, true)]     // over end — still considered "last" so Next stays disabled
    public void IsLastPage(int currentPage, int totalPages, bool expected)
    {
        Assert.Equal(expected, GridPagination.IsLastPage(currentPage, totalPages));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(100, false)]
    [InlineData(0, true)]   // defensive lower bound
    public void IsFirstPage(int currentPage, bool expected)
    {
        Assert.Equal(expected, GridPagination.IsFirstPage(currentPage));
    }
}
