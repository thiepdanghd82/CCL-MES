using CCL.MES.Application.Services;
using CCL.MES.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// Spec (= ProductRevision) read surface. <c>NpiSpecRead</c> policy gates
/// the entire controller — same role membership the legacy Blazor pages
/// enforce (Admin / Supervisor / Engineer). Mutation endpoints (Create,
/// Approve, Copy, Edit, Revise, Trash, Restore) deliberately omitted from
/// P10.1 — MAUI is read-only on Spec data in P10.2 per Q4 (master data
/// stays online-only and uses its existing legacy UI for now).
/// </summary>
[ApiController]
[Authorize(Policy = "NpiSpecRead")]
[Route(ApiVersion.Prefix + "/specs")]
public sealed class SpecsController : ControllerBase
{
    private readonly SpecService _svc;
    public SpecsController(SpecService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] SpecListView view = SpecListView.Active)
    {
        var result = await _svc.SpecsAsync(search, page, pageSize, view);
        return Ok(result);
    }

    [HttpGet("{revisionId:long}")]
    public async Task<IActionResult> Detail(long revisionId)
    {
        var detail = await _svc.SpecDetailAsync(revisionId);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("products")]
    public async Task<IActionResult> Products() =>
        Ok(await _svc.ProductsForDropdownAsync());
}
