using CCL.MES.Api.Hubs;
using CCL.MES.Application;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared.Quality;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CCL.MES.Api.Services;

/// <summary>
/// Keeps the MUTABLE <see cref="WoTraceIndex"/> row (one per WO) in sync so
/// the Traceability list is real-time: a WO shows up the moment it's
/// scanned/found — even before any phase is frozen. Touch is idempotent
/// (upsert by WoId) and, on success, fires <see cref="ShopfloorNotifierV2"/>
/// so subscribed clients pull the fresh list (notify-then-pull). It reads
/// WoTraceSnapshots ONLY to recompute the frozen flags — it never mutates a
/// snapshot, so immutability is untouched.
/// </summary>
public interface ITraceIndexService
{
    /// <summary>Upsert the index for the WO with this number (NOOP if the WO
    /// doesn't exist). Fires a real-time notify on success.</summary>
    Task TouchAsync(string woNo, CancellationToken ct = default);

    /// <summary>Upsert by WO id (used by the freeze hooks + backfill).</summary>
    Task TouchByIdAsync(long woId, CancellationToken ct = default);
}

public sealed class TraceIndexService : ITraceIndexService
{
    private readonly IMesDbContext _db;
    private readonly ShopfloorNotifierV2 _notifier;
    private readonly ILogger<TraceIndexService> _log;

    public TraceIndexService(IMesDbContext db, ShopfloorNotifierV2 notifier, ILogger<TraceIndexService> log)
    {
        _db = db; _notifier = notifier; _log = log;
    }

    public async Task TouchAsync(string woNo, CancellationToken ct = default)
    {
        var woId = await _db.WorkOrders.AsNoTracking()
            .Where(w => EF.Functions.Like(w.WoNo, woNo))
            .Select(w => (long?)w.Id).FirstOrDefaultAsync(ct);
        if (woId is long id) await TouchByIdAsync(id, ct);
    }

    public async Task TouchByIdAsync(long woId, CancellationToken ct = default)
    {
        // Project only the fields we need — the index does not need the rest.
        // Historical note: this was originally a workaround for bad CurrentStep
        // rows ('Done') that threw on conversion. Repaired 2026-08-19 (see
        // docs/RUNBOOK-CURRENTSTEP-REPAIR-2026-08-19.md); kept for cost, not fear.
        var wo = await _db.WorkOrders.AsNoTracking().Where(w => w.Id == woId)
            .Select(w => new { w.Id, w.WoNo, w.ProductId, w.CustomerId, w.MesPhase, w.ProductName })
            .FirstOrDefaultAsync(ct);
        if (wo is null) return;

        var product = await _db.Products.AsNoTracking()
            .Where(p => p.Id == wo.ProductId).Select(p => new { p.ProductCode, p.Name }).FirstOrDefaultAsync(ct);
        var customer = await _db.Customers.AsNoTracking()
            .Where(c => c.Id == wo.CustomerId).Select(c => c.Name).FirstOrDefaultAsync(ct);

        // Frozen flags recomputed from the immutable snapshots (read-only).
        var phases = await _db.WoTraceSnapshots.AsNoTracking()
            .Where(s => s.WoId == woId)
            .Select(s => new { s.Phase, s.FrozenAtUtc }).ToListAsync(ct);
        var latestFrozen = phases.Count == 0 ? (DateTime?)null : phases.Max(p => p.FrozenAtUtc);

        var now = DateTime.UtcNow;
        var row = await _db.WoTraceIndexes.FirstOrDefaultAsync(x => x.WoId == woId, ct);
        if (row is null)
        {
            row = new WoTraceIndex { WoId = woId, FirstScannedAtUtc = now, CreatedAt = now };
            _db.WoTraceIndexes.Add(row);
        }
        row.WoNo = wo.WoNo;
        row.ProductCode = product?.ProductCode;
        row.ProductName = product?.Name ?? wo.ProductName;
        row.Customer = customer;
        row.CurrentMesPhase = wo.MesPhase ?? "";
        row.LastScannedAtUtc = now;
        row.LastUpdatedAtUtc = now;
        row.ProductFrozen = phases.Any(p => p.Phase == TracePhase.Product);
        row.IpqcFrozen = phases.Any(p => p.Phase == TracePhase.Ipqc);
        row.FqcFrozen = phases.Any(p => p.Phase == TracePhase.Fqc);
        row.OqcFrozen = phases.Any(p => p.Phase == TracePhase.Oqc);
        row.LatestFrozenAtUtc = latestFrozen;

        await _db.SaveChangesAsync(ct);

        // Notify AFTER a successful commit only (notify-then-pull contract).
        try { await _notifier.NotifyChangedAsync($"trace_updated:{wo.WoNo}"); }
        catch (Exception ex) { _log.LogWarning(ex, "[trace] notify failed for {WoNo}", wo.WoNo); }
    }
}
