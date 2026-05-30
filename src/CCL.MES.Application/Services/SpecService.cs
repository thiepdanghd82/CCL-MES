using CCL.MES.Domain;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

public class SpecService
{
    private readonly IMesDbContext _db;
    public SpecService(IMesDbContext db) => _db = db;

    public Task<List<Spec>> GetAllAsync() =>
        _db.Specs
            .Include(s => s.Versions)
            .ThenInclude(v => v.Parameters)
            .OrderByDescending(s => s.Id)
            .ToListAsync();

    public async Task<SpecVersion> CreateAsync(CreateSpecRequest r)
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
        return v;
    }
}
