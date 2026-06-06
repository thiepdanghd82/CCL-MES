using CCL.MES.Application;
using CCL.MES.Domain;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.ReasonCodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.7b-3 — operator-facing reason-code list for PREPRESS dashboard
/// pickers (Material / Plate / Cutter NG dropdowns). Returns the
/// active <see cref="ReasonCode"/> rows filtered by <c>kind</c>.
/// Authorization: any authenticated user (operator role).
///
/// Companion endpoint <c>QcSpecController.ReasonCodes</c> is gated to
/// the NpiSpecRead policy (Admin/Supervisor/Engineer) and returns a
/// shape coupled to QC capture; that's intentionally separate from
/// the operator-facing picker which any role on the shop floor must
/// be able to load.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/reason-codes")]
public sealed class ReasonCodesController : ControllerBase
{
    private readonly IMesDbContext _db;

    public ReasonCodesController(IMesDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// List active reason codes. Filter by <paramref name="kind"/>
    /// (Pause / Scrap / Recovery — case-insensitive). Omit to return
    /// every active code. 422 invalid_kind on unknown filter so the
    /// client sees a typed-mistake fast instead of silently empty.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ReasonCodeOption>>> List(
        [FromQuery] string? kind)
    {
        ReasonCodeKind? filter = null;
        if (!string.IsNullOrWhiteSpace(kind))
        {
            if (!Enum.TryParse<ReasonCodeKind>(kind, ignoreCase: true, out var parsed))
            {
                return UnprocessableEntity(ApiError.Of(
                    "reason_codes.invalid_kind",
                    $"Kind '{kind}' is not one of Pause / Scrap / Recovery."));
            }
            filter = parsed;
        }

        var query = _db.ReasonCodes.AsNoTracking().Where(r => r.Active);
        if (filter is not null)
            query = query.Where(r => r.Kind == filter.Value);

        var rows = await query
            .OrderBy(r => r.Kind)
            .ThenBy(r => r.Sort)
            .ThenBy(r => r.Code)
            .Select(r => new ReasonCodeOption
            {
                Code = r.Code,
                LabelEn = r.LabelEn,
                LabelVi = r.LabelVi,
                Kind = r.Kind.ToString(),
                Sort = r.Sort,
            })
            .ToListAsync();

        return Ok(rows);
    }
}
