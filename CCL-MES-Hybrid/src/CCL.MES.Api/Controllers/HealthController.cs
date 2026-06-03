using CCL.MES.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// Liveness + readiness probes. Anonymous so a load balancer / kubelet can
/// hit them without holding a token. Intentionally cheap — no DB query
/// here, just process-up confirmation. Smart readiness (DB ping, migration
/// state, etc.) lands in a separate PR if/when ops asks for it.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route(ApiVersion.Prefix + "/[controller]")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok(new
    {
        status = "ok",
        version = "P10.1",
        timeUtc = DateTime.UtcNow,
    });

    [HttpGet("ready")]
    public IActionResult Ready() => Ok(new { ready = true });
}
