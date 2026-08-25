using CCL.MES.Application;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using CCL.MES.Shared.IpqcReview;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>
/// IPQC first-article — lazy-materialise + join + view-build for the MATERIAL
/// (SYSTEM) reconciliation grid. Owns all DbContext access (thin-controller):
/// materialises one <see cref="WoIpqcMaterialCheck"/> per WO-level BOM line,
/// resolves the WoMaterial → MaterialLot → IqcInspection join, and computes the
/// divergence (live for un-confirmed rows, frozen snapshot for confirmed ones).
///
/// SaveChanges lives here only on the GET lazy-materialise path (mirrors
/// <see cref="IpqcCheckMaterializer.EnsureForGetAsync"/>); the mutation path
/// returns tracked rows and lets <see cref="WoMutationExecutor"/> commit.
/// </summary>
public sealed class IpqcMaterialMaterializer
{
    private readonly IMesDbContext _db;

    public IpqcMaterialMaterializer(IMesDbContext db)
    {
        _db = db;
    }

    // Join-derived facts for one BOM line (before divergence classification).
    private readonly record struct JoinInfo(
        string MaterialCode, string? MaterialDescription, string? ActualLotNo,
        bool HasShadowFk, string? ExpectedPartNo, string? LotStatus,
        string? IqcReceiptNo, string? IqcResult);

    /// <summary>GET entry point: load the WO, lazy-materialise + build the view.
    /// Returns null when the WO does not exist (controller → 404).</summary>
    public async Task<IpqcMaterialSystemView?> GetViewAsync(long woId, CancellationToken ct = default)
    {
        var wo = await _db.WorkOrders.AsNoTracking().FirstOrDefaultAsync(w => w.Id == woId, ct);
        if (wo is null) return null;
        return await EnsureAndBuildViewAsync(wo, ct);
    }

    /// <summary>Material readiness rollup for the GoRun gate (Q1). Rows that were
    /// never materialised (no BOM) → AllResolved true (legacy parity).</summary>
    public async Task<(bool AllResolved, bool AnyPendingWaiver, bool AnyRejected)> RollupAsync(
        long woId, CancellationToken ct = default)
    {
        var rows = await _db.WoIpqcMaterialChecks.AsNoTracking()
            .Where(r => r.WorkOrderId == woId).ToListAsync(ct);
        return IpqcMaterialRollup.Compute(rows);
    }

    /// <summary>Validate an NG reason code against the Scrap catalog (kept here so
    /// the controller stays DbContext-free).</summary>
    public Task<bool> IsValidScrapReasonAsync(string code, CancellationToken ct = default)
        => _db.ReasonCodes.AsNoTracking().AnyAsync(r => r.Code == code && r.Kind == ReasonCodeKind.Scrap, ct);

    /// <summary>GET path: ensure rows exist (lazy-materialise from WoMaterial),
    /// then build the view. Concurrent first-readers race on the unique index —
    /// losers clear + refetch (no audit; this is an insert race, not a WO
    /// RowVersion conflict).</summary>
    public async Task<IpqcMaterialSystemView> EnsureAndBuildViewAsync(WorkOrder wo, CancellationToken ct = default)
    {
        var existing = await _db.WoIpqcMaterialChecks.AsNoTracking()
            .AnyAsync(r => r.WorkOrderId == wo.Id, ct);
        if (!existing)
        {
            var join = await LoadJoinAsync(wo.Id, ct);
            foreach (var kv in join.OrderBy(k => k.Key))
                _db.WoIpqcMaterialChecks.Add(NewPendingRow(wo.Id, kv.Key, kv.Value));
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                if (_db is DbContext dbCtx) dbCtx.ChangeTracker.Clear();
            }
        }

