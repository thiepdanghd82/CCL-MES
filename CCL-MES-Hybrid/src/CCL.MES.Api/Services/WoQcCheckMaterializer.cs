using CCL.MES.Application;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>
/// A2 thin-controller — lazy-materialise + self-heal của WoQcCheck (đường GET
/// /qc/{kind}) rút khỏi <c>WoQcReviewController</c> để SaveChanges rời
/// <c>Controllers/</c> (gate-thin đi xuống, L40) và luật materialise sống ở MỘT
/// nơi test được.
///
/// <para><b>Khác đường mutation.</b> Conflict ở đây là <see cref="DbUpdateException"/>
/// đua trên UNIQUE index (hai người đọc đầu tiên cùng insert) — KHÔNG phải
/// RowVersion concurrency (WO_STATE_CONFLICT). Kẻ thua clear tracker + refetch;
/// không emit audit (đúng bản inline cũ — đây là insert-if-missing lúc đọc, không
/// phải mutation nghiệp vụ).</para>
///
/// <para><b>Byte-identical.</b> Thứ tự + query + nhánh heal giữ nguyên hệt block
/// GET cũ (test wire /qc GET không sửa mà vẫn xanh là bằng chứng). Resolve
/// snapshot Q4 (Product override → default → "{}") vẫn LAZY trong nhánh (chỉ gọi
/// khi thiếu/rỗng) — không thêm query trên happy-path.</para>
///
/// <para>Đường MUTATION (<c>GetOrCreateCheckAsync</c> ở WoQcMutationControllerBase)
/// giữ nguyên — nó không SaveChanges (commit ở CommitAndAudit) nên không đụng gate.</para>
/// </summary>
public sealed class WoQcCheckMaterializer
{
    private readonly IMesDbContext _db;

    public WoQcCheckMaterializer(IMesDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Đảm bảo có một WoQcCheck (kèm Items) cho (woId, kind); tạo mới hoặc heal
    /// snapshot rỗng nếu cần. Trả bản AsNoTracking sau cùng (có thể null nếu WO
    /// bị xoá giữa chừng — gần như không xảy ra).
    /// </summary>
    public async Task<WoQcCheck?> EnsureMaterializedAsync(
        long woId, string kind, long productId, CancellationToken ct = default)
    {
        var check = await _db.WoQcChecks.AsNoTracking()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.WorkOrderId == woId && c.QcKind == kind, ct);
        if (check is null)
        {
            // P10.7e-3 FIX (L23) — resolve profile via Q4 3-level chain BEFORE
            // materialising. Without this the snapshot is "{}" and the dashboard
            // renders 0/0 items.
            var resolvedSnapshot = await ResolveProfileSnapshotAsync(productId, kind, ct);
            try
            {
                _db.WoQcChecks.Add(new WoQcCheck
                {
                    WorkOrderId = woId,
                    QcKind = kind,
                    ProfileSnapshotJson = resolvedSnapshot,
                    Judgment = WoQcJudgment.Pending,
                });
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Race lost — another caller inserted first.
                if (_db is DbContext dbCtx) dbCtx.ChangeTracker.Clear();
            }
            check = await _db.WoQcChecks.AsNoTracking()
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.WorkOrderId == woId && c.QcKind == kind, ct);
        }
        else if (string.IsNullOrWhiteSpace(check.ProfileSnapshotJson) || check.ProfileSnapshotJson == "{}")
        {
            // P10.7e-3 FIX — heal pre-fix rows materialised with empty snapshot.
            // Frozen at THIS read (not retroactive to later profile edits).
            var resolvedSnapshot = await ResolveProfileSnapshotAsync(productId, kind, ct);
            if (resolvedSnapshot != "{}" && resolvedSnapshot != check.ProfileSnapshotJson)
            {
                var tracked = await _db.WoQcChecks.FirstOrDefaultAsync(c => c.Id == check.Id, ct);
                if (tracked is not null)
                {
                    tracked.ProfileSnapshotJson = resolvedSnapshot;
                    try { await _db.SaveChangesAsync(ct); }
                    catch (DbUpdateException) { /* race; next reader will heal */ }
                }
                check = await _db.WoQcChecks.AsNoTracking()
                    .Include(c => c.Items)
                    .FirstOrDefaultAsync(c => c.WorkOrderId == woId && c.QcKind == kind, ct);
            }
        }
        return check;
    }

    /// <summary>Q4 3-level resolution — mirror
    /// <c>WoQcMutationControllerBase.ResolveProfileSnapshotAsync</c> (đường
    /// mutation giữ bản của nó). Logic thật ở <c>QcProfileResolver</c> (single
    /// source); đây chỉ là lớp đọc override JSON qua EF.</summary>
    private async Task<string> ResolveProfileSnapshotAsync(long productId, string kind, CancellationToken ct)
    {
        string? overrideJson = null;
        if (productId > 0)
        {
            overrideJson = await _db.Products.AsNoTracking()
                .Where(p => p.Id == productId)
                .Select(p => p.QcProfileOverride)
                .FirstOrDefaultAsync(ct);
        }
        return QcProfileResolver.ResolveSnapshot(overrideJson, kind);
    }
}
