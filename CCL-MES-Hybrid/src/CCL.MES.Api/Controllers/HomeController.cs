using CCL.MES.Application;
using CCL.MES.Domain;
using CCL.MES.Shared;
using CCL.MES.Shared.Home;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.10 — Home dashboard aggregate. Read-only; any authenticated user.
/// Live-recomputes the 4 KPI counts on each GET (mirrors the SpecHub
/// Home tiles). No mutation surface, no If-Match / Idempotency-Key.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/home")]
public sealed class HomeController : ControllerBase
{
    private readonly IMesDbContext _db;

    public HomeController(IMesDbContext db) => _db = db;

    [HttpGet("summary")]
    public async Task<ActionResult<HomeSummaryDto>> Summary(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;

        var specs = _db.ProductRevisions.AsNoTracking().Where(x => !x.IsTrashed);
        var specsTotal = await specs.CountAsync(ct);
        var pending = await specs
            .Where(x => x.Status == ProductRevisionStatus.InReview).CountAsync(ct);
        var drafts = await specs
            .Where(x => x.Status == ProductRevisionStatus.Draft).CountAsync(ct);

        var todayActivity = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.UpdatedAt.HasValue && w.UpdatedAt.Value.Date == today)
            .CountAsync(ct);

        return Ok(new HomeSummaryDto
        {
            SpecsTotal = specsTotal,
            PendingApprovals = pending,
            Drafts = drafts,
            TodayActivity = todayActivity,
        });
    }
}
