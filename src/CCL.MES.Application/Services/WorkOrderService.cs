using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

public class WorkOrderService
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;
    public WorkOrderService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task<List<WorkOrder>> GetAllAsync() =>
        _db.WorkOrders
            .Include(w => w.Customer)
            .Include(w => w.Product)
            .Include(w => w.Inspections)
            .OrderByDescending(w => w.Id)
            .ToListAsync();

    /// <summary>
    /// Phase 8 PR #32a — Shop Order list page. Returns a pre-flattened
    /// <see cref="ShopOrderListResult"/> with Active + Closed split, plus
    /// the BOM-material cumulative count derived from
    /// <see cref="ManufacturingStructure"/> link via ProductCode.
    ///
    /// Pre-flattens at service layer so the Razor render path has no
    /// EF navigations dangling (avoids #27 hot-path query surprises).
    /// Includes <see cref="ProductRevision"/>.Print + Diecut so the
    /// process label can be derived without a second round-trip.
    ///
    /// NG / Reject quantity NOT included (Q5 default: render "—" in
    /// PR #32a; defer NG tracking to a later PR).
    /// </summary>
    public async Task<ShopOrderListResult> ShopOrderListAsync()
    {
        var rows = await _db.WorkOrders
            .AsNoTracking()
            .Include(w => w.Customer)
            .Include(w => w.Product)
            .Include(w => w.Inspections)
            .Include(w => w.ProductRevision)
                .ThenInclude(r => r!.Print)
            .Include(w => w.ProductRevision)
                .ThenInclude(r => r!.Diecut)
            .OrderByDescending(w => w.Id)
            .ToListAsync();

        // Distinct ProductCodes to count BOM in a single grouped query.
        var productCodes = rows
            .Select(w => w.Product?.ProductCode)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();

        var bomCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (productCodes.Count > 0)
        {
            var grouped = await _db.ManufacturingStructures
                .AsNoTracking()
                .Where(s => productCodes.Contains(s.ParentPart))
                .GroupBy(s => s.ParentPart)
                .Select(g => new { ParentPart = g.Key, Count = g.Count() })
                .ToListAsync();
            foreach (var g in grouped)
            {
                bomCounts[g.ParentPart] = g.Count;
            }
        }

        // Phase 8 PR #32d — NG/Reject aggregation per-WO. READ-ONLY surface
        // of ProductionLog.RejectQty (Phase 6) written by OeeService.FinishAsync.
        // Single grouped query keyed by WorkOrderId; the dict is keyed by WO id
        // (not WoNo) so the lookup is direct against w.Id below. WOs with no
        // ProductionLog entry default to 0. KHÔNG đụng OeeService / ProductionLog
        // entity / state machine — pure read aggregation.
        var woIds = rows.Select(w => w.Id).ToList();
        var rejectCounts = new Dictionary<long, int>(rows.Count);
        if (woIds.Count > 0)
        {
            var rejectGroups = await _db.ProductionLogs
                .AsNoTracking()
                .Where(p => woIds.Contains(p.WorkOrderId))
                .GroupBy(p => p.WorkOrderId)
                .Select(g => new { WorkOrderId = g.Key, Reject = g.Sum(p => p.RejectQty) })
                .ToListAsync();
            foreach (var g in rejectGroups)
            {
                rejectCounts[g.WorkOrderId] = g.Reject;
            }
        }

        var items = new List<WorkOrderCardItem>(rows.Count);
        foreach (var w in rows)
        {
            var badge = WorkOrderStatusBadge.From(w);
            var processLabel = BuildProcessLabel(w.ProductRevision);
            var bomCount = w.Product?.ProductCode is { Length: > 0 } code
                && bomCounts.TryGetValue(code, out var n) ? n : 0;
            var reject = rejectCounts.TryGetValue(w.Id, out var r) ? r : 0;

            items.Add(new WorkOrderCardItem(
                Id:                w.Id,
                WoNo:              w.WoNo,
                CustomerName:      w.Customer?.Name,
                ProductCode:       w.Product?.ProductCode,
                ProductName:       w.Product?.Name ?? w.ProductName,
                MachineCode:       w.MachineCode,
                MachineName:       w.MachineName,
                ProcessLabel:      processLabel,
                TargetQty:         w.TargetQty,
                ProducedQty:       w.ProducedQty,
                RejectQty:         reject,
                Uom:               w.Uom,
                BomMaterialsCount: bomCount,
                Status:            w.Status,
                CurrentStep:       w.CurrentStep,
                BadgeToken:        badge.Token,
                BadgeLabelKey:     badge.LabelKey,
                BadgeCssClass:     badge.CssClass,
                BadgeIcon:         badge.Icon));
        }

        var active = items
            .Where(i => i.Status != WoStatus.Closed
                        && i.Status != WoStatus.Finished
                        && i.Status != WoStatus.Cancelled)
            .ToList();
        var closed = items
            .Where(i => i.Status == WoStatus.Closed
                        || i.Status == WoStatus.Finished
                        || i.Status == WoStatus.Cancelled)
            .ToList();

        return new ShopOrderListResult(active, closed);
    }

    private static string? BuildProcessLabel(ProductRevision? rev)
    {
        var print = rev?.Print?.ProcessCode;
        var cut = rev?.Diecut?.CutProcessCode;
        if (!string.IsNullOrWhiteSpace(print) && !string.IsNullOrWhiteSpace(cut))
            return $"{print} + {cut}";
        if (!string.IsNullOrWhiteSpace(print)) return print;
        if (!string.IsNullOrWhiteSpace(cut)) return cut;
        return null;
    }

    /// <summary>
    /// Phase 8 PR #32b — Shop Order per-WO drawer view. Pure READ —
    /// no mutation, no state machine touch, no audit emit. Caller is
    /// the read-only drawer that opens on scan-success, card-click,
    /// or manual exact-match lookup.
    ///
    /// Single round-trip via Include for the WO + 2 light follow-up
    /// queries: BOM materials via ManufacturingStructures.Where(
    /// s => s.ParentPart == ProductCode), QC inspections via
    /// OrderByDescending(Id).Take(5). Returns null when WoNo not
    /// found (case-insensitive trim match).
    /// </summary>
    public async Task<WorkOrderDrawerView?> GetDrawerAsync(string woNo)
    {
        if (string.IsNullOrWhiteSpace(woNo)) return null;
        var normalised = woNo.Trim();

        var wo = await _db.WorkOrders
            .AsNoTracking()
            .Include(w => w.Customer)
            .Include(w => w.Product)
            .Include(w => w.Inspections)
            .Include(w => w.ProductRevision)
                .ThenInclude(r => r!.Print)
            .Include(w => w.ProductRevision)
                .ThenInclude(r => r!.Diecut)
            .FirstOrDefaultAsync(w => w.WoNo == normalised);

        if (wo is null) return null;

        var badge = WorkOrderStatusBadge.From(wo);
        var processLabel = BuildProcessLabel(wo.ProductRevision);

        var materials = new List<DrawerMaterialRow>();
        if (!string.IsNullOrWhiteSpace(wo.Product?.ProductCode))
        {
            var bom = await _db.ManufacturingStructures
                .AsNoTracking()
                .Where(s => s.ParentPart == wo.Product.ProductCode)
                .OrderBy(s => s.ComponentPart)
                .ToListAsync();
            foreach (var row in bom)
            {
                materials.Add(new DrawerMaterialRow(
                    PartNo:      row.ComponentPart,
                    Description: row.ComponentDescription,
                    QtyAssembly: row.QtyAssembly,
                    Uom:         row.Uom));
            }
        }

        // QC: 5 most recent — service trims here so UI doesn't need a take.
        var qc = wo.Inspections
            .OrderByDescending(i => i.Id)
            .Take(5)
            .Select(i => new DrawerQcRow(
                Type:        i.Type,
                Result:      i.Result,
                InspectorId: i.InspectorId,
                ApprovedAt:  i.ApprovedAt))
            .ToList();

        // Phase 8 PR #32d — NG/Reject sum per-WO (READ-only of ProductionLog).
        // Single aggregated query — same pattern as ShopOrderListAsync.
        // Returns 0 when WO has no production log entries (e.g. demo WO before
        // Finish). KHÔNG đụng OeeService / ProductionLog entity / state machine.
        var rejectQty = await _db.ProductionLogs
            .AsNoTracking()
            .Where(p => p.WorkOrderId == wo.Id)
            .SumAsync(p => (int?)p.RejectQty) ?? 0;

        // Phase 8 PR #32c — History timeline. Read AuditLog WHERE
        // TargetType="WorkOrder" AND TargetId=woId.ToString(). Top 50
        // most recent; the UI does no further pagination. QC events
        // intentionally excluded (TargetType="QcInspection") so the
        // existing "QC History" section is not duplicated. OEE
        // mutations do not emit audit (acceptable per PR #32c plan).
        var woIdStr = wo.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var history = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.TargetType == "WorkOrder" && a.TargetId == woIdStr)
            .OrderByDescending(a => a.Timestamp)
            .Take(50)
            .Select(a => new WoHistoryRow(
                Timestamp:     a.Timestamp,
                ActorUsername: a.ActorUsername,
                Action:        a.Action,
                Detail:        a.Detail))
            .ToListAsync();

        return new WorkOrderDrawerView(
            Id:              wo.Id,
            WoNo:            wo.WoNo,
            CustomerName:    wo.Customer?.Name,
            ProductCode:     wo.Product?.ProductCode,
            ProductName:     wo.Product?.Name ?? wo.ProductName,
            SpecCode:        wo.ProductRevision?.SpecCode,
            RevisionCode:    wo.ProductRevision?.RevisionCode,
            MachineCode:     wo.MachineCode,
            MachineName:     wo.MachineName,
            ProcessLabel:    processLabel,
            TargetQty:       wo.TargetQty,
            ProducedQty:     wo.ProducedQty,
            RejectQty:       rejectQty,
            Uom:             wo.Uom,
            PlannedStart:    wo.PlannedStart,
            PlannedEnd:      wo.PlannedEnd,
            CurrentStep:     wo.CurrentStep,
            BadgeToken:      badge.Token,
            BadgeLabelKey:   badge.LabelKey,
            BadgeCssClass:   badge.CssClass,
            BadgeIcon:       badge.Icon,
            Materials:       materials,
            QcInspections:   qc,
            History:         history);
    }

    public Task<WorkOrder?> GetAsync(long id) =>
        _db.WorkOrders
            .Include(w => w.Customer)
            .Include(w => w.Product)
            .Include(w => w.Inspections)
            .Include(w => w.History)
            .FirstOrDefaultAsync(w => w.Id == id);

    public async Task<WorkOrder> CreateAsync(CreateWoRequest r)
    {
        var wo = new WorkOrder
        {
            WoNo = r.WoNo,
            CustomerId = r.CustomerId,
            ProductId = r.ProductId,
            ProductName = r.ProductName,
            ProductRevisionId = r.ProductRevisionId,
            MachineCode = r.MachineCode,
            MachineName = r.MachineName,
            TargetQty = r.TargetQty,
            Uom = string.IsNullOrWhiteSpace(r.Uom) ? "pcs" : r.Uom!,
            Status = WoStatus.Draft,
            CurrentStep = ProcessStepCode.PrePressCheck
        };
        _db.WorkOrders.Add(wo);
        await _db.SaveChangesAsync();
        return wo;
    }

    // P10.7-audit-exempt: this Application-layer service is the shared
    // helper called by BOTH the legacy CCL.MES.Web Razor pages (no
    // RowVersion / If-Match — pre-P10.7 contract) AND the Hybrid Api
    // WorkOrdersController (which enforces the RV gate BEFORE calling
    // this service). The optimistic-concurrency check therefore lives
    // at the controller layer; the service emits the audit row (see
    // _audit.EmitAsync below) but doesn't repeat the RV check. Any
    // new mutation method on this service must either accept the
    // RowVersion as a parameter + enforce here, or document the same
    // exemption rationale.
    public async Task<AdvanceResult> AdvanceAsync(long id, string? user)
    {
        var wo = await _db.WorkOrders
            .Include(w => w.Inspections)
            .FirstOrDefaultAsync(w => w.Id == id);

        // Phase 5 — emit a WoErrorCode so the Web layer can localise the
        // dynamic portion of the message ("Cannot advance: <localized>")
        // via WoErrorKeys. Domain stays language-free.
        if (wo is null) return new AdvanceResult(false, WoErrorCode.WorkOrderNotFound, "-");

        var check = WorkOrderStateMachine.CanAdvance(wo);
        if (!check.Allowed)
            return new AdvanceResult(false, check.Error, wo.CurrentStep.ToString());

        var from = wo.CurrentStep;
        var next = WorkOrderStateMachine.Next(from)!.Value;
        wo.CurrentStep = next;
        wo.Status = next switch
        {
            ProcessStepCode.Closed => WoStatus.Closed,
            ProcessStepCode.Running => WoStatus.InProgress,
            _ => wo.Status == WoStatus.Draft ? WoStatus.InProgress : wo.Status
        };

        _db.WoStatusHistories.Add(new WoStatusHistory
        {
            WorkOrderId = wo.Id,
            FromStep = from,
            ToStep = next,
            Action = "Advance",
            ByUser = user
        });

        // P10.7a-1 — keep MesPhase in sync with the legacy CurrentStep
        // every time the legacy advance path moves the WO. Projection
        // is deterministic + idempotent; failing to project here would
        // make the new canonical column drift from the legacy column
        // and break the next P10.7a-* PR that reads MesPhase as the
        // source of truth.
        wo.MesPhase = WorkOrderStateMachine.ProjectFromLegacy(next).ToString();

        await _db.SaveChangesAsync();
        // Phase 6 Bước 5 — emit WO_ADVANCE with from/to step.
        await _audit.EmitAsync(
            AuditAction.WoAdvance, user ?? "anonymous", actorRole: "",
            targetType: "WorkOrder", targetId: wo.Id.ToString(),
            detail: JsonSerializer.Serialize(new { wo_no = wo.WoNo, from = from.ToString(), to = next.ToString() }));
        return new AdvanceResult(true, null, wo.CurrentStep.ToString());
    }


    // Phase 6 Bước 5 — actor param added (was a gap noted in PHASE6-STEP5-PLAN.md §1.1).
    public async Task<WorkOrder?> UpdateFlagsAsync(long id, UpdateFlagsRequest r, string? user)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == id);
        if (wo is null) return null;

        if (r.MaterialsReady.HasValue) wo.MaterialsReady = r.MaterialsReady.Value;
        if (r.SetupConfirmed.HasValue) wo.SetupConfirmed = r.SetupConfirmed.Value;
        if (r.RohsOk.HasValue) wo.RohsOk = r.RohsOk.Value;
        if (r.ProducedQty.HasValue) wo.ProducedQty = r.ProducedQty.Value;
        wo.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        await _audit.EmitAsync(
            AuditAction.WoFlagsUpdate, user ?? "anonymous", actorRole: "",
            targetType: "WorkOrder", targetId: wo.Id.ToString(),
            detail: JsonSerializer.Serialize(new {
                wo_no = wo.WoNo,
                materials_ready = r.MaterialsReady,
                setup_confirmed = r.SetupConfirmed,
                rohs_ok = r.RohsOk,
                produced_qty = r.ProducedQty,
            }));
        return wo;
    }
}

