using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Api.Policies;
using CCL.MES.Api.Services;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.WoQcReview;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.7e-2 — FQC + OQC review write surface per contract §5.6 +
/// §3.4 (the 7e-1 grid amendment). Single controller handles both
/// kinds via the {kind} path param ("fqc" | "oqc") — the data-driven
/// schema (Q3) makes items + judgment shape identical; only the
/// 3-sig (Q5) Inspector/Reviewer/Approver chain differs (OQC only).
///
/// Endpoints:
///   GET  /api/v2/work-orders/{id}/qc/{kind}                    Read view (items + judgment + sigs)
///   PUT  /api/v2/work-orders/{id}/qc/{kind}/items/{itemKey}    Set 1 item Status (Ok | Ng)
///   POST /api/v2/work-orders/{id}/qc/fqc/judgment              FQC Pass | Reject (single-sig Inspector)
///   POST /api/v2/work-orders/{id}/qc/oqc/inspect               OQC sig 1 — Inspector commits
///   POST /api/v2/work-orders/{id}/qc/oqc/review                OQC sig 2 — Reviewer (≠ Inspector)
///   POST /api/v2/work-orders/{id}/qc/oqc/approve               OQC sig 3 — Approver final + advance to SHIPPED
///   POST /api/v2/work-orders/{id}/qc/oqc/reject                OQC Reject → FQC_PENDING (transient per Q2)
///
/// Authorization: QcRead policy on GET (Admin / QC / Engineer can
/// view) + QcEdit policy on every mutation (Admin / QC only).
///
/// Concurrency contract mirrors 7d:
///   428 missing If-Match
///   400 missing Idempotency-Key
///   404 WO not found OR kind invalid
///   409 wo.state_conflict on stale If-Match (+ WO_STATE_CONFLICT audit)
///   422 wo.invalid_phase / qc.invalid_kind / qc.invalid_status /
///       qc.invalid_reason_code / qc.invalid_ng_note / qc.invalid_item_key /
///       qc.invalid_judgment / qc.judgment_inconsistent / qc.not_ready_for_judgment /
///       qc.invalid_reason / oqc.same_user_as_inspector /
///       oqc.same_user_as_reviewer
///
/// Q5 — OQC 3-sig invariants (default-ON per L20):
///   Reviewer ≠ Inspector   when OqcRequireDistinctReviewer = on
///       → 422 oqc.same_user_as_inspector + WO_OQC_REVIEW_DENIED audit
///   Approver ≠ Reviewer    when OqcRequireDistinctApprover = on
///       → 422 oqc.same_user_as_reviewer + WO_OQC_APPROVE_DENIED audit
///   Approver ≠ Inspector   when OqcRequireApproverDistinctFromInspector = on
///       → 422 oqc.same_user_as_inspector + WO_OQC_APPROVE_DENIED audit
///   Each violation emits the denied audit INSTEAD of the success audit
///   so forensic replay shows the policy violation.
///
/// FQC sig count (§3.4 follow-up): single-sig (Inspector only) per
/// SpecHub prototype's _mesRenderFqc shape. FqcJudgment endpoint requires
/// only the Inspector signature; ReviewedBy + ApprovedBy stay null on
/// FQC rows. A future plant requiring FQC dual-sig can flip a new
/// OPS_FQC_REQUIRE_DISTINCT_REVIEWER env var (default OFF) per L20
/// pattern without a code change.
/// </summary>
[ApiController]
[Route(ApiVersion.Prefix + "/work-orders")]
public sealed class WoQcReviewController : WoQcMutationControllerBase
{
    private readonly WoQcSigPolicyOptions _sigPolicy;

    private readonly Services.ITraceFreezeService _trace;
    private readonly Services.WorkCenterSpeedLookup _wcSpeed;

    public WoQcReviewController(
        IMesDbContext db,
        IAuditWriter audit,
        IOptions<WoQcSigPolicyOptions> sigPolicy,
        Services.ITraceFreezeService trace,
        Services.WorkCenterSpeedLookup wcSpeed)
        : base(db, audit)
    {
        _sigPolicy = sigPolicy.Value;
        _trace = trace;
        _wcSpeed = wcSpeed;
    }

