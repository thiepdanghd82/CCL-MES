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

        var items = new List<WorkOrderCardItem>(rows.Count);
        foreach (var w in rows)
        {
            var badge = WorkOrderStatusBadge.From(w);
            var processLabel = BuildProcessLabel(w.ProductRevision);
            var bomCount = w.Product?.ProductCode is { Length: > 0 } code
                && bomCounts.TryGetValue(code, out var n) ? n : 0;

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
            Uom:             wo.Uom,
            PlannedStart:    wo.PlannedStart,
            PlannedEnd:      wo.PlannedEnd,
            CurrentStep:     wo.CurrentStep,
            BadgeToken:      badge.Token,
            BadgeLabelKey:   badge.LabelKey,
            BadgeCssClass:   badge.CssClass,
            BadgeIcon:       badge.Icon,
            Materials:       materials,
            QcInspections:   qc);
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
    List<DrawerQcRow> QcInspections);

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
