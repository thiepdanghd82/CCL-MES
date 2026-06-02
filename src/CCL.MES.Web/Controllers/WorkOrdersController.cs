using CCL.MES.Application;
using CCL.MES.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

// Phase 8 security hardening (docs/PERMISSION_MATRIX.md §6.2) — defense
// in depth. The Blazor UI never goes through this controller (WO actions
// call WorkOrderService directly via @inject in-process), so adding
// role gates here only affects HTTP callers / curl. Verified zero UI
// HTTP callers before this change. Roles mirror the per-button
// AuthorizeView gates in Components/WorkOrderDrawer.razor:
//   Advance / Flags / Create → Admin, Supervisor
// State-machine business guard (WorkOrderStateMachine.CanAdvance) is
// UNTOUCHED — these gates are an additional layer, not a replacement.
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
    [Authorize(Roles = "Admin,Supervisor")]
    public async Task<IActionResult> Create([FromBody] CreateWoRequest r)
    {
        var wo = await _svc.CreateAsync(r);
        return CreatedAtAction(nameof(Get), new { id = wo.Id }, wo);
    }

    [HttpPost("{id:long}/advance")]
    [Authorize(Roles = "Admin,Supervisor")]
    public async Task<IActionResult> Advance(long id, [FromQuery] string? user)
    {
        var res = await _svc.AdvanceAsync(id, user);
        return res.Ok ? Ok(res) : BadRequest(res);
    }

    [HttpPost("{id:long}/flags")]
    [Authorize(Roles = "Admin,Supervisor")]
    public async Task<IActionResult> Flags(long id, [FromBody] UpdateFlagsRequest r, [FromQuery] string? user)
    {
        var wo = await _svc.UpdateFlagsAsync(id, r, user);
        return wo is null ? NotFound() : Ok(wo);
    }
}
