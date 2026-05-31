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
        return PageAsync(q.OrderByDescending(x => x.Id), page, pageSize);
    }

    // Phase 6 Bước 1 — local copy of NpiService.PageAsync (7 LOC). Kept
    // local rather than promoting to a shared helper to avoid touching the
    // 4 NpiService callsites; if a 3rd service ever needs paging the
    // natural extraction point arrives then. Behavior must stay identical
    // to NpiService.PageAsync — if one changes, both should.
    private static async Task<PagedResult<T>> PageAsync<T>(IQueryable<T> q, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 500 ? 50 : pageSize;
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<T>(items, total, page, pageSize);
    }

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
