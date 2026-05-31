using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// Phase 6 Bước 7 — IQC (Incoming Quality Check) service.
///
/// Khác QcService:
///   - Không nhận <c>WorkOrderId</c> (IQC chạy pre-WO trên raw mat batch).
///   - <c>ApproveAsync</c> KHÔNG cascade <c>WO.Status=OnHold</c> khi Fail
///     vì chưa có WO. Audit row vẫn ghi, operator quyết action quarantine
///     ngoài app (Q4 — defer auto-quarantine sang Phase 7).
///   - <c>CreateAsync</c> resolve <c>PartNo → RawMaterialId</c> nếu catalog
///     có match; nếu không, để FK null + giữ PartNo text (hybrid Q1).
/// </summary>
public class IqcService
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    public IqcService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<IqcInspection> CreateAsync(CreateIqcRequest r, string actor, string actorRole)
    {
        // Hybrid FK: tra catalog theo PartNo, nếu match thì set hard FK.
        // Snapshot SupplierName: nếu request không nêu, lấy từ RawMaterial.
        long? rawMaterialId = null;
        string? supplierSnapshot = r.SupplierName;
        if (!string.IsNullOrWhiteSpace(r.PartNo))
        {
            var rm = await _db.RawMaterials.FirstOrDefaultAsync(x => x.PartNo == r.PartNo);
            if (rm is not null)
            {
                rawMaterialId = rm.Id;
                if (string.IsNullOrWhiteSpace(supplierSnapshot))
                    supplierSnapshot = rm.SupplierName;
            }
        }

        var insp = new IqcInspection
        {
            RawMaterialId = rawMaterialId,
            PartNo = r.PartNo,
            BatchNumber = r.BatchNumber,
            LotNumber = r.LotNumber,
            ReceivedDate = r.ReceivedDate,
            SupplierName = supplierSnapshot,
            Quantity = r.Quantity,
            UomQty = r.UomQty,
            InspectorId = r.InspectorId,
            SampleSize = r.SampleSize,
            Result = QcResult.Pending,
        };
        foreach (var d in r.Details)
        {
            insp.Details.Add(new IqcResultDetail
            {
                ItemName = d.ItemName,
                MeasuredValue = d.MeasuredValue,
                Pass = d.Pass,
                DefectCode = d.DefectCode,
                Qty = d.Qty,
            });
        }
        _db.IqcInspections.Add(insp);
        await _db.SaveChangesAsync();

        // Detail JSON KHÔNG carry PII. PartNo / batch / qty là operational
        // metadata; InspectorId là username chứ không phải PII.
        await _audit.EmitAsync(
            AuditAction.IqcCreate, actor, actorRole,
            targetType: "IqcInspection", targetId: insp.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                part_no = r.PartNo,
                batch = r.BatchNumber,
                qty = r.Quantity,
                sample_size = r.SampleSize,
                detail_count = r.Details.Count,
                raw_material_id = rawMaterialId,
            }));
        return insp;
    }

    /// <summary>
    /// Phê duyệt IQC. KHÔNG cascade WO (IQC là pre-WO; raw mat fail thì
    /// quarantine ngoài app theo Q4).
    /// </summary>
    public async Task<IqcInspection?> ApproveAsync(long inspectionId, bool pass, string actor, string actorRole)
    {
        var insp = await _db.IqcInspections
            .Include(i => i.Details)
            .FirstOrDefaultAsync(i => i.Id == inspectionId);
        if (insp is null) return null;
        if (insp.Result != QcResult.Pending) return insp;  // idempotent — đã approved

        insp.Result = pass ? QcResult.Pass : QcResult.Fail;
        insp.ApprovedBy = actor;
        insp.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        await _audit.EmitAsync(
            AuditAction.IqcApprove, actor, actorRole,
            targetType: "IqcInspection", targetId: insp.Id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                part_no = insp.PartNo,
                batch = insp.BatchNumber,
                result = insp.Result.ToString(),
            }));
        return insp;
    }

    /// <summary>
    /// List paginated theo (search, status, date range). Search match
    /// PartNo / BatchNumber / SupplierName qua <c>EF.Functions.Like</c>
    /// (provider-agnostic — Bước 6.5 đã verify hoạt động đúng cả SQLite +
    /// SQL Server).
    /// </summary>
    public async Task<PagedResult<IqcInspection>> ListAsync(
        string? search, QcResult? status, DateTime? from, DateTime? to,
        int page, int pageSize)
    {
        var q = _db.IqcInspections.AsNoTracking()
            .OrderByDescending(x => x.ReceivedDate)
            .ThenByDescending(x => x.Id)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                EF.Functions.Like(x.PartNo, $"%{s}%")
                || EF.Functions.Like(x.BatchNumber, $"%{s}%")
                || (x.SupplierName != null && EF.Functions.Like(x.SupplierName, $"%{s}%")));
        }
        if (status.HasValue)
            q = q.Where(x => x.Result == status.Value);
        if (from.HasValue)
            q = q.Where(x => x.ReceivedDate >= from.Value);
        if (to.HasValue)
            q = q.Where(x => x.ReceivedDate <= to.Value);

        return await PagingHelper.PageAsync(q, page, pageSize);
    }

    public async Task<IqcInspection?> GetWithDetailsAsync(long id)
    {
        return await _db.IqcInspections
            .AsNoTracking()
            .Include(i => i.Details)
            .FirstOrDefaultAsync(i => i.Id == id);
    }
}

public record CreateIqcRequest(
    string PartNo,
    string BatchNumber,
    string? LotNumber,
    DateTime ReceivedDate,
    string? SupplierName,
    double Quantity,
    string? UomQty,
    string? InspectorId,
    int SampleSize,
    List<CreateIqcDetail> Details);

public record CreateIqcDetail(
    string ItemName,
    string? MeasuredValue,
    bool Pass,
    string? DefectCode,
    int Qty);
