using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// A2 thin-controller — generic WO-mutation infrastructure shared across the
/// WoQc + IPQC write surfaces. Holds the concurrency-safe prelude
/// (Idempotency-Key 400 → If-Match 428 → 404 → ETag-compare 409), the
/// always-safe conflict response builder (L45 audit trail), the ETag
/// normaliser, and the small actor/error helpers.
///
/// The MECHANISM is identical across controllers; only the TYPED conflict
/// response differs (WoQcSetResponse vs IpqcSetResponse — same shape
/// {Ok, ErrorCode, ETag, MesPhase}). Each controller injects an
/// <c>onConflict</c> factory that builds its own typed 409 body; the header
/// (Response.Headers.ETag) + audit (WoStateConflict) stay in this base and
/// fire BEFORE the factory runs, so byte-identical error codes / audit detail
/// / ETag headers are preserved for every derived controller.
/// </summary>
public abstract class WoMutationControllerBase : ControllerBase
{
    protected readonly IMesDbContext _db;
    protected readonly IAuditWriter _audit;

    protected WoMutationControllerBase(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    protected string ActorName() => User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    protected string ActorRole() => User.FindFirstValue(ClaimTypes.Role) ?? "";

    protected IActionResult Invalid(string code, string detail)
        => UnprocessableEntity(ApiError.Of(code, detail));

    /// <summary>
    /// Shared prelude. Byte-identical to the per-controller preludes it
    /// replaces; the ONLY per-controller difference is the typed 409 body,
    /// supplied by <paramref name="onConflict"/> which receives
    /// (serverEtag, mesPhase) and returns the Conflict(...) result. The
    /// WoStateConflict audit + ETag header are emitted here, before the
    /// factory runs — same order as the inlined originals.
    /// </summary>
    protected async Task<(IActionResult? Error, WorkOrder? WoForUpdate)> PreludeAsync(
        long id, string actor, string role, string attemptedAction,
        Func<WorkOrder?, string, IActionResult> onConflict)
    {
        var idemKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idemKey))
            return (BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required.")), null);

        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
            return (StatusCode(StatusCodes.Status428PreconditionRequired,
                ApiError.Of("wo.if_match_required",
                    "If-Match header required.")), null);

        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo is null)
            return (NotFound(ApiError.Of("wo.not_found",
                $"No work order with id {id}.")), null);

        var serverEtag = Convert.ToBase64String(wo.RowVersion);
        var clientEtag = NormalizeETag(ifMatch);
        if (!string.Equals(serverEtag, clientEtag, StringComparison.Ordinal))
        {
            var conflictDetail = JsonSerializer.Serialize(new
            {
                wo_id = id,
                wo_no = wo.WoNo,
                attempted_action = attemptedAction,
                client_version = clientEtag,
                server_version = serverEtag,
            });
            await _audit.EmitAsync(
                action: AuditAction.WoStateConflict,
                actor: actor,
                actorRole: role,
                targetType: "WorkOrder",
                targetId: id.ToString(),
                detail: conflictDetail);

            Response.Headers.ETag = $"\"{serverEtag}\"";
            return (onConflict(wo, serverEtag), null);
        }

        return (null, wo);
    }

    // Shared conflict-response builder for the ALWAYS-SAFE concurrency path.
    // Both CommitAndAuditAsync (mutation) and PostPhoto (photo upload) lose the
    // WO-row race identically: clear the dirty tracker, re-read the fresh WO,
    // stamp the fresh ETag, leave an audit trail, return 409. Gathered here so
    // the L45 convention ("a conflict MUST leave an audit trace") lives in one
    // place. `attemptedAction` + the typed 409 (onConflict) are the ONLY
    // per-call-site differences.
    protected async Task<IActionResult> HandleWoStateConflictAsync(
        long woId, string actor, string role, string attemptedAction,
        Func<WorkOrder?, string, IActionResult> onConflict,
        CancellationToken ct = default)
    {
        if (_db is Microsoft.EntityFrameworkCore.DbContext dbCtx)
            dbCtx.ChangeTracker.Clear();
        var fresh = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == woId, ct);
        var freshEtag = fresh is null ? "" : Convert.ToBase64String(fresh.RowVersion);
        Response.Headers.ETag = $"\"{freshEtag}\"";
        // L45 — nhánh SaveChanges CŨNG phải để lại vết. Convention đã có sẵn ở
        // WorkOrdersController/AdminWorkOrdersController (detail mang
        // source="ef_concurrency"); các controller làm sau đánh rơi nó khi gom
        // xử lý conflict vào helper/catch riêng. Emit đứng SAU ChangeTracker.Clear():
        // ApiAuditWriter dùng CHUNG DbContext scoped của request, tracker còn bẩn
        // thì SaveChanges của audit kéo theo UPDATE đã fail và ném lại.
        await _audit.EmitAsync(
            action: AuditAction.WoStateConflict,
            actor: actor,
            actorRole: role,
            targetType: "WorkOrder",
            targetId: woId.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                wo_id = woId,
                wo_no = fresh?.WoNo,
                attempted_action = attemptedAction,
                server_version = freshEtag,
                source = "ef_concurrency",
            }));

        return onConflict(fresh, freshEtag);
    }

    protected static string NormalizeETag(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith("W/", StringComparison.Ordinal)) trimmed = trimmed[2..];
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
            trimmed = trimmed[1..^1];
        return trimmed;
    }
}
