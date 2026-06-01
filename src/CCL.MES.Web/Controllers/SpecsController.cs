using CCL.MES.Application;
using CCL.MES.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

/// <summary>
/// Phase 8 PR #28 — REWORKED sau Spec → ProductRevision clean rewrite.
///   - GET /api/specs               → SpecsAsync paginated DTO
///   - POST /api/specs              → CreateAsync trả ProductRevision
///   - POST /api/specs/revisions/{revisionId}/approve → ApproveAsync trả ProductRevision
///
/// Compatibility note: cũ route `/api/specs/versions/{versionId}/approve` đổi
/// thành `revisions/{revisionId}`; KHÔNG có external API consumer hiện tại
/// (chỉ internal Blazor circuit). Nếu tương lai cần stable contract, alias
/// đường cũ thêm dòng `[HttpPost("versions/{versionId:long}/approve")]`.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SpecsController : ControllerBase
{
    private readonly SpecService _svc;
    public SpecsController(SpecService svc) => _svc = svc;

    /// <summary>
    /// GET grid paginated. Optional `?search=&page=&pageSize=`. KHÔNG có
    /// auth middleware ở đây — Web SPA dùng SpecService trực tiếp qua DI.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _svc.SpecsAsync(search, page, pageSize));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSpecRequest r, [FromQuery] string? user)
        => Ok(await _svc.CreateAsync(r, user));

    [HttpPost("revisions/{revisionId:long}/approve")]
    public async Task<IActionResult> Approve(long revisionId, [FromQuery] string? user)
    {
        var rev = await _svc.ApproveAsync(revisionId, user);
        return rev is null ? NotFound() : Ok(rev);
    }
}
