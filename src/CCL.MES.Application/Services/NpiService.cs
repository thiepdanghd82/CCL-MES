using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>Kết quả truy vấn có phân trang.</summary>
public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize)
{
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 0;
}

/// <summary>
/// Truy vấn dữ liệu NPI (WorkCenter / RawMaterial / Routing / Structure)
/// với tìm kiếm toàn cục + phân trang phía server (chịu được hàng chục nghìn dòng).
/// </summary>
public class NpiService
{
    private readonly IMesDbContext _db;
    public NpiService(IMesDbContext db) => _db = db;

    // Phase 6 Bước 2B — local PageAsync removed; consolidated into
    // PagingHelper.PageAsync. Behavior identical; helper is public static.

    public Task<PagedResult<WorkCenter>> WorkCentersAsync(string? search, int page, int pageSize)
    {
        var q = _db.WorkCenters.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.Code.Contains(s) || x.Description.Contains(s) || (x.Area != null && x.Area.Contains(s)));
        }
        return PagingHelper.PageAsync(q.OrderBy(x => x.Code), page, pageSize);
    }

    public Task<PagedResult<RawMaterial>> RawMaterialsAsync(string? search, int page, int pageSize)
    {
        var q = _db.RawMaterials.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.PartNo.Contains(s)
                || (x.PartDescription != null && x.PartDescription.Contains(s))
                || (x.SupplierName != null && x.SupplierName.Contains(s))
                || (x.SupplierId != null && x.SupplierId.Contains(s)));
        }
        return PagingHelper.PageAsync(q.OrderBy(x => x.PartNo), page, pageSize);
    }

    public Task<PagedResult<RoutingOperation>> RoutingAsync(string? search, int page, int pageSize)
    {
        var q = _db.RoutingOperations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.PartNo.Contains(s)
                || (x.PartDescription != null && x.PartDescription.Contains(s))
                || (x.Operation != null && x.Operation.Contains(s))
                || (x.WorkCenterNo != null && x.WorkCenterNo.Contains(s)));
        }
        return PagingHelper.PageAsync(q.OrderBy(x => x.PartNo).ThenBy(x => x.OpNo), page, pageSize);
    }

    public Task<PagedResult<ManufacturingStructure>> StructuresAsync(string? search, int page, int pageSize)
    {
        var q = _db.ManufacturingStructures.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.ParentPart.Contains(s)
                || (x.ParentDescription != null && x.ParentDescription.Contains(s))
                || x.ComponentPart.Contains(s)
                || (x.ComponentDescription != null && x.ComponentDescription.Contains(s)));
        }
        return PagingHelper.PageAsync(q.OrderBy(x => x.ParentPart), page, pageSize);
    }
}
