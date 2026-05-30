using CCL.MES.Application;
using CCL.MES.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

[ApiController]
[Route("api/qc")]
public class QcController : ControllerBase
{
    private readonly QcService _svc;
    public QcController(QcService svc) => _svc = svc;

    [HttpPost("inspections")]
    public async Task<IActionResult> Create([FromBody] CreateQcRequest r)
        => Ok(await _svc.CreateAsync(r));

    [HttpPost("inspections/{id:long}/approve")]
    public async Task<IActionResult> Approve(long id, [FromQuery] bool pass = true, [FromQuery] string? user = null)
    {
        var insp = await _svc.ApproveAsync(id, pass, user);
        return insp is null ? NotFound() : Ok(insp);
    }
}
