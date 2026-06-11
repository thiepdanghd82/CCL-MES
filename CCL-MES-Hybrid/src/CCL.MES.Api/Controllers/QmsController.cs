using CCL.MES.Application;
using CCL.MES.Shared;
using CCL.MES.Shared.Qms;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.9 — QMS Inspection Queue (SpecHub parity). Read-only worklist of
/// WOs due for each QC stage, derived from MesPhase. Any-auth (QC roles
/// act on the per-WO dashboards; this is just the "what's waiting" view).
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/qms")]
public sealed class QmsController : ControllerBase
{
    private readonly IMesDbContext _db;

    public QmsController(IMesDbContext db) => _db = db;

    private const string IpqcPhase = "IPQC_WAIT";
    private const string FqcPhase = "FQC_PENDING";
    private const string OqcPhase = "OQC_PENDING";

    [HttpGet("queue")]
    public async Task<ActionResult<QmsQueueDto>> Queue(CancellationToken ct)
    {
        var rows = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.MesPhase == IpqcPhase || w.MesPhase == FqcPhase || w.MesPhase == OqcPhase)
            .OrderBy(w => w.UpdatedAt)   // oldest-waiting first — FIFO worklist
            .Select(w => new
            {
                w.MesPhase,
                Row = new QmsQueueRow
                {
                    WoId = w.Id,
                    WoNo = w.WoNo,
                    ProductName = w.ProductName,
                    MachineCode = w.MachineCode,
                    TargetQty = w.TargetQty,
                    QtyDone = w.QtyDoneCached,
                    UpdatedAt = w.UpdatedAt,
                },
            })
            .ToListAsync(ct);

        var ipqc = rows.Where(r => r.MesPhase == IpqcPhase).Select(r => r.Row).ToList();
        var fqc = rows.Where(r => r.MesPhase == FqcPhase).Select(r => r.Row).ToList();
        var oqc = rows.Where(r => r.MesPhase == OqcPhase).Select(r => r.Row).ToList();

        return Ok(new QmsQueueDto
        {
            IpqcCount = ipqc.Count,
            FqcCount = fqc.Count,
            OqcCount = oqc.Count,
            Ipqc = ipqc,
            Fqc = fqc,
            Oqc = oqc,
        });
    }
}
