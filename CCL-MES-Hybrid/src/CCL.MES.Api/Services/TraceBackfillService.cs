using CCL.MES.Application;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared.Quality;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>
/// One-off (idempotent) backfill so WOs that already ran BEFORE the trace
/// feature existed show up in Traceability: builds the index row for every
/// WO and freezes each phase that has ALREADY concluded (data present). Safe
/// to re-run — freeze is idempotent by ContentHash, index is upsert.
/// </summary>
public sealed class TraceBackfillService
{
    private readonly IMesDbContext _db;
    private readonly ITraceFreezeService _freeze;
    private readonly ITraceIndexService _index;

    public TraceBackfillService(IMesDbContext db, ITraceFreezeService freeze, ITraceIndexService index)
    {
        _db = db; _freeze = freeze; _index = index;
    }

    public readonly record struct Result(int WorkOrders, int PhasesFrozen);

    public async Task<Result> BackfillAllAsync(string actor, CancellationToken ct = default)
    {
        var wos = await _db.WorkOrders.AsNoTracking()
            .Select(w => new { w.Id, w.MesPhase }).ToListAsync(ct);

        int frozen = 0;
        foreach (var w in wos)
        {
            // Per-WO isolation — one bad legacy row must not abort the whole
            // backfill (idempotent, so a re-run picks up any that were skipped).
            try
            {
                foreach (var phase in await EligiblePhasesAsync(w.Id, w.MesPhase ?? "", ct))
                {
                    var before = await _db.WoTraceSnapshots.CountAsync(s => s.WoId == w.Id && s.Phase == phase, ct);
                    await _freeze.FreezeAsync(w.Id, phase, actor, ct);
                    var after = await _db.WoTraceSnapshots.CountAsync(s => s.WoId == w.Id && s.Phase == phase, ct);
                    if (after > before) frozen++;
                }
                // Ensure an index row even for WOs with no concluded phase yet.
                await _index.TouchByIdAsync(w.Id, ct);
            }
            catch { /* skip this WO; re-run is safe */ }
        }
        return new Result(wos.Count, frozen);
    }

    // A phase is backfillable only when its source data has actually concluded
    // — never freeze an empty/pending phase.
    private async Task<List<string>> EligiblePhasesAsync(long woId, string mesPhase, CancellationToken ct)
    {
        var list = new List<string>();

        // Product: WO has left PREPRESS and materials were materialised.
        if (mesPhase is not ("" or "NEW" or "PREPRESS")
            && await _db.WoMaterials.AsNoTracking().AnyAsync(m => m.WorkOrderId == woId, ct))
            list.Add(TracePhase.Product);

        // IPQC: a judgment was submitted.
        if (await _db.WoIpqcChecks.AsNoTracking()
            .AnyAsync(c => c.WorkOrderId == woId && c.Judgment != IpqcJudgment.Pending, ct))
            list.Add(TracePhase.Ipqc);

        // FQC / OQC: a QC judgment concluded.
        if (await _db.WoQcChecks.AsNoTracking()
            .AnyAsync(c => c.WorkOrderId == woId && c.QcKind == "FQC" && c.Judgment != WoQcJudgment.Pending, ct))
            list.Add(TracePhase.Fqc);
        if (await _db.WoQcChecks.AsNoTracking()
            .AnyAsync(c => c.WorkOrderId == woId && c.QcKind == "OQC" && c.Judgment != WoQcJudgment.Pending, ct))
            list.Add(TracePhase.Oqc);

        return list;
    }
}
