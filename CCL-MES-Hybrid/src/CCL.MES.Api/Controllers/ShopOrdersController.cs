using CCL.MES.Application;
using CCL.MES.Shared;
using CCL.MES.Shared.Machines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.8 — Shop Order History (SpecHub parity). Read-only forensic
/// record of closed work orders (MesPhase SHIPPED or CANCELLED) with
/// KPI roll-ups, honouring the period + search filters. Any-auth.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/shop-orders")]
public sealed class ShopOrdersController : ControllerBase
{
    private readonly IMesDbContext _db;

    public ShopOrdersController(IMesDbContext db) => _db = db;

    private static readonly string[] ClosedPhases = { "SHIPPED", "CANCELLED" };

    [HttpGet("history")]
    public async Task<ActionResult<ShopOrderHistoryDto>> History(
        [FromQuery] string? period,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        var q = _db.WorkOrders.AsNoTracking()
            .Where(w => ClosedPhases.Contains(w.MesPhase));

        // Period filter on UpdatedAt (when the WO was last touched =
        // effectively when it closed).
        var cutoff = PeriodCutoff(period);
        if (cutoff is not null)
            q = q.Where(w => w.UpdatedAt.HasValue && w.UpdatedAt.Value >= cutoff.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(w =>
                w.WoNo.Contains(s)
                || w.ProductName.Contains(s)
                || (w.MachineCode != null && w.MachineCode.Contains(s))
                || (w.Customer != null && w.Customer.Name.Contains(s)));
        }

        var rows = await q
            .OrderByDescending(w => w.UpdatedAt)
            .Take(200)
            .Select(w => new ShopOrderHistoryRow
            {
                WoId = w.Id,
                WoNo = w.WoNo,
                CustomerName = w.Customer != null ? w.Customer.Name : "",
                ProductName = w.ProductName,
                MachineCode = w.MachineCode,
                MesPhase = w.MesPhase,
                TargetQty = w.TargetQty,
                QtyDone = w.QtyDoneCached,
                QtyNg = w.QtyNgCached,
                YieldPct = w.QtyDoneCached == 0
                    ? 0
                    : (int)((100L * (w.QtyDoneCached - w.QtyNgCached)) / w.QtyDoneCached),
                FinishedAt = w.UpdatedAt,
            })
            .ToListAsync(ct);

        var output = rows.Sum(r => r.QtyDone);
        var reject = rows.Sum(r => r.QtyNg);

        return Ok(new ShopOrderHistoryDto
        {
            TotalWos = rows.Count,
            Output = output,
            Reject = reject,
            YieldPct = output == 0 ? 0 : (int)(100L * (output - reject) / output),
            Rows = rows,
        });
    }

    private static DateTime? PeriodCutoff(string? period)
    {
        var today = DateTime.UtcNow.Date;
        return period switch
        {
            "today" => today,
            "7d" => today.AddDays(-7),
            "30d" => today.AddDays(-30),
            _ => null,
        };
    }
}
