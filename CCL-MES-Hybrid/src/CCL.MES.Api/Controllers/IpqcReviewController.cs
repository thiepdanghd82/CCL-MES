using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.IpqcReview;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.7d-2 — IPQC + QA Approval write surface per contract §5.5 + §5.6.
///
/// Endpoints:
///   GET  /api/v2/work-orders/{id}/ipqc                 Read 4 slot statuses + judgment + QA state
///   PUT  /api/v2/work-orders/{id}/ipqc/material        Set Material slot
///   PUT  /api/v2/work-orders/{id}/ipqc/print-a         Set PrintA (Color) slot
///   PUT  /api/v2/work-orders/{id}/ipqc/print-b         Set PrintB (Registration) slot
///   PUT  /api/v2/work-orders/{id}/ipqc/print-c         Set PrintC (Content) slot
///   POST /api/v2/work-orders/{id}/ipqc/judgment        Submit GoRun / StopLine / SpecialAccept
///   POST /api/v2/work-orders/{id}/qa/approve           QA Approve / Reject (dual-sig guarded)
///
/// Authorization: any authenticated user (per SpecHub §3 role matrix —
/// qc role does both IPQC + QA Approve). The Q3 dual-sig guard
/// enforces "QA approver ≠ IPQC submitter" at runtime; no separate role.
///
/// Concurrency contract mirrors 7a-1.3 / 7b-2 / 7c-2:
///   428 missing If-Match
///   400 missing Idempotency-Key
///   404 WO not found
///   409 wo.state_conflict on stale If-Match (+ WO_STATE_CONFLICT audit)
///   422 wo.invalid_phase / ipqc.invalid_status / ipqc.invalid_reason_code /
///       ipqc.invalid_ng_note / ipqc.invalid_judgment / ipqc.judgment_inconsistent
///       / ipqc.not_ready_for_judgment / ipqc.invalid_special_accept_reason /
///       qa.invalid_outcome / qa.same_user_as_ipqc_submitter / qa.invalid_qa_reason
///
/// ATOMIC PATTERN — every mutation endpoint:
///   1. Prelude (If-Match + Idem-Key + WO fetch + RowVersion check)
///   2. Body + catalog validation
///   3. Phase guard (must be IPQC_WAIT for slot/judgment; QA_PENDING for qa/approve)
///   4. Lazy-materialise WoIpqcCheck row if absent
///   5. Domain service call (mutates entities; does NOT SaveChanges)
///   6. wo.UpdatedAt + UpdatedBy touch (forces RowVersion bump)
///   7. SINGLE SaveChanges (catch DbUpdateConcurrencyException → 409 + ChangeTracker.Clear per L12)
///   8. Audit emit (after successful SaveChanges)
///   9. Return 200 + post-write state + bumped ETag
///
/// Dual-sig guard (Q3 CRITICAL):
///   POST /qa/approve compares the authenticated user's username
///   against IpqcSubmittedBy via WoIpqcCheckService.ValidateDualSig.
///   When the flag is ON (default) and usernames match (case-insensitive
///   ordinal), the controller emits 422 + qa.same_user_as_ipqc_submitter
///   + WO_QA_APPROVE_DENIED audit row (NOT WO_QA_APPROVE) so forensic
///   replay shows the attempt without false-positive approve tracking.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/work-orders")]
public sealed class IpqcReviewController : ControllerBase
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;
    private readonly IpqcDualSigOptions _dualSig;

    public IpqcReviewController(
        IMesDbContext db,
        IAuditWriter audit,
        IOptions<IpqcDualSigOptions> dualSig)
    {
        _db = db;
        _audit = audit;
        _dualSig = dualSig.Value;
    }

    // ── GET /work-orders/{id}/ipqc ─────────────────────────────────

    /// <summary>P10.7d-2 — read view for the IPQC + QA dashboards.
    /// Lazy-materialises a Pending row on first read so legacy WOs
    /// (those that didn't get the migration backfill because they were
    /// in PREPRESS at migration time, then later advanced) still render
    /// correctly. Idempotent — controller serialises with the UNIQUE
    /// index so concurrent first-reads can't double-insert.</summary>
    [HttpGet("{id:long}/ipqc")]
    public async Task<IActionResult> Get(long id, CancellationToken ct = default)
    {
        var wo = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wo is null)
            return NotFound(ApiError.Of("wo.not_found", $"No work order with id {id}."));

        var check = await _db.WoIpqcChecks.AsNoTracking()
            .FirstOrDefaultAsync(c => c.WorkOrderId == id, ct);
        if (check is null)
        {
            // Lazy-materialise blank row. Concurrent first-readers race
            // on the UNIQUE index — losers refetch.
            try
            {
                _db.WoIpqcChecks.Add(new WoIpqcCheck
                {
                    WorkOrderId = id,
                    MaterialStatus = IpqcCheckStatus.Pending,
                    PrintAStatus = IpqcCheckStatus.Pending,
                    PrintBStatus = IpqcCheckStatus.Pending,
                    PrintCStatus = IpqcCheckStatus.Pending,
                    Judgment = IpqcJudgment.Pending,
                    QaOutcome = QaOutcome.Pending,
                });
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Race lost; another caller already inserted. Refetch.
                if (_db is Microsoft.EntityFrameworkCore.DbContext dbCtx)
                    dbCtx.ChangeTracker.Clear();
            }
            check = await _db.WoIpqcChecks.AsNoTracking()
                .FirstOrDefaultAsync(c => c.WorkOrderId == id, ct);
        }

        var etag = Convert.ToBase64String(wo.RowVersion);
        var (ready, allOk, anyNg) = IpqcReadinessRollup.Compute(check);

        var view = new IpqcView
        {
            WoId = wo.Id,
            WoNo = wo.WoNo,
            MesPhase = wo.MesPhase,
            ETag = etag,
            MaterialStatus = check?.MaterialStatus.ToString() ?? "Pending",
            MaterialNgReasonCode = check?.MaterialNgReasonCode,
            MaterialNgNote = check?.MaterialNgNote,
            PrintAStatus = check?.PrintAStatus.ToString() ?? "Pending",
            PrintANgReasonCode = check?.PrintANgReasonCode,
            PrintANgNote = check?.PrintANgNote,
            PrintBStatus = check?.PrintBStatus.ToString() ?? "Pending",
            PrintBNgReasonCode = check?.PrintBNgReasonCode,
            PrintBNgNote = check?.PrintBNgNote,
            PrintCStatus = check?.PrintCStatus.ToString() ?? "Pending",
            PrintCNgReasonCode = check?.PrintCNgReasonCode,
            PrintCNgNote = check?.PrintCNgNote,
            Judgment = check?.Judgment.ToString() ?? "Pending",
            SpecialAcceptReason = check?.SpecialAcceptReason,
            IpqcSubmittedBy = check?.IpqcSubmittedBy,
            IpqcSubmittedAt = check?.IpqcSubmittedAt,
            QaOutcome = check?.QaOutcome.ToString() ?? "Pending",
            QaReason = check?.QaReason,
            QaApprovedBy = check?.QaApprovedBy,
            QaApprovedAt = check?.QaApprovedAt,
            IsReadyForJudgment = ready,
            AllOk = allOk,
            AnyNg = anyNg,
        };

        Response.Headers.ETag = $"\"{etag}\"";
        return Ok(view);
    }

    // ── PUT /work-orders/{id}/ipqc/{slot} ──────────────────────────

    [HttpPut("{id:long}/ipqc/material")]
    public Task<IActionResult> PutMaterialSlot(long id, [FromBody] SetIpqcSlotRequest? req) =>
        PutSlotAsync(id, WoIpqcCheckService.CheckSlot.Material, req);

    [HttpPut("{id:long}/ipqc/print-a")]
    public Task<IActionResult> PutPrintASlot(long id, [FromBody] SetIpqcSlotRequest? req) =>
        PutSlotAsync(id, WoIpqcCheckService.CheckSlot.PrintA, req);

    [HttpPut("{id:long}/ipqc/print-b")]
    public Task<IActionResult> PutPrintBSlot(long id, [FromBody] SetIpqcSlotRequest? req) =>
        PutSlotAsync(id, WoIpqcCheckService.CheckSlot.PrintB, req);

    [HttpPut("{id:long}/ipqc/print-c")]
    public Task<IActionResult> PutPrintCSlot(long id, [FromBody] SetIpqcSlotRequest? req) =>
        PutSlotAsync(id, WoIpqcCheckService.CheckSlot.PrintC, req);

    private async Task<IActionResult> PutSlotAsync(
        long id, WoIpqcCheckService.CheckSlot slot, SetIpqcSlotRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role,
            $"ipqc_set_{slot.ToString().ToLowerInvariant()}");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "IPQC_WAIT")
            return Invalid("wo.invalid_phase",
                $"ipqc/{slot} requires MesPhase = IPQC_WAIT; current = {wo.MesPhase}.");

        if (req is null || string.IsNullOrWhiteSpace(req.Status))
            return Invalid("ipqc.invalid_status",
                "Status is required (\"Ok\" or \"Ng\").");
        if (!Enum.TryParse<IpqcCheckStatus>(req.Status, ignoreCase: true, out var status)
            || status == IpqcCheckStatus.Pending)
            return Invalid("ipqc.invalid_status",
                $"Status must be \"Ok\" or \"Ng\"; got \"{req.Status}\".");

        if (status == IpqcCheckStatus.Ng)
        {
            var ngErr = await ValidateNgAsync(req.NgReasonCode, req.NgNote);
            if (ngErr is not null) return ngErr;
        }

        var check = await GetOrCreateCheckAsync(id);

        WoIpqcCheckService.SetSlot(check, slot, status,
            status == IpqcCheckStatus.Ng ? req.NgReasonCode : null,
            status == IpqcCheckStatus.Ng ? req.NgNote : null,
            actor, DateTime.UtcNow);

        return await CommitAndAuditAsync(id, wo, check, actor, role,
            AuditAction.WoIpqcCheck,
            new
            {
                slot = slot.ToString(),
                status = status.ToString(),
                ng_reason_code = status == IpqcCheckStatus.Ng ? req.NgReasonCode : null,
                ng_note = status == IpqcCheckStatus.Ng ? req.NgNote : null,
            });
    }

    // ── POST /work-orders/{id}/ipqc/judgment ───────────────────────

    [HttpPost("{id:long}/ipqc/judgment")]
    public async Task<IActionResult> PostJudgment(
        long id, [FromBody] SubmitIpqcJudgmentRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "ipqc_judgment");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "IPQC_WAIT")
            return Invalid("wo.invalid_phase",
                $"ipqc/judgment requires MesPhase = IPQC_WAIT; current = {wo.MesPhase}.");

        if (req is null || string.IsNullOrWhiteSpace(req.Judgment))
            return Invalid("ipqc.invalid_judgment",
                "Judgment is required (\"GoRun\", \"StopLine\", or \"SpecialAccept\").");
        if (!Enum.TryParse<IpqcJudgment>(req.Judgment, ignoreCase: true, out var judgment)
            || judgment == IpqcJudgment.Pending)
            return Invalid("ipqc.invalid_judgment",
                $"Judgment must be GoRun / StopLine / SpecialAccept; got \"{req.Judgment}\".");

        var check = await GetOrCreateCheckAsync(id);
        var (ready, _, _) = IpqcReadinessRollup.Compute(check);
        if (!ready)
            return Invalid("ipqc.not_ready_for_judgment",
                "All 4 slots (Material + PrintA + PrintB + PrintC) must be resolved before judgment.");
        if (!IpqcReadinessRollup.IsJudgmentConsistent(check, judgment))
            return Invalid("ipqc.judgment_inconsistent",
                $"Judgment \"{judgment}\" is inconsistent with slot results " +
                $"(GoRun requires all OK; SpecialAccept requires at least one NG).");

        if (judgment == IpqcJudgment.SpecialAccept)
        {
            if (string.IsNullOrWhiteSpace(req.SpecialAcceptReason)
                || req.SpecialAcceptReason!.Length > 500)
                return Invalid("ipqc.invalid_special_accept_reason",
                    "SpecialAcceptReason is required (1-500 chars) for SpecialAccept judgment.");
        }

        var now = DateTime.UtcNow;
        WoIpqcCheckService.SubmitJudgment(check!, judgment,
            judgment == IpqcJudgment.SpecialAccept ? req.SpecialAcceptReason : null,
            actor, now);

        wo.MesPhase = judgment switch
        {
            IpqcJudgment.GoRun         => "IPQC_APPROVED",
            IpqcJudgment.StopLine      => "PREPRESS",
            IpqcJudgment.SpecialAccept => "QA_PENDING",
            _ => wo.MesPhase,
        };

        return await CommitAndAuditAsync(id, wo, check, actor, role,
            AuditAction.WoIpqcJudgment,
            new
            {
                outcome = judgment.ToString(),
                special_accept_reason = judgment == IpqcJudgment.SpecialAccept
                    ? req.SpecialAcceptReason : null,
            });
    }

    // ── POST /work-orders/{id}/qa/approve ──────────────────────────

    [HttpPost("{id:long}/qa/approve")]
    public async Task<IActionResult> PostQaApprove(
        long id, [FromBody] QaApproveRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "qa_approve");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "QA_PENDING")
            return Invalid("wo.invalid_phase",
                $"qa/approve requires MesPhase = QA_PENDING; current = {wo.MesPhase}.");

        if (req is null || string.IsNullOrWhiteSpace(req.Outcome))
            return Invalid("qa.invalid_outcome",
                "Outcome is required (\"Approve\" or \"Reject\").");
        if (!Enum.TryParse<QaOutcome>(req.Outcome, ignoreCase: true, out var outcome)
            || outcome == QaOutcome.Pending)
            return Invalid("qa.invalid_outcome",
                $"Outcome must be Approve or Reject; got \"{req.Outcome}\".");

        var check = await GetOrCreateCheckAsync(id);

        // Q3 CRITICAL — dual-sig guard.
        // Always read the IPQC submitter for audit purposes, even when
        // flag is OFF (so WO_QA_APPROVE audit detail can include it).
        var ipqcSubmittedBy = check.IpqcSubmittedBy ?? "";
        if (!WoIpqcCheckService.ValidateDualSig(
                ipqcSubmittedBy, actor, _dualSig.RequireDistinctQaApprover))
        {
            // Emit WO_QA_APPROVE_DENIED audit (NOT WO_QA_APPROVE) so
            // forensic replay shows the attempt without false-positive
            // approve tracking. Detail per §5.6.
            var deniedDetail = JsonSerializer.Serialize(new
            {
                wo_id = id,
                wo_no = wo.WoNo,
                reason = "same_user_as_ipqc_submitter",
                attempted_by = actor,
                ipqc_submitted_by = ipqcSubmittedBy,
            });
            await _audit.EmitAsync(
                action: AuditAction.WoQaApproveDenied,
                actor: actor,
                actorRole: role,
                targetType: "WorkOrder",
                targetId: id.ToString(),
                detail: deniedDetail);

            return Invalid("qa.same_user_as_ipqc_submitter",
                "Người duyệt QA không được trùng với người gửi IPQC — vi phạm nguyên tắc 4-mắt.");
        }

        if (outcome == QaOutcome.Reject)
        {
            if (string.IsNullOrWhiteSpace(req.QaReason) || req.QaReason!.Length > 500)
                return Invalid("qa.invalid_qa_reason",
                    "QaReason is required (1-500 chars) for Reject outcome.");
        }
        else if (req.QaReason is not null && req.QaReason.Length > 500)
        {
            return Invalid("qa.invalid_qa_reason",
                "QaReason must be 0-500 chars on Approve outcome.");
        }

        var now = DateTime.UtcNow;
        WoIpqcCheckService.SubmitQaApproval(check, outcome, req.QaReason, actor, now);

        wo.MesPhase = outcome == QaOutcome.Approve ? "IPQC_APPROVED" : "PREPRESS";

        return await CommitAndAuditAsync(id, wo, check, actor, role,
            AuditAction.WoQaApprove,
            new
            {
                outcome = outcome.ToString(),
                qa_reason = req.QaReason,
                ipqc_submitted_by = ipqcSubmittedBy,
                qa_approved_by = actor,
                flag_state = _dualSig.FlagState,
            });
    }

    // ── Helpers ────────────────────────────────────────────────────

    /// <summary>Lazy-materialise the IPQC check row if absent (catches
    /// pre-migration WOs that were in PREPRESS at migration time + later
    /// advanced past SETTING). Idempotent under UNIQUE index race.</summary>
    private async Task<WoIpqcCheck> GetOrCreateCheckAsync(long woId)
    {
        var check = await _db.WoIpqcChecks.FirstOrDefaultAsync(c => c.WorkOrderId == woId);
        if (check is not null) return check;

        check = new WoIpqcCheck
        {
            WorkOrderId = woId,
            MaterialStatus = IpqcCheckStatus.Pending,
            PrintAStatus = IpqcCheckStatus.Pending,
            PrintBStatus = IpqcCheckStatus.Pending,
            PrintCStatus = IpqcCheckStatus.Pending,
            Judgment = IpqcJudgment.Pending,
            QaOutcome = QaOutcome.Pending,
        };
        _db.WoIpqcChecks.Add(check);
        return check;
    }

    private async Task<IActionResult?> ValidateNgAsync(string? ngReasonCode, string? ngNote)
    {
        if (string.IsNullOrWhiteSpace(ngReasonCode))
            return Invalid("ipqc.invalid_reason_code",
                "NgReasonCode is required when Status = Ng.");
        if (string.IsNullOrWhiteSpace(ngNote) || ngNote!.Length > 500)
            return Invalid("ipqc.invalid_ng_note",
                "NgNote must be 1-500 chars when Status = Ng.");

        var exists = await _db.ReasonCodes.AsNoTracking()
            .AnyAsync(r => r.Code == ngReasonCode && r.Kind == ReasonCodeKind.Scrap);
        if (!exists)
            return Invalid("ipqc.invalid_reason_code",
                $"NgReasonCode \"{ngReasonCode}\" is not a registered Scrap reason.");
        return null;
    }

    private IActionResult Invalid(string code, string detail)
        => UnprocessableEntity(ApiError.Of(code, detail));

    // ── Prelude (mirrors 7c-2 RunningSurfaceController) ────────────

    private async Task<(IActionResult? Error, WorkOrder? WoForUpdate)> PreludeAsync(
        long id, string actor, string role, string attemptedAction)
    {
        var idemKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idemKey))
        {
            return (BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required.")), null);
        }

        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return (StatusCode(StatusCodes.Status428PreconditionRequired,
                ApiError.Of("wo.if_match_required",
                    "If-Match header required.")), null);
        }

        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo is null)
        {
            return (NotFound(ApiError.Of("wo.not_found",
                $"No work order with id {id}.")), null);
        }

        var serverEtagRaw = Convert.ToBase64String(wo.RowVersion);
        var clientEtagRaw = NormalizeETag(ifMatch);
        if (!string.Equals(serverEtagRaw, clientEtagRaw, StringComparison.Ordinal))
        {
            var conflictDetail = JsonSerializer.Serialize(new
            {
                wo_id = id,
                wo_no = wo.WoNo,
                attempted_action = attemptedAction,
                client_version = clientEtagRaw,
                server_version = serverEtagRaw,
            });
            await _audit.EmitAsync(
                action: AuditAction.WoStateConflict,
                actor: actor,
                actorRole: role,
                targetType: "WorkOrder",
                targetId: id.ToString(),
                detail: conflictDetail);

            Response.Headers.ETag = $"\"{serverEtagRaw}\"";
            return (Conflict(new IpqcSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = serverEtagRaw,
                MesPhase = wo.MesPhase,
            }), null);
        }

        return (null, wo);
    }

    // ── Commit + audit ─────────────────────────────────────────────

    private async Task<IActionResult> CommitAndAuditAsync(
        long woId, WorkOrder wo, WoIpqcCheck check,
        string actor, string role,
        string action, object extraDetail)
    {
        // Touch the WO row so the SQLite UPDATE trigger bumps RowVersion
        // + EF [Timestamp] concurrency check fires under parallel writes
        // (matches 7c-2 atomic pattern).
        wo.UpdatedAt = DateTime.UtcNow;
        wo.UpdatedBy = actor;

        try
        {
            // SINGLE SaveChanges commits the check mutation + WO touch
            // atomically. Race losers throw here.
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return await HandleConcurrencyAsync(woId, wo);
        }

        // Post-save: re-read RowVersion via AsNoTracking (Lesson L11).
        var freshRowVersion = await _db.WorkOrders.Where(w => w.Id == woId).AsNoTracking()
            .Select(w => w.RowVersion).SingleOrDefaultAsync();
        var newEtagRaw = freshRowVersion is not null && freshRowVersion.Length > 0
            ? Convert.ToBase64String(freshRowVersion) : "";

        Response.Headers.ETag = $"\"{newEtagRaw}\"";

        var (ready, allOk, anyNg) = IpqcReadinessRollup.Compute(check);

        var detailObj = new
        {
            wo_id = woId,
            wo_no = wo.WoNo,
            mes_phase_after = wo.MesPhase,
            extra = extraDetail,
        };
        await _audit.EmitAsync(
            action: action,
            actor: actor,
            actorRole: role,
            targetType: "WorkOrder",
            targetId: woId.ToString(),
            detail: JsonSerializer.Serialize(detailObj));

        return Ok(new IpqcSetResponse
        {
            Ok = true,
            ETag = newEtagRaw,
            MesPhase = wo.MesPhase,
            IsReadyForJudgment = ready,
            AllOk = allOk,
            AnyNg = anyNg,
        });
    }

    private async Task<IActionResult> HandleConcurrencyAsync(long woId, WorkOrder wo)
    {
        // L12 — clear change tracker BEFORE the next read so downstream
        // middleware SaveChanges doesn't replay the failed write.
        if (_db is Microsoft.EntityFrameworkCore.DbContext dbCtx)
            dbCtx.ChangeTracker.Clear();

        var freshRv = await _db.WorkOrders.Where(w => w.Id == woId).AsNoTracking()
            .Select(w => new { w.RowVersion, w.MesPhase })
            .SingleOrDefaultAsync();
        var freshEtag = freshRv?.RowVersion is not null && freshRv.RowVersion.Length > 0
            ? Convert.ToBase64String(freshRv.RowVersion) : "";
        Response.Headers.ETag = $"\"{freshEtag}\"";
        return Conflict(new IpqcSetResponse
        {
            Ok = false,
            ErrorCode = "wo.state_conflict",
            ETag = freshEtag,
            MesPhase = freshRv?.MesPhase ?? wo.MesPhase,
        });
    }

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
