namespace CCL.MES.Shared.Envelopes;

/// <summary>
/// Pagination envelope returned by list endpoints (e.g.
/// <c>GET /api/v2/specs?page=1&amp;pageSize=20</c>). Shape matches the legacy
/// Application <c>PagedResult&lt;T&gt;</c> record so callsites that swap
/// the underlying Application DTO for an HTTP DTO see identical field names.
/// </summary>
public sealed record PagedResponse<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }

    public int TotalPages =>
        PageSize <= 0 ? 0 : (int)Math.Ceiling((double)Total / PageSize);

    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
