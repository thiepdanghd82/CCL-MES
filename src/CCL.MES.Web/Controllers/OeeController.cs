using CCL.MES.Application;
using CCL.MES.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

// Phase 8 security hardening (docs/PERMISSION_MATRIX.md §6.2) — defense
// in depth. The audit doc originally noted "GET-only" for OeeController
// but the 4 POST endpoints (start / pause / resume / finish) exist —
// they're an alternative HTTP surface to the in-process service path
// used by Components/WorkOrderDrawer.razor (which @injects OeeService
// directly and calls Oee.StartAsync etc.). Adding role gates here only
// affects HTTP callers / curl. Verified zero UI HTTP callers before
// this change. Roles mirror the per-button AuthorizeView gate at
// Components/WorkOrderDrawer.razor:248 (`Admin, Supervisor, Operator`).
// OeeService body UNTOUCHED.
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
    [Authorize(Roles = "Admin,Supervisor,Operator")]
    public async Task<IActionResult> Start(long woId, [FromQuery] string? op)
        => (await _svc.StartAsync(woId, op)) is { } l ? Ok(l) : NotFound();

    [HttpPost("workorders/{woId:long}/pause")]
    [Authorize(Roles = "Admin,Supervisor,Operator")]
    public async Task<IActionResult> Pause(long woId, [FromBody] PauseRequest req)
        => (await _svc.PauseAsync(woId, req)) is { } l ? Ok(l) : NotFound();

    [HttpPost("workorders/{woId:long}/resume")]
    [Authorize(Roles = "Admin,Supervisor,Operator")]
    public async Task<IActionResult> Resume(long woId, [FromQuery] string? op)
        => (await _svc.ResumeAsync(woId, op)) is { } l ? Ok(l) : NotFound();

    [HttpPost("workorders/{woId:long}/finish")]
    [Authorize(Roles = "Admin,Supervisor,Operator")]
    public async Task<IActionResult> Finish(long woId, [FromBody] FinishRunRequest req)
        => (await _svc.FinishAsync(woId, req)) is { } w ? Ok(w) : NotFound();
}
