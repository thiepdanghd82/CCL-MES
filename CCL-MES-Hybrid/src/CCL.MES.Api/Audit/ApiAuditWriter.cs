using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace CCL.MES.Api.Audit;

/// <summary>
/// API-side implementation of <see cref="IAuditWriter"/>. Behaviour mirrors
/// the legacy <c>CCL.MES.Web.Services.AuditService</c> (Phase 6 Bước 5):
/// append-only writes to the <c>AuditLogs</c> table with remote IP picked
/// up from <see cref="IHttpContextAccessor"/>, Detail trimmed to 4 KB.
///
/// We DO NOT modify or import the legacy AuditService — keeping a
/// behaviour-identical copy here satisfies the "READ-ONLY legacy"
/// constraint and lets the API run without the Web project on its path.
/// <see cref="Source"/> defaults to <c>"Api"</c> so audit rows emitted via
/// HTTP are distinguishable from rows emitted via the legacy Web cookie path.
/// </summary>
public sealed class ApiAuditWriter : IAuditWriter
{
    private const int MaxDetailLength = 4096;

    private readonly IMesDbContext _db;
    private readonly IHttpContextAccessor _http;

    public ApiAuditWriter(IMesDbContext db, IHttpContextAccessor http)
    {
        _db = db;
        _http = http;
    }

    public async Task EmitAsync(
        string action,
        string actor,
        string actorRole,
        string? targetType = null,
        string? targetId = null,
        string? detail = null,
        string source = "Api")
    {
        var row = new AuditLog
        {
            Timestamp = DateTime.UtcNow,
            ActorUsername = string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor,
            ActorRole = actorRole ?? "",
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Detail = TrimDetail(detail),
            IpAddress = ResolveIp(),
            Source = source,
        };
        _db.AuditLogs.Add(row);
        await _db.SaveChangesAsync();
    }

    private string? ResolveIp()
    {
        var ctx = _http.HttpContext;
        if (ctx is null) return null;
        var fwd = ctx.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrEmpty(fwd))
            return fwd.Split(',', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return ctx.Connection.RemoteIpAddress?.ToString();
    }

    private static string? TrimDetail(string? detail) =>
        detail is null ? null
        : detail.Length <= MaxDetailLength ? detail
        : detail[..MaxDetailLength];
}
