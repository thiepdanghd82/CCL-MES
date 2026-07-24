using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.Routing;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// P10.7b-1 — materialises the PREPRESS row-level child tables for a WO:
/// <list type="bullet">
/// <item>One <see cref="WoMaterial"/> per <see cref="ManufacturingStructure"/>
/// row where <c>ParentPart == Product.ProductCode</c> at the time of
/// WO creation (BOM snapshot per contract §5.3).</item>
/// <item>One <see cref="WoPlateCheck"/> per WO (1:1).</item>
/// <item>One <see cref="WoCutterCheck"/> per WO (1:1).</item>
/// </list>
///
/// Called from <c>WorkOrderService.CreateAsync</c> for new WOs + by the
/// 7b-2 endpoint as a lazy materialiser for legacy WOs that lacked a
/// snapshot at creation time. Idempotent — re-running on a WO that
/// already has rows is a NOOP, never duplicates.
///
/// Per §5.3: rows are frozen at snapshot time; BOM revisions post-WO
/// do NOT update existing rows. The service does not look up newer BOM
/// versions for already-snapshotted WOs.
/// </summary>
public sealed class PrepressBomSnapshotService
{
    private readonly IMesDbContext _db;
    public PrepressBomSnapshotService(IMesDbContext db) => _db = db;

    /// <summary>
    /// Materialise the 3 child surfaces for <paramref name="workOrderId"/>.
    /// Returns the number of <c>WoMaterial</c> rows inserted (0 if the WO
    /// already has a materials snapshot OR the BOM lookup returned no
    /// rows). Plate + cutter rows always inserted if missing.
    /// </summary>
    public async Task<int> MaterializeAsync(long workOrderId, CancellationToken ct = default)
    {
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == workOrderId, ct);
        if (wo is null) return 0;

        var now = DateTime.UtcNow;
        var materialsInserted = 0;

        // Plate + Cutter: 1:1 — only insert if missing.
        var hasPlate = await _db.WoPlateChecks.AnyAsync(p => p.WorkOrderId == workOrderId, ct);
        if (!hasPlate)
        {
            _db.WoPlateChecks.Add(new WoPlateCheck
            {
                WorkOrderId = workOrderId,
                Status = PrepressCheckStatus.Pending,
                CreatedAt = now,
            });
        }

        var hasCutter = await _db.WoCutterChecks.AnyAsync(c => c.WorkOrderId == workOrderId, ct);
        if (!hasCutter)
        {
            _db.WoCutterChecks.Add(new WoCutterCheck
            {
                WorkOrderId = workOrderId,
                Status = PrepressCheckStatus.Pending,
                CreatedAt = now,
            });
        }

