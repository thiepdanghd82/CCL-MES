namespace CCL.MES.Hybrid.Client.Grid;

/// <summary>
/// P10.5a — pure pagination math. Sits behind every NPI grid so Razor
/// pages can read <see cref="TotalPages"/> + clamp navigation without
/// duplicating arithmetic. Pure-C# + zero deps — testable from xUnit
/// without a Razor host.
/// </summary>
public static class GridPagination
{
    /// <summary>
    /// Total number of pages given a row count and page size. Always
    /// returns at least 1 so an empty result still shows "Trang 1 / 1"
    /// (less confusing than "Trang 1 / 0"). Negative or zero page size
    /// is treated as 1 to avoid divide-by-zero.
    /// </summary>
    public static int TotalPages(int total, int pageSize)
    {
        if (total <= 0) return 1;
        var size = pageSize <= 0 ? 1 : pageSize;
        return Math.Max(1, (int)Math.Ceiling((double)total / size));
    }

    /// <summary>Clamp a 1-based page index into [1, totalPages].</summary>
    public static int Clamp(int requested, int totalPages)
    {
        if (totalPages < 1) return 1;
        if (requested < 1) return 1;
        if (requested > totalPages) return totalPages;
        return requested;
    }

    /// <summary>
    /// True when the current page is at the upper bound — used to
    /// disable the "Next" button. Returns true when the data is
    /// missing so the UI doesn't accidentally advance off the end of
    /// a not-yet-fetched dataset.
    /// </summary>
    public static bool IsLastPage(int currentPage, int totalPages) =>
        currentPage >= totalPages;

    /// <summary>
    /// True when the current page is at the lower bound — used to
    /// disable the "Prev" button.
    /// </summary>
    public static bool IsFirstPage(int currentPage) => currentPage <= 1;
}
