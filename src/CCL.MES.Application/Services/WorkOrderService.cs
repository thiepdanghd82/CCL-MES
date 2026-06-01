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
