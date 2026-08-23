using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Policies;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.Prepress;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.7b-2 — PREPRESS row-level write surface per contract §5.2.
///
/// Endpoints:
///   GET /api/v2/work-orders/{id}/prepress           Read 3 child sets + rollup
///   PUT /api/v2/work-orders/{id}/materials/{idx}    Set one wo_materials row
///   PUT /api/v2/work-orders/{id}/plate-check        Set the 1:1 plate row
///   PUT /api/v2/work-orders/{id}/cutter-check       Set the 1:1 cutter row
///
/// Authorization: any authenticated user. The contract §2.2 row PREPRESS
/// says "Exit by OP role"; for in-PREPRESS row writes the 7b-2 surface
/// stays at base auth (per Henry breakdown adj — no 4-eye in 7b).
///
/// Concurrency contract mirrors /advance from 7a-1.3:
///   428 missing If-Match
///   409 wo.state_conflict on stale If-Match (+ WO_STATE_CONFLICT audit)
///   400 missing Idempotency-Key
///   422 invalid_phase / invalid_status / invalid_reason_code / invalid_body
///
/// Rollup recompute fires inside the SAME SaveChanges as the child row
/// write so SQLite write-lock serialises concurrent operators — closes
/// the "2 ops, 2 rows, both flip rollup wrong" race condition Henry
/// flagged as critical condition #1 in the breakdown.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/work-orders")]
public sealed class PrepressController : WoMutationControllerBase
{
    private readonly PrepressBomSnapshotService _snapshot;
    private readonly MaterialLotScanService _lots;
    private readonly Services.WoMutationExecutor _executor;

    public PrepressController(
        IMesDbContext db,
        IAuditWriter audit,
        PrepressBomSnapshotService snapshot,
        MaterialLotScanService lots,
        Services.WoMutationExecutor executor)
        : base(db, audit)
    {
        _snapshot = snapshot;
        _lots = lots;
        _executor = executor;
    }

    // ── GET /work-orders/{id}/prepress ─────────────────────────────

    [HttpGet("{id:long}/prepress")]
    public async Task<ActionResult<PrepressView>> Get(long id)
    {
        var wo = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
        if (wo is null)
            return NotFound(ApiError.Of("wo.not_found", $"No work order with id {id}."));

        // Lazy materialise on first read so legacy WOs without a snapshot
        // get rows fluently. Idempotent: NOOP when rows already exist.
        await _snapshot.MaterializeAsync(id);

        var materials = await _db.WoMaterials.AsNoTracking()
            .Where(m => m.WorkOrderId == id)
            .OrderBy(m => m.BomLineIdx)
            .ToListAsync();
        var plate = await _db.WoPlateChecks.AsNoTracking()
            .SingleOrDefaultAsync(p => p.WorkOrderId == id);
        var cutter = await _db.WoCutterChecks.AsNoTracking()
            .SingleOrDefaultAsync(c => c.WorkOrderId == id);

        var freshRv = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => w.RowVersion)
            .SingleOrDefaultAsync();
        var etag = freshRv is not null && freshRv.Length > 0
            ? Convert.ToBase64String(freshRv) : "";

