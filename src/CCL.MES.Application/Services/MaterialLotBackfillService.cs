using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>A1 — con số của một lần chạy backfill.</summary>
public sealed record MaterialLotBackfillReport
{
    /// <summary>Số dòng <c>WoMaterials</c> có LotNo khác rỗng đã xét.</summary>
    public int Candidates { get; init; }
    public int LotsCreated { get; init; }
    public int LotsReused { get; init; }
    public int ConsumptionsCreated { get; init; }

    /// <summary>Đã có dấu <c>backfill-a1</c> từ lần chạy trước ⇒ bỏ qua. Lần
    /// chạy thứ hai phải có <c>Skipped == Candidates</c> và
    /// <c>ConsumptionsCreated == 0</c>.</summary>
    public int Skipped { get; init; }

    /// <summary>Đ6 — lô không khớp IQC nào, tạo ở trạng thái Quarantine.</summary>
    public int Quarantined { get; init; }

    /// <summary>Lô khớp một phiếu IQC ⇒ thừa hưởng kết luận của phiếu đó.</summary>
    public int InheritedFromIqc { get; init; }
}

/// <summary>
/// A1 — dựng mạch lô cho dữ liệu ĐÃ CÓ: mỗi <c>WoMaterials.LotNo</c> khác rỗng
/// sinh ra một <c>MaterialLot</c> + một dòng <c>WoMaterialConsumptions</c>, để
/// mọi WO cũ cũng trả lời được câu "cuộn nào đã vào đơn này".
///
/// <para><b>Idempotent bằng DẤU, không bằng unique index</b> (§2 hệ quả 2).
/// Đ4 đã xoá <c>UNIQUE(WoMaterialId, MaterialLotId)</c>, nên backfill mất chỗ
/// dựa cũ. Thay bằng <c>CreatedBy = "backfill-a1"</c> + điều kiện
/// <c>NOT EXISTS (… WHERE CreatedBy='backfill-a1' AND WoMaterialId=?)</c>.
/// Chạy hai lần phải ra cùng rowcount — có test bắt buộc.</para>
///
/// <para><b>Đảo lại được bằng một câu</b> (Đ6):
/// <c>DELETE FROM WoMaterialConsumptions WHERE CreatedBy='backfill-a1';</c>
/// rồi <c>DELETE FROM MaterialLots WHERE CreatedBy='backfill-a1';</c></para>
/// </summary>
public sealed class MaterialLotBackfillService
{
    /// <summary>Dấu nhận biết dòng do backfill sinh ra. Đổi chuỗi này = mất
    /// tính idempotent của mọi lần chạy trước.</summary>
    public const string Marker = "backfill-a1";

    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    public MaterialLotBackfillService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<MaterialLotBackfillReport> RunAsync(
        string actor = Marker, string role = "Sys", CancellationToken ct = default)
    {
        var candidates = await _db.WoMaterials
            .Where(m => m.LotNo != null && m.LotNo != "")
            .OrderBy(m => m.Id)
            .ToListAsync(ct);

        var alreadyDone = await _db.WoMaterialConsumptions
            .Where(c => c.CreatedBy == Marker)
            .Select(c => c.WoMaterialId)
            .ToListAsync(ct);
        var doneSet = alreadyDone.ToHashSet();

        int created = 0, reused = 0, consumptions = 0, skipped = 0, quarantined = 0, inherited = 0;
        var now = DateTime.UtcNow;

        foreach (var m in candidates)
        {
            if (doneSet.Contains(m.Id)) { skipped++; continue; }

            var lotNo = MaterialLotStatusPolicy.Normalize(m.LotNo);
            if (lotNo.Length == 0) { skipped++; continue; }
            var partNo = MaterialLotStatusPolicy.Normalize(m.MaterialCode);

            // Tìm lô đã có. So sánh `==` thuần — cột mang COLLATE NOCASE nên
            // "LOT-001" tìm ra "lot-001". Đây chính là chỗ lớp 1 làm việc.
            var lot = await _db.MaterialLots
                .FirstOrDefaultAsync(l => l.LotNo == lotNo && l.PartNo == partNo, ct);

            if (lot is null)
            {
                var rawMaterialId = await _db.RawMaterials
                    .Where(r => r.PartNo == partNo).Select(r => (long?)r.Id).FirstOrDefaultAsync(ct);

                // Nối vào phiếu IQC nếu tìm được — join theo LotNumber + PartNo.
                // Đây là lần DUY NHẤT chuỗi lô được dùng để đối chiếu: chính là
                // lúc dựng khoá số. Sau backfill mọi truy vấn đi bằng FK.
                var iqc = await _db.IqcInspections.AsNoTracking()
                    .Where(i => i.LotNumber == lotNo && i.PartNo == partNo)
                    .OrderByDescending(i => i.Id)
                    .FirstOrDefaultAsync(ct);

                string status;
                if (iqc is null)
                {
                    // Đ6 — không khớp ⇒ Quarantine, không đoán bừa là đạt.
                    status = nameof(MaterialLotStatus.Quarantine);
                    quarantined++;
                }
                else
                {
                    status = iqc.Result switch
                    {
                        QcResult.Pass => nameof(MaterialLotStatus.Released),
                        QcResult.Fail => nameof(MaterialLotStatus.Rejected),
                        _             => nameof(MaterialLotStatus.Quarantine),
                    };
                    inherited++;
                }

                var qty = m.QtyLoaded ?? m.QtyRequired;
                if (double.IsNaN(qty) || double.IsInfinity(qty) || qty <= 0) qty = 1;

                lot = new MaterialLot
                {
                    LotNo = lotNo,
                    PartNo = partNo,
                    RawMaterialId = rawMaterialId,
                    IqcInspectionId = iqc?.Id,
                    SupplierName = iqc?.SupplierName,
                    ReceivedAt = iqc?.ReceivedDate ?? m.CreatedAt,
                    QtyReceived = qty,
                    // Lô lịch sử: hàng đã dùng rồi, không còn tồn để quét tiếp.
                    QtyAvailable = 0,
                    Uom = m.Uom,
                    Status = status,
                    StatusReason = "A1 backfill — dựng mạch lô cho dữ liệu đã có.",
                    StatusChangedBy = Marker,
                    StatusChangedAt = now,
                    CreatedAt = now,
                    CreatedBy = Marker,
                };
                _db.MaterialLots.Add(lot);
                await _db.SaveChangesAsync(ct);   // cần Id cho FK bên dưới
                created++;
            }
            else
            {
                reused++;
            }

            var qtyUsed = m.QtyLoaded ?? m.QtyRequired;
            if (double.IsNaN(qtyUsed) || double.IsInfinity(qtyUsed) || qtyUsed <= 0) qtyUsed = 1;

            _db.WoMaterialConsumptions.Add(new WoMaterialConsumption
            {
                WoId = m.WorkOrderId,
                LegId = _db is DbContext c ? c.Entry(m).Property<long?>("WoLegId").CurrentValue : null,
                WoMaterialId = m.Id,
                MaterialLotId = lot.Id,
                QtyUsed = qtyUsed,
                Uom = m.Uom,
                ScannedBy = m.CheckedBy ?? Marker,
                ScannedAt = m.CheckedAt ?? m.CreatedAt,
                CreatedAt = now,
                CreatedBy = Marker,      // ← dấu idempotent, xem chú thích class
            });
            consumptions++;

            MaterialLotScanService.SetLotFk(_db, m, lot.Id);
            m.LotNo = lot.LotNo;     // mirror canonical (kiểu chữ theo lô)
            doneSet.Add(m.Id);
        }

        await _db.SaveChangesAsync(ct);

        var report = new MaterialLotBackfillReport
        {
            Candidates = candidates.Count,
            LotsCreated = created,
            LotsReused = reused,
            ConsumptionsCreated = consumptions,
            Skipped = skipped,
            Quarantined = quarantined,
            InheritedFromIqc = inherited,
        };

        // MỘT dòng audit cho cả lần chạy (không phải mỗi lô một dòng) — cùng
        // granularity với NPI_IMPORT / SPEC_BACKFILL_DETAIL.
        await _audit.EmitAsync(
            MaterialLotAuditAction.MaterialLotStatusSet, actor, role,
            targetType: "MaterialLot", targetId: Marker,
            detail: JsonSerializer.Serialize(new
            {
                backfill = Marker,
                candidates = report.Candidates,
                lots_created = report.LotsCreated,
                lots_reused = report.LotsReused,
                consumptions_created = report.ConsumptionsCreated,
                skipped = report.Skipped,
                quarantined = report.Quarantined,
                inherited_from_iqc = report.InheritedFromIqc,
            }),
            source: "Console");

        return report;
    }
}
