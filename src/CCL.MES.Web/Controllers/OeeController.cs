using CCL.MES.Application;
using CCL.MES.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

[ApiController]
[Route("api/oee")]
public class OeeController : ControllerBase
{
    private readonly OeeService _svc;
    public OeeController(OeeService svc) => _svc = svc;

    [HttpGet("machines")]
    public async Task<IActionResult> Machines() => Ok(await _svc.GetMachinesAsync());

    [HttpGet("machines/{id:long}")]
    public async Task<IActionResult> Get(long id, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var r = await _svc.ComputeAsync(id, from, to);
        return r is null ? NotFound() : Ok(r);
    }

    [HttpPost("workorders/{woId:long}/start")]
    public async Task<IActionResult> Start(long woId, [FromQuery] string? op)
        => (await _svc.StartAsync(woId, op)) is { } l ? Ok(l) : NotFound();

    [HttpPost("workorders/{woId:long}/pause")]
    public async Task<IActionResult> Pause(long woId, [FromBody] PauseRequest req)
        => (await _svc.PauseAsync(woId, req)) is { } l ? Ok(l) : NotFound();

    [HttpPost("workorders/{woId:long}/resume")]
    public async Task<IActionResult> Resume(long woId, [FromQuery] string? op)
        => (await _svc.ResumeAsync(woId, op)) is { } l ? Ok(l) : NotFound();

    [HttpPost("workorders/{woId:long}/finish")]
    public async Task<IActionResult> Finish(long woId, [FromBody] FinishRunRequest req)
        => (await _svc.FinishAsync(woId, req)) is { } w ? Ok(w) : NotFound();
}
