using CCL.MES.Application;
using CCL.MES.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

// Phase 8 security hardening (docs/PERMISSION_MATRIX.md §6.2) — defense
// in depth. WO-level QC (drawer "QC IPQC/FQC/OQC Pass" buttons) calls
// QcService directly via @inject in-process, so adding role gates here
// only affects HTTP callers / curl. Verified zero UI HTTP callers
// before this change. Roles mirror the per-button AuthorizeView gate
// in Components/WorkOrderDrawer.razor:240 (`Admin, Supervisor, QC`).
// QcService.CreateAsync / ApproveAsync bodies UNTOUCHED.
[ApiController]
[Route("api/qc")]
[Authorize(Roles = "Admin,Supervisor,QC")]
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
