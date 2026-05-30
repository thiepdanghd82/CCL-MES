using CCL.MES.Application;
using CCL.MES.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WorkOrdersController : ControllerBase
{
    private readonly WorkOrderService _svc;
    public WorkOrdersController(WorkOrderService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _svc.GetAllAsync());

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id)
    {
        var wo = await _svc.GetAsync(id);
        return wo is null ? NotFound() : Ok(wo);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWoRequest r)
    {
        var wo = await _svc.CreateAsync(r);
        return CreatedAtAction(nameof(Get), new { id = wo.Id }, wo);
    }

    [HttpPost("{id:long}/advance")]
    public async Task<IActionResult> Advance(long id, [FromQuery] string? user)
    {
        var res = await _svc.AdvanceAsync(id, user);
        return res.Ok ? Ok(res) : BadRequest(res);
    }

    [HttpPost("{id:long}/flags")]
    public async Task<IActionResult> Flags(long id, [FromBody] UpdateFlagsRequest r)
    {
        var wo = await _svc.UpdateFlagsAsync(id, r);
        return wo is null ? NotFound() : Ok(wo);
    }
}
