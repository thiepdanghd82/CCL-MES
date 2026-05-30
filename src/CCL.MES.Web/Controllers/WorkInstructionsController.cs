using CCL.MES.Application;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

[ApiController]
[Route("api/workinstructions")]
public class WorkInstructionsController : ControllerBase
{
    private readonly WiService _svc;
    public WorkInstructionsController(WiService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> GetAll() => Ok(await _svc.GetAllAsync());

    [HttpGet("for")]
    public async Task<IActionResult> For([FromQuery] long productId, [FromQuery] ProcessStepCode step)
        => (await _svc.GetForAsync(productId, step)) is { } wi ? Ok(wi) : NotFound();

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateWiRequest r) => Ok(await _svc.CreateAsync(r));

    [HttpPost("{id:long}/approve")]
    public async Task<IActionResult> Approve(long id)
        => (await _svc.ApproveAsync(id)) is { } wi ? Ok(wi) : NotFound();
}