        // Materials: re-sync from the IFS BOM (Structure) by BOM-line
        // ordinal. The BOM-sourced columns (Part No / Description /
        // Required / UOM / Scrap Factor / Scrap %) are refreshed from
        // ManufacturingStructure every call, but the operator-entered
        // columns (QtyLoaded / LotNo / Status / NG) are PRESERVED. We only
        // write when something actually changed so the WO's optimistic
        // ETag stays stable for concurrent operators. When the BOM lookup
        // yields no rows (BOM not imported yet / no ProductRevision link)
        // we leave any existing rows untouched — never wipe a snapshot
        // just because the Structure is temporarily absent.
        var dirty = false;
        if (wo.ProductRevisionId is { } revId)
        {
            var revision = await _db.ProductRevisions
                .Where(r => r.Id == revId)
                .Select(r => new { r.ProductId })
                .FirstOrDefaultAsync(ct);
            if (revision is not null)
            {
                var productCode = await _db.Products
                    .Where(p => p.Id == revision.ProductId)
                    .Select(p => p.ProductCode)
                    .FirstOrDefaultAsync(ct);
                if (!string.IsNullOrEmpty(productCode))
                {
                    var bomRows = await _db.ManufacturingStructures
                        .Where(ms => ms.ParentPart == productCode)
                        .OrderBy(ms => ms.Id)
                        .ToListAsync(ct);

                    if (bomRows.Count > 0)
                    {
                        var existing = await _db.WoMaterials
                            .Where(m => m.WorkOrderId == workOrderId)
                            .ToListAsync(ct);
                        var byIdx = existing.ToDictionary(m => m.BomLineIdx);

                        for (var i = 0; i < bomRows.Count; i++)
                        {
                            var ms = bomRows[i];
                            var reqQty = ms.QtyAssembly * wo.TargetQty;

                            if (byIdx.TryGetValue(i, out var row))
                            {
                                // Refresh BOM-sourced fields; keep operator fields.
                                if (row.MaterialCode != ms.ComponentPart) { row.MaterialCode = ms.ComponentPart; dirty = true; }
                                if (row.MaterialDescription != ms.ComponentDescription) { row.MaterialDescription = ms.ComponentDescription; dirty = true; }
                                if (row.QtyRequired != reqQty) { row.QtyRequired = reqQty; dirty = true; }
                                if (row.Uom != ms.Uom) { row.Uom = ms.Uom; dirty = true; }
                                if (row.ScrapFactor != ms.ScrapFactor) { row.ScrapFactor = ms.ScrapFactor; dirty = true; }
                                if (row.ScrapPercent != ms.ScrapPct) { row.ScrapPercent = ms.ScrapPct; dirty = true; }
                            }
                            else
                            {
                                _db.WoMaterials.Add(new WoMaterial
                                {
                                    WorkOrderId = workOrderId,
                                    BomLineIdx = i,
                                    MaterialCode = ms.ComponentPart,
                                    MaterialDescription = ms.ComponentDescription,
                                    // §5.3 baseline = QtyAssembly × TargetQty.
                                    QtyRequired = reqQty,
                                    Uom = ms.Uom,
                                    ScrapFactor = ms.ScrapFactor,
                                    ScrapPercent = ms.ScrapPct,
                                    Status = PrepressCheckStatus.Pending,
                                    CreatedAt = now,
                                });
                                materialsInserted++;
                                dirty = true;
                            }
                        }

                        // BOM shrank: drop trailing rows the operator hasn't
                        // touched. Rows with recorded work are kept so we
                        // never destroy an operator's entry.
                        foreach (var row in existing.Where(m => m.BomLineIdx >= bomRows.Count))
                        {
                            if (row.Status == PrepressCheckStatus.Pending
                                && row.QtyLoaded is null
                                && string.IsNullOrEmpty(row.LotNo))
                            {
                                _db.WoMaterials.Remove(row);
                                dirty = true;
                            }
                        }
                    }
                }
            }
        }

