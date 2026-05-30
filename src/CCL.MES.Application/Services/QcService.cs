using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

public class QcService
{
    private readonly IMesDbContext _db;
    public QcService(IMesDbContext db) => _db = db;

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
        return insp;
    }
}