        return await BuildViewAsync(wo, ct);
    }

    /// <summary>Mutation path: return the tracked rows (materialise all if none
    /// exist yet — no SaveChanges; the executor commits).</summary>
    public async Task<List<WoIpqcMaterialCheck>> GetOrCreateRowsForMutationAsync(long woId, CancellationToken ct = default)
    {
        var rows = await _db.WoIpqcMaterialChecks.Where(r => r.WorkOrderId == woId).ToListAsync(ct);
        if (rows.Count > 0) return rows;

        var join = await LoadJoinAsync(woId, ct);
        foreach (var kv in join.OrderBy(k => k.Key))
        {
            var row = NewPendingRow(woId, kv.Key, kv.Value);
            _db.WoIpqcMaterialChecks.Add(row);
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Compute the divergence snapshot for one BOM line at confirm time
    /// (freeze source). Returns null when the line has no WoMaterial.</summary>
    public async Task<WoIpqcMaterialCheckService.MaterialDivergenceSnapshot?> ComputeSnapshotAsync(
        long woId, int bomLineIdx, CancellationToken ct = default)
    {
        var join = await LoadJoinAsync(woId, ct);
        if (!join.TryGetValue(bomLineIdx, out var info)) return null;
        return ToSnapshot(info);
    }

    /// <summary>Build the view DTO from the persisted rows + the live join.
    /// Confirmed rows render their frozen snapshot; Pending rows render the live
    /// divergence so the operator sees the warning before confirming.</summary>
    public async Task<IpqcMaterialSystemView> BuildViewAsync(WorkOrder wo, CancellationToken ct = default)
    {
        var rows = await _db.WoIpqcMaterialChecks.AsNoTracking()
            .Where(r => r.WorkOrderId == wo.Id)
            .OrderBy(r => r.Sort).ThenBy(r => r.BomLineIdx)
            .ToListAsync(ct);
        var join = await LoadJoinAsync(wo.Id, ct);

        var (allResolved, anyPending, anyRejected) = IpqcMaterialRollup.Compute(rows);

        var viewRows = rows.Select(r =>
        {
            var confirmed = r.Status != IpqcCheckStatus.Pending;
            // Confirmed → frozen snapshot; Pending → live join.
            if (!confirmed && join.TryGetValue(r.BomLineIdx, out var info))
            {
                var snap = ToSnapshot(info);
                return new IpqcMaterialRow
                {
                    BomLineIdx = r.BomLineIdx,
                    MaterialCode = r.MaterialCode,
                    MaterialDescription = r.MaterialDescription,
                    SourceIqcReceiptNo = snap.SourceIqcReceiptNo,
                    ActualAtMachine = snap.ActualLotNo,
                    ExpectedPartNo = snap.ExpectedPartNo,
                    MaterialLotStatus = snap.MaterialLotStatus,
                    IqcResult = snap.IqcResult,
                    DivergenceKind = snap.DivergenceKind,
                    DivergenceFlags = snap.DivergenceFlags,
                    IsDivergent = snap.IsDivergent,
                    Status = r.Status.ToString(),
                    NgReasonCode = r.NgReasonCode,
                    NgNote = r.NgNote,
                    DivergenceApprovalStatus = r.DivergenceApprovalStatus.ToString(),
                    ApprovedBy = r.ApprovedBy,
                    ApprovedAt = r.ApprovedAt,
                    ApprovalReason = r.ApprovalReason,
                };
            }

            return new IpqcMaterialRow
            {
                BomLineIdx = r.BomLineIdx,
                MaterialCode = r.MaterialCode,
                MaterialDescription = r.MaterialDescription,
                SourceIqcReceiptNo = r.SourceIqcReceiptNo,
                ActualAtMachine = r.ActualLotNo,
                ExpectedPartNo = r.ExpectedPartNo,
                MaterialLotStatus = r.MaterialLotStatusSnapshot,
                IqcResult = r.IqcResultSnapshot,
                DivergenceKind = r.DivergenceKind,
                DivergenceFlags = r.DivergenceFlags,
                IsDivergent = r.DivergenceFlags != 0,
                Status = r.Status.ToString(),
                NgReasonCode = r.NgReasonCode,
                NgNote = r.NgNote,
                DivergenceApprovalStatus = r.DivergenceApprovalStatus.ToString(),
                ApprovedBy = r.ApprovedBy,
                ApprovedAt = r.ApprovedAt,
                ApprovalReason = r.ApprovalReason,
            };
        }).ToList();

        return new IpqcMaterialSystemView
        {
            WoId = wo.Id,
            WoNo = wo.WoNo,
            MesPhase = wo.MesPhase,
            ETag = Convert.ToBase64String(wo.RowVersion),
            AllResolved = allResolved,
            AnyPendingWaiver = anyPending,
            AnyRejected = anyRejected,
            Rows = viewRows,
        };
    }

    private static WoIpqcMaterialCheck NewPendingRow(long woId, int bomLineIdx, JoinInfo info) => new()
    {
        WorkOrderId = woId,
        BomLineIdx = bomLineIdx,
        MaterialCode = info.MaterialCode,
        MaterialDescription = info.MaterialDescription,
        Status = IpqcCheckStatus.Pending,
        DivergenceApprovalStatus = DivergenceApprovalStatus.NotRequired,
        DivergenceKind = "None",
        Sort = bomLineIdx,
    };

    private static WoIpqcMaterialCheckService.MaterialDivergenceSnapshot ToSnapshot(JoinInfo info)
    {
        var d = MaterialSystemDivergence.Compute(new MaterialSystemDivergence.Input(
            HasShadowFk: info.HasShadowFk,
            IqcResult: info.IqcResult,
            MaterialCode: info.MaterialCode,
            LotPartNo: info.ExpectedPartNo,
            LotStatus: info.LotStatus));
        return new WoIpqcMaterialCheckService.MaterialDivergenceSnapshot(
            SourceIqcReceiptNo: info.IqcReceiptNo,
            ExpectedPartNo: info.ExpectedPartNo,
            ActualLotNo: info.ActualLotNo,
            MaterialLotStatus: info.LotStatus,
            IqcResult: info.IqcResult,
            HasShadowFk: info.HasShadowFk,
            DivergenceFlags: (int)d.Flags,
            DivergenceKind: d.Kind,
            IsDivergent: d.IsDivergent);
    }

    /// <summary>Resolve the WoMaterial → MaterialLot → IqcInspection join for
    /// every WO-level (WoLegId IS NULL) BOM line, keyed by BomLineIdx. Three
    /// small AsNoTracking reads (the shadow MaterialLotId FK is projected via
    /// EF.Property so it works without touching the WoMaterial entity file).</summary>
    private async Task<Dictionary<int, JoinInfo>> LoadJoinAsync(long woId, CancellationToken ct)
    {
        var mats = await _db.WoMaterials.AsNoTracking()
            .Where(m => m.WorkOrderId == woId && EF.Property<long?>(m, "WoLegId") == null)
            .Select(m => new
            {
                m.BomLineIdx,
                m.MaterialCode,
                m.MaterialDescription,
                m.LotNo,
                m.PartScan,
                MaterialLotId = EF.Property<long?>(m, "MaterialLotId"),
            })
            .ToListAsync(ct);

        var lotIds = mats.Where(m => m.MaterialLotId != null)
            .Select(m => m.MaterialLotId!.Value).Distinct().ToList();
        var lots = lotIds.Count == 0
            ? new()
            : await _db.MaterialLots.AsNoTracking()
                .Where(l => lotIds.Contains(l.Id))
                .Select(l => new { l.Id, l.PartNo, l.Status, l.IqcInspectionId })
                .ToListAsync(ct);

        var iqcIds = lots.Where(l => l.IqcInspectionId != null)
            .Select(l => l.IqcInspectionId!.Value).Distinct().ToList();
        var iqcs = iqcIds.Count == 0
            ? new()
            : await _db.IqcInspections.AsNoTracking()
                .Where(i => iqcIds.Contains(i.Id))
                .Select(i => new { i.Id, i.ReceiptNo, i.Result })
                .ToListAsync(ct);

        var dict = new Dictionary<int, JoinInfo>();
        foreach (var m in mats)
        {
            var lot = m.MaterialLotId is null ? null : lots.FirstOrDefault(l => l.Id == m.MaterialLotId);
            var iqc = lot?.IqcInspectionId is null ? null : iqcs.FirstOrDefault(i => i.Id == lot.IqcInspectionId);
            dict[m.BomLineIdx] = new JoinInfo(
                MaterialCode: m.MaterialCode,
                MaterialDescription: m.MaterialDescription,
                ActualLotNo: string.IsNullOrWhiteSpace(m.LotNo) ? m.PartScan : m.LotNo,
                HasShadowFk: m.MaterialLotId != null,
                ExpectedPartNo: lot?.PartNo,
                LotStatus: lot?.Status,
                IqcReceiptNo: iqc?.ReceiptNo,
                IqcResult: iqc?.Result.ToString());
        }
        return dict;
    }
}
