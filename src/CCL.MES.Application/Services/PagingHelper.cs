using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// Phase 6 Bước 2B — shared paginated-query helper. Extracted from
/// <c>NpiService</c> when the third service (UserAdminService) needed
/// the same logic; consolidates two near-identical local copies (NpiService
/// + the SpecService variant added in Phase 6 Bước 1) into a single
/// public static. Behaviour preserved exactly so existing callers
/// (NpiService.{WorkCenters,RawMaterials,Routing,Structures}Async) keep
/// working unchanged after the swap.
/// </summary>
public static class PagingHelper
{
    public static async Task<PagedResult<T>> PageAsync<T>(IQueryable<T> q, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 500 ? 50 : pageSize;
        var total = await q.CountAsync();
        var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedResult<T>(items, total, page, pageSize);
    }
}
