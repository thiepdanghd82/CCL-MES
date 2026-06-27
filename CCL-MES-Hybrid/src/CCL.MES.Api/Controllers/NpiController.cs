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
    private readonly NpiImportService _import;
    public NpiController(NpiService svc, NpiImportService import)
    {
        _svc = svc;
        _import = import;
    }

    // P10.5 follow-up — CSV import for the three big grids (SpecHub
    // "Import…" parity). Write surface → tighten to editor roles even
    // though the read grids are open to QC too.
    [HttpPost("{kind}/import")]
    [Authorize(Roles = "Admin,Supervisor,Engineer")]
    [RequestSizeLimit(256L * 1024 * 1024)]
    public async Task<IActionResult> Import(string kind, IFormFile? file, CancellationToken ct)
    {
        var k = (kind ?? "").ToLowerInvariant();
        if (k is not ("structures" or "routings" or "rawmaterials"))
            return NotFound();
        if (file is null || file.Length == 0)
            return UnprocessableEntity(new { code = "import.no_file", error = "No CSV file was uploaded." });

        await using var stream = file.OpenReadStream();
        var actor = User.Identity?.Name;
        var result = await _import.ImportAsync(k, stream, actor, ct);
        return Ok(result);
    }

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
