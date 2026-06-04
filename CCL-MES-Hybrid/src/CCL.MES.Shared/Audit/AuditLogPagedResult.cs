namespace CCL.MES.Shared.Audit;

/// <summary>
/// P10.6e — paged response envelope for
/// <c>GET /api/v2/audit/log</c>. Same shape as the legacy
/// <c>PagedResult&lt;T&gt;</c> the rest of the Hybrid API returns
/// (mirror of <c>NpiPagedRaw&lt;T&gt;</c> on the wire) so the client
/// grid component contract is uniform.
/// </summary>
public sealed record AuditLogPagedResult
{
    public IReadOnlyList<AuditLogEntryDto> Items { get; init; } = Array.Empty<AuditLogEntryDto>();
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}
