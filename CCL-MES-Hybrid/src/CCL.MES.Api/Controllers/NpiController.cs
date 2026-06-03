using CCL.MES.Application.Services;
using CCL.MES.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// NPI master-data read surface — Work Centers / Raw Materials / Routings /
/// Structures. <c>NpiRead</c> policy mirrors the legacy page-level gate
/// (Admin / Supervisor / Engineer / QC). All four endpoints are
/// server-side paginated since the underlying tables can grow into the
/// thousands.
/// </summary>
[ApiController]
[Authorize(Policy = "NpiRead")]
[Route(ApiVersion.Prefix + "/npi")]
public sealed class NpiController : ControllerBase
{
    private readonly NpiService _svc;
    public NpiController(NpiService svc) => _svc = svc;

    [HttpGet("workcenters")]
    public async Task<IActionResult> WorkCenters(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _svc.WorkCentersAsync(search, page, pageSize));

    [HttpGet("workcenters/{id:long}")]
    public async Task<IActionResult> WorkCenterDetail(long id)
    {
        var dto = await _svc.WorkCenterDetailAsync(id);
        return dto is null ? NotFound() : Ok(dto);
    }

    [HttpGet("rawmaterials")]
    public async Task<IActionResult> RawMaterials(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _svc.RawMaterialsAsync(search, page, pageSize));

    [HttpGet("routings")]
    public async Task<IActionResult> Routings(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _svc.RoutingAsync(search, page, pageSize));

    [HttpGet("structures")]
    public async Task<IActionResult> Structures(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _svc.StructuresAsync(search, page, pageSize));
}
