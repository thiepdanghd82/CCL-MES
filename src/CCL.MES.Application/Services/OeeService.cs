using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// Ghi nhận sự kiện sản xuất theo máy (Start/Pause/Resume/Finish) và tính OEE.
/// OEE = Availability x Performance x Quality.
/// </summary>
public class OeeService
{
    private readonly IMesDbContext _db;
    public OeeService(IMesDbContext db) => _db = db;

    public Task<List<Machine>> GetMachinesAsync() =>
        _db.Machines.OrderBy(m => m.Code).ToListAsync();

    private async Task<(WorkOrder wo, Machine machine)?> ResolveAsync(long woId)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == woId);
        if (wo is null) return null;
        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.Code == wo.MachineCode);
        if (machine is null) return null;
        return (wo, machine);
    }

    private Task<ProductionLog?> OpenLogAsync(long woId) =>
        _db.ProductionLogs
            .Where(p => p.WorkOrderId == woId && p.EndAt == null)
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync();

    public async Task<ProductionLog?> StartAsync(long woId, string? op)
    {
        var r = await ResolveAsync(woId);
        if (r is null) return null;
        var (wo, machine) = r.Value;

        var open = await OpenLogAsync(woId);
        if (open is not null) { open.EndAt = DateTime.UtcNow; }

        var log = new ProductionLog
        {
            WorkOrderId = woId, MachineId = machine.Id,
            EventType = ProductionEventType.Run, StartAt = DateTime.UtcNow, OperatorId = op
        };
        machine.CurrentState = ProductionEventType.Run;
        _db.ProductionLogs.Add(log);
        await _db.SaveChangesAsync();
        return log;
    }

    public async Task<ProductionLog?> PauseAsync(long woId, PauseRequest req)
    {
        var r = await ResolveAsync(woId);
        if (r is null) return null;
        var (_, machine) = r.Value;

        var open = await OpenLogAsync(woId);
        if (open is not null) open.EndAt = DateTime.UtcNow;

        var stop = new ProductionLog
        {
            WorkOrderId = woId, MachineId = machine.Id,
            EventType = ProductionEventType.Stop, StartAt = DateTime.UtcNow,
            OperatorId = req.OperatorId, DowntimeReasonId = req.DowntimeReasonId
        };
        machine.CurrentState = ProductionEventType.Stop;
        _db.ProductionLogs.Add(stop);
        await _db.SaveChangesAsync();
        return stop;
    }

    public async Task<ProductionLog?> ResumeAsync(long woId, string? op) => await StartAsync(woId, op);

    public async Task<WorkOrder?> FinishAsync(long woId, FinishRunRequest req)
    {
        var r = await ResolveAsync(woId);
        if (r is null) return null;
        var (wo, machine) = r.Value;

        var open = await OpenLogAsync(woId);
        if (open is not null)
        {
            open.EndAt = DateTime.UtcNow;
            open.GoodQty = req.GoodQty;
            open.RejectQty = req.RejectQty;
            open.OperatorId = req.OperatorId ?? open.OperatorId;
        }

        wo.ProducedQty += req.GoodQty;
        machine.CurrentState = ProductionEventType.Idle;
        await _db.SaveChangesAsync();
        return wo;
    }

    public async Task<OeeResult?> ComputeAsync(long machineId, DateTime? from = null, DateTime? to = null)
    {
        var machine = await _db.Machines.FirstOrDefaultAsync(m => m.Id == machineId);
        if (machine is null) return null;

        var fromT = from ?? DateTime.UtcNow.Date;
        var toT = to ?? DateTime.UtcNow;

        var logs = await _db.ProductionLogs
            .Where(p => p.MachineId == machineId && p.StartAt >= fromT && p.StartAt <= toT)
            .ToListAsync();

        double runMin = logs.Where(l => l.EventType == ProductionEventType.Run).Sum(l => l.DurationMinutes);
        double downMin = logs.Where(l => l.EventType == ProductionEventType.Stop).Sum(l => l.DurationMinutes);
        double setupMin = logs.Where(l => l.EventType == ProductionEventType.Setup).Sum(l => l.DurationMinutes);
        int good = logs.Sum(l => l.GoodQty);
        int reject = logs.Sum(l => l.RejectQty);
        int total = good + reject;

        double planned = runMin + downMin + setupMin;
        double availability = planned > 0 ? runMin / planned : 0;
        double idealMin = machine.IdealCycleTimeSec * total / 60.0;
        double performance = runMin > 0 ? Math.Min(1.0, idealMin / runMin) : 0;
        double quality = total > 0 ? (double)good / total : 0;
        double oee = availability * performance * quality;

        return new OeeResult(
            machine.Id, machine.Code, machine.Name, machine.CurrentState.ToString(),
            Math.Round(planned, 1), Math.Round(runMin, 1), Math.Round(downMin, 1),
            good, reject,
            Math.Round(availability, 3), Math.Round(performance, 3),
            Math.Round(quality, 3), Math.Round(oee, 3));
    }
}