// ── Phase 8 PR #32a — Shop Order DTOs ────────────────────────────────────

/// <summary>Pre-flattened WO row for Shop Order card render. NG quantity
/// intentionally absent (Q5 default: render "—" in PR #32a).</summary>
public sealed record WorkOrderCardItem(
    long Id,
    string WoNo,
    string? CustomerName,
    string? ProductCode,
    string? ProductName,
    string? MachineCode,
    string? MachineName,
    string? ProcessLabel,
    int TargetQty,
    int ProducedQty,
    // Phase 8 PR #32d — NG/Reject aggregate from ProductionLog (read-only).
    int RejectQty,
    string Uom,
    int BomMaterialsCount,
    WoStatus Status,
    ProcessStepCode CurrentStep,
    string BadgeToken,
    string BadgeLabelKey,
    string BadgeCssClass,
    string BadgeIcon);

public sealed record ShopOrderListResult(
    List<WorkOrderCardItem> Active,
    List<WorkOrderCardItem> Closed);

// ── Phase 8 PR #32b — Drawer DTOs (read-only) ────────────────────────────

public sealed record WorkOrderDrawerView(
    long Id,
    string WoNo,
    string? CustomerName,
    string? ProductCode,
    string? ProductName,
    string? SpecCode,
    string? RevisionCode,
    string? MachineCode,
    string? MachineName,
    string? ProcessLabel,
    int TargetQty,
    int ProducedQty,
    // Phase 8 PR #32d — NG/Reject aggregate (read-only of ProductionLog).
    int RejectQty,
    string Uom,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    // Phase 8 WO-Consolidation — Action section needs CurrentStep for
    // visibility gating (mirror Phase 6 row pattern). Server-side
    // state-machine guard still authoritative in Wo.AdvanceAsync.
    ProcessStepCode CurrentStep,
    string BadgeToken,
    string BadgeLabelKey,
    string BadgeCssClass,
    string BadgeIcon,
    List<DrawerMaterialRow> Materials,
    List<DrawerQcRow> QcInspections,
    // Phase 8 PR #32c — top-50 audit timeline for this WO. Empty list
    // when AuditLogs has no rows targeting this WoId.
    List<WoHistoryRow> History);

public sealed record DrawerMaterialRow(
    string PartNo,
    string? Description,
    double QtyAssembly,
    string? Uom);

public sealed record DrawerQcRow(
    QcType Type,
    QcResult Result,
    string? InspectorId,
    DateTime? ApprovedAt);

/// <summary>
/// Phase 8 PR #32c — Single audit-log row projection consumed by the
/// drawer's History section. Detail is raw JSON; the UI pretty-renders
/// known actions (WO_ADVANCE / WO_FLAGS_UPDATE) and falls back to a
/// truncated JSON string for unknown codes.
/// </summary>
public sealed record WoHistoryRow(
    DateTime Timestamp,
    string ActorUsername,
    string Action,
    string? Detail);
