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
