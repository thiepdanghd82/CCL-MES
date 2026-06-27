using CCL.MES.Application;
using CCL.MES.Domain;
using CCL.MES.Shared;
using CCL.MES.Shared.Machines;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.8 — Shop Order History (SpecHub forensic-record parity). Read-only
/// record of closed work orders (MesPhase SHIPPED or CANCELLED) with KPI
/// roll-ups + per-WO runtime / setup / pause / OEE + sidebar aggregates,
/// honouring period / search / status / customer / machine filters.
/// Any-auth. No mutation surface.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/shop-orders")]
public sealed class ShopOrdersController : ControllerBase
{
    private readonly IMesDbContext _db;

    public ShopOrdersController(IMesDbContext db) => _db = db;

    private static readonly string[] ClosedPhases = { "SHIPPED", "CANCELLED" };

    // SpecHub status vocabulary: SHIPPED → "Done", CANCELLED → "Stopped".
    private static string StatusLabel(string mesPhase) => mesPhase == "CANCELLED" ? "Stopped" : "Done";

    [HttpGet("history")]
    public async Task<ActionResult<ShopOrderHistoryDto>> History(
        [FromQuery] string? period,
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] string? customer,
        [FromQuery] string? machine,
        CancellationToken ct = default)
    {
        var q = _db.WorkOrders.AsNoTracking()
            .Where(w => ClosedPhases.Contains(w.MesPhase));

        var cutoff = PeriodCutoff(period);
        if (cutoff is not null)
            q = q.Where(w => w.UpdatedAt.HasValue && w.UpdatedAt.Value >= cutoff.Value);

        // status: "DONE" → SHIPPED, "STOPPED" → CANCELLED.
        if (!string.IsNullOrWhiteSpace(status))
        {
            var phase = status.Trim().ToUpperInvariant() == "STOPPED" ? "CANCELLED" : "SHIPPED";
            q = q.Where(w => w.MesPhase == phase);
        }
        if (!string.IsNullOrWhiteSpace(customer))
            q = q.Where(w => w.Customer != null && w.Customer.Name == customer);
        if (!string.IsNullOrWhiteSpace(machine))
            q = q.Where(w => w.MachineCode == machine);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(w =>
                w.WoNo.Contains(s)
                || w.ProductName.Contains(s)
                || (w.MachineCode != null && w.MachineCode.Contains(s))
                || (w.Customer != null && w.Customer.Name.Contains(s)));
        }

        var raw = await q
            .OrderByDescending(w => w.UpdatedAt)
            .Take(200)
            .Select(w => new
            {
                w.Id,
                w.WoNo,
                Customer = w.Customer != null ? w.Customer.Name : "",
                w.ProductName,
                ProductCode = w.Product != null ? w.Product.ProductCode : null,
                w.MachineCode,
                w.MesPhase,
                w.TargetQty,
                Done = w.QtyDoneCached,
                Ng = w.QtyNgCached,
                Setup = w.SettingDurationSec,
                w.UpdatedAt,
            })
            .ToListAsync(ct);

        var woIds = raw.Select(r => r.Id).ToList();
        var machineCodes = raw.Where(r => r.MachineCode != null)
            .Select(r => r.MachineCode!).Distinct().ToList();
        var now = DateTime.UtcNow;

        // ── batch 1: runtime per WO (sum of run-session durations) ──
        var sessions = await _db.WoRunSessions.AsNoTracking()
            .Where(s => woIds.Contains(s.WoId))
            .Select(s => new { s.WoId, s.StartedAt, s.EndedAt })
            .ToListAsync(ct);
        var runtimeByWo = sessions
            .GroupBy(s => s.WoId)
            .ToDictionary(g => g.Key, g => g.Sum(s =>
            {
                var ended = s.EndedAt ?? now;
                return ended > s.StartedAt ? (long)(ended - s.StartedAt).TotalSeconds : 0L;
            }));

        // ── batch 2: pause events per WO, grouped by reason ──
        var pauses = await _db.WoPauseEvents.AsNoTracking()
            .Where(p => woIds.Contains(p.WoId))
            .Select(p => new { p.WoId, p.ReasonCode, p.StartedAt, p.EndedAt })
            .ToListAsync(ct);
        var pausesByWo = pauses.GroupBy(p => p.WoId).ToDictionary(g => g.Key, g => g.ToList());

        // ── batch 3: work-center ideal speed (OEE performance leg) +
        //    description (the "process" sub-label). MachineCode matches
        //    WorkCenter.Code — the same canonical entity the dashboard
        //    uses. IdealSpeedPcsH (pcs/hour) → ideal cycle = 3600/speed. ──
        var wcByCode = await _db.WorkCenters.AsNoTracking()
            .Where(wc => machineCodes.Contains(wc.Code))
            .ToDictionaryAsync(wc => wc.Code, wc => new { wc.IdealSpeedPcsH, Desc = wc.Description }, ct);

        // ── reason-code labels (Pause kind) for the downtime roll-up ──
        var reasonLabels = await _db.ReasonCodes.AsNoTracking()
            .Where(rc => rc.Kind == ReasonCodeKind.Pause)
            .ToDictionaryAsync(rc => rc.Code, rc => rc.LabelEn, ct);

        var rows = new List<ShopOrderHistoryRow>(raw.Count);
        foreach (var r in raw)
        {
            var runtime = runtimeByWo.TryGetValue(r.Id, out var rt) ? rt : 0L;
            var pauseList = pausesByWo.TryGetValue(r.Id, out var pl) ? pl : null;
            long pauseSec = 0;
            var reasonBuckets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (pauseList is not null)
            {
                foreach (var p in pauseList)
                {
                    var ended = p.EndedAt ?? now;
                    if (ended > p.StartedAt) pauseSec += (long)(ended - p.StartedAt).TotalSeconds;
                    var code = string.IsNullOrEmpty(p.ReasonCode) ? "(unknown)" : p.ReasonCode;
                    reasonBuckets[code] = reasonBuckets.TryGetValue(code, out var n) ? n + 1 : 1;
                }
            }

            // OEE = Availability × Performance × Quality (honest; null legs → null OEE).
            double? avail = (runtime + pauseSec) > 0 ? (double)runtime / (runtime + pauseSec) : null;
            double? perf = null;
            string? process = null;
            if (r.MachineCode is not null && wcByCode.TryGetValue(r.MachineCode, out var wc))
            {
                process = wc.Desc;
                if (wc.IdealSpeedPcsH is double speed && speed > 0 && runtime > 0)
                {
                    var idealCycleSec = 3600.0 / speed;
                    var plannedUnits = runtime / idealCycleSec;
                    if (plannedUnits > 0) perf = Math.Min(1.0, r.Done / plannedUnits);
                }
            }
            double? qual = r.Done > 0 ? (double)(r.Done - r.Ng) / r.Done : null;
            int? oeePct = (avail is not null && perf is not null && qual is not null)
                ? (int)Math.Round(100 * avail.Value * perf.Value * qual.Value)
                : null;

            rows.Add(new ShopOrderHistoryRow
            {
                WoId = r.Id,
                WoNo = r.WoNo,
                CustomerName = r.Customer,
                ProductName = r.ProductName,
                ProductCode = r.ProductCode,
                MachineCode = r.MachineCode,
                Process = process,
                MesPhase = r.MesPhase,
                TargetQty = r.TargetQty,
                QtyDone = r.Done,
                QtyNg = r.Ng,
                YieldPct = r.Done == 0 ? 0 : (int)(100L * (r.Done - r.Ng) / r.Done),
                ProgressPct = r.TargetQty == 0 ? 0 : (int)Math.Min(100, 100L * r.Done / r.TargetQty),
                OeePct = oeePct,
                RuntimeSec = runtime,
                SetupSec = r.Setup ?? 0,
                PauseCount = pauseList?.Count ?? 0,
                PauseReasons = reasonBuckets
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => new ShopOrderAggRow
                    {
                        Key = kv.Key,
                        Label = reasonLabels.TryGetValue(kv.Key, out var lbl) ? lbl : kv.Key,
                        Count = kv.Value,
                    }).ToList(),
                FinishedAt = r.UpdatedAt,
            });
        }

        var output = rows.Sum(r => r.QtyDone);
        var reject = rows.Sum(r => r.QtyNg);
        var oees = rows.Where(r => r.OeePct is not null).Select(r => r.OeePct!.Value).ToList();

        ShopOrderAggRow[] TopBy(Func<ShopOrderHistoryRow, string?> key) => rows
            .Where(r => !string.IsNullOrEmpty(key(r)))
            .GroupBy(r => key(r)!)
            .Select(g => new ShopOrderAggRow { Key = g.Key, Count = g.Count() })
            .OrderByDescending(a => a.Count).ThenBy(a => a.Key)
            .Take(5).ToArray();

        var topDowntime = rows.SelectMany(r => r.PauseReasons)
            .GroupBy(p => p.Key)
            .Select(g => new ShopOrderAggRow
            {
                Key = g.Key,
                Label = g.First().Label,
                Count = g.Sum(x => x.Count),
            })
            .OrderByDescending(a => a.Count).ThenBy(a => a.Key)
            .Take(5).ToArray();

        // Stable dropdown options over ALL closed WOs (ignore active filter).
        var allClosed = _db.WorkOrders.AsNoTracking().Where(w => ClosedPhases.Contains(w.MesPhase));
        var customers = await allClosed.Where(w => w.Customer != null)
            .Select(w => w.Customer!.Name).Distinct().OrderBy(n => n).ToListAsync(ct);
        var machines = await allClosed.Where(w => w.MachineCode != null)
            .Select(w => w.MachineCode!).Distinct().OrderBy(m => m).ToListAsync(ct);

        return Ok(new ShopOrderHistoryDto
        {
            TotalWos = rows.Count,
            DoneCount = rows.Count(r => r.MesPhase != "CANCELLED"),
            StoppedCount = rows.Count(r => r.MesPhase == "CANCELLED"),
            Output = output,
            Reject = reject,
            YieldPct = output == 0 ? 0 : (int)(100L * (output - reject) / output),
            AvgOeePct = oees.Count == 0 ? null : (int)Math.Round(oees.Average()),
            Rows = rows,
            TopCustomers = TopBy(r => r.CustomerName),
            TopMachines = TopBy(r => r.MachineCode),
            TopDowntimeReasons = topDowntime,
            Customers = customers,
            Machines = machines,
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
