using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using CCL.MES.Infrastructure;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.WorkOrders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.7a-2.2 — Admin-only Work Order recovery surface.
///
/// Single endpoint at <c>POST /api/v2/admin/work-orders/{id}/force-phase</c>
/// implementing the §8.1 force-unstick path. Reconciles the §2.2 ("admin / sys
/// only") and §8.1 ("Requires sys role") conflict per Henry-confirmed Q1:
/// <list type="bullet">
/// <item>The endpoint is <c>AdminOnly</c> — admin users hit the wire path
/// (sys-recovery is <c>IsActive=false</c> by design, so a literal "sys role"
/// caller cannot exist).</item>
/// <item>The audit row records the live admin's username as <c>ActorUsername</c>
/// + the seeded sys-recovery user's id under <c>detail.sys_user_id</c>. §8.1
/// "sys role" attribution lives in the detail JSON, not the principal.</item>
/// </list>
///
/// Concurrency contract mirrors <c>POST /work-orders/{id}/advance</c> from
/// 7a-1.3: If-Match required (428), stale → 409 + WO_STATE_CONFLICT audit,
/// Idempotency-Key required (400). Forceable (FROM, TO) edges are limited
/// to the 11 cells classified as recovery-only in §3.1, guarded by
/// <see cref="WorkOrderStateMachine.IsForceablePhase"/>.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route(ApiVersion.Prefix + "/admin/work-orders")]
public sealed class AdminWorkOrdersController : ControllerBase
{
    private readonly WorkOrderService _svc;
    private readonly IAuditWriter _audit;
    private readonly IMesDbContext _db;
    private readonly Services.WoMutationExecutor _executor;

    public AdminWorkOrdersController(WorkOrderService svc, IAuditWriter audit, IMesDbContext db,
        Services.WoMutationExecutor executor)
    {
        _svc = svc;
        _audit = audit;
        _db = db;
        _executor = executor;
    }

    /// <summary>P10.7a-2.2 — admin force-phase endpoint. See class summary
    /// for §8.1 mapping. Status codes mirror /advance from 7a-1.3 so the
    /// operator UX is consistent across the WO-mutation surface:
    /// 200 success / 401 anon / 403 non-admin / 404 missing /
    /// 428 missing If-Match / 409 stale If-Match | unforceable transition /
    /// 400 missing Idempotency-Key / 422 body validation.</summary>
    [HttpPost("{id:long}/force-phase")]
    public async Task<ActionResult<ForcePhaseResponse>> ForcePhase(
        long id, [FromBody] ForcePhaseRequest? req)
    {
        var actor    = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role     = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var actorIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0";

        // 1. Load WO — 404 if genuinely missing.
        var existing = await _svc.GetAsync(id);
        if (existing is null)
            return NotFound(ApiError.Of("wo.not_found", $"No work order with id {id}."));

        // 2. If-Match required (428 — RFC 6585 §3 — matches /advance).
        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return StatusCode(StatusCodes.Status428PreconditionRequired,
                ApiError.Of("wo.if_match_required",
                    "If-Match header required for force-phase. Re-fetch the WO summary to obtain a current ETag."));
        }

