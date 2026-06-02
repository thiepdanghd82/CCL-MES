using CCL.MES.Application.Services;
using Xunit;

namespace CCL.MES.Tests.Unit;

/// <summary>
/// Phase 9 T1 — Pure-record tests for <see cref="PagedResult{T}"/>.
///
/// <para>
/// <b>Scope note</b>: <see cref="PagingHelper.PageAsync{T}"/> itself calls
/// <c>q.CountAsync()</c> + <c>q.ToListAsync()</c> on an
/// <c>IQueryable&lt;T&gt;</c> — those EF Core extensions REQUIRE the
/// queryable to expose <c>IAsyncEnumerable</c>, which only EF-backed
/// queryables do. Testing the clamp + Skip/Take semantics therefore
/// needs a DbContext + real SQLite — landing in T2 alongside the
/// shared <c>IsolatedDbFixture</c>. T1 covers the only piece of
/// paging code that is purely synchronous: the
/// <see cref="PagedResult{T}.TotalPages"/> derivation.
/// </para>
/// </summary>
public class PagedResultTests
{
    [Theory]
    [InlineData(0,   10,  0)]    // empty result → 0 pages
    [InlineData(1,   10,  1)]    // 1 row in a 10-row page → 1 page
    [InlineData(10,  10,  1)]    // exactly fills 1 page
    [InlineData(11,  10,  2)]    // ceiling to 2
    [InlineData(100, 10, 10)]
    [InlineData(99,  10, 10)]    // last partial page counted
    [InlineData(50,  50,  1)]
    [InlineData(51,  50,  2)]
    public void TotalPages_ceils_total_over_pageSize(int total, int pageSize, int expected)
    {
        var pr = new PagedResult<int>(Items: System.Array.Empty<int>(), Total: total, Page: 1, PageSize: pageSize);
        Assert.Equal(expected, pr.TotalPages);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TotalPages_returns_zero_when_pageSize_non_positive(int pageSize)
    {
        // Guards a divide-by-zero in the UI — record property short-circuits.
        var pr = new PagedResult<int>(Items: System.Array.Empty<int>(), Total: 100, Page: 1, PageSize: pageSize);
        Assert.Equal(0, pr.TotalPages);
    }
}
