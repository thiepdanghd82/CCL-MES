using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Services;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
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
    protected const string KindFqc = "FQC";
    protected const string KindOqc = "OQC";

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

    // ═══════════════════════════════════════════════════════════════
    // Shared QC-mutation helpers (moved VERBATIM from WoQcReviewController
    // so a WoQcPhotoController slice reuses kind normalisation + check
    // materialisation + Q4 3-level profile resolution — NO behaviour change).
    // ═══════════════════════════════════════════════════════════════

    protected static string? NormaliseKind(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim().ToUpperInvariant();
        return s switch { "FQC" => KindFqc, "OQC" => KindOqc, _ => null };
    }

    protected async Task<WoQcCheck> GetOrCreateCheckAsync(long woId, string kind, long? productId = null)
    {
        var check = await _db.WoQcChecks
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.WorkOrderId == woId && c.QcKind == kind);
        if (check is not null)
        {
            // P10.7e-3 FIX — heal empty snapshot on mutation path too.
            if ((string.IsNullOrWhiteSpace(check.ProfileSnapshotJson) || check.ProfileSnapshotJson == "{}")
                && productId.HasValue)
            {
                var snap = await ResolveProfileSnapshotAsync(productId.Value, kind, CancellationToken.None);
                if (snap != "{}") check.ProfileSnapshotJson = snap;
            }
            return check;
        }

        var resolved = productId.HasValue
            ? await ResolveProfileSnapshotAsync(productId.Value, kind, CancellationToken.None)
            : (QcProfileSeed.GetDefaultProfileJson(kind) ?? "{}");
        check = new WoQcCheck
        {
            WorkOrderId = woId,
            QcKind = kind,
            ProfileSnapshotJson = resolved,
            Judgment = WoQcJudgment.Pending,
        };
        _db.WoQcChecks.Add(check);
        return check;
    }

    /// <summary>P10.7e-3 FIX — Q4 3-level profile resolution chain:
    ///   L1: Product.QcProfileOverride (per-product override JSON;
    ///       shape must include "kind" matching FQC / OQC)
    ///   L2: QcProfileSeed.GetDefaultProfileJson(kind) (system default)
    ///   L3: "{}" empty (only when both levels miss; checks render an
    ///       empty banner so IT notices and seeds the profile).
    /// Frozen at materialise time per Q3 — profile edits don't
    /// retroactively change rows already in flight.</summary>
    protected async Task<string> ResolveProfileSnapshotAsync(long productId, string kind, CancellationToken ct)
    {
        // L1 override JSON read stays here (EF async); the 3-level pure
        // resolution lives in QcProfileResolver (A2 thin-controller, L47).
        string? overrideJson = null;
        if (productId > 0)
        {
            overrideJson = await _db.Products.AsNoTracking()
                .Where(p => p.Id == productId)
                .Select(p => p.QcProfileOverride)
                .FirstOrDefaultAsync(ct);
        }
        return QcProfileResolver.ResolveSnapshot(overrideJson, kind);
    }
}
