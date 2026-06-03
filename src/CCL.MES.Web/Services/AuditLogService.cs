using CCL.MES.Application;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 6 Bước 5 — read-side wrapper for the AuditLogs table consumed
/// by the Settings → Syslog tab. Mutation belongs on
/// <see cref="AuditService"/> (the IAuditWriter implementation); this
/// service is list-only.
/// </summary>
public class AuditLogService
{
    private readonly IMesDbContext _db;
    public AuditLogService(IMesDbContext db) => _db = db;

    public Task<PagedResult<AuditLog>> ListAsync(
        string? search,
        string? action,
        string? actor,
        DateTime? from,
        DateTime? to,
        int page,
        int pageSize)
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
        if (from.HasValue)
        {
            var f = from.Value.ToUniversalTime();
            q = q.Where(x => x.Timestamp >= f);
        }
        if (to.HasValue)
        {
            var t = to.Value.ToUniversalTime();
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

        return PagingHelper.PageAsync(q.OrderByDescending(x => x.Id), page, pageSize);
    }

    public async Task<IReadOnlyList<string>> DistinctActionsAsync() =>
        await _db.AuditLogs.AsNoTracking()
            .Select(x => x.Action)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

    /// <summary>
    /// Phase 9 audit-export — same filter shape as
    /// <see cref="ListAsync"/> but returns ALL matching rows (no
    /// pagination) for CSV/XLSX export. <paramref name="hardCap"/>
    /// guards against an admin running an unfiltered query against a
    /// multi-million row table on prod — exceeding the cap returns
    /// the over-cap count so the controller can fail-fast 400 instead
    /// of generating a multi-GB workbook.
    /// </summary>
    public async Task<AuditLogExportListResult> ListForExportAsync(
        string? search,
        string? action,
        string? actor,
        DateTime? from,
        DateTime? to,
        int hardCap)
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
        if (from.HasValue)
        {
            var f = from.Value.ToUniversalTime();
            q = q.Where(x => x.Timestamp >= f);
        }
        if (to.HasValue)
        {
            var t = to.Value.ToUniversalTime();
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

        // COUNT-first guard so an over-cap query rejects before EF materialises
        // a multi-GB row set in memory.
        var total = await q.CountAsync();
        if (total > hardCap)
        {
            return new AuditLogExportListResult(
                Items: Array.Empty<AuditLog>(),
                MatchCount: total,
                Exceeded: true);
        }

        var rows = await q
            .OrderByDescending(x => x.Id)
            .ToListAsync();

        return new AuditLogExportListResult(rows, total, Exceeded: false);
    }
}

/// <summary>
/// Result envelope for <see cref="AuditLogService.ListForExportAsync"/>.
/// When <see cref="Exceeded"/> is true the caller MUST refuse the
/// export (the row set was NOT materialised); <see cref="MatchCount"/>
/// carries the count so the UI can suggest a narrower filter.
/// </summary>
public sealed record AuditLogExportListResult(
    IReadOnlyList<AuditLog> Items,
    int MatchCount,
    bool Exceeded);
