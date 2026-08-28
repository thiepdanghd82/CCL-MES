using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// P11 per-leg IPQC (Q6, Henry-approved) — materialise ONE <see cref="WoIpqcCheck"/>
/// (+ <see cref="WoIpqcCheckItem"/>s) scoped to a <see cref="WoLeg"/> using the
/// leg's SINGLE <c>ProcessLine</c>. This is the per-area PARTITION of a 1-leg WO's
/// multi-line IPQC bundle: golden 81092002 bundles SILK+PRESS_CNC+FINISHING = 57
/// items in one check; a PRINT(SILK) leg gets only the SILK subset, a CUT(PRESS_CNC)
/// leg only PRESS_CNC, etc.
///
/// Reuses the pure <see cref="IpqcLibraryMaterializer"/> (unchanged) — only the
/// library FILTER differs (single <c>leg.ProcessLine</c> vs the WO's resolved
/// multi-line set). <c>WoLegId</c> is a shadow property → EF.Property in LINQ +
/// Entry().Property() on write (8 entity classes untouched).
///
/// Idempotent: a leg that already owns a <see cref="WoIpqcCheck"/> is a NOOP.
/// Never touches the WO-level (WoLegId NULL) check → 1-leg parity intact.
/// </summary>
public sealed class IpqcLegMaterializer
{
    private readonly IMesDbContext _db;
    public IpqcLegMaterializer(IMesDbContext db) => _db = db;

    // Outcome vocabulary mirrors the WO-level TryAutoSync (auditable).
    public const string Materialized = "Materialized";
    public const string SkippedNoLibrary = "SkippedNoLibrary";
    public const string SkippedNoLine = "SkippedNoLine";       // leg.ProcessLine empty/unresolved
    public const string AlreadyExists = "AlreadyExists";
    public const string NotFound = "NotFound";

    public async Task<string> MaterializeForLegAsync(long legId, CancellationToken ct = default)
    {
        var ctx = (DbContext)_db;

        var leg = await _db.WoLegs.FirstOrDefaultAsync(l => l.Id == legId, ct);
        if (leg is null) return NotFound;

        // Idempotent — leg already has its own IPQC check.
        if (await _db.WoIpqcChecks.AnyAsync(c => EF.Property<long?>(c, "WoLegId") == legId, ct))
            return AlreadyExists;

        var line = (leg.ProcessLine ?? "").Trim();
        if (line.Length == 0) return SkippedNoLine;

        var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == leg.WorkOrderId, ct);
        if (wo is null) return NotFound;
        var productCode = await _db.Products
            .Where(p => p.Id == wo.ProductId).Select(p => p.ProductCode).FirstOrDefaultAsync(ct);

        // Thu hẹp về ĐÚNG MỘT line của leg (per-area, Q6) — nhưng việc thu hẹp
        // do QcLineLibrarySelector làm, không phải WHERE ở SQL: line như
        // PRESS_CNC lấy hạng mục qua cờ tick-box chứ không qua ProcessLine.
        var lib = await _db.CheckItemLibraries.AsNoTracking()
            .Where(c => c.Active && c.Ipqc
                     && (c.ProductCode == null || c.ProductCode == productCode))
            .ToListAsync(ct);

        var check = new WoIpqcCheck
        {
            WorkOrderId = leg.WorkOrderId,
            MaterialStatus = IpqcCheckStatus.Pending, PrintAStatus = IpqcCheckStatus.Pending,
            PrintBStatus = IpqcCheckStatus.Pending, PrintCStatus = IpqcCheckStatus.Pending,
            Judgment = IpqcJudgment.Pending, QaOutcome = QaOutcome.Pending,
        };

        var selected = QcLineLibrarySelector.Select(lib, new[] { line });
        var outcome = SkippedNoLibrary;
        if (selected.Count > 0)
        {
            var built = IpqcLibraryMaterializer.Build(selected, new[] { line });
            if (built.Items.Count > 0)
            {
                check.ItemsProfileSnapshotJson = built.ProfileSnapshotJson;
                check.ResolvedLines = line;
                foreach (var it in built.Items) check.Items.Add(it);
                outcome = Materialized;
            }
        }

        _db.WoIpqcChecks.Add(check);
        ctx.Entry(check).Property("WoLegId").CurrentValue = legId;
        foreach (var it in check.Items)                 // items tracked via cascade after Add
            ctx.Entry(it).Property("WoLegId").CurrentValue = legId;

        await _db.SaveChangesAsync(ct);
        return outcome;
    }
}
