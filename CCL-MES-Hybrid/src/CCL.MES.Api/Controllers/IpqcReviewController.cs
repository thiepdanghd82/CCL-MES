using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Api.Policies;
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
public sealed class IpqcReviewController : WoMutationControllerBase
{
    private readonly IpqcDualSigOptions _dualSig;
    private readonly Services.ITraceFreezeService _trace;
    private readonly Services.WoMutationExecutor _executor;
    private readonly Services.IpqcCheckMaterializer _materializer;
    private readonly Services.IpqcMaterialMaterializer _materialChecks;

    public IpqcReviewController(
        IMesDbContext db,
        IAuditWriter audit,
        IOptions<IpqcDualSigOptions> dualSig,
        Services.ITraceFreezeService trace,
        Services.WoMutationExecutor executor,
        Services.IpqcCheckMaterializer materializer,
        Services.IpqcMaterialMaterializer materialChecks)
        : base(db, audit)
    {
        _dualSig = dualSig.Value;
        _trace = trace;
        _executor = executor;
        _materializer = materializer;
        _materialChecks = materialChecks;
    }

    // Best-effort trace freeze — never breaks the confirm; idempotent in service.
    private async Task FreezeSafe(long woId, string phase, string actor)
    {
        try { await _trace.FreezeAsync(woId, phase, actor); }
        catch { /* best-effort */ }
    }

    // ── GET /work-orders/{id}/ipqc ─────────────────────────────────

    /// <summary>P10.7d-2 — read view for the IPQC + QA dashboards.
    /// Lazy-materialises a Pending row on first read so legacy WOs
    /// (those that didn't get the migration backfill because they were
    /// in PREPRESS at migration time, then later advanced) still render
    /// correctly. Idempotent — controller serialises with the UNIQUE
    /// index so concurrent first-reads can't double-insert.</summary>
    [HttpGet("{id:long}/ipqc")]
    public async Task<IActionResult> Get(long id, long? legId = null, CancellationToken ct = default)
    {
        var wo = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wo is null)
            return NotFound(ApiError.Of("wo.not_found", $"No work order with id {id}."));

        // P11 per-leg: when a legId is given, read THAT leg's own IPQC check
        // (materialised at fork, per leg.ProcessLine). Short-circuit the WO-level
        // lazy/self-heal flow. Idempotent ensure so a legacy fork still gets rows.
        if (legId is not null)
        {
            await new IpqcLegMaterializer(_db).MaterializeForLegAsync(legId.Value, ct);
            var legCheck = await _db.WoIpqcChecks.AsNoTracking().Include(c => c.Items)
                .FirstOrDefaultAsync(c => EF.Property<long?>(c, "WoLegId") == legId, ct);
            return Ok(BuildView(wo, legCheck,
                (legCheck?.Items.Count ?? 0) > 0 ? Services.IpqcCheckMaterializer.Materialized : "LegacyManual"));
        }

