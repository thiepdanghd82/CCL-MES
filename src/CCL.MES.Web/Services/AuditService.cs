using System.Security.Claims;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.AspNetCore.Http;

namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 6 Bước 5 — Web implementation of <see cref="IAuditWriter"/>.
/// Append-only writes to the AuditLogs table with the remote IP picked
/// up from the current HttpContext (Forwarded headers respected if any
/// upstream proxy is configured to populate them).
/// Truncates Detail to 4 KB defensively; callers SHOULD have already
/// sanitized but defence-in-depth.
/// </summary>
public class AuditService : IAuditWriter
{
    private const int MaxDetailLength = 4096;

    private readonly IMesDbContext _db;
    private readonly IHttpContextAccessor _http;

    public AuditService(IMesDbContext db, IHttpContextAccessor http)
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
        string source = "Web")
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
        // Forwarded-For first (proxy) then RemoteIp.
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

/// <summary>
/// Phase 6 Bước 5 — convenience extensions to extract actor identity
/// from a <see cref="ClaimsPrincipal"/> in one call, since most emit
/// sites use the principal directly.
/// </summary>
public static class AuditPrincipalExtensions
{
    public static (string Username, string Role) AuditIdentity(this ClaimsPrincipal? principal)
    {
        if (principal?.Identity is null || !principal.Identity.IsAuthenticated)
            return ("anonymous", "");
        var name = principal.Identity.Name ?? "anonymous";
        var role = principal.FindFirstValue(ClaimTypes.Role) ?? "";
        return (name, role);
    }
}
