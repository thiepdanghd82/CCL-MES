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
using Microsoft.Extensions.Options;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// IPQC first-article — MATERIAL (SYSTEM) reconciliation write surface
/// (Henry 2026-08-25). Reconciles the LOT the operator scanned in PREPRESS
/// against the LOT IQC released; a divergence is a SOFT lock cleared by an
/// Engineer waiver. Thin controller: all DbContext access + join + view-build
/// live in <see cref="IpqcMaterialMaterializer"/>; SaveChanges in
/// <see cref="WoMutationExecutor"/>; mutation helpers in
/// <see cref="WoIpqcMaterialCheckService"/>.
///
/// Endpoints:
///   GET  {id}/ipqc/material-system                          IpqcSubmit — read grid
///   PUT  {id}/ipqc/material-system/{bomLineIdx}             IpqcSubmit — confirm OK/NG (freeze snapshot)
///   POST {id}/ipqc/material-system/{bomLineIdx}/approve-divergence  EngineerWaive — waiver decision
///
/// Atomic pattern + concurrency codes identical to IpqcReviewController
/// (428/400/404/409/422 + single SaveChanges + WO-row touch + audit-after-save).
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/work-orders")]
public sealed class WoIpqcMaterialController : WoMutationControllerBase
{
    private readonly Services.WoMutationExecutor _executor;
    private readonly Services.IpqcMaterialMaterializer _materializer;
    private readonly IpqcMaterialWaiverOptions _waiver;

    public WoIpqcMaterialController(
        IMesDbContext db,
        IAuditWriter audit,
        Services.WoMutationExecutor executor,
        Services.IpqcMaterialMaterializer materializer,
        IOptions<IpqcMaterialWaiverOptions> waiver)
        : base(db, audit)
    {
        _executor = executor;
        _materializer = materializer;
        _waiver = waiver.Value;
    }

    // ── GET {id}/ipqc/material-system ──────────────────────────────
    [HttpGet("{id:long}/ipqc/material-system"), Authorize(Policy = "IpqcSubmit")]
    public async Task<IActionResult> Get(long id, CancellationToken ct = default)
    {
        var view = await _materializer.GetViewAsync(id, ct);
        if (view is null)
            return NotFound(ApiError.Of("wo.not_found", $"No work order with id {id}."));
        return Ok(view);
    }

    // ── PUT {id}/ipqc/material-system/{bomLineIdx} (confirm OK/NG) ──
    [HttpPut("{id:long}/ipqc/material-system/{bomLineIdx:int}"), Authorize(Policy = "IpqcSubmit")]
    public async Task<IActionResult> Confirm(
        long id, int bomLineIdx, [FromBody] SetIpqcMaterialRequest? req, CancellationToken ct = default)
    {
        var actor = ActorName();
        var role = ActorRole();

        var pre = await PreludeAsync(id, actor, role, $"ipqc_material_{bomLineIdx}");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "IPQC_WAIT")
            return Invalid("wo.invalid_phase",
                $"ipqc/material-system requires MesPhase = IPQC_WAIT; current = {wo.MesPhase}.");

        if (req is null || string.IsNullOrWhiteSpace(req.Status))
            return Invalid("ipqc.invalid_status", "Status is required (\"Ok\" or \"Ng\").");
        if (!Enum.TryParse<IpqcCheckStatus>(req.Status, ignoreCase: true, out var status)
            || status == IpqcCheckStatus.Pending)
            return Invalid("ipqc.invalid_status", $"Status must be \"Ok\" or \"Ng\"; got \"{req.Status}\".");

        if (status == IpqcCheckStatus.Ng)
        {
            if (string.IsNullOrWhiteSpace(req.NgReasonCode))
                return Invalid("ipqc.invalid_reason_code", "NgReasonCode is required when Status = Ng.");
            if (string.IsNullOrWhiteSpace(req.NgNote) || req.NgNote!.Length > 500)
                return Invalid("ipqc.invalid_ng_note", "NgNote must be 1-500 chars when Status = Ng.");
            if (!await _materializer.IsValidScrapReasonAsync(req.NgReasonCode!, ct))
                return Invalid("ipqc.invalid_reason_code",
                    $"NgReasonCode \"{req.NgReasonCode}\" is not a registered Scrap reason.");
        }

        var rows = await _materializer.GetOrCreateRowsForMutationAsync(id, ct);
        var row = rows.FirstOrDefault(r => r.BomLineIdx == bomLineIdx);
        if (row is null)
            return Invalid("ipqc.invalid_material_line",
                $"BOM line {bomLineIdx} is not a material of WO {id}.");

        var snap = await _materializer.ComputeSnapshotAsync(id, bomLineIdx, ct);
        if (snap is null)
            return Invalid("ipqc.invalid_material_line",
                $"BOM line {bomLineIdx} is not a material of WO {id}.");

        WoIpqcMaterialCheckService.Confirm(row, status, snap.Value,
            req.NgReasonCode, req.NgNote, actor, DateTime.UtcNow);