        // WO-level (1-leg / legacy) — lazy-materialise + auto-sync + self-heal
        // moved to IpqcCheckMaterializer (SaveChanges out of the controller).
        // Byte-identical GET behaviour.
        var (check, autoSync) = await _materializer.EnsureForGetAsync(wo, ct);
        return Ok(BuildView(wo, check, autoSync));
    }

    /// <summary>Build the IPQC view DTO from a check (WO-level or per-leg).
    /// Extracted so the per-leg branch reuses the exact same shape.</summary>
    private static IpqcView BuildView(WorkOrder wo, WoIpqcCheck? check, string autoSync)
    {
        var etag = Convert.ToBase64String(wo.RowVersion);
        var checkItems = check?.Items?.OrderBy(i => i.Sort).ToList() ?? new List<WoIpqcCheckItem>();
        var (ready, allOk, anyNg) = IpqcReadinessRollup.Compute(check, checkItems);

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
            ResolvedLines = check?.ResolvedLines,
            AutoSyncStatus = autoSync,
            Items = checkItems.Select(i => new IpqcViewItem
            {
                ItemKey = i.ItemKey,
                ProcessLine = i.ProcessLine,
                GroupLabel = i.GroupLabel,
                Label = i.Label,
                AcceptanceCriteria = i.AcceptanceCriteria,
                Method = i.Method,
                Severity = i.Severity,
                DefectCode = i.DefectCode,
                Status = i.Status.ToString(),
                NgReasonCode = i.NgReasonCode,
                NgNote = i.NgNote,
                CheckType = i.CheckType,
                MeasuredValue = i.MeasuredValue,
            }).ToList(),
        };

        return view;
    }

    // ── PUT /work-orders/{id}/ipqc/{slot} (IpqcSubmit policy) ─────

    [HttpPut("{id:long}/ipqc/material"), Authorize(Policy = "IpqcSubmit")]
    public Task<IActionResult> PutMaterialSlot(long id, [FromBody] SetIpqcSlotRequest? req) =>
        PutSlotAsync(id, WoIpqcCheckService.CheckSlot.Material, req);

    [HttpPut("{id:long}/ipqc/print-a"), Authorize(Policy = "IpqcSubmit")]
    public Task<IActionResult> PutPrintASlot(long id, [FromBody] SetIpqcSlotRequest? req) =>
        PutSlotAsync(id, WoIpqcCheckService.CheckSlot.PrintA, req);

    [HttpPut("{id:long}/ipqc/print-b"), Authorize(Policy = "IpqcSubmit")]
    public Task<IActionResult> PutPrintBSlot(long id, [FromBody] SetIpqcSlotRequest? req) =>
        PutSlotAsync(id, WoIpqcCheckService.CheckSlot.PrintB, req);

    [HttpPut("{id:long}/ipqc/print-c"), Authorize(Policy = "IpqcSubmit")]
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

        var check = await _materializer.GetOrCreateForMutationAsync(id, wo);

        // F1 (finding #1): WO đã materialize bộ item data-driven → slot-PUT legacy
        // vô nghĩa (rollup tính theo items, sẽ bỏ qua slot). Từ chối thẳng để 2 đường
        // ghi không phân kỳ; operator phải dùng PUT /ipqc/item/{itemKey}.
        if (check.Items.Count > 0)
            return Invalid("ipqc.slot_write_in_item_mode",
                "WO này dùng IPQC data-driven (auto-sync). Dùng endpoint /ipqc/item/{itemKey}, không phải slot legacy.");

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

    // ── PUT /work-orders/{id}/ipqc/item/{itemKey} (Phương án C B4) ──

    /// <summary>Đánh OK/NG cho 1 hạng mục IPQC data-driven (auto-sync).
    /// Cùng hợp đồng atomic + NG-validation như slot legacy; 422
    /// <c>ipqc.invalid_item</c> nếu key không thuộc bộ đã materialize.</summary>
    [HttpPut("{id:long}/ipqc/item/{itemKey}"), Authorize(Policy = "IpqcSubmit")]
    public async Task<IActionResult> PutItem(
        long id, string itemKey, [FromBody] SetIpqcItemRequest? req, long? legId = null)
    {
        var actor = User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";

        var pre = await PreludeAsync(id, actor, role, $"ipqc_item_{itemKey}");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        // Phase gate: per-leg keys off the LEG's phase (WO stays in SPLIT while
        // legs run their own flow); WO-level keys off wo.MesPhase (unchanged).
        WoLeg? leg = null;
        if (legId is not null)
        {
            leg = await _db.WoLegs.FirstOrDefaultAsync(l => l.Id == legId && l.WorkOrderId == id);
            if (leg is null) return NotFound(ApiError.Of("leg.not_found", $"No leg {legId} on WO {id}."));
            if (leg.LegPhase != "IPQC_WAIT")
                return Invalid("leg.invalid_phase",
                    $"ipqc/item requires the leg's LegPhase = IPQC_WAIT; current = {leg.LegPhase}.");
        }
        else if (wo.MesPhase != "IPQC_WAIT")
        {
            return Invalid("wo.invalid_phase",
                $"ipqc/item requires MesPhase = IPQC_WAIT; current = {wo.MesPhase}.");
        }

        if (req is null || string.IsNullOrWhiteSpace(req.Status))
            return Invalid("ipqc.invalid_status", "Status is required (\"Ok\" or \"Ng\").");
        if (!Enum.TryParse<IpqcCheckStatus>(req.Status, ignoreCase: true, out var status)
            || status == IpqcCheckStatus.Pending)
            return Invalid("ipqc.invalid_status",
                $"Status must be \"Ok\" or \"Ng\"; got \"{req.Status}\".");

        if (status == IpqcCheckStatus.Ng)
        {
            var ngErr = await ValidateNgAsync(req.NgReasonCode, req.NgNote);
            if (ngErr is not null) return ngErr;
        }

        var check = legId is null
            ? await _materializer.GetOrCreateForMutationAsync(id, wo)
            : await GetOrCreateLegCheckAsync(legId.Value);

        var ok = WoIpqcCheckService.SetItem(check, itemKey, status,
            status == IpqcCheckStatus.Ng ? req.NgReasonCode : null,
            status == IpqcCheckStatus.Ng ? req.NgNote : null,
            actor, DateTime.UtcNow, req.MeasuredValue);
        if (!ok)
            return Invalid("ipqc.invalid_item",
                $"Item \"{itemKey}\" không thuộc bộ hạng mục IPQC của WO này.");

        return await CommitAndAuditAsync(id, wo, check, actor, role,
            AuditAction.WoIpqcCheck,
            new
            {
                wo_leg_id = legId,
                item_key = itemKey,
                status = status.ToString(),
                ng_reason_code = status == IpqcCheckStatus.Ng ? req.NgReasonCode : null,
                ng_note = status == IpqcCheckStatus.Ng ? req.NgNote : null,
            });
    }

    /// <summary>P11 per-leg: the leg's own IPQC check (materialised at fork by
    /// leg.ProcessLine). Idempotently ensures it exists, then returns it TRACKED
    /// so SetItem mutations persist.</summary>
    private async Task<WoIpqcCheck> GetOrCreateLegCheckAsync(long legId)
    {
        var check = await _db.WoIpqcChecks.Include(c => c.Items)
            .FirstOrDefaultAsync(c => EF.Property<long?>(c, "WoLegId") == legId);
        if (check is not null) return check;
        await new IpqcLegMaterializer(_db).MaterializeForLegAsync(legId);
        return await _db.WoIpqcChecks.Include(c => c.Items)
            .FirstAsync(c => EF.Property<long?>(c, "WoLegId") == legId);
    }

    // ── POST /work-orders/{id}/ipqc/judgment ───────────────────────

    [HttpPost("{id:long}/ipqc/judgment"), Authorize(Policy = "IpqcSubmit")]
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

        var parse = IpqcJudgmentPolicy.ParseJudgment(req?.Judgment);
        if (!parse.IsValid)
            return Invalid(parse.ErrorCode!, parse.ErrorMessage!);
        var judgment = parse.Judgment;

        var check = await _materializer.GetOrCreateForMutationAsync(id, wo);
        var judgItems = check.Items?.ToList();
        var (ready, _, _) = IpqcReadinessRollup.Compute(check, judgItems);
        if (!ready)
            return Invalid("ipqc.not_ready_for_judgment",
                "Mọi hạng mục IPQC phải được xác nhận (OK/NG) trước khi phán định.");
        if (!IpqcReadinessRollup.IsJudgmentConsistent(check, judgItems, judgment))
            return Invalid("ipqc.judgment_inconsistent",
                $"Judgment \"{judgment}\" is inconsistent with slot results " +
                $"(GoRun requires all OK; SpecialAccept requires at least one NG).");

        // IPQC first-article (Q1 soft-lock) — GoRun is blocked while any MATERIAL
        // (SYSTEM) row diverges from IQC without an Engineer waiver. StopLine /
        // SpecialAccept are NOT gated (StopLine is the escape hatch; a genuine
        // material problem routes back to PREPRESS). Deadlock-free by design.
        if (judgment == IpqcJudgment.GoRun)
        {
            var (allResolved, _, _) = await _materialChecks.RollupAsync(id);
            if (!allResolved)
                return Invalid("ipqc.material_divergence_unresolved",
                    "Vật tư lệch LOT so với IQC chưa được kỹ sư phê duyệt (waiver) — không thể Go Run.");
        }

        var reasonError = IpqcJudgmentPolicy.ValidateSpecialAcceptReason(
            judgment, req!.SpecialAcceptReason);
        if (reasonError is not null)
            return Invalid(reasonError.Value.ErrorCode, reasonError.Value.Message);

        var now = DateTime.UtcNow;
        WoIpqcCheckService.SubmitJudgment(check!, judgment,
            judgment == IpqcJudgment.SpecialAccept ? req.SpecialAcceptReason : null,
            actor, now);

        var transition = IpqcJudgmentPolicy.Transition(judgment);
        if (transition.NextPhase.Length > 0)
            wo.MesPhase = transition.NextPhase;

        var result = await CommitAndAuditAsync(id, wo, check, actor, role,
            AuditAction.WoIpqcJudgment,
            new
            {
                outcome = judgment.ToString(),
                special_accept_reason = judgment == IpqcJudgment.SpecialAccept
                    ? req.SpecialAcceptReason : null,
            });
        // Freeze IPQC snapshot the moment the judgment concludes OK (GoRun).
        if (result is OkObjectResult && transition.FreezeOnGoRun)
            await FreezeSafe(id, CCL.MES.Shared.Quality.TracePhase.Ipqc, actor);
        return result;
    }

    // ── POST /work-orders/{id}/qa/approve ──────────────────────────

    [HttpPost("{id:long}/qa/approve"), Authorize(Policy = "QaApprove")]
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

        var outcomeParse = IpqcJudgmentPolicy.ParseQaOutcome(req?.Outcome);
        if (!outcomeParse.IsValid)
            return Invalid(outcomeParse.ErrorCode!, outcomeParse.ErrorMessage!);
        var outcome = outcomeParse.Outcome;

        var check = await _materializer.GetOrCreateForMutationAsync(id, wo);

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

        var reasonErr = IpqcJudgmentPolicy.ValidateQaReason(outcome, req?.QaReason);
        if (reasonErr is not null)
            return Invalid(reasonErr.Value.ErrorCode, reasonErr.Value.Message);

        var now = DateTime.UtcNow;
        WoIpqcCheckService.SubmitQaApproval(check, outcome, req?.QaReason, actor, now);

        wo.MesPhase = IpqcJudgmentPolicy.QaApproveTransition(outcome).NextPhase;

        var result = await CommitAndAuditAsync(id, wo, check, actor, role,
            AuditAction.WoQaApprove,
            new
            {
                outcome = outcome.ToString(),
                qa_reason = req?.QaReason,
                ipqc_submitted_by = ipqcSubmittedBy,
                qa_approved_by = actor,
                flag_state = _dualSig.FlagState,
            });
        // SpecialAccept path concluding OK via QA → freeze IPQC snapshot.
        if (result is OkObjectResult && IpqcJudgmentPolicy.QaApproveTransition(outcome).FreezeOnApprove)
            await FreezeSafe(id, CCL.MES.Shared.Quality.TracePhase.Ipqc, actor);
        return result;
    }

    // ── Helpers ────────────────────────────────────────────────────

    // Lazy-materialise + AUTO-SYNC (Phương án C) + self-heal + NewPendingCheck
    // moved to Services/IpqcCheckMaterializer (A2 — SaveChanges out of the
    // controller; shared by GET + mutation paths). Constants exposed there too.

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

    // ── Prelude (mirrors 7c-2 RunningSurfaceController) ────────────

    // A2 thin-controller — the concurrency MECHANISM (Idem-Key/If-Match/404/
    // ETag-compare + WoStateConflict audit + ETag header) now lives in
    // WoMutationControllerBase. This thin wrapper keeps the call signature the
    // IPQC endpoints already use and binds the IPQC-typed 409 body via the
    // onConflict factory. Byte-identical to the inlined original: MesPhase is a
    // non-null WorkOrder column so `phase` (= wo.MesPhase ?? "") equals the old
    // `wo.MesPhase` for every real value.
    private Task<(IActionResult? Error, WorkOrder? WoForUpdate)> PreludeAsync(
        long id, string actor, string role, string attemptedAction)
        => base.PreludeAsync(id, actor, role, attemptedAction,
            (wo, etag) => Conflict(new IpqcSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = etag,
                MesPhase = wo?.MesPhase ?? "",
            }));

    // ── Commit + audit ─────────────────────────────────────────────

    private async Task<IActionResult> CommitAndAuditAsync(
        long woId, WorkOrder wo, WoIpqcCheck check,
        string actor, string role,
        string action, object extraDetail)
    {
        // Touch the WO row (trigger bumps RowVersion) then hand the atomic
        // save + L45 conflict path to the shared executor (A2 — SaveChanges
        // lives in Services/, not the controller).
        wo.UpdatedAt = DateTime.UtcNow;
        wo.UpdatedBy = actor;

        var outcome = await _executor.SaveAndResolveAsync(HttpContext, woId, wo.WoNo, actor, role, action);
        if (outcome.Conflict)
            return Conflict(new IpqcSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = outcome.ETag,
                MesPhase = outcome.Fresh?.MesPhase ?? wo.MesPhase,
            });

        var (ready, allOk, anyNg) = IpqcReadinessRollup.Compute(check, check.Items?.ToList());

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
            ETag = outcome.ETag,
            MesPhase = wo.MesPhase,
            IsReadyForJudgment = ready,
            AllOk = allOk,
            AnyNg = anyNg,
        });
    }

}
