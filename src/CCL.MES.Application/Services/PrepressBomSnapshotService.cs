using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
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

        // Materials: snapshot from BOM IFF (a) no rows yet for this WO
        // AND (b) ProductRevision link is intact AND (c) MS has rows for
        // this product. Otherwise skip — operator fills via 7b-2 endpoint.
        var hasMaterials = await _db.WoMaterials.AnyAsync(m => m.WorkOrderId == workOrderId, ct);
        if (!hasMaterials && wo.ProductRevisionId is { } revId)
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
                    for (var i = 0; i < bomRows.Count; i++)
                    {
                        var ms = bomRows[i];
                        _db.WoMaterials.Add(new WoMaterial
                        {
                            WorkOrderId = workOrderId,
                            BomLineIdx = i,
                            MaterialCode = ms.ComponentPart,
                            MaterialDescription = ms.ComponentDescription,
                            // §5.3 baseline = QtyAssembly × TargetQty.
                            // Scrap factor NOT applied here; scrap is policy.
                            QtyRequired = ms.QtyAssembly * wo.TargetQty,
                            Uom = ms.Uom,
                            Status = PrepressCheckStatus.Pending,
                            CreatedAt = now,
                        });
                        materialsInserted++;
                    }
                }
            }
        }

        await _db.SaveChangesAsync(ct);
        return materialsInserted;
    }
}