        return Ok(new PrepressView
        {
            WoId = id,
            WoNo = wo.WoNo,
            MesPhase = wo.MesPhase,
            MaterialsReady = wo.MaterialsReady,
            ETag = etag,
            Materials = materials.Select(m => MaterialToDto(m, wo.TargetQty)).ToList(),
            PlateCheck = plate is null ? null : PlateToDto(plate),
            CutterCheck = cutter is null ? null : CutterToDto(cutter),
        });
    }

    // ── PUT /materials/{bom_line_idx} ──────────────────────────────

    [HttpPut("{id:long}/materials/{bomLineIdx:int}")]
    public async Task<IActionResult> PutMaterial(
        long id, int bomLineIdx, [FromBody] SetPrepressMaterialRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "prepress_material_set");
        if (pre.Error is not null) return pre.Error;

        var statusParse = PrepressPolicy.ParseStatus(req?.Status);
        if (!statusParse.IsValid) return Invalid(statusParse.ErrorCode!, statusParse.ErrorMessage!);
        var newStatus = statusParse.Status;

        var ngErr = await ValidateNgAsync(newStatus, req?.NgReasonCode, req?.NgNote);
        if (ngErr is not null) return ngErr;

        var row = await _db.WoMaterials
            .FirstOrDefaultAsync(m => m.WorkOrderId == id && m.BomLineIdx == bomLineIdx);
        if (row is null)
            return NotFound(ApiError.Of("wo.material_row_not_found",
                $"No wo_materials row for wo_id={id}, bom_line_idx={bomLineIdx}."));

        var fromStatus = row.Status;
        row.Status = newStatus;
        row.QtyLoaded = req?.QtyLoaded ?? row.QtyLoaded;
        // A1 §3.3 — mã lô KHÔNG còn được gán thẳng từ body vào cột nữa. Chuỗi đi
        // qua MaterialLotScanService: chuẩn hoá → thử resolve về một MaterialLot
        // thật → set FK MaterialLotId + mirror LotNo TỪ ENTITY LÔ. Chưa resolve
        // được thì vẫn giữ nhãn kèm warning (siết read-only là Phase 3, sau
        // go-live); cờ Mes:MaterialLot:EnforceReleased bật thì từ chối.
        string? lotWarning = null;
        if (req?.LotNo is not null)
        {
            var attach = await _lots.AttachLotLabelAsync(row, req.LotNo, actor, role);
            if (!attach.Ok)
                return UnprocessableEntity(ApiError.Of(attach.ErrorCode!, attach.MessageEn!));
            lotWarning = attach.Warning;
        }
        // Persist scanned part + BOM-resolved description ONLY when the request
        // carried them (a plain OK/NG must not wipe a previously-scanned code).
        if (!string.IsNullOrWhiteSpace(req?.PartScan)) row.PartScan = req!.PartScan;
        if (!string.IsNullOrWhiteSpace(req?.PartScanDescription)) row.PartScanDescription = req!.PartScanDescription;
        row.NgReasonCode = newStatus == PrepressCheckStatus.Ng ? req!.NgReasonCode : null;
        row.NgNote = newStatus == PrepressCheckStatus.Ng ? req!.NgNote : null;
        row.CheckedBy = actor;
        row.CheckedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = actor;

        return await CommitAndAuditAsync(id, pre.WoForUpdate!,
            AuditAction.WoPrepressMaterialSet, actor, role,
            extraDetail: new
            {
                bom_line_idx = bomLineIdx,
                material_code = row.MaterialCode,
                from_status = fromStatus.ToString(),
                to_status = newStatus.ToString(),
                qty_loaded = row.QtyLoaded,
                lot_no = row.LotNo,
                lot_warning = lotWarning,
                part_scan = row.PartScan,
                part_scan_description = row.PartScanDescription,
                ng_reason_code = row.NgReasonCode,
                ng_note = row.NgNote,
            });
    }

    // ── POST /materials/{bom_line_idx}/special-accept ──────────────
    // A PD leader (Engineer) or Supervisor concedes a material despite a
    // defect: the row is recorded OK (so MaterialsReady can roll up and the
    // WO can advance) but the deviation is retained (NgReasonCode + note) and
    // audited with special_accept=true. Role-gated to Admin/Supervisor/Engineer.

    private static readonly string[] SpecialAcceptRoles =
        { UserRole.Admin, UserRole.Supervisor, UserRole.Engineer };

    [HttpPost("{id:long}/materials/{bomLineIdx:int}/special-accept")]
    public async Task<IActionResult> SpecialAcceptMaterial(
        long id, int bomLineIdx, [FromBody] SpecialAcceptMaterialRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        if (!SpecialAcceptRoles.Contains(role))
            return StatusCode(StatusCodes.Status403Forbidden, ApiError.Of(
                "prepress.special_accept_forbidden",
                "Only a PD leader (Engineer) or Supervisor can special-accept a material."));

        var pre = await PreludeAsync(id, actor, role, "prepress_material_special_accept");
        if (pre.Error is not null) return pre.Error;

        // Special accept needs a real defect code; the justification note is
        // OPTIONAL (unlike Mark NG, which requires a note).
        if (string.IsNullOrWhiteSpace(req?.NgReasonCode))
            return UnprocessableEntity(ApiError.Of("prepress.invalid_reason_code",
                "A defect code is required to special-accept."));
        if (req.Note is { Length: > 500 })
            return UnprocessableEntity(ApiError.Of("prepress.invalid_ng_note",
                "Note must be 500 characters or fewer."));
        var reasonOk = await _db.ReasonCodes.AsNoTracking()
            .AnyAsync(r => r.Code == req.NgReasonCode && r.Kind == ReasonCodeKind.Scrap);
        if (!reasonOk)
            return UnprocessableEntity(ApiError.Of("prepress.invalid_reason_code",
                $"NgReasonCode '{req.NgReasonCode}' is not a registered Scrap reason."));

        var row = await _db.WoMaterials
            .FirstOrDefaultAsync(m => m.WorkOrderId == id && m.BomLineIdx == bomLineIdx);
        if (row is null)
            return NotFound(ApiError.Of("wo.material_row_not_found",
                $"No wo_materials row for wo_id={id}, bom_line_idx={bomLineIdx}."));

        // Concession → OK, but keep the deviation on the row for the record.
        row.Status = PrepressCheckStatus.Ok;
        row.NgReasonCode = req!.NgReasonCode;
        row.NgNote = req.Note;
        // Persist the scanned code + resolve the description server-side from
        // the BOM row (the scan string may be a bare code with no description).
        if (!string.IsNullOrWhiteSpace(req.PartScan))
        {
            row.PartScan = req.PartScan;
            row.PartScanDescription = row.MaterialDescription;
        }
        row.CheckedBy = actor;
        row.CheckedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = actor;

        return await CommitAndAuditAsync(id, pre.WoForUpdate!,
            AuditAction.WoPrepressMaterialSet, actor, role,
            extraDetail: new
            {
                bom_line_idx = bomLineIdx,
                material_code = row.MaterialCode,
                to_status = "Ok",
                special_accept = true,
                approver = actor,
                approver_role = role,
                ng_reason_code = row.NgReasonCode,
                note = row.NgNote,
                part_scan = row.PartScan,
                part_scan_description = row.PartScanDescription,
            });
    }

    // ── PUT /plate-check ───────────────────────────────────────────

    [HttpPut("{id:long}/plate-check")]
    public async Task<IActionResult> PutPlate(
        long id, [FromBody] SetPrepressPlateRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "prepress_plate_set");
        if (pre.Error is not null) return pre.Error;

        var statusParse = PrepressPolicy.ParseStatus(req?.Status);
        if (!statusParse.IsValid) return Invalid(statusParse.ErrorCode!, statusParse.ErrorMessage!);
        var newStatus = statusParse.Status;

        var ngErr = await ValidateNgAsync(newStatus, req?.NgReasonCode, req?.NgNote);
        if (ngErr is not null) return ngErr;

        // Plate row should exist (materialised by Get or by migration backfill).
        // If missing (shouldn't happen post-snapshot), create on demand.
        var row = await _db.WoPlateChecks.FirstOrDefaultAsync(p => p.WorkOrderId == id);
        if (row is null)
        {
            row = new WoPlateCheck { WorkOrderId = id, CreatedAt = DateTime.UtcNow };
            _db.WoPlateChecks.Add(row);
        }

        var fromStatus = row.Status;
        row.Status = newStatus;
        row.PlateNo = req?.PlateNo ?? row.PlateNo;
        row.NgReasonCode = newStatus == PrepressCheckStatus.Ng ? req!.NgReasonCode : null;
        row.NgNote = newStatus == PrepressCheckStatus.Ng ? req!.NgNote : null;
        row.CheckedBy = actor;
        row.CheckedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = actor;

        return await CommitAndAuditAsync(id, pre.WoForUpdate!,
            AuditAction.WoPrepressPlateSet, actor, role,
            extraDetail: new
            {
                from_status = fromStatus.ToString(),
                to_status = newStatus.ToString(),
                plate_no = row.PlateNo,
                ng_reason_code = row.NgReasonCode,
                ng_note = row.NgNote,
            });
    }

    // ── PUT /cutter-check ──────────────────────────────────────────

    [HttpPut("{id:long}/cutter-check")]
    public async Task<IActionResult> PutCutter(
        long id, [FromBody] SetPrepressCutterRequest? req)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, "prepress_cutter_set");
        if (pre.Error is not null) return pre.Error;

        var statusParse = PrepressPolicy.ParseStatus(req?.Status);
        if (!statusParse.IsValid) return Invalid(statusParse.ErrorCode!, statusParse.ErrorMessage!);
        var newStatus = statusParse.Status;

        var ngErr = await ValidateNgAsync(newStatus, req?.NgReasonCode, req?.NgNote);
        if (ngErr is not null) return ngErr;

        var row = await _db.WoCutterChecks.FirstOrDefaultAsync(c => c.WorkOrderId == id);
        if (row is null)
        {
            row = new WoCutterCheck { WorkOrderId = id, CreatedAt = DateTime.UtcNow };
            _db.WoCutterChecks.Add(row);
        }

        var fromStatus = row.Status;
        row.Status = newStatus;
        row.CutterNo = req?.CutterNo ?? row.CutterNo;
        row.NgReasonCode = newStatus == PrepressCheckStatus.Ng ? req!.NgReasonCode : null;
        row.NgNote = newStatus == PrepressCheckStatus.Ng ? req!.NgNote : null;
        row.CheckedBy = actor;
        row.CheckedAt = DateTime.UtcNow;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedBy = actor;

        return await CommitAndAuditAsync(id, pre.WoForUpdate!,
            AuditAction.WoPrepressCutterSet, actor, role,
            extraDetail: new
            {
                from_status = fromStatus.ToString(),
                to_status = newStatus.ToString(),
                cutter_no = row.CutterNo,
                ng_reason_code = row.NgReasonCode,
                ng_note = row.NgNote,
            });
    }

    // ── Prelude: shared If-Match / Idempotency-Key / state guard ──

    // A2 thin-controller — GIỮ INLINE (không gộp vào base). Two reasons the
    // base WoMutationControllerBase.PreludeAsync can't absorb this one:
    //   1. The PREPRESS 409 body (PrepressSetResponse) carries an extra field
    //      read off the stale `wo` — MaterialsReady — that the base onConflict
    //      factory (which only exposes serverEtag + mesPhase) can't reach.
    //   2. After the ETag-compare passes, this prelude ALSO runs a semantic
    //      PHASE GUARD (MesPhase ∈ {NEW, PREPRESS} → else 422 wo.invalid_phase)
    //      that the base prelude has no equivalent of.
    // The concurrency MECHANISM up to the ETag-compare is byte-identical to the
    // base; only the extra conflict field + phase guard keep it inline. Flagged
    // in the A2 report. NormalizeETag now resolves to the base static.
    private async Task<(IActionResult? Error, WorkOrder? WoForUpdate)> PreludeAsync(
        long id, string actor, string role, string attemptedAction)
    {
        // Idempotency-Key required for ALL writes (matches /advance).
        var idemKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idemKey))
        {
            return (BadRequest(ApiError.Of("wo.idempotency_key_required",
                "Idempotency-Key header required.")), null);
        }

        // If-Match required (428).
        var ifMatch = Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return (StatusCode(StatusCodes.Status428PreconditionRequired,
                ApiError.Of("wo.if_match_required",
                    "If-Match header required. Re-fetch /prepress for current ETag.")), null);
        }

        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo is null)
        {
            return (NotFound(ApiError.Of("wo.not_found",
                $"No work order with id {id}.")), null);
        }

        // Stale RowVersion → 409 + WO_STATE_CONFLICT audit (mirrors /advance).
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
            return (Conflict(new PrepressSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = serverEtagRaw,
                MaterialsReady = wo.MaterialsReady,
            }), null);
        }

        // State guard: MesPhase MUST be PREPRESS or NEW (§5.2). Outside →
        // 422 invalid_phase (semantic guard, NOT concurrency drift).
        if (wo.MesPhase != "PREPRESS" && wo.MesPhase != "NEW")
        {
            return (UnprocessableEntity(ApiError.Of("wo.invalid_phase",
                $"PREPRESS row writes require MesPhase ∈ {{NEW, PREPRESS}}; current = {wo.MesPhase}.")), null);
        }

        return (null, wo);
    }

    // ── Body validation: NG catalog lookup (format via PrepressPolicy) ──
    // Status parse + NG format (status→reason→note) live in PrepressPolicy
    // (pure, unit-tested, reused by materials/plate/cutter). This method keeps
    // ONLY the I/O part: the ReasonCodes(Kind=Scrap) catalog lookup, run AFTER
    // the pure format check passes — byte-identical order to the old inline.

    private async Task<IActionResult?> ValidateNgAsync(
        PrepressCheckStatus status, string? ngReasonCode, string? ngNote)
    {
        var fmtErr = PrepressPolicy.ValidateNgFormat(status, ngReasonCode, ngNote);
        if (fmtErr is not null) return Invalid(fmtErr.Value.ErrorCode, fmtErr.Value.Message);

        // Non-Ng short-circuits inside ValidateNgFormat (returns null) → no
        // catalog lookup needed; mirror that here.
        if (status != PrepressCheckStatus.Ng) return null;

        var exists = await _db.ReasonCodes.AsNoTracking()
            .AnyAsync(r => r.Code == ngReasonCode && r.Kind == ReasonCodeKind.Scrap);
        if (!exists)
        {
            return UnprocessableEntity(ApiError.Of("prepress.invalid_reason_code",
                $"NgReasonCode '{ngReasonCode}' is not a registered Scrap reason."));
        }
        return null;
    }

    // ── Commit: SaveChanges → recompute rollup → SaveChanges → audit
    //    Rollup recompute MUST read the just-saved child rows; SQLite's
    //    per-database write lock serialises concurrent operators so the
    //    rollup-mid-race bug Henry flagged can't fire — the second op
    //    waits for the first to commit before reading the materials list.

    private async Task<IActionResult> CommitAndAuditAsync(
        long woId, WorkOrder wo,
        string action, string actor, string role,
        object extraDetail)
    {
        // Read the WO's child surfaces using TRACKED queries so the
        // tracked entity the caller just mutated is reflected in the
        // local collection (DbSet queries return tracked instances
        // when they're already in the change tracker). AsNoTracking
        // would bypass the tracker + return stale DB values + the
        // rollup would mis-flip.
        var materials = await _db.WoMaterials
            .Where(m => m.WorkOrderId == woId).ToListAsync();
        var plate = await _db.WoPlateChecks
            .SingleOrDefaultAsync(p => p.WorkOrderId == woId);
        var cutter = await _db.WoCutterChecks
            .SingleOrDefaultAsync(c => c.WorkOrderId == woId);

        var (hasSnap, allOk) = MaterialsReadinessRollup.Compute(materials, plate, cutter);

        // Always touch the WO row so the SQLite UPDATE trigger bumps
        // RowVersion + EF's [Timestamp] concurrency check fires correctly under
        // parallel writes. Child-only writes that don't touch WO leave its
        // RowVersion stale + 10 concurrent ops would all "succeed" on the same
        // If-Match. Touch UpdatedAt unconditionally; set MaterialsReady
        // conditionally. Then hand the atomic save + L45 conflict path to the
        // shared executor (A2 — SaveChanges lives in Services/, not the controller).
        wo.UpdatedAt = DateTime.UtcNow;
        wo.UpdatedBy = actor;
        if (hasSnap)
        {
            wo.MaterialsReady = allOk;
        }

        var outcome = await _executor.SaveAndResolveAsync(HttpContext, woId, wo.WoNo, actor, role, action);
        if (outcome.Conflict)
            return Conflict(new PrepressSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = outcome.ETag,
                MaterialsReady = wo.MaterialsReady,
            });

        // Audit emit per row write.
        var detailObj = new
        {
            wo_id = woId,
            wo_no = wo.WoNo,
            materials_ready_after = wo.MaterialsReady,
            extra = extraDetail,
        };
        await _audit.EmitAsync(
            action: action,
            actor: actor,
            actorRole: role,
            targetType: "WorkOrder",
            targetId: woId.ToString(),
            detail: JsonSerializer.Serialize(detailObj));

        return Ok(new PrepressSetResponse
        {
            Ok = true,
            MaterialsReady = wo.MaterialsReady,
            ETag = outcome.ETag,
        });
    }

    // ── DTO mappers ────────────────────────────────────────────────

    private static PrepressMaterialRow MaterialToDto(WoMaterial m, int targetQty = 0) => new()
    {
        Id = m.Id,
        BomLineIdx = m.BomLineIdx,
        MaterialCode = m.MaterialCode,
        MaterialDescription = m.MaterialDescription,
        // QPA = QtyAssembly from the BOM. QtyRequired was stored as
        // QtyAssembly × TargetQty, so divide back to recover the per-assembly
        // figure exactly (guard against a 0 target).
        Qpa = targetQty != 0 ? m.QtyRequired / targetQty : 0,
        QtyRequired = m.QtyRequired,
        Uom = m.Uom,
        ScrapFactor = m.ScrapFactor,
        ScrapPercent = m.ScrapPercent,
        QtyLoaded = m.QtyLoaded,
        LotNo = m.LotNo,
        PartScan = m.PartScan,
        PartScanDescription = m.PartScanDescription,
        Status = m.Status.ToString(),
        NgReasonCode = m.NgReasonCode,
        NgNote = m.NgNote,
        CheckedBy = m.CheckedBy,
        CheckedAt = m.CheckedAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(m.CheckedAt.Value, DateTimeKind.Utc)),
    };

    private static PrepressPlateRow PlateToDto(WoPlateCheck p) => new()
    {
        Id = p.Id,
        Status = p.Status.ToString(),
        PlateNo = p.PlateNo,
        NgReasonCode = p.NgReasonCode,
        NgNote = p.NgNote,
        CheckedBy = p.CheckedBy,
        CheckedAt = p.CheckedAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(p.CheckedAt.Value, DateTimeKind.Utc)),
    };

    private static PrepressCutterRow CutterToDto(WoCutterCheck c) => new()
    {
        Id = c.Id,
        Status = c.Status.ToString(),
        CutterNo = c.CutterNo,
        NgReasonCode = c.NgReasonCode,
        NgNote = c.NgNote,
        CheckedBy = c.CheckedBy,
        CheckedAt = c.CheckedAt is null ? null : new DateTimeOffset(DateTime.SpecifyKind(c.CheckedAt.Value, DateTimeKind.Utc)),
    };
}