        return await CommitAndAuditAsync(id, wo, rows, row, actor, role,
            AuditAction.WoIpqcMaterialCheck,
            new
            {
                bom_line_idx = bomLineIdx,
                material_code = row.MaterialCode,
                status = status.ToString(),
                divergence_kind = row.DivergenceKind,
                source_iqc_receipt_no = row.SourceIqcReceiptNo,
                actual_lot_no = row.ActualLotNo,
                ng_reason_code = status == IpqcCheckStatus.Ng ? req.NgReasonCode : null,
                ng_note = status == IpqcCheckStatus.Ng ? req.NgNote : null,
                requires_waiver = row.DivergenceApprovalStatus == DivergenceApprovalStatus.PendingEngineer,
            });
    }

    // ── POST {id}/ipqc/material-system/{bomLineIdx}/approve-divergence ──
    [HttpPost("{id:long}/ipqc/material-system/{bomLineIdx:int}/approve-divergence"),
     Authorize(Policy = "EngineerWaive")]
    public async Task<IActionResult> ApproveDivergence(
        long id, int bomLineIdx, [FromBody] ApproveDivergenceRequest? req, CancellationToken ct = default)
    {
        var actor = ActorName();
        var role = ActorRole();

        var pre = await PreludeAsync(id, actor, role, $"ipqc_material_waiver_{bomLineIdx}");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "IPQC_WAIT")
            return Invalid("wo.invalid_phase",
                $"approve-divergence requires MesPhase = IPQC_WAIT; current = {wo.MesPhase}.");

        if (req is null || string.IsNullOrWhiteSpace(req.Outcome)
            || !TryParseOutcome(req.Outcome, out var approve))
            return Invalid("material.invalid_outcome", "Outcome must be \"Approve\" or \"Reject\".");
        if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason!.Length > 500)
            return Invalid("material.invalid_reason", "Reason is required (1-500 chars).");

        var rows = await _materializer.GetOrCreateRowsForMutationAsync(id, ct);
        var row = rows.FirstOrDefault(r => r.BomLineIdx == bomLineIdx);
        if (row is null)
            return Invalid("ipqc.invalid_material_line",
                $"BOM line {bomLineIdx} is not a material of WO {id}.");

        // Only a frozen-divergent row (PendingEngineer / already decided) can be
        // waived — a matched row has nothing to waive.
        if (row.DivergenceApprovalStatus == DivergenceApprovalStatus.NotRequired)
            return Invalid("material.not_divergent",
                $"BOM line {bomLineIdx} has no divergence to waive (confirm it first, or the lot matched IQC).");

        // Q1 dual-sig — approver must differ from the confirmer.
        if (!WoIpqcMaterialCheckService.ValidateDistinctWaiver(
                row.ConfirmedBy, actor, _waiver.RequireDistinctMaterialWaiver))
        {
            await _audit.EmitAsync(
                action: AuditAction.WoIpqcMaterialApproveDenied,
                actor: actor, actorRole: role,
                targetType: "WorkOrder", targetId: id.ToString(),
                detail: JsonSerializer.Serialize(new
                {
                    wo_id = id,
                    wo_no = wo.WoNo,
                    bom_line_idx = bomLineIdx,
                    reason = "same_user_as_confirmer",
                    attempted_by = actor,
                    confirmed_by = row.ConfirmedBy,
                }));
            return Invalid("material.same_user_as_confirmer",
                "Người phê duyệt waiver không được trùng người xác nhận vật tư — nguyên tắc 4-mắt.");
        }

        WoIpqcMaterialCheckService.ApproveDivergence(row, approve, req.Reason!, actor, DateTime.UtcNow);

        return await CommitAndAuditAsync(id, wo, rows, row, actor, role,
            AuditAction.WoIpqcMaterialApprove,
            new
            {
                bom_line_idx = bomLineIdx,
                material_code = row.MaterialCode,
                divergence_kind = row.DivergenceKind,
                outcome = approve ? "Approve" : "Reject",
                approval_reason = req.Reason,
                confirmed_by = row.ConfirmedBy,
                approved_by = actor,
                flag_state = _waiver.FlagState,
            });
    }

    private static bool TryParseOutcome(string raw, out bool approve)
    {
        switch (raw.Trim().ToLowerInvariant())
        {
            case "approve": approve = true; return true;
            case "reject": approve = false; return true;
            default: approve = false; return false;
        }
    }

    // ── Prelude (typed 409 body) ───────────────────────────────────
    private Task<(IActionResult? Error, WorkOrder? WoForUpdate)> PreludeAsync(
        long id, string actor, string role, string attemptedAction)
        => base.PreludeAsync(id, actor, role, attemptedAction,
            (wo, etag) => Conflict(new IpqcMaterialSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = etag,
                MesPhase = wo?.MesPhase ?? "",
            }));

    // ── Commit + audit ─────────────────────────────────────────────
    private async Task<IActionResult> CommitAndAuditAsync(
        long woId, WorkOrder wo, List<WoIpqcMaterialCheck> rows, WoIpqcMaterialCheck row,
        string actor, string role, string action, object extraDetail)
    {
        wo.UpdatedAt = DateTime.UtcNow;
        wo.UpdatedBy = actor;

        var outcome = await _executor.SaveAndResolveAsync(HttpContext, woId, wo.WoNo, actor, role, action);
        if (outcome.Conflict)
            return Conflict(new IpqcMaterialSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = outcome.ETag,
                MesPhase = outcome.Fresh?.MesPhase ?? wo.MesPhase,
            });

        var (allResolved, anyPending, anyRejected) = IpqcMaterialRollup.Compute(rows);

        await _audit.EmitAsync(
            action: action, actor: actor, actorRole: role,
            targetType: "WorkOrder", targetId: woId.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                wo_id = woId,
                wo_no = wo.WoNo,
                mes_phase_after = wo.MesPhase,
                extra = extraDetail,
            }));

        return Ok(new IpqcMaterialSetResponse
        {
            Ok = true,
            ETag = outcome.ETag,
            MesPhase = wo.MesPhase,
            AllResolved = allResolved,
            AnyPendingWaiver = anyPending,
            AnyRejected = anyRejected,
            RowApprovalStatus = row.DivergenceApprovalStatus.ToString(),
        });
    }
}
