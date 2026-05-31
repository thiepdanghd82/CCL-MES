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

    /// <summary>
    /// Phase 6 Bước 1 — paginated list for the Engineer Spec grid UI.
    /// Mirrors the NpiService pattern (search + page + AsNoTracking) so the
    /// Razor page reads identical to the 4 existing NPI grids.
    /// </summary>
    public Task<PagedResult<Spec>> SpecsAsync(string? search, int page, int pageSize)
    {
        var q = _db.Specs
            .AsNoTracking()
            .Include(s => s.Versions)
            .Include(s => s.Product)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => EF.Functions.Like(x.SpecCode, $"%{s}%")
                || EF.Functions.Like(x.Title, $"%{s}%")
                || (x.Product != null && EF.Functions.Like(x.Product.Name, $"%{s}%")));
        }
        return PagingHelper.PageAsync(q.OrderByDescending(x => x.Id), page, pageSize);
    }

    // Phase 6 Bước 5 — actor param added (was a gap noted in PHASE6-STEP5-PLAN.md §1.1).
    // Phase 6 close-out — local PageAsync removed (consolidated into shared
    // PagingHelper.PageAsync in Bước 2B; carry-over note in Bước 1 closed).
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
