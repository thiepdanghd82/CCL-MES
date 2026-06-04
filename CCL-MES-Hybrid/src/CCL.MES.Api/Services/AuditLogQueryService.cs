using CCL.MES.Application;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared.Audit;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>
/// P10.6e — read-side wrapper for the AuditLogs table consumed by the
/// Hybrid <c>SettingsAuditLog</c> page + the CSV/XLSX export endpoints.
///
/// Mirrors the legacy <c>CCL.MES.Web.Services.AuditLogService</c> filter
/// shape exactly (search / action / actor / from / to). The API project
/// does NOT reference CCL.MES.Web, so the read service is ported here
/// (same pattern as <c>UserProfileService</c> + <c>BackupApiService</c>
/// in earlier P10.6 PRs).
///
/// The Application + Infrastructure layers already register the CSV +
/// XLSX exporters as singletons via <c>AddInfrastructure()</c>; the
/// API controller resolves them straight from DI.
///
/// HARD CAP on export = 100,000 rows (matches PHASE9 plan §3 Q3 +
/// the legacy controller default). Over-cap returns the matched-row
/// count so the operator can narrow the filter via the
/// <c>backup.too_large</c>-style RFC-7807 envelope.
/// </summary>
public sealed class AuditLogQueryService
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;
    public const int ExportHardCap = 100_000;

    private readonly IMesDbContext _db;

    public AuditLogQueryService(IMesDbContext db) => _db = db;

    public async Task<AuditLogPagedResult> ListAsync(
        string? search,
        string? action,
        string? actor,
        DateTime? fromUtc,
        DateTime? toUtc,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > MaxPageSize ? DefaultPageSize : pageSize;

        var q = BuildFilteredQuery(search, action, actor, fromUtc, toUtc);

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => ToDto(x))
            .ToListAsync(ct);

        return new AuditLogPagedResult
        {
            Items = rows,
            Total = total,
            Page = page,
            PageSize = pageSize,
        };
    }

    public async Task<IReadOnlyList<string>> DistinctActionsAsync(CancellationToken ct)
    {
        return await _db.AuditLogs.AsNoTracking()
            .Select(x => x.Action)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(ct);
    }

    public async Task<ExportListResult> ListForExportAsync(
        string? search,
        string? action,
        string? actor,
        DateTime? fromUtc,
        DateTime? toUtc,
        CancellationToken ct)
    {
        var q = BuildFilteredQuery(search, action, actor, fromUtc, toUtc);
        var total = await q.CountAsync(ct);
        if (total > ExportHardCap)
            return new ExportListResult(Array.Empty<AuditLog>(), total, Exceeded: true);

        var rows = await q.OrderByDescending(x => x.Id).ToListAsync(ct);
        return new ExportListResult(rows, total, Exceeded: false);
    }

    // ── helpers ─────────────────────────────────────────────────────

    private IQueryable<AuditLog> BuildFilteredQuery(
        string? search, string? action, string? actor,
        DateTime? fromUtc, DateTime? toUtc)
    {
        var q = _db.AuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
        {
            var a = action.Trim();
            q = q.Where(x => x.Action == a);
        }
        if (!string.IsNullOrWhiteSpace(actor))
        {
            var u = actor.Trim();
            q = q.Where(x => EF.Functions.Like(x.ActorUsername, $"%{u}%"));
        }
        if (fromUtc.HasValue)
        {
            var f = fromUtc.Value.ToUniversalTime();
            q = q.Where(x => x.Timestamp >= f);
        }
        if (toUtc.HasValue)
        {
            var t = toUtc.Value.ToUniversalTime();
            q = q.Where(x => x.Timestamp <= t);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => EF.Functions.Like(x.ActorUsername, $"%{s}%")
                || EF.Functions.Like(x.Action, $"%{s}%")
                || (x.TargetType != null && EF.Functions.Like(x.TargetType, $"%{s}%"))
                || (x.TargetId != null && EF.Functions.Like(x.TargetId, $"%{s}%"))
                || (x.Detail != null && EF.Functions.Like(x.Detail, $"%{s}%")));
        }
        return q;
    }

    private static AuditLogEntryDto ToDto(AuditLog e) => new()
    {
        Id = e.Id,
        TimestampUtc = e.Timestamp,
        ActorUsername = e.ActorUsername,
        ActorRole = e.ActorRole,
        Action = e.Action,
        TargetType = e.TargetType,
        TargetId = e.TargetId,
        Detail = e.Detail,
        IpAddress = e.IpAddress,
        Source = e.Source,
    };

    public sealed record ExportListResult(
        IReadOnlyList<AuditLog> Items,
        int MatchCount,
        bool Exceeded);
}
