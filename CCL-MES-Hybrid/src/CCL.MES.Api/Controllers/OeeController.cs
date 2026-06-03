using CCL.MES.Application.Services;
using CCL.MES.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// OEE read surface — machines list + compute per machine. Mutation
/// (Start / Pause / Resume / Finish) is the natural P10.4 offline-safe
/// pilot under Q4 (append-only ProductionLog rows). P10.1 ships the read
/// path only; writes land with the sync engine.
/// </summary>
[ApiController]
[Authorize]
[Route(ApiVersion.Prefix + "/oee")]
public sealed class OeeController : ControllerBase
{
    private readonly OeeService _svc;
    public OeeController(OeeService svc) => _svc = svc;

    [HttpGet("machines")]
    public async Task<IActionResult> Machines() =>
        Ok(await _svc.GetMachinesAsync());

    [HttpGet("machines/{machineId:long}/compute")]
    public async Task<IActionResult> Compute(
        long machineId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var result = await _svc.ComputeAsync(machineId, from, to);
        return result is null ? NotFound() : Ok(result);
    }
}
