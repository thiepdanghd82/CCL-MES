using CCL.MES.Application;
using CCL.MES.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SpecsController : ControllerBase
{
    private readonly SpecService _svc;
    public SpecsController(SpecService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _svc.GetAllAsync());

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSpecRequest r, [FromQuery] string? user)
        => Ok(await _svc.CreateAsync(r, user));

    [HttpPost("versions/{versionId:long}/approve")]
    public async Task<IActionResult> Approve(long versionId, [FromQuery] string? user)
    {
        var v = await _svc.ApproveAsync(versionId, user);
        return v is null ? NotFound() : Ok(v);
    }
}