    // Best-effort trace freeze — never breaks the confirm; idempotent in service.
    private async Task FreezeSafe(long woId, string phase, string actor)
    {
        try { await _trace.FreezeAsync(woId, phase, actor); }
        catch { /* best-effort */ }
    }

    // ═══════════════════════════════════════════════════════════════
    // GET — view
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Read the QC check view for a WO + kind. Lazy-materialises
    /// a Pending row on first read (mirrors 7d IPQC lazy materialise).
    /// MesPhase projected per L19 amendment.</summary>
    [HttpGet("{id:long}/qc/{kind}"), Authorize(Policy = "QcRead")]
    public async Task<IActionResult> Get(long id, string kind, CancellationToken ct = default)
    {
        var normKind = NormaliseKind(kind);
        if (normKind is null)
            return UnprocessableEntity(ApiError.Of("qc.invalid_kind",
                $"Kind must be \"fqc\" or \"oqc\"; got \"{kind}\"."));

        var wo = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wo is null)
            return NotFound(ApiError.Of("wo.not_found", $"No work order with id {id}."));

        var check = await _db.WoQcChecks.AsNoTracking()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.WorkOrderId == id && c.QcKind == normKind, ct);
        if (check is null)
        {
            // P10.7e-3 FIX (Henry RCA on PR #123) — resolve profile via Q4
            // 3-level chain BEFORE materialising. Without this the snapshot
            // is "{}" and the dashboard renders 0/0 items. See L23.
            var resolvedSnapshot = await ResolveProfileSnapshotAsync(wo.ProductId, normKind, ct);
            try
            {
                _db.WoQcChecks.Add(new WoQcCheck
                {
                    WorkOrderId = id,
                    QcKind = normKind,
                    ProfileSnapshotJson = resolvedSnapshot,
                    Judgment = WoQcJudgment.Pending,
                });
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Race lost — another caller inserted first.
                if (_db is Microsoft.EntityFrameworkCore.DbContext dbCtx)
                    dbCtx.ChangeTracker.Clear();
            }
            check = await _db.WoQcChecks.AsNoTracking()
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.WorkOrderId == id && c.QcKind == normKind, ct);
        }
        else if (string.IsNullOrWhiteSpace(check.ProfileSnapshotJson) || check.ProfileSnapshotJson == "{}")
        {
            // P10.7e-3 FIX — heal pre-fix rows that were materialised with
            // empty snapshot. Resolve now + persist so the next read is fast +
            // future profile edits still don't retroactively change a row
            // already in flight (snapshot is frozen at THIS read, not at
            // each subsequent one).
            var resolvedSnapshot = await ResolveProfileSnapshotAsync(wo.ProductId, normKind, ct);
            if (resolvedSnapshot != "{}" && resolvedSnapshot != check.ProfileSnapshotJson)
            {
                var tracked = await _db.WoQcChecks.FirstOrDefaultAsync(c => c.Id == check.Id, ct);
                if (tracked is not null)
                {
                    tracked.ProfileSnapshotJson = resolvedSnapshot;
                    try { await _db.SaveChangesAsync(ct); }
                    catch (DbUpdateException) { /* race; next reader will heal */ }
                }
                check = await _db.WoQcChecks.AsNoTracking()
                    .Include(c => c.Items)
                    .FirstOrDefaultAsync(c => c.WorkOrderId == id && c.QcKind == normKind, ct);
            }
        }

        var etag = Convert.ToBase64String(wo.RowVersion);
        var profileExpected = QcProfileResolver.ProfileKeyCount(check?.ProfileSnapshotJson);
        var (ready, allOk, anyNg) = WoQcReadinessRollup.Compute(check, profileExpected);

        // P10.7e-3 — per-item photo IDs for the thumbnail strip. Single
        // query keyed on the check's children — cheaper than N+1 round-trips
        // even for the 28-item OQC profile.
        var photoLookup = new Dictionary<long, List<long>>();
        if (check is not null && check.Items.Count > 0)
        {
            var itemIds = check.Items.Select(i => i.Id).ToList();
            var rows = await _db.WoQcPhotos.AsNoTracking()
                .Where(p => itemIds.Contains(p.WoQcCheckItemId))
                .OrderBy(p => p.Id)
                .Select(p => new { p.WoQcCheckItemId, p.Id })
                .ToListAsync(ct);
            foreach (var r in rows)
            {
                if (!photoLookup.TryGetValue(r.WoQcCheckItemId, out var list))
                    photoLookup[r.WoQcCheckItemId] = list = new List<long>();
                list.Add(r.Id);
            }
        }

        // P10.7e-3 FIX — merge profile-declared item keys with persisted
        // WoQcCheckItem rows. Profile order is canonical (matches the
        // declaration order operators see on the paper form CCL-10-F6).
        // Items not yet touched render as Pending; rows from previous
        // PUTs overlay status/NG fields/photo IDs.
        var profileKeys = QcProfileResolver.ExtractProfileItemKeys(check?.ProfileSnapshotJson);
        var itemRowByKey = (check?.Items ?? new List<WoQcCheckItem>())
            .GroupBy(i => i.ItemKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        // Stragglers — persisted rows whose key is NOT in the current
        // profile snapshot (would only happen if an admin shrank the
        // profile after a check froze; per Q3 we don't drop those — they
        // tail-append so the auditor sees the full history).
        var stragglerKeys = itemRowByKey.Keys
            .Where(k => !profileKeys.Contains(k, StringComparer.OrdinalIgnoreCase))
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var viewItems = new List<WoQcViewItem>(profileKeys.Count + stragglerKeys.Count);
        foreach (var key in profileKeys.Concat(stragglerKeys))
        {
            if (itemRowByKey.TryGetValue(key, out var row))
            {
                viewItems.Add(new WoQcViewItem
                {
                    ItemKey = key,
                    Status = row.Status.ToString(),
                    NgReasonCode = row.NgReasonCode,
                    NgNote = row.NgNote,
                    PhotoIds = photoLookup.TryGetValue(row.Id, out var ids)
                        ? ids
                        : Array.Empty<long>(),
                });
            }
            else
            {
                viewItems.Add(new WoQcViewItem
                {
                    ItemKey = key,
                    Status = "Pending",
                    NgReasonCode = null,
                    NgNote = null,
                    PhotoIds = Array.Empty<long>(),
                });
            }
        }

        var view = new WoQcView
        {
            WoId = wo.Id,
            WoNo = wo.WoNo,
            MesPhase = wo.MesPhase ?? "",
            ETag = etag,
            QcKind = normKind,
            ProfileSnapshotJson = check?.ProfileSnapshotJson ?? "{}",
            Items = viewItems,
            Judgment = check?.Judgment.ToString() ?? "Pending",
            JudgmentReason = check?.JudgmentReason,
            InspectedBy = check?.InspectedBy,
            InspectedAt = check?.InspectedAt,
            ReviewedBy = check?.ReviewedBy,
            ReviewedAt = check?.ReviewedAt,
            ApprovedBy = check?.ApprovedBy,
            ApprovedAt = check?.ApprovedAt,
            IsReadyForJudgment = ready,
            AllOk = allOk,
            AnyNg = anyNg,
        };

        Response.Headers.ETag = $"\"{etag}\"";
        return Ok(view);
    }

    // ═══════════════════════════════════════════════════════════════
    // PUT — set 1 item
    // ═══════════════════════════════════════════════════════════════

    [HttpPut("{id:long}/qc/{kind}/items/{itemKey}"), Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> PutItem(
        long id, string kind, string itemKey, [FromBody] SetWoQcItemRequest? req)
    {
        var normKind = NormaliseKind(kind);
        if (normKind is null)
            return Invalid("qc.invalid_kind", $"Kind must be \"fqc\" or \"oqc\"; got \"{kind}\".");
        if (string.IsNullOrWhiteSpace(itemKey) || itemKey.Length > 64)
            return Invalid("qc.invalid_item_key", "ItemKey must be 1-64 chars.");

        var actor = ActorName();
        var role = ActorRole();
        var pre = await PreludeAsync(id, actor, role, $"qc_{normKind.ToLowerInvariant()}_set_item");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        var expectedPhase = WoQcJudgmentPolicy.ExpectedPhaseForKind(normKind);
        if (wo.MesPhase != expectedPhase)
            return Invalid("wo.invalid_phase",
                $"qc/{normKind}/items requires MesPhase = {expectedPhase}; current = {wo.MesPhase}.");

        if (req is null || string.IsNullOrWhiteSpace(req.Status))
            return Invalid("qc.invalid_status", "Status is required (\"Ok\" or \"Ng\").");
        if (!Enum.TryParse<IpqcCheckStatus>(req.Status, ignoreCase: true, out var status)
            || status == IpqcCheckStatus.Pending)
            return Invalid("qc.invalid_status", $"Status must be \"Ok\" or \"Ng\"; got \"{req.Status}\".");

        if (status == IpqcCheckStatus.Ng)
        {
            var ngErr = await ValidateNgAsync(req.NgReasonCode, req.NgNote);
            if (ngErr is not null) return ngErr;
        }

        var check = await GetOrCreateCheckAsync(id, normKind, wo.ProductId);

        // P10.7e-3 FIX — validate itemKey against the profile snapshot.
        // Prevents bypassing seed (operator POSTs arbitrary key, server
        // creates a row, judgment "ready" without ever touching the
        // canonical profile items). Stragglers (legacy item keys removed
        // from a later profile rev) tolerated when persisted; new writes
        // gated to the snapshot's declared keys.
        var profileKeySet = QcProfileResolver.ExtractProfileItemKeys(check.ProfileSnapshotJson);
        if (profileKeySet.Count > 0
            && !profileKeySet.Contains(itemKey, StringComparer.OrdinalIgnoreCase)
            && !check.Items.Any(i => string.Equals(i.ItemKey, itemKey, StringComparison.OrdinalIgnoreCase)))
        {
            return Invalid("qc.invalid_item_key",
                $"ItemKey \"{itemKey}\" is not declared in the {normKind} profile snapshot.");
        }

        // Find or create the child item row.
        var item = check.Items.FirstOrDefault(i => i.ItemKey == itemKey);
        if (item is null)
        {
            item = new WoQcCheckItem
            {
                WoQcCheckId = check.Id,
                ItemKey = itemKey,
                Status = status,
            };
            check.Items.Add(item);
        }
        item.Status = status;
        item.NgReasonCode = status == IpqcCheckStatus.Ng ? req.NgReasonCode : null;
        item.NgNote = status == IpqcCheckStatus.Ng ? req.NgNote : null;

        return await CommitAndAuditAsync(id, wo, check, actor, role,
            AuditAction.WoQcCheckItem,
            new
            {
                kind = normKind,
                item_key = itemKey,
                status = status.ToString(),
                ng_reason_code = status == IpqcCheckStatus.Ng ? req.NgReasonCode : null,
                ng_note = status == IpqcCheckStatus.Ng ? req.NgNote : null,
            });
    }

    // ═══════════════════════════════════════════════════════════════
    // POST — FQC judgment (single-sig Inspector)
    // ═══════════════════════════════════════════════════════════════

    [HttpPost("{id:long}/qc/fqc/judgment"), Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> PostFqcJudgment(
        long id, [FromBody] SubmitFqcJudgmentRequest? req)
    {
        var actor = ActorName();
        var role = ActorRole();

        var pre = await PreludeAsync(id, actor, role, "fqc_judgment");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "FQC_PENDING")
            return Invalid("wo.invalid_phase",
                $"qc/fqc/judgment requires MesPhase = FQC_PENDING; current = {wo.MesPhase}.");

        // Parse phán quyết TRƯỚC readiness (giữ nguyên thứ tự mã lỗi — L47).
        var parse = WoQcJudgmentPolicy.ParseJudgment(req?.Judgment);
        if (!parse.IsValid)
            return Invalid(parse.ErrorCode!, parse.Message!);
        var judgment = parse.Judgment;

        var check = await GetOrCreateCheckAsync(id, KindFqc, wo.ProductId);
        var profileExpected = QcProfileResolver.ProfileKeyCount(check.ProfileSnapshotJson);
        var (ready, _, _) = WoQcReadinessRollup.Compute(check, profileExpected);
        if (!ready)
            return Invalid("qc.not_ready_for_judgment",
                "Every profile item must be resolved (Ok or Ng) before judgment.");

        // Lý do-khi-Reject SAU readiness (giữ nguyên thứ tự — L47).
        var reasonError = WoQcJudgmentPolicy.ValidateRejectReason(judgment, req?.JudgmentReason);
        if (reasonError is not null)
            return Invalid(reasonError.Value.ErrorCode, reasonError.Value.Message);

        var transition = WoQcJudgmentPolicy.Transition(judgment);
        var persistedReason = WoQcJudgmentPolicy.PersistedReason(judgment, req?.JudgmentReason);

        var now = DateTime.UtcNow;
        check.Judgment = judgment;
        check.JudgmentReason = persistedReason;
        check.InspectedBy = actor;
        check.InspectedAt = now;

        wo.MesPhase = transition.NextPhase;

        var result = await CommitAndAuditAsync(id, wo, check, actor, role,
            transition.AuditAction,
            new
            {
                outcome = judgment.ToString(),
                judgment_reason = persistedReason,
                inspected_by = actor,
            });
        // Freeze FQC snapshot when the judgment concludes Pass.
        if (result is OkObjectResult && transition.FreezeOnPass)
            await FreezeSafe(id, CCL.MES.Shared.Quality.TracePhase.Fqc, actor);
        return result;
    }

    // ═══════════════════════════════════════════════════════════════
    // POST — OQC 3-sig chain
    // ═══════════════════════════════════════════════════════════════

    [HttpPost("{id:long}/qc/oqc/inspect"), Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> PostOqcInspect(
        long id, [FromBody] OqcInspectRequest? req)
    {
        var actor = ActorName();
        var role = ActorRole();

        var pre = await PreludeAsync(id, actor, role, "oqc_inspect");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "OQC_PENDING")
            return Invalid("wo.invalid_phase",
                $"qc/oqc/inspect requires MesPhase = OQC_PENDING; current = {wo.MesPhase}.");

        var check = await GetOrCreateCheckAsync(id, KindOqc, wo.ProductId);
        var profileExpected = QcProfileResolver.ProfileKeyCount(check.ProfileSnapshotJson);
        var (ready, _, _) = WoQcReadinessRollup.Compute(check, profileExpected);
        if (!ready)
            return Invalid("qc.not_ready_for_judgment",
                "Every profile item must be resolved (Ok or Ng) before Inspector signs.");

        var now = DateTime.UtcNow;
        check.InspectedBy = actor;
        check.InspectedAt = now;

        return await CommitAndAuditAsync(id, wo, check, actor, role,
            AuditAction.WoOqcInspect,
            new
            {
                inspected_by = actor,
                flag_state = _sigPolicy.FlagState,
                note = req?.Note,
            });
    }

    [HttpPost("{id:long}/qc/oqc/review"), Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> PostOqcReview(
        long id, [FromBody] OqcReviewRequest? req)
    {
        var actor = ActorName();
        var role = ActorRole();

        var pre = await PreludeAsync(id, actor, role, "oqc_review");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "OQC_PENDING")
            return Invalid("wo.invalid_phase",
                $"qc/oqc/review requires MesPhase = OQC_PENDING; current = {wo.MesPhase}.");

        var check = await GetOrCreateCheckAsync(id, KindOqc);

        // Luật chuỗi chữ ký sống trong OqcSignaturePolicy (thuần, unit-test được);
        // controller chỉ thực thi phán quyết.
        var orderV = OqcSignaturePolicy.CheckOrder(
            OqcSignatureStep.Review, check.InspectedBy, check.ReviewedBy);
        if (!orderV.Allowed)
            return Invalid(orderV.ErrorCode!, orderV.Message!);

        var distinctV = OqcSignaturePolicy.CheckDistinct(
            OqcSignatureStep.Review, check.InspectedBy, check.ReviewedBy, actor, _sigPolicy);
        if (!distinctV.Allowed)
        {
            var denyDetail = JsonSerializer.Serialize(new
            {
                wo_id = id,
                reason = distinctV.DenyReason,
                attempted_by = actor,
                inspected_by = check.InspectedBy,
                flag_state = _sigPolicy.FlagState,
            });
            await _audit.EmitAsync(
                action: AuditAction.WoOqcReviewDenied,
                actor: actor,
                actorRole: role,
                targetType: "WorkOrder",
                targetId: id.ToString(),
                detail: denyDetail);

            return UnprocessableEntity(new WoQcSetResponse
            {
                Ok = false,
                ErrorCode = "oqc.same_user_as_inspector",
                ETag = Convert.ToBase64String(wo.RowVersion),
                MesPhase = wo.MesPhase ?? "",
            });
        }

        var now = DateTime.UtcNow;
        check.ReviewedBy = actor;
        check.ReviewedAt = now;

        return await CommitAndAuditAsync(id, wo, check, actor, role,
            AuditAction.WoOqcReview,
            new
            {
                reviewed_by = actor,
                inspected_by = check.InspectedBy,
                flag_state = _sigPolicy.FlagState,
                note = req?.Note,
            });
    }

    [HttpPost("{id:long}/qc/oqc/approve"), Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> PostOqcApprove(
        long id, [FromBody] OqcApproveRequest? req)
    {
        var actor = ActorName();
        var role = ActorRole();

        var pre = await PreludeAsync(id, actor, role, "oqc_approve");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != "OQC_PENDING")
            return Invalid("wo.invalid_phase",
                $"qc/oqc/approve requires MesPhase = OQC_PENDING; current = {wo.MesPhase}.");

        var check = await GetOrCreateCheckAsync(id, KindOqc);

        var approveOrderV = OqcSignaturePolicy.CheckOrder(
            OqcSignatureStep.Approve, check.InspectedBy, check.ReviewedBy);
        if (!approveOrderV.Allowed)
            return Invalid(approveOrderV.ErrorCode!, approveOrderV.Message!);

        // Parse outcome TRƯỚC CheckDistinct (giữ nguyên thứ tự mã lỗi — L47).
        var outcome = WoQcJudgmentPolicy.ParseOqcOutcome(req?.Outcome);
        if (!outcome.IsValid)
            return Invalid(outcome.ErrorCode!, outcome.ErrorMessage!);
        var isApprove = outcome.IsApprove;
        var isReject  = !isApprove;

        var oqcReasonError = WoQcJudgmentPolicy.ValidateOqcRejectReason(isReject, req?.JudgmentReason);
        if (oqcReasonError is not null)
            return Invalid(oqcReasonError.Value.ErrorCode, oqcReasonError.Value.Message);

        // Q5 — tách vai. CỐ Ý gọi ở ĐÚNG vị trí cũ (sau phần kiểm outcome/lý do
        // reject) để mã lỗi trả về không đổi với request vừa sai outcome vừa
        // trùng vai.
        var approveDistinctV = OqcSignaturePolicy.CheckDistinct(
            OqcSignatureStep.Approve, check.InspectedBy, check.ReviewedBy, actor, _sigPolicy);
        if (!approveDistinctV.Allowed)
        {
            return await DenyApprove(id, wo, actor, role, check,
                approveDistinctV.DenyReason!,
                approveDistinctV.ErrorCode!);
        }

        var oqcTransition = WoQcJudgmentPolicy.OqcApproveTransition(isApprove);
        var now = DateTime.UtcNow;
        check.ApprovedBy = actor;
        check.ApprovedAt = now;
        check.Judgment = oqcTransition.Judgment;
        check.JudgmentReason = isReject ? req!.JudgmentReason : null;
        wo.MesPhase = oqcTransition.NextPhase;

        if (isApprove)
        {
            // Q1 — OQC Pass advances to SHIPPED.
            var resp = await CommitAndAuditAsync(id, wo, check, actor, role,
                oqcTransition.AuditAction,
                new
                {
                    approved_by = actor,
                    reviewed_by = check.ReviewedBy,
                    inspected_by = check.InspectedBy,
                    outcome = "Approve",
                    flag_state = _sigPolicy.FlagState,
                });
            // Stamp the WO_SHIPPED audit too — covers the transition leg.
            await _audit.EmitAsync(
                action: AuditAction.WoShipped,
                actor: actor,
                actorRole: role,
                targetType: "WorkOrder",
                targetId: id.ToString(),
                detail: JsonSerializer.Serialize(new
                {
                    wo_id = id,
                    shipped_at = now,
                    oqc_approver = actor,
                }));
            // Freeze OQC snapshot on approve (WO shipped).
            if (resp is OkObjectResult && oqcTransition.FreezeOnApprove)
                await FreezeSafe(id, CCL.MES.Shared.Quality.TracePhase.Oqc, actor);
            return resp;
        }

        // Q2 — OQC Reject → FQC_PENDING re-loop.
        return await CommitAndAuditAsync(id, wo, check, actor, role,
            oqcTransition.AuditAction,
            new
            {
                approved_by = actor,
                reviewed_by = check.ReviewedBy,
                inspected_by = check.InspectedBy,
                outcome = "Reject",
                reject_reason = req!.JudgmentReason,
                flag_state = _sigPolicy.FlagState,
            });
    }

    private async Task<IActionResult> DenyApprove(
        long id, WorkOrder wo, string actor, string role, WoQcCheck check,
        string reason, string errorCode)
    {
        var denyDetail = JsonSerializer.Serialize(new
        {
            wo_id = id,
            reason,
            attempted_by = actor,
            reviewed_by = check.ReviewedBy,
            inspected_by = check.InspectedBy,
            flag_state = _sigPolicy.FlagState,
        });
        await _audit.EmitAsync(
            action: AuditAction.WoOqcApproveDenied,
            actor: actor,
            actorRole: role,
            targetType: "WorkOrder",
            targetId: id.ToString(),
            detail: denyDetail);

        return UnprocessableEntity(new WoQcSetResponse
        {
            Ok = false,
            ErrorCode = errorCode,
            ETag = Convert.ToBase64String(wo.RowVersion),
            MesPhase = wo.MesPhase ?? "",
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private async Task<IActionResult?> ValidateNgAsync(string? ngReasonCode, string? ngNote)
    {
        if (string.IsNullOrWhiteSpace(ngReasonCode))
            return Invalid("qc.invalid_reason_code",
                "NgReasonCode is required when Status = Ng.");
        if (string.IsNullOrWhiteSpace(ngNote) || ngNote!.Length > 500)
            return Invalid("qc.invalid_ng_note",
                "NgNote must be 1-500 chars when Status = Ng.");
        var exists = await _db.ReasonCodes.AsNoTracking()
            .AnyAsync(r => r.Code == ngReasonCode && r.Kind == ReasonCodeKind.Scrap);
        if (!exists)
            return Invalid("qc.invalid_reason_code",
                $"NgReasonCode \"{ngReasonCode}\" is not a registered Scrap reason.");
        return null;
    }

    private async Task<IActionResult> CommitAndAuditAsync(
        long woId, WorkOrder wo, WoQcCheck check,
        string actor, string role, string action, object extraDetail)
    {
        wo.UpdatedAt = DateTime.UtcNow;
        wo.UpdatedBy = actor;
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            return await HandleWoStateConflictAsync(woId, actor, role, action);
        }

        // Re-read freshly to capture the trigger-bumped RowVersion (L11).
        var freshWo = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == woId)
            .Select(w => new { w.RowVersion, w.MesPhase })
            .SingleAsync();
        var newEtag = Convert.ToBase64String(freshWo.RowVersion);

        var detail = JsonSerializer.Serialize(new
        {
            wo_id = woId,
            wo_no = wo.WoNo,
            mes_phase_after = freshWo.MesPhase,
            extra = extraDetail,
        });
        await _audit.EmitAsync(
            action: action,
            actor: actor,
            actorRole: role,
            targetType: "WorkOrder",
            targetId: woId.ToString(),
            detail: detail);

        var profileExpected = QcProfileResolver.ProfileKeyCount(check.ProfileSnapshotJson);
        var (ready, allOk, anyNg) = WoQcReadinessRollup.Compute(check, profileExpected);
        Response.Headers.ETag = $"\"{newEtag}\"";
        return Ok(new WoQcSetResponse
        {
            Ok = true,
            ETag = newEtag,
            MesPhase = freshWo.MesPhase ?? "",
            IsReadyForJudgment = ready,
            AllOk = allOk,
            AnyNg = anyNg,
        });
    }

    // ═══════════════════════════════════════════════════════════════
    // GET — WO summary report (Q8 — read-only, live-recomputed)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>P10.7e-3 Q8 — live-recomputed summary report. Powers the
    /// ShippedSummaryDashboard. NOT frozen at SHIPPED time — late
    /// corrections via /run/qty/correct flow through. Read-only; no
    /// mutation surface. MesPhase projected per L19 amendment.</summary>
    [HttpGet("{id:long}/summary-report"), Authorize(Policy = "QcRead")]
    public async Task<IActionResult> GetSummaryReport(long id, CancellationToken ct = default)
    {
        var wo = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wo is null)
            return NotFound(ApiError.Of("wo.not_found", $"No work order with id {id}."));

        var qtyDone = wo.ProducedQty;
        // QtyNg lives in WoQtyEntries (append-only with Add/Correct semantics)
        // — sum deltas instead of using the denormalised cache so the report
        // reflects late corrections via /run/qty/correct (Q8 invariant).
        var qtyNg = await _db.WoQtyEntries.AsNoTracking()
            .Where(e => e.WoId == id)
            .SumAsync(e => (int?)e.QtyNgDelta, ct) ?? 0;

        var sessions = await _db.WoRunSessions.AsNoTracking()
            .Where(s => s.WoId == id)
            .OrderBy(s => s.StartedAt)
            .Select(s => new Services.WoSummarySessionSpan(s.StartedAt, s.EndedAt))
            .ToListAsync(ct);

        var pauseEvents = await _db.WoPauseEvents.AsNoTracking()
            .Where(p => p.WoId == id)
            .Select(p => new Services.WoSummaryPauseSpan(p.ReasonCode, p.StartedAt, p.EndedAt))
            .ToListAsync(ct);

        // Đợt 1 C3 — speed comes from WorkCenter.IdealSpeedPcsH, the single
        // canonical source (same one ShopOrdersController reads). Async EF, so
        // it is resolved here and handed to the pure builder as a plain result.
        var speed = await _wcSpeed.ResolveAsync(wo.WoNo, wo.MachineCode, ct);

        // QC summary — load all 3 legs (IPQC + FQC + OQC). Some may be absent
        // if the WO never reached that phase; the builder renders those Pending.
        var checks = await _db.WoQcChecks.AsNoTracking()
            .Where(c => c.WorkOrderId == id)
            .Select(c => new
            {
                c.QcKind,
                c.Judgment,
                c.InspectedBy,
                c.ReviewedBy,
                c.ApprovedBy,
                c.JudgmentReason,
            })
            .ToListAsync(ct);
        var ipqcRow = await _db.WoIpqcChecks.AsNoTracking()
            .Where(c => c.WorkOrderId == id)
            .Select(c => new
            {
                c.Judgment,
                c.IpqcSubmittedBy,
                c.QaApprovedBy,
                c.QaReason,
                c.SpecialAcceptReason,
            })
            .FirstOrDefaultAsync(ct);

        Services.WoSummaryQcLegInput? MapCheck(string kind)
        {
            var c = checks.FirstOrDefault(x => x.QcKind == kind);
            return c is null ? null : new Services.WoSummaryQcLegInput
            {
                Judgment = c.Judgment,
                InspectedBy = c.InspectedBy,
                ReviewedBy = c.ReviewedBy,
                ApprovedBy = c.ApprovedBy,
                JudgmentReason = c.JudgmentReason,
            };
        }

        var report = Services.WoSummaryReportBuilder.Build(new Services.WoSummaryReportInput
        {
            WoId = wo.Id,
            WoNo = wo.WoNo,
            MesPhase = wo.MesPhase,
            TargetQty = wo.TargetQty,
            QtyDone = qtyDone,
            QtyNg = qtyNg,
            UpdatedAt = wo.UpdatedAt,
            Now = DateTime.UtcNow,
            Sessions = sessions,
            PauseEvents = pauseEvents,
            WorkCenterResolved = speed.Resolved,
            IdealSpeedPcsH = speed.IdealSpeedPcsH,
            Ipqc = ipqcRow is null ? null : new Services.WoSummaryIpqcInput
            {
                Judgment = ipqcRow.Judgment.ToString(),
                IpqcSubmittedBy = ipqcRow.IpqcSubmittedBy,
                QaApprovedBy = ipqcRow.QaApprovedBy,
                QaReason = ipqcRow.QaReason,
                SpecialAcceptReason = ipqcRow.SpecialAcceptReason,
            },
            Fqc = MapCheck(KindFqc),
            Oqc = MapCheck(KindOqc),
        });

        return Ok(report);
    }
}
