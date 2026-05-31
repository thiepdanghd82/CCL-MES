using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

public class SpecService
{
    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;
    public SpecService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    public Task<List<Spec>> GetAllAsync() =>
        _db.Specs
            .Include(s => s.Versions)
            .ThenInclude(v => v.Parameters)
            .OrderByDescending(s => s.Id)
            .ToListAsync();

    // Phase 6 Bước 5 — actor param added (was a gap noted in PHASE6-STEP5-PLAN.md §1.1).
    public async Task<SpecVersion> CreateAsync(CreateSpecRequest r, string? user)
    {
        var spec = new Spec
        {
            ProductId = r.ProductId,
            SpecCode = r.SpecCode,
            Title = r.Title
        };
        var ver = new SpecVersion { VersionNo = 1, Status = SpecStatus.Draft };
        foreach (var p in r.Parameters)
        {
            ver.Parameters.Add(new SpecParameter
            {
                ParamName = p.ParamName,
                Nominal = p.Nominal,
                TolMin = p.TolMin,
                TolMax = p.TolMax,
                Uom = p.Uom,
                IsCritical = p.IsCritical
            });
        }
        spec.Versions.Add(ver);
        _db.Specs.Add(spec);
        await _db.SaveChangesAsync();
        await _audit.EmitAsync(
            AuditAction.SpecCreate, user ?? "anonymous", actorRole: "",
            targetType: "Spec", targetId: spec.Id.ToString(),
            detail: JsonSerializer.Serialize(new {
                spec_code = r.SpecCode,
                title = r.Title,
                product_id = r.ProductId,
                version_no = ver.VersionNo,
                param_count = r.Parameters.Count,
            }));
        return ver;
    }

    public async Task<SpecVersion?> ApproveAsync(long versionId, string? user)
    {
        var v = await _db.SpecVersions.FirstOrDefaultAsync(x => x.Id == versionId);
        if (v is null) return null;
        v.Status = SpecStatus.Approved;
        v.ApprovedBy = user;
        v.ApprovedAt = DateTime.UtcNow;
        v.EffectiveDate ??= DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await _audit.EmitAsync(
            AuditAction.SpecApprove, user ?? "anonymous", actorRole: "",
            targetType: "SpecVersion", targetId: v.Id.ToString(),
            detail: JsonSerializer.Serialize(new {
                spec_id = v.SpecId,
                version_no = v.VersionNo,
            }));
        return v;
    }
}
