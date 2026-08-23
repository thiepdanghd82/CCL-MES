using CCL.MES.Application;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>
/// A2 thin-controller — lazy-materialise + AUTO-SYNC (Phương án C) + self-heal của
/// WoIpqcCheck rút khỏi <c>IpqcReviewController</c>. SaveChanges của đường GET
/// /ipqc rời <c>Controllers/</c> (gate-thin đi xuống, L40); luật materialise +
/// resolve QcLine + build từ CheckItemLibrary sống ở MỘT service test được, dùng
/// chung cho CẢ đường GET (materialise + save) lẫn đường mutation
/// (<c>GetOrCreateCheckAsync</c> chỉ tạo tracked, không save — commit ở executor).
///
/// <para><b>Khác đường mutation nghiệp vụ.</b> Conflict ở GET là
/// <see cref="DbUpdateException"/> đua UNIQUE index (hai reader đầu cùng insert),
/// KHÔNG phải RowVersion concurrency — kẻ thua clear+refetch, không emit audit.</para>
///
/// <para><b>Byte-identical.</b> Constants + NewPendingCheck + IsPristine +
/// TryAutoSyncAsync + nhánh null/heal/else dời VERBATIM; freeze (đã có
/// snapshot/items thì không hồi tố) giữ nguyên. Test wire /ipqc GET + auto-sync
/// (Plan C) không sửa mà vẫn xanh là bằng chứng.</para>
/// </summary>
public sealed class IpqcCheckMaterializer
{
    // F2 — autoSyncStatus tokens (DERIVE; trùng IpqcView.AutoSyncStatus doc).
    public const string Materialized = "Materialized";
    public const string SkippedUnmapped = "SkippedUnmapped";
    public const string SkippedNoLibrary = "SkippedNoLibrary";
    public const string LegacyManual = "LegacyManual";

    private readonly IMesDbContext _db;

    public IpqcCheckMaterializer(IMesDbContext db)
    {
        _db = db;
    }

    public static WoIpqcCheck NewPendingCheck(long woId) => new()
    {
        WorkOrderId = woId,
        MaterialStatus = IpqcCheckStatus.Pending,
        PrintAStatus = IpqcCheckStatus.Pending,
        PrintBStatus = IpqcCheckStatus.Pending,
        PrintCStatus = IpqcCheckStatus.Pending,
        Judgment = IpqcJudgment.Pending,
        QaOutcome = QaOutcome.Pending,
    };

    /// <summary>Check chưa có bất kỳ dữ liệu operator nào (an toàn để self-heal materialize).</summary>
    private static bool IsPristine(WoIpqcCheck c) =>
        c.Judgment == IpqcJudgment.Pending
        && c.MaterialStatus == IpqcCheckStatus.Pending
        && c.PrintAStatus == IpqcCheckStatus.Pending
        && c.PrintBStatus == IpqcCheckStatus.Pending
        && c.PrintCStatus == IpqcCheckStatus.Pending;

    /// <summary>
    /// Đường GET /ipqc WO-level (1-leg / legacy): tạo mới + auto-sync, hoặc
    /// self-heal (F2) nếu check rỗng-pristine. Trả (check AsNoTracking, autoSync).
    /// Sở hữu 2 SaveChanges (insert / heal) — đua UNIQUE index → clear+refetch.
    /// Byte-identical với block inline cũ trong controller.
    /// </summary>
    public async Task<(WoIpqcCheck? Check, string AutoSync)> EnsureForGetAsync(WorkOrder wo, CancellationToken ct = default)
    {
        var id = wo.Id;

        // WO-level (1-leg / legacy) — filter WoLegId IS NULL so per-leg rows of a
        // forked WO are never picked up here (correctness).
        var check = await _db.WoIpqcChecks.AsNoTracking().Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.WorkOrderId == id && EF.Property<long?>(c, "WoLegId") == null, ct);

