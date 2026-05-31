using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

public class QcService
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;
    public QcService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Phase 6 Bước 3 — paginated list for the IPQC + OQC grid UIs. Type
    /// filter is required so a page bound to one inspection type cannot
    /// accidentally display rows from another. Includes the linked WO +
    /// Product + Details so the row can show ctx without N+1 queries.
    /// </summary>
    public Task<PagedResult<QcInspection>> ListAsync(QcType type, string? search, int page, int pageSize)
    {
        var q = _db.QcInspections
            .AsNoTracking()
            .Include(i => i.WorkOrder)
            .ThenInclude(w => w!.Product)
            .Include(i => i.Details)
            .Where(i => i.Type == type);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(i =>
                (i.InspectorId != null && EF.Functions.Like(i.InspectorId, $"%{s}%"))
                || (i.WorkOrder != null && EF.Functions.Like(i.WorkOrder.WoNo, $"%{s}%"))
                || (i.ApprovedBy != null && EF.Functions.Like(i.ApprovedBy, $"%{s}%")));
        }
        return PagingHelper.PageAsync(q.OrderByDescending(i => i.Id), page, pageSize);
    }

    public async Task<QcInspection> CreateAsync(CreateQcRequest r)
    {
        var insp = new QcInspection
        {
            WorkOrderId = r.WorkOrderId,
            Type = r.Type,
            InspectorId = r.InspectorId,
            SampleSize = r.SampleSize,
            Result = QcResult.Pending
        };
        foreach (var d in r.Details)
        {
            insp.Details.Add(new QcResultDetail
            {
                ItemName = d.ItemName,
                MeasuredValue = d.MeasuredValue,
                Pass = d.Pass,
                DefectCode = d.DefectCode,
                Qty = d.Qty
            });
        }
        _db.QcInspections.Add(insp);
        await _db.SaveChangesAsync();
        await _audit.EmitAsync(
            AuditAction.QcCreate, r.InspectorId ?? "anonymous", actorRole: "",
            targetType: "QcInspection", targetId: insp.Id.ToString(),
            detail: JsonSerializer.Serialize(new {
                wo_id = r.WorkOrderId,
                type = r.Type.ToString(),
                sample_size = r.SampleSize,
                detail_count = r.Details.Count,
            }));
        return insp;
    }

    /// <summary>Phê duyệt phiếu QC. Nếu Fail -> WO chuyển On-Hold.</summary>
    public async Task<QcInspection?> ApproveAsync(long inspectionId, bool pass, string? user)
    {
        var insp = await _db.QcInspections
            .Include(i => i.Details)
            .FirstOrDefaultAsync(i => i.Id == inspectionId);
        if (insp is null) return null;

        insp.Result = pass ? QcResult.Pass : QcResult.Fail;
        insp.ApprovedBy = user;
        insp.ApprovedAt = DateTime.UtcNow;

        if (!pass)
        {
            var wo = await _db.WorkOrders.FirstOrDefaultAsync(w => w.Id == insp.WorkOrderId);
            if (wo is not null) wo.Status = WoStatus.OnHold;
        }

        await _db.SaveChangesAsync();
        await _audit.EmitAsync(
            AuditAction.QcApprove, user ?? "anonymous", actorRole: "",
            targetType: "QcInspection", targetId: insp.Id.ToString(),
            detail: JsonSerializer.Serialize(new {
                wo_id = insp.WorkOrderId,
                type = insp.Type.ToString(),
                result = insp.Result.ToString(),
            }));
        return insp;
    }
}
