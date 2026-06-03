using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// Work-instruction read surface. Default <c>FallbackPolicy</c> (auth
/// required) — every authenticated user can read WIs. Author + approve
/// endpoints deliberately omitted; they're Engineer-only and the legacy
/// UI handles them today.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/work-instructions")]
public sealed class WiController : ControllerBase
{
    private readonly WiService _svc;
    public WiController(WiService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await _svc.GetAllAsync());

    [HttpGet("for")]
    public async Task<IActionResult> ForProductStep(
        [FromQuery] long productId,
        [FromQuery] ProcessStepCode step)
    {
        var wi = await _svc.GetForAsync(productId, step);
        return wi is null ? NotFound() : Ok(wi);
    }
}
