using System.Security.Claims;
using System.Text.Json;
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
using CCL.MES.Shared.SettingChecks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.7g — SETTING check persist write surface. Persists per-item makeready
/// checks (Print/Cut) that were attestation-only in 7c-3.
///
/// Endpoints:
///   GET  /api/v2/work-orders/{id}/setting-checks              lazy-materialise + view
///   PUT  /api/v2/work-orders/{id}/setting-checks/{itemKey}    set OK/NG + defect + applicable
///   POST /api/v2/work-orders/{id}/setting-checks/item         F4 add item (Engineer+ master / Operator ad-hoc)
///   POST /api/v2/work-orders/{id}/setting-checks/defect       QC-add-new defect per-product (Engineer+)
///
/// Atomic pattern mirrors 7c-2 RunningSurfaceController for every mutation:
///   Prelude (If-Match 428 + Idempotency-Key 400 + WO fetch + RowVersion 409)
///   → body validate → phase guard → service call (no SaveChanges)
///   → wo.UpdatedAt/By touch → SINGLE SaveChanges → audit emit → 200 + bumped ETag.
///
/// Advance-guard (QF/Q4/Q7): the SETTING → IPQC_WAIT transition lives on
/// RunningSurfaceController /setting/done; this PR adds a rollup precondition
/// there (422 setting.incomplete when any Applicable item is not Ok).
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/work-orders")]
public sealed class SettingChecksController : WoMutationControllerBase
{
    private readonly Services.WoMutationExecutor _executor;
    private readonly Services.SettingCheckMaterializer _materializer;

    public SettingChecksController(
        IMesDbContext db, IAuditWriter audit,
        Services.WoMutationExecutor executor,
        Services.SettingCheckMaterializer materializer)
        : base(db, audit)
    {
        _executor = executor;
        _materializer = materializer;
    }

    // ── GET /setting-checks ────────────────────────────────────────

    [HttpGet("{id:long}/setting-checks")]
    public async Task<IActionResult> Get(long id, CancellationToken ct = default)
    {
        var wo = await _db.WorkOrders.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == id, ct);
        if (wo is null)
            return NotFound(ApiError.Of("wo.not_found", $"No work order with id {id}."));