        // Save only when plate/cutter were inserted or the BOM re-sync
        // actually changed something — keeps the WO ETag stable otherwise.
        if (!hasPlate || !hasCutter || dirty)
            await _db.SaveChangesAsync(ct);
        return materialsInserted;
    }

    /// <summary>
    /// P11 per-leg (Henry-approved): materialise the PREPRESS surface for ONE
    /// <see cref="WoLeg"/>, scoped by <c>WoLegId = legId</c> — mirror the golden
    /// structure of a 1-leg WO (81092000) but per branch:
    /// <list type="bullet">
    /// <item><b>Materials</b> — Option A: FULL BOM per leg (⚠ Ops-confirm: no
    /// material→leg split rule yet; each leg snapshots every BOM line, same as
    /// the 1-leg WO, only <c>WoLegId</c> differs).</item>
    /// <item><b>Plate/Cutter</b> — only when the leg's kind needs it
    /// (<see cref="LegPrepressTools"/>): PRINT→plate, CUT/TAPE→cutter,
    /// PRINT_CUT→both, ASSEMBLY→neither.</item>
    /// </list>
    /// Idempotent — existence scoped by <c>WoLegId</c> so re-running never
    /// duplicates and legs never collide. WO 1-leg (WoLegId NULL) path is the
    /// method above and is left completely untouched (parity).
    /// Returns the number of <see cref="WoMaterial"/> rows inserted.
    /// </summary>
    public async Task<int> MaterializeForLegAsync(long legId, CancellationToken ct = default)
    {
        // WoLegId is a SHADOW property (configured in MesDbContext, not a CLR
        // member) — read via EF.Property in LINQ + write via Entry().Property()
        // after the entity is tracked. Keeps the 8 entity classes untouched.
        var ctx = (DbContext)_db;

        var leg = await _db.WoLegs.FirstOrDefaultAsync(l => l.Id == legId, ct);
        if (leg is null) return 0;
        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == leg.WorkOrderId, ct);
        if (wo is null) return 0;

        var now = DateTime.UtcNow;
        var materialsInserted = 0;
        var dirty = false;

        // Plate/Cutter: per Q-B only the relevant leg kinds get a tool check.
        if (LegPrepressTools.NeedsPlate(leg.LegKind)
            && !await _db.WoPlateChecks.AnyAsync(p => EF.Property<long?>(p, "WoLegId") == legId, ct))
        {
            var plate = new WoPlateCheck { WorkOrderId = wo.Id, Status = PrepressCheckStatus.Pending, CreatedAt = now };
            _db.WoPlateChecks.Add(plate);
            ctx.Entry(plate).Property("WoLegId").CurrentValue = legId;
            dirty = true;
        }
        if (LegPrepressTools.NeedsCutter(leg.LegKind)
            && !await _db.WoCutterChecks.AnyAsync(c => EF.Property<long?>(c, "WoLegId") == legId, ct))
        {
            var cutter = new WoCutterCheck { WorkOrderId = wo.Id, Status = PrepressCheckStatus.Pending, CreatedAt = now };
            _db.WoCutterChecks.Add(cutter);
            ctx.Entry(cutter).Property("WoLegId").CurrentValue = legId;
            dirty = true;
        }

        // Materials — Option A full BOM per leg. Same resolution as WO-level
        // (ProductRevision → ProductCode → ManufacturingStructures) but the
        // snapshot rows carry WoLegId. Frozen: existing per-leg rows are not
        // BOM-refreshed (mirror §5.3 freeze); insert-if-missing only.
        var productCode = wo.ProductRevisionId is { } revId
            ? await _db.ProductRevisions.Where(r => r.Id == revId)
                .Join(_db.Products, r => r.ProductId, p => p.Id, (r, p) => p.ProductCode)
                .FirstOrDefaultAsync(ct)
            : null;
        if (!string.IsNullOrEmpty(productCode))
        {
            var bomRows = await _db.ManufacturingStructures
                .Where(ms => ms.ParentPart == productCode)
                .OrderBy(ms => ms.Id)
                .ToListAsync(ct);
            if (bomRows.Count > 0)
            {
                var existingIdx = (await _db.WoMaterials
                    .Where(m => EF.Property<long?>(m, "WoLegId") == legId)
                    .Select(m => m.BomLineIdx).ToListAsync(ct))
                    .ToHashSet();

                for (var i = 0; i < bomRows.Count; i++)
                {
                    if (existingIdx.Contains(i)) continue; // idempotent per (WoLegId, BomLineIdx)
                    var ms = bomRows[i];
                    var mat = new WoMaterial
                    {
                        WorkOrderId = wo.Id,
                        BomLineIdx = i,
                        MaterialCode = ms.ComponentPart,
                        MaterialDescription = ms.ComponentDescription,
                        QtyRequired = ms.QtyAssembly * wo.TargetQty,
                        Uom = ms.Uom,
                        ScrapFactor = ms.ScrapFactor,
                        ScrapPercent = ms.ScrapPct,
                        Status = PrepressCheckStatus.Pending,
                        CreatedAt = now,
                    };
                    _db.WoMaterials.Add(mat);
                    ctx.Entry(mat).Property("WoLegId").CurrentValue = legId;
                    materialsInserted++;
                    dirty = true;
                }
            }
        }

        if (dirty) await _db.SaveChangesAsync(ct);
        return materialsInserted;
    }
}
