using CCL.MES.Application;
using CCL.MES.Shared;
using CCL.MES.Shared.Machines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.8 — Machine Dashboard (SpecHub parity). Read-only; any
/// authenticated user. Live-recomputes a per-work-center status from
/// the active work order matched by the denormalised
/// <c>WorkOrder.MachineCode == WorkCenter.Code</c> string key.
/// No mutation surface.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/machines")]
public sealed class MachinesController : ControllerBase
{
    private readonly IMesDbContext _db;

    public MachinesController(IMesDbContext db) => _db = db;

    // A WO occupies its machine while it is anywhere from prepress prep
    // through the run. Once it reaches FQC it is off the press, so the
    // machine reads idle again (unless another WO is queued on it).
    private static readonly string[] ActivePhases =
    {
        "PREPRESS", "SETTING", "IPQC_WAIT", "QA_PENDING",
        "IPQC_APPROVED", "RUNNING", "PAUSED",
    };

    private static string StatusOf(string? mesPhase) => mesPhase switch
    {
        "RUNNING" or "PAUSED" => "Running",
        "SETTING" or "PREPRESS" or "IPQC_WAIT" or "QA_PENDING" or "IPQC_APPROVED" => "Setup",
        _ => "Idle",
    };

    [HttpGet("dashboard")]
    public async Task<ActionResult<MachineDashboardDto>> Dashboard(CancellationToken ct)
    {
        // Active WOs (in-memory): latest per machine wins.
        var activeWos = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.MachineCode != null && ActivePhases.Contains(w.MesPhase))
            .OrderByDescending(w => w.UpdatedAt)
            .Select(w => new
            {
                w.MachineCode,
                w.WoNo,
                w.MesPhase,
                w.TargetQty,
                w.QtyDoneCached,
                w.QtyNgCached,
                w.UpdatedAt,
            })
            .ToListAsync(ct);

        var byMachine = new Dictionary<string, (string WoNo, string Phase, int Target, int Done, int Ng, DateTime? At)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var w in activeWos)
        {
            // OrderByDescending(UpdatedAt) already ran, so the first row
            // per machine is the freshest — keep it, skip the rest.
            if (w.MachineCode is null || byMachine.ContainsKey(w.MachineCode)) continue;
            byMachine[w.MachineCode] = (w.WoNo, w.MesPhase, w.TargetQty, w.QtyDoneCached, w.QtyNgCached, w.UpdatedAt);
        }

        var wcs = await _db.WorkCenters.AsNoTracking()
            .Where(wc => wc.Active != false)
            .OrderBy(wc => wc.Area).ThenBy(wc => wc.Code)
            .Select(wc => new { wc.Id, wc.Code, wc.Description, wc.Area })
            .ToListAsync(ct);

        var items = new List<MachineDashboardItem>(wcs.Count);
        int running = 0, setup = 0, idle = 0;
        foreach (var wc in wcs)
        {
            byMachine.TryGetValue(wc.Code, out var active);
            var hasActive = active.WoNo is not null;
            var status = hasActive ? StatusOf(active.Phase) : "Idle";
            switch (status)
            {
                case "Running": running++; break;
                case "Setup": setup++; break;
                default: idle++; break;
            }

            items.Add(new MachineDashboardItem
            {
                WorkCenterId = wc.Id,
                Code = wc.Code,
                Description = wc.Description,
                Area = wc.Area,
                Status = status,
                ActiveWoNo = hasActive ? active.WoNo : null,
                ActiveMesPhase = hasActive ? active.Phase : null,
                TargetQty = hasActive ? active.Target : null,
                QtyDone = hasActive ? active.Done : null,
                QtyNg = hasActive ? active.Ng : null,
                LastUpdatedAt = hasActive ? active.At : null,
            });
        }

        return Ok(new MachineDashboardDto
        {
            Total = items.Count,
            Running = running,
            Setup = setup,
            Idle = idle,
            Machines = items,
        });
    }
}
