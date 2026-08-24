using CCL.MES.Application;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.Routing;
using CCL.MES.Shared.RunningSurface;
using CCL.MES.Shared.SettingChecks;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>
/// A2 thin-controller — lazy-materialise (GET path SaveChanges) + read-view
/// build + routing→process-scope resolve for the SETTING check surface, pulled
/// out of <c>SettingChecksController</c> so the controller stays thin (L40) and
/// the GET-materialise SaveChanges lives in Services/, not Controllers/ (mirror
/// <see cref="IpqcCheckMaterializer"/>).
///
/// <para>Conflict on the GET-materialise is a <see cref="DbUpdateException"/>
/// race on the composite unique index (two first-readers insert) — the loser
/// clears the tracker + refetches; it is NOT a WO RowVersion conflict, so no
/// audit. Mutation-path materialise stays in the Application
/// <see cref="SettingCheckService"/> (tracked-only, no SaveChanges — committed
/// by the executor).</para>
/// </summary>
public sealed class SettingCheckMaterializer
{
    private readonly IMesDbContext _db;

    public SettingCheckMaterializer(IMesDbContext db) => _db = db;

    /// <summary>Resolve which SETTING processes apply from the WO routing plan.
    /// Unknown / no-routing → both true (safe fallback per SettingProcessScope).</summary>
    public async Task<(bool HasPrint, bool HasCut)> ResolveProcessScopeAsync(
        WorkOrder wo, string? productCode, CancellationToken ct = default)
    {
        var ops = string.IsNullOrWhiteSpace(productCode)
            ? new List<RoutingLegResolver.RoutingOp>()
            : await _db.RoutingOperations.AsNoTracking().Where(r => r.PartNo == productCode)
                .Select(r => new RoutingLegResolver.RoutingOp(
                    r.OpNo, r.Operation, r.WorkCenterNo, r.WorkCenterDescription))
                .ToListAsync(ct);
        var map = await _db.ProcessLegMaps.AsNoTracking().Where(m => m.Active)
            .Select(m => new RoutingLegResolver.MapEntry(
                m.MatchType, m.MatchValue, m.LegKind, m.Method, m.ProcessLine, m.Sort))
            .ToListAsync(ct);
        var plan = RoutingLegResolver.Resolve(ops, map);
        return SettingProcessScope.FromLegKinds(plan.Legs.Select(l => l.LegKind));
    }

    /// <summary>GET path: lazy-materialise the item set on first visit (owns the
    /// SaveChanges + unique-index race handling), then return the ordered rows.</summary>
    public async Task<IReadOnlyList<WoSettingCheckItem>> EnsureForGetAsync(
        long woId, bool hasPrint, bool hasCut, CancellationToken ct = default)
    {
        var count = await _db.WoSettingCheckItems.AsNoTracking()
            .CountAsync(i => i.WorkOrderId == woId, ct);
        if (count == 0)
        {
            var svc = new SettingCheckService(_db);
            var added = await svc.MaterializeAsync(woId, hasPrint, hasCut, ct);
            if (added > 0)
            {
                try { await _db.SaveChangesAsync(ct); }
                catch (DbUpdateException)
                {
                    if (_db is DbContext dbCtx) dbCtx.ChangeTracker.Clear();
                }
            }
        }

        return await _db.WoSettingCheckItems.AsNoTracking()
            .Where(i => i.WorkOrderId == woId)
            .OrderBy(i => i.Sort).ThenBy(i => i.Id)
            .ToListAsync(ct);
    }

    /// <summary>Build the read view DTO from a materialised item set + the WO's
    /// applicable defect options (base + per-product). One options query.</summary>
    public async Task<SettingChecksView> BuildViewAsync(
        WorkOrder wo, string? productCode, bool hasPrint, bool hasCut,
        IReadOnlyList<WoSettingCheckItem> items, CancellationToken ct = default)
    {
        var etag = Convert.ToBase64String(wo.RowVersion);

        var itemIds = items.Select(i => i.ItemKey).Distinct().ToList();
        var opts = await _db.CheckItemDefectOptions.AsNoTracking()
            .Where(o => o.Active && itemIds.Contains(o.ItemId)
                     && (o.ProductCode == null || o.ProductCode == productCode))
            .OrderBy(o => o.Sort).ThenBy(o => o.Id)
            .ToListAsync(ct);
        var byItem = opts.GroupBy(o => o.ItemId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var itemViews = items.Select(i => new SettingCheckItemView
        {
            ItemKey = i.ItemKey,
            ProcessKind = i.ProcessKind,
            Label = i.Label,
            Standard = i.Standard,
            GroupLabel = i.GroupLabel,
            Applicable = i.Applicable,
            Status = i.Status.ToString(),
            DefectCode = i.DefectCode,
            NgNote = i.NgNote,
            AdHoc = i.AdHoc,
            Sort = i.Sort,
            DefectOptions = (byItem.TryGetValue(i.ItemKey, out var os) ? os : new List<CheckItemDefectOption>())
                .Select(o => new SettingDefectOptionView
                {
                    DefectCode = o.DefectCode,
                    LabelVi = o.LabelVi,
                    LabelEn = o.LabelEn,
                    PerProduct = o.ProductCode != null,
                    Sort = o.Sort,
                }).ToList(),
        }).ToList();

        return new SettingChecksView
        {
            WoId = wo.Id,
            WoNo = wo.WoNo,
            MesPhase = wo.MesPhase,
            ETag = etag,
            ProductCode = productCode,
            HasPrint = hasPrint,
            HasCut = hasCut,
            Ready = SettingCheckService.Rollup(items, hasPrint, hasCut),
            Items = itemViews,
        };
    }
}
