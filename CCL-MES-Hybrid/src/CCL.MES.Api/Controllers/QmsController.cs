using CCL.MES.Application;
using CCL.MES.Domain.Entities;
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

    [HttpGet("qc-history")]
    public async Task<ActionResult<QcHistoryDto>> QcHistory(
        [FromQuery] string? kind,
        [FromQuery] string? judgment,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        // Completed FQC/OQC checks (a verdict was reached), joined to the WO.
        var q = from c in _db.WoQcChecks.AsNoTracking()
                join w in _db.WorkOrders.AsNoTracking() on c.WorkOrderId equals w.Id
                where c.Judgment != WoQcJudgment.Pending
                select new { Check = c, w.WoNo };

        if (!string.IsNullOrWhiteSpace(kind))
        {
            var k = kind.Trim().ToUpperInvariant();
            q = q.Where(x => x.Check.QcKind == k);
        }
        if (string.Equals(judgment, "pass", StringComparison.OrdinalIgnoreCase))
            q = q.Where(x => x.Check.Judgment == WoQcJudgment.Pass);
        else if (string.Equals(judgment, "reject", StringComparison.OrdinalIgnoreCase))
            q = q.Where(x => x.Check.Judgment == WoQcJudgment.Reject);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.WoNo.Contains(s));
        }

        var raw = await q
            .OrderByDescending(x => x.Check.ApprovedAt ?? x.Check.InspectedAt)
            .Take(200)
            .Select(x => new
            {
                x.Check.Id,
                x.Check.WorkOrderId,
                x.WoNo,
                x.Check.QcKind,
                x.Check.Judgment,
                x.Check.JudgmentReason,
                x.Check.InspectedBy,
                x.Check.ReviewedBy,
                x.Check.ApprovedBy,
                x.Check.ApprovedAt,
                x.Check.InspectedAt,
            })
            .ToListAsync(ct);

        var rows = raw.Select(x => new QcHistoryRow
        {
            CheckId = x.Id,
            WoId = x.WorkOrderId,
            WoNo = x.WoNo,
            QcKind = x.QcKind,
            Judgment = x.Judgment.ToString(),
            JudgmentReason = x.JudgmentReason,
            InspectedBy = x.InspectedBy,
            ReviewedBy = x.ReviewedBy,
            ApprovedBy = x.ApprovedBy,
            CompletedAt = x.ApprovedAt ?? x.InspectedAt,
        }).ToList();

        var pass = rows.Count(r => r.Judgment == "Pass");
        var reject = rows.Count(r => r.Judgment == "Reject");

        return Ok(new QcHistoryDto
        {
            Total = rows.Count,
            Pass = pass,
            Reject = reject,
            PassRatePct = rows.Count == 0 ? 0 : (int)(100L * pass / rows.Count),
            Rows = rows,
        });
    }
}