        // 3. Idempotency-Key required (400 — matches /advance).
        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required for force-phase. Generate a UUID per intent."));
        }

        // 4. Stale RowVersion → 409 + WO_STATE_CONFLICT audit (matches /advance).
        var serverEtagRaw = Convert.ToBase64String(existing.RowVersion);
        var clientEtagRaw = NormalizeETag(ifMatch);
        if (!string.Equals(serverEtagRaw, clientEtagRaw, StringComparison.Ordinal))
        {
            var conflictDetail = JsonSerializer.Serialize(new
            {
                wo_id = id,
                wo_no = existing.WoNo,
                attempted_action = "force_phase",
                client_version = clientEtagRaw,
                server_version = serverEtagRaw,
                actor_id = actorIdStr,
            });
            await _audit.EmitAsync(
                action: AuditAction.WoStateConflict,
                actor: actor,
                actorRole: role,
                targetType: "WorkOrder",
                targetId: id.ToString(),
                detail: conflictDetail);

            Response.Headers.ETag = $"\"{serverEtagRaw}\"";
            return Conflict(new ForcePhaseResponse
            {
                Ok = false,
                CurrentStep = existing.CurrentStep.ToString(),
                ErrorCode = "wo.state_conflict",
                ETag = serverEtagRaw,
            });
        }

        // 5. Body validation per Henry Q2 / Q4a (structured reason + same-state guard).
        if (req is null)
            return UnprocessableEntity(ApiError.Of("force.invalid_body", "Request body is required."));

        if (!Enum.TryParse<ProcessStepCode>(req.TargetStep, ignoreCase: false, out var targetLegacy))
        {
            return UnprocessableEntity(ApiError.Of("force.invalid_target_step",
                $"TargetStep '{req.TargetStep}' is not a known ProcessStepCode."));
        }

        if (string.IsNullOrWhiteSpace(req.ReasonCode))
            return UnprocessableEntity(ApiError.Of("force.invalid_reason_code", "ReasonCode is required."));

        var reasonCodeExists = await _db.ReasonCodes
            .AsNoTracking()
            .AnyAsync(r => r.Code == req.ReasonCode && r.Kind == ReasonCodeKind.Recovery);
        if (!reasonCodeExists)
        {
            return UnprocessableEntity(ApiError.Of("force.invalid_reason_code",
                $"ReasonCode '{req.ReasonCode}' is not a Recovery-kind code. Use one of the 6 seeded REC-* values."));
        }

        if (string.IsNullOrWhiteSpace(req.ReasonNote) || req.ReasonNote.Length > 500)
        {
            return UnprocessableEntity(ApiError.Of("force.invalid_reason_note",
                "ReasonNote must be non-empty and ≤ 500 characters."));
        }

        // Project to canonical phases for IsForceablePhase consultation.
        var fromPhase = WorkOrderStateMachine.ProjectFromLegacy(existing.CurrentStep);
        // TargetStep is the legacy enum, but the contract operates on canonical
        // phases — use the explicit caller intent (CANCELLED has no legacy
        // pre-image so callers send "Closed" + reason=REC-* to mean CANCELLED).
        var toPhase = ProjectTargetToCanonical(targetLegacy, fromPhase);

        // 6. Same-state guard (Q4a → 422).
        if (fromPhase == toPhase)
        {
            return UnprocessableEntity(ApiError.Of("force.same_state_force",
                $"WO already at MesPhase={fromPhase}. Force-phase requires a transition target."));
        }

        // 7. IsForceablePhase guard (Q1 / §3.1 — 422 if not in 11-cell recovery-only set).
        // Per P10.7a-2.3 status-code rationale: unforceable transitions are a
        // SEMANTIC rejection (the (from, to) pair is forbidden FOREVER by
        // §3.1's grid) NOT a transient concurrency drift. 409 is reserved
        // for stale If-Match (admin can refetch + retry); 422 means the
        // request inputs are wrong (admin must pick a different targetStep
        // or accept the current state). Keeps 409 unambiguous = "refetch
        // ETag and retry" so retry logic doesn't get confused with a
        // domain-rule rejection that retrying would never satisfy.
        if (!WorkOrderStateMachine.IsForceablePhase(fromPhase, toPhase))
        {
            return UnprocessableEntity(ApiError.Of("force.unforceable_transition",
                $"Force-phase from {fromPhase} to {toPhase} is not a recovery-only cell per §3.1. " +
                "Allowed cells: SETTING→PREPRESS + every non-terminal source → CANCELLED."));
        }

        // 8. Mutation in a single transaction: update WO MesPhase + CurrentStep,
        //    append WoStatusHistory row, emit SYS_RECOVERY audit. Per §8.1 B3
        //    no child-table rows (wo_setting_log / run_sessions / ipqc_checks)
        //    are modified — only the WO header + history + audit.
        var fromStepStr = existing.CurrentStep.ToString();
        var legacyTarget = WorkOrderStateMachine.ProjectToLegacy(toPhase);

        existing.MesPhase = toPhase.ToString();
        existing.CurrentStep = legacyTarget;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedBy = actor;

        _db.WoStatusHistories.Add(new WoStatusHistory
        {
            WorkOrderId = id,
            FromStep = Enum.Parse<ProcessStepCode>(fromStepStr),
            ToStep = legacyTarget,
            Action = "SysRecovery",
            ByUser = actor,
            Reason = req.ReasonNote,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actor,
        });

        // sys_user_id reference for the audit row — best-effort lookup; if the
        // seed didn't run (e.g. fresh DB before the Test-env opt-in), record
        // null rather than fail the recovery action.
        var sysUserId = await _db.Users
            .AsNoTracking()
            .Where(u => u.Username == DbSeeder.SysRecoveryUsername)
            .Select(u => (long?)u.Id)
            .SingleOrDefaultAsync();

        // 8b. Atomic save + L45 conflict path via the shared executor (A2 —
        //     SaveChanges out of the controller; actorId → conflict detail keeps
        //     its actor_id field byte-identical). Mutated WO + WoStatusHistory row
        //     commit together in the executor's single SaveChanges.
        var outcome = await _executor.SaveAndResolveAsync(
            HttpContext, id, existing.WoNo, actor, role, "force_phase", actorIdStr);
        if (outcome.Conflict)
            return Conflict(new ForcePhaseResponse
            {
                Ok = false,
                CurrentStep = existing.CurrentStep.ToString(),
                ErrorCode = "wo.state_conflict",
                ETag = outcome.ETag,
            });

        // 9. SYS_RECOVERY audit emit per §7.2 detail shape + Henry-confirmed Q2
        //    structured reason. Caller's identity is in ActorUsername;
        //    detail.sys_user_id points at the seeded sys-recovery user.
        var sysRecoveryDetail = JsonSerializer.Serialize(new
        {
            wo_id = id,
            wo_no = existing.WoNo,
            from_phase = fromPhase.ToString(),
            to_phase = toPhase.ToString(),
            reason = new { code = req.ReasonCode, note = req.ReasonNote },
            sys_user_id = sysUserId,
            actor_id = actorIdStr,
        });
        await _audit.EmitAsync(
            action: AuditAction.SysRecovery,
            actor: actor,
            actorRole: role,
            targetType: "WorkOrder",
            targetId: id.ToString(),
            detail: sysRecoveryDetail);

        // 10. ETag already set by the executor (fresh RowVersion post-save, L11).
        return Ok(new ForcePhaseResponse
        {
            Ok = true,
            CurrentStep = legacyTarget.ToString(),
            ErrorCode = null,
            ETag = outcome.ETag,
        });
    }

    /// <summary>
    /// Map the legacy <see cref="ProcessStepCode"/> the caller supplied to
    /// the canonical <see cref="MesPhase"/> the contract operates on.
    /// <c>ProcessStepCode.Closed</c> is overloaded in the legacy enum
    /// (DONE + CANCELLED both project to it) — for force-phase the caller
    /// intent is always CANCELLED (terminal-no-force-to-DONE per §3.1
    /// rule "*→DONE = no recovery-only cell"), so we resolve the ambiguity
    /// by mapping <c>Closed</c> to <see cref="MesPhase.CANCELLED"/>. Any
    /// other legacy step maps deterministically through
    /// <see cref="WorkOrderStateMachine.ProjectFromLegacy"/>.
    /// </summary>
    private static MesPhase ProjectTargetToCanonical(ProcessStepCode legacy, MesPhase fromForContext)
    {
        if (legacy == ProcessStepCode.Closed) return MesPhase.CANCELLED;
        return WorkOrderStateMachine.ProjectFromLegacy(legacy);
    }

    /// <summary>RFC 7232 strip — mirrors /advance.</summary>
    private static string NormalizeETag(string raw)
    {
        var s = raw.Trim();
        if (s.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
            s = s.Substring(2);
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s.Substring(1, s.Length - 2);
        return s;
    }
}
