using CCL.MES.Application;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// Q12 — system-log viewer (audit timeline) read endpoint.
/// AdminOnly per the legacy <c>/settings/system-log</c> page-level gate.
/// Read-only — operators can never mutate the audit trail from the API
/// (or anywhere else). The legacy AuditLogExport endpoint
/// (<c>POST /api/audit-export</c> in CCL.MES.Web) handles CSV/XLSX
/// downloads; a port mirror is not needed in P10.1 because admins already
/// have a working export flow via cookie auth.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route(ApiVersion.Prefix + "/system-log")]
public sealed class SystemLogController : ControllerBase
{
    private readonly IMesDbContext _db;
    public SystemLogController(IMesDbContext db) => _db = db;

    /// <summary>
    /// Paginated reverse-chronological view of <c>audit_logs</c>. Filter
    /// by action code, actor username, target type, and time range. Page
    /// size is hard-capped server-side at 200 to keep the response small.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<AuditLog>>> List(
        [FromQuery] string? action,
        [FromQuery] string? actor,
        [FromQuery] string? targetType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;
        if (pageSize > 200) pageSize = 200;

        var q = _db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(x => x.Timestamp)
            .ThenByDescending(x => x.Id)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(x => x.Action == action);
        if (!string.IsNullOrWhiteSpace(actor))
            q = q.Where(x => x.ActorUsername == actor);
        if (!string.IsNullOrWhiteSpace(targetType))
            q = q.Where(x => x.TargetType == targetType);
        if (from.HasValue)
            q = q.Where(x => x.Timestamp >= from.Value);
        if (to.HasValue)
            q = q.Where(x => x.Timestamp <= to.Value);

        var total = await q.CountAsync();
        var items = await q
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new PagedResponse<AuditLog>
        {
            Items = items,
            Total = total,
            Page = page,
            PageSize = pageSize,
        });
    }
}
