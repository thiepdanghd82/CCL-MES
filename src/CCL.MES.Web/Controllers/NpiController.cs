using CCL.MES.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

[ApiController]
[Route("api/npi")]
public class NpiController : ControllerBase
{
    private readonly NpiService _svc;
    public NpiController(NpiService svc) => _svc = svc;

    [HttpGet("workcenters")]
    public async Task<IActionResult> WorkCenters([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _svc.WorkCentersAsync(search, page, pageSize));

    [HttpGet("raw-materials")]
    public async Task<IActionResult> RawMaterials([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _svc.RawMaterialsAsync(search, page, pageSize));

    [HttpGet("routing")]
    public async Task<IActionResult> Routing([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _svc.RoutingAsync(search, page, pageSize));

    [HttpGet("structures")]
    public async Task<IActionResult> Structures([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _svc.StructuresAsync(search, page, pageSize));
}
