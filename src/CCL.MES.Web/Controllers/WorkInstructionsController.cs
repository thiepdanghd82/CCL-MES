using CCL.MES.Application;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

// Phase 8 security hardening (docs/PERMISSION_MATRIX.md §6.2) — defense
// in depth. The Pages/WorkInstructions.razor surface is read-only
// (no Create/Approve buttons today) and calls WiService directly via
// @inject in-process. Adding role gates here only affects HTTP callers
// / curl. Verified zero UI HTTP callers before this change.
//
// WI is engineered content (mirrors NPI mutation convention) → Create
// restricted to Admin + Engineer. Approve is a supervisory action →
// Admin + Supervisor. WiService body UNTOUCHED.
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
    [Authorize(Roles = "Admin,Engineer")]
    public async Task<IActionResult> Create([FromBody] CreateWiRequest r) => Ok(await _svc.CreateAsync(r));

    [HttpPost("{id:long}/approve")]
    [Authorize(Roles = "Admin,Supervisor")]
    public async Task<IActionResult> Approve(long id)
        => (await _svc.ApproveAsync(id)) is { } wi ? Ok(wi) : NotFound();
}
