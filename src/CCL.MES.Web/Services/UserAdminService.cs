using CCL.MES.Application;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 6 Bước 2B — admin read-only list of users for the Account
/// Control tab. Mutation methods (create / disable / role-change / reset
/// password for another user) are deliberately deferred to Bước 4 so they
/// land alongside the RBAC role whitelist + recover-admin script. This
/// keeps Bước 2B scope-light: the page surfaces the existing roster + a
/// search box, nothing that mutates state.
/// </summary>
public class UserAdminService
{
    private readonly IMesDbContext _db;
    public UserAdminService(IMesDbContext db) => _db = db;

    public Task<PagedResult<User>> ListAsync(string? search, int page, int pageSize)
    {
        var q = _db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(u => EF.Functions.Like(u.Username, $"%{s}%")
                          || (u.DisplayName != null && EF.Functions.Like(u.DisplayName, $"%{s}%"))
                          || EF.Functions.Like(u.Role, $"%{s}%"));
        }
        return PagingHelper.PageAsync(q.OrderBy(u => u.Username), page, pageSize);
    }
}