        var productCode = await ProductCodeAsync(wo, ct);
        var (hasPrint, hasCut) = await _materializer.ResolveProcessScopeAsync(wo, productCode, ct);
        var items = await _materializer.EnsureForGetAsync(id, hasPrint, hasCut, ct);
        var view = await _materializer.BuildViewAsync(wo, productCode, hasPrint, hasCut, items, ct);
        Response.Headers.ETag = $"\"{view.ETag}\"";
        return Ok(view);
    }

    // ── PUT /setting-checks/{itemKey} (SettingItemSet policy) ──────

    [HttpPut("{id:long}/setting-checks/{itemKey}"), Authorize(Policy = "SettingItemSet")]
    public async Task<IActionResult> PutItem(
        long id, string itemKey, [FromBody] SetSettingItemRequest? req)
    {
        var actor = ActorName();
        var role = ActorRole();

        var pre = await PreludeAsync(id, actor, role, $"setting_item_{itemKey}");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != nameof(MesPhase.SETTING))
            return Invalid("wo.invalid_phase",
                $"setting-checks requires MesPhase = SETTING; current = {wo.MesPhase}.");

        if (req is null || string.IsNullOrWhiteSpace(req.Status))
            return Invalid("setting.invalid_status", "Status is required (\"Ok\" or \"Ng\").");
        if (!Enum.TryParse<PrepressCheckStatus>(req.Status, ignoreCase: true, out var status)
            || status == PrepressCheckStatus.Pending)
            return Invalid("setting.invalid_status",
                $"Status must be \"Ok\" or \"Ng\"; got \"{req.Status}\".");

        var item = await _db.WoSettingCheckItems
            .FirstOrDefaultAsync(i => i.WorkOrderId == id && i.ItemKey == itemKey);
        if (item is null)
            return Invalid("setting.invalid_item",
                $"Item \"{itemKey}\" không thuộc bộ hạng mục SETTING của WO này.");

        // N/A (Applicable=false) — record + skip result validation.
        if (req.Applicable && status == PrepressCheckStatus.Ng)
        {
            var ngErr = await ValidateNgAsync(item.ItemKey, req.DefectCode, req.NgNote);
            if (ngErr is not null) return ngErr;
        }

        var fromStatus = item.Status.ToString();
        SettingCheckService.SetStatus(item, status,
            status == PrepressCheckStatus.Ng ? req.DefectCode : null,
            status == PrepressCheckStatus.Ng ? req.NgNote : null,
            req.Applicable, actor, DateTime.UtcNow);

        var productCode = await ProductCodeAsync(wo);
        var (hasPrint, hasCut) = await _materializer.ResolveProcessScopeAsync(wo, productCode);

        return await CommitAndAuditAsync(id, wo, actor, role,
            AuditAction.WoSettingItemSet, hasPrint, hasCut,
            new
            {
                process_kind = item.ProcessKind,
                item_key = item.ItemKey,
                from_status = fromStatus,
                to_status = status.ToString(),
                applicable = req.Applicable,
                defect_code = status == PrepressCheckStatus.Ng ? req.DefectCode : null,
                ng_note = status == PrepressCheckStatus.Ng ? req.NgNote : null,
            });
    }

    // ── POST /setting-checks/item (F4) — SettingItemSet (Operator ad-hoc OK) ──

    /// <summary>F4 add hạng mục. Engineer+ → cũng ghi MASTER
    /// <see cref="CheckItemLibrary"/> ProductCode=&lt;mã WO&gt; stage Setting
    /// (nhớ LOT sau) + row per-WO. Operator → CHỈ WoSettingCheckItem AdHoc=true
    /// (server tự hạ xuống ad-hoc theo role — không 403).</summary>
    [HttpPost("{id:long}/setting-checks/item"), Authorize(Policy = "SettingItemSet")]
    public async Task<IActionResult> PostItem(
        long id, [FromBody] AddSettingItemRequest? req)
    {
        var actor = ActorName();
        var role = ActorRole();

        var pre = await PreludeAsync(id, actor, role, "setting_item_add");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != nameof(MesPhase.SETTING))
            return Invalid("wo.invalid_phase",
                $"setting-checks requires MesPhase = SETTING; current = {wo.MesPhase}.");

        var processKind = (req?.ProcessKind ?? "").Trim();
        if (processKind != SettingCheckService.ProcessPrint
            && processKind != SettingCheckService.ProcessCut)
            return Invalid("setting.invalid_process_kind",
                "ProcessKind must be \"Print\" or \"Cut\".");
        var label = (req?.Label ?? "").Trim();
        if (label.Length is 0 or > 512)
            return Invalid("setting.invalid_label", "Label is required (1-512 chars).");
        var standard = string.IsNullOrWhiteSpace(req?.Standard) ? null : req!.Standard!.Trim();

        var canWriteMaster = IsEngineerPlus(role);

        var productCode = await ProductCodeAsync(wo);

        // Sort AFTER the last existing item of this process.
        var maxSort = await _db.WoSettingCheckItems.AsNoTracking()
            .Where(i => i.WorkOrderId == id && i.ProcessKind == processKind)
            .Select(i => (int?)i.Sort).MaxAsync() ?? 0;

        var svc = new SettingCheckService(_db);
        var item = svc.AddAdHocItem(id, processKind, label, standard, maxSort + 10, actor);

        var wroteMaster = false;
        if (canWriteMaster && !string.IsNullOrWhiteSpace(productCode))
        {
            // MASTER per-product library row (nhớ LOT sau). Natural key ItemId —
            // dùng WO-scoped id để không đụng base seed. Non-deleting; idempotent
            // trong 1 request (F4 add mới nên không cần upsert-check).
            var libItemId = $"SET-{productCode}-{Guid.NewGuid():N}"[..40];
            _db.CheckItemLibraries.Add(new CheckItemLibrary
            {
                ItemId = libItemId,
                ProcessLine = processKind,
                ProductCode = productCode,
                GroupLabel = "",
                Code = "",
                Setting = true,
                Active = true,
                ItemVi = label, ItemEn = label,
                AcceptanceVi = standard ?? "", AcceptanceEn = standard ?? "",
                Sort = maxSort + 10,
                CreatedBy = actor,
            });
            wroteMaster = true;
        }

        var (hasPrint, hasCut) = await _materializer.ResolveProcessScopeAsync(wo, productCode);

        var result = await CommitAndAuditAsync(id, wo, actor, role,
            AuditAction.WoSettingItemAdded, hasPrint, hasCut,
            new
            {
                process_kind = processKind,
                item_key = item.ItemKey,
                ad_hoc = true,
                wrote_master = wroteMaster,
            }, addedKey: item.ItemKey);
        return result;
    }

    // ── POST /setting-checks/defect (QC-add-new) — SettingItemAdd (Engineer+) ──

    [HttpPost("{id:long}/setting-checks/defect"), Authorize(Policy = "SettingItemAdd")]
    public async Task<IActionResult> PostDefect(
        long id, [FromBody] AddSettingDefectRequest? req)
    {
        var actor = ActorName();
        var role = ActorRole();

        var pre = await PreludeAsync(id, actor, role, "setting_defect_add");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        if (wo.MesPhase != nameof(MesPhase.SETTING))
            return Invalid("wo.invalid_phase",
                $"setting-checks requires MesPhase = SETTING; current = {wo.MesPhase}.");

        var itemId = (req?.ItemId ?? "").Trim();
        var defectCode = (req?.DefectCode ?? "").Trim();
        var labelVi = (req?.LabelVi ?? "").Trim();
        var labelEn = (req?.LabelEn ?? "").Trim();
        if (itemId.Length is 0 or > 64)
            return Invalid("setting.invalid_defect", "ItemId is required (1-64 chars).");
        if (defectCode.Length is 0 or > 64)
            return Invalid("setting.invalid_defect", "DefectCode is required (1-64 chars).");
        if (labelVi.Length is 0 or > 256 || labelEn.Length is 0 or > 256)
            return Invalid("setting.invalid_defect",
                "LabelVi + LabelEn are required (1-256 chars each).");

        var productCode = await ProductCodeAsync(wo);
        if (string.IsNullOrWhiteSpace(productCode))
            return Invalid("setting.no_product",
                "WO has no product code — cannot register a per-product defect.");

        // Reject a duplicate (ItemId, DefectCode, ProductCode) — idempotency at
        // the natural key so a double-tap doesn't create two options.
        var dup = await _db.CheckItemDefectOptions.AsNoTracking().AnyAsync(o =>
            o.ItemId == itemId && o.DefectCode == defectCode && o.ProductCode == productCode);
        if (dup)
            return Invalid("setting.defect_exists",
                $"Defect \"{defectCode}\" already exists for {itemId}/{productCode}.");

        var maxSort = await _db.CheckItemDefectOptions.AsNoTracking()
            .Where(o => o.ItemId == itemId).Select(o => (int?)o.Sort).MaxAsync() ?? 0;

        var svc = new SettingCheckService(_db);
        svc.AddDefectOption(itemId, defectCode, labelVi, labelEn, productCode!, maxSort + 10, actor);

        var (hasPrint, hasCut) = await _materializer.ResolveProcessScopeAsync(wo, productCode);

        return await CommitAndAuditAsync(id, wo, actor, role,
            AuditAction.WoSettingDefectAdded, hasPrint, hasCut,
            new
            {
                item_id = itemId,
                defect_code = defectCode,
                product_code = productCode,
                added_by = actor,
            }, addedKey: defectCode);
    }

    // ── Helpers ────────────────────────────────────────────────────

    private static bool IsEngineerPlus(string role) =>
        role is UserRole.Admin or UserRole.Supervisor or UserRole.Engineer;

    private Task<string?> ProductCodeAsync(WorkOrder wo, CancellationToken ct = default) =>
        _db.Products.AsNoTracking()
            .Where(p => p.Id == wo.ProductId).Select(p => p.ProductCode)
            .FirstOrDefaultAsync(ct);

    private async Task<IActionResult?> ValidateNgAsync(string itemKey, string? defectCode, string? ngNote)
    {
        if (string.IsNullOrWhiteSpace(defectCode))
            return Invalid("setting.invalid_defect",
                "DefectCode is required when Status = Ng.");
        if (string.IsNullOrWhiteSpace(ngNote) || ngNote!.Length > 500)
            return Invalid("setting.invalid_ng_note",
                "NgNote must be 1-500 chars when Status = Ng.");

        // The chosen defect MUST belong to this item's drop-list (base or
        // per-product). Free-text codes rejected (Lesson L17 catalog guard).
        var ok = await _db.CheckItemDefectOptions.AsNoTracking()
            .AnyAsync(o => o.Active && o.ItemId == itemKey && o.DefectCode == defectCode);
        if (!ok)
            return Invalid("setting.invalid_defect",
                $"DefectCode \"{defectCode}\" không thuộc danh mục của hạng mục \"{itemKey}\".");
        return null;
    }

    // ── Prelude (typed 409 body) ───────────────────────────────────

    private Task<(IActionResult? Error, WorkOrder? WoForUpdate)> PreludeAsync(
        long id, string actor, string role, string attemptedAction)
        => base.PreludeAsync(id, actor, role, attemptedAction,
            (wo, etag) => Conflict(new SettingChecksSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = etag,
                MesPhase = wo?.MesPhase ?? "",
            }));

    // ── Commit: SINGLE SaveChanges + post-write ETag + audit ───────

    private async Task<IActionResult> CommitAndAuditAsync(
        long woId, WorkOrder wo, string actor, string role,
        string action, bool hasPrint, bool hasCut, object extraDetail,
        string? addedKey = null)
    {
        wo.UpdatedAt = DateTime.UtcNow;
        wo.UpdatedBy = actor;

        var outcome = await _executor.SaveAndResolveAsync(
            HttpContext, woId, wo.WoNo, actor, role, action);
        if (outcome.Conflict)
            return Conflict(new SettingChecksSetResponse
            {
                Ok = false,
                ErrorCode = "wo.state_conflict",
                ETag = outcome.ETag,
                MesPhase = outcome.Fresh?.MesPhase ?? wo.MesPhase,
            });

        // Post-write rollup — re-read the item set fresh (mutation persisted).
        var items = await _db.WoSettingCheckItems.AsNoTracking()
            .Where(i => i.WorkOrderId == woId).ToListAsync();
        var ready = SettingCheckService.Rollup(items, hasPrint, hasCut);

        var detailObj = new
        {
            wo_id = woId,
            wo_no = wo.WoNo,
            mes_phase_after = wo.MesPhase,
            ready_after = ready,
            extra = extraDetail,
        };
        await _audit.EmitAsync(
            action: action,
            actor: actor,
            actorRole: role,
            targetType: "WorkOrder",
            targetId: woId.ToString(),
            detail: JsonSerializer.Serialize(detailObj));

        return Ok(new SettingChecksSetResponse
        {
            Ok = true,
            ETag = outcome.ETag,
            MesPhase = wo.MesPhase,
            Ready = ready,
            AddedKey = addedKey,
        });
    }
}
