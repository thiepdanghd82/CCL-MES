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
}