        string autoSync;
        if (check is null)
        {
            // Lazy-materialise row + AUTO-SYNC items (Phương án C Bước 4).
            // Concurrent first-readers race on the UNIQUE index — losers refetch.
            var fresh = NewPendingCheck(id);
            autoSync = await TryAutoSyncAsync(wo, fresh, ct);
            try
            {
                _db.WoIpqcChecks.Add(fresh);
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                if (_db is DbContext dbCtx) dbCtx.ChangeTracker.Clear();
            }
            check = await _db.WoIpqcChecks.AsNoTracking().Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.WorkOrderId == id && EF.Property<long?>(c, "WoLegId") == null, ct);
        }
        else if (check.Items.Count == 0 && IsPristine(check))
        {
            // F2 (finding #2) — SELF-HEAL: check tạo trước khi có routing/library
            // (mode 4-slot rỗng) nhưng CHƯA nhập gì → thử materialize lại (an toàn).
            var tracked = await _db.WoIpqcChecks.Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.WorkOrderId == id, ct);
            autoSync = tracked is null ? LegacyManual : await TryAutoSyncAsync(wo, tracked, ct);
            if (tracked is not null && tracked.Items.Count > 0)
            {
                try { await _db.SaveChangesAsync(ct); }
                catch (DbUpdateException)
                {
                    if (_db is DbContext dbCtx) dbCtx.ChangeTracker.Clear();
                }
                check = await _db.WoIpqcChecks.AsNoTracking().Include(c => c.Items)
                    .FirstOrDefaultAsync(c => c.WorkOrderId == id && EF.Property<long?>(c, "WoLegId") == null, ct);
            }
        }
        else
        {
            // Đã materialize (Materialized) hoặc đã nhập tay 4-slot (LegacyManual,
            // KHÔNG tự chuyển — giữ dữ liệu operator, QA quyết).
            autoSync = check.Items.Count > 0 ? Materialized : LegacyManual;
        }

        return (check, autoSync);
    }

    /// <summary>Tạo tracked check cho đường MUTATION nếu chưa có (KHÔNG SaveChanges
    /// — commit ở executor). Auto-sync khi tạo mới (wo != null). Idempotent.</summary>
    public async Task<WoIpqcCheck> GetOrCreateForMutationAsync(long woId, WorkOrder? wo = null)
    {
        var check = await _db.WoIpqcChecks.Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.WorkOrderId == woId);
        if (check is not null) return check;

        check = NewPendingCheck(woId);
        if (wo is not null)
            await TryAutoSyncAsync(wo, check, CancellationToken.None);
        _db.WoIpqcChecks.Add(check);
        return check;
    }

    /// <summary>Auto-sync (Phương án C): resolve QcLine từ routing → build items từ
    /// CheckItemLibrary → gán snapshot + items. FREEZE nếu đã có snapshot/items
    /// (sửa thư viện KHÔNG hồi tố). Trả token autoSyncStatus. Dời verbatim.</summary>
    public async Task<string> TryAutoSyncAsync(WorkOrder wo, WoIpqcCheck check, CancellationToken ct)
    {
        // FREEZE: đã có snapshot/items → đã materialize rồi (sửa thư viện không hồi tố).
        if (!string.IsNullOrWhiteSpace(check.ItemsProfileSnapshotJson) || check.Items.Count > 0)
            return Materialized;

        var productCode = await _db.Products.AsNoTracking()
            .Where(p => p.Id == wo.ProductId)
            .Select(p => p.ProductCode)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(productCode)) return LegacyManual;

        var ops = await _db.RoutingOperations.AsNoTracking()
            .Where(r => r.PartNo == productCode)
            .Select(r => new { r.OpNo, r.Operation, r.WorkCenterNo, r.WorkCenterDescription })
            .ToListAsync(ct);
        if (ops.Count == 0) return LegacyManual;

        var map = await _db.ProcessLineMaps.AsNoTracking()
            .Where(m => m.Active)
            .Select(m => new QcLineResolver.MapEntry(m.MatchType, m.MatchValue, m.QcLine, m.Sort))
            .ToListAsync(ct);
        var resolution = QcLineResolver.Resolve(ops.Select(o =>
            new QcLineResolver.RoutingOp(o.OpNo, o.Operation, o.WorkCenterNo, o.WorkCenterDescription)), map);

        if (resolution.Lines.Count == 0)
            // Có op không map được → cảnh báo Unmapped; ngược lại (toàn NONE/không routing) → legacy.
            return resolution.Unmapped.Count > 0 ? SkippedUnmapped : LegacyManual;

        var lines = resolution.Lines.ToList();
        var lib = await _db.CheckItemLibraries.AsNoTracking()
            .Where(c => c.Active && c.Ipqc && lines.Contains(c.ProcessLine)
                     && (c.ProductCode == null || c.ProductCode == productCode))
            .ToListAsync(ct);
        if (lib.Count == 0) return SkippedNoLibrary;

        var built = IpqcLibraryMaterializer.Build(lib, lines);
        if (built.Items.Count == 0) return SkippedNoLibrary;

        check.ItemsProfileSnapshotJson = built.ProfileSnapshotJson;
        check.ResolvedLines = string.Join(",", lines);
        foreach (var it in built.Items) check.Items.Add(it);
        return Materialized;
    }
}
