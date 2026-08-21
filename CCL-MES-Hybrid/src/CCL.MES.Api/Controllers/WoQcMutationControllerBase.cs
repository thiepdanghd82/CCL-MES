using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.WoQcReview;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// A2 thin-controller lát 7 — shared mutation infrastructure for the
/// WoQc write surfaces. Holds the concurrency-safe prelude (Idempotency-Key
/// 400 → If-Match 428 → 404 → ETag-compare 409), the always-safe conflict
/// response builder (L45 audit trail), the ETag normaliser, and the small
/// actor/error helpers. Extracted VERBATIM from <see cref="WoQcReviewController"/>
/// so a future WoQcPhotoController slice can reuse the same prelude — NO
/// behaviour change: byte-identical error codes / audit detail / ETag headers.
/// </summary>
public abstract class WoQcMutationControllerBase : ControllerBase
{
    protected readonly IMesDbContext _db;
    protected readonly IAuditWriter _audit;

    protected WoQcMutationControllerBase(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    protected string ActorName() => User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
    protected string ActorRole() => User.FindFirstValue(ClaimTypes.Role) ?? "";

    protected IActionResult Invalid(string code, string detail)
        => UnprocessableEntity(ApiError.Of(code, detail));

    protected async Task<(IActionResult? Error, WorkOrder? WoForUpdate)> PreludeAsync(
        long id, string actor, string role, string attemptedAction)
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
            return (Conflict(new WoQcSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = serverEtag,
                MesPhase = wo.MesPhase ?? "",
            }), null);
        }

        return (null, wo);
    }

    // Shared conflict-response builder for the ALWAYS-SAFE concurrency path.
    // Both CommitAndAuditAsync (mutation) and PostPhoto (photo upload) lose the
    // WO-row race identically: clear the dirty tracker, re-read the fresh WO,
    // stamp the fresh ETag, leave an audit trail, return 409. Gathered here so
    // the L45 convention ("a conflict MUST leave an audit trace") lives in one
    // place. `attemptedAction` is the ONLY per-call-site difference.
    protected async Task<IActionResult> HandleWoStateConflictAsync(
        long woId, string actor, string role, string attemptedAction,
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

        return Conflict(new WoQcSetResponse
        {
            Ok = false,
            ErrorCode = "wo.state_conflict",
            ETag = freshEtag,
            MesPhase = fresh?.MesPhase ?? "",
        });
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
