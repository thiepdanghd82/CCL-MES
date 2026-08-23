using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>
/// A2 — khối COMMIT của bề mặt Semi-Stock (P11.5-2) rút khỏi
/// <c>SemiStockController</c> để SaveChanges rời <c>Controllers/</c> (gate-thin
/// đi xuống, L40). Controller vẫn giữ VALIDATION + chuẩn bị entity (FEFO plan,
/// mutate lot, add allocation); service chỉ nắm <c>SaveChanges + (catch) + audit</c>.
///
/// <para><b>Domain SemiLot (KHÁC WO).</b> Concurrency ở đây trên
/// <c>SemiLot.RowVersion</c> (L38), không phải WO — nên KHÔNG đi qua
/// <see cref="WoMutationExecutor"/> (vốn touch WO + emit WO_STATE_CONFLICT).
/// Đua thua trả <c>semi.lot_conflict</c>. Giữ NGUYÊN hành vi cũ: nhánh conflict
/// clear tracker + trả về (controller dựng 409) — audit thành công
/// (SemiLotReserve/Consume) đứng ngay sau, byte-identical với bản inline.</para>
/// </summary>
public sealed class SemiLotMutationService
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    public SemiLotMutationService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>Post 1 lô vào kho (insert thuần — không có nhánh concurrency).
    /// Trả Id lô đã tạo.</summary>
    public async Task<long> PostAsync(SemiLot lot, string actor, string role, CancellationToken ct)
    {
        _db.SemiLots.Add(lot);
        await _db.SaveChangesAsync(ct);
        await _audit.EmitAsync(AuditAction.SemiLotPost, actor, role, "SemiLot", lot.Id.ToString(),
            JsonSerializer.Serialize(new { lot_no = lot.LotNo, semi_kind = lot.SemiKind, source_wo_id = lot.SourceWorkOrderId, qty = lot.QtyProduced }));
        return lot.Id;
    }

    /// <summary>Commit reserve: caller đã mutate lot + add allocation (tracked).
    /// Đua thua <see cref="DbUpdateConcurrencyException"/> → clear tracker + trả
    /// <c>false</c> (controller dựng 409 semi.lot_conflict). Thành công → audit
    /// SemiLotReserve + trả <c>true</c>.</summary>
    public async Task<bool> CommitReserveAsync(
        long woId, long legId, int allocated, IReadOnlyList<long> lotIds, string actor, string role, CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            if (_db is DbContext ctx) ctx.ChangeTracker.Clear();
            return false;
        }
        await _audit.EmitAsync(AuditAction.SemiLotReserve, actor, role, "WoLeg", legId.ToString(),
            JsonSerializer.Serialize(new { wo_id = woId, assembly_leg_id = legId, qty = allocated, lot_ids = lotIds }));
        return true;
    }

    /// <summary>Commit consume: caller đã mutate lot + allocation (tracked). Đua
    /// thua → clear + <c>false</c>. Thành công → audit SemiLotConsume + <c>true</c>.</summary>
    public async Task<bool> CommitConsumeAsync(
        long woId, long legId, int consumed, IReadOnlyList<long> lotIds, string actor, string role, CancellationToken ct)
    {
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            if (_db is DbContext ctx) ctx.ChangeTracker.Clear();
            return false;
        }
        await _audit.EmitAsync(AuditAction.SemiLotConsume, actor, role, "WoLeg", legId.ToString(),
            JsonSerializer.Serialize(new { wo_id = woId, assembly_leg_id = legId, qty = consumed, lot_ids = lotIds }));
        return true;
    }
}
