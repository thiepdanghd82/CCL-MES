using CCL.MES.Application.Services;
using CCL.MES.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// IQC (incoming raw-material) inspection read surface. <c>QcRead</c>
/// policy gates the controller — same role membership as the legacy
/// Iqc.razor page (Admin / Supervisor / QC).
/// </summary>
[ApiController]
[Authorize(Policy = "QcRead")]
[Route(ApiVersion.Prefix + "/iqc")]
public sealed class IqcController : ControllerBase
{
    private readonly IqcService _svc;
    public IqcController(IqcService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] CCL.MES.Domain.QcResult? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _svc.ListAsync(search, status, from, to, page, pageSize));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id)
    {
        var insp = await _svc.GetWithDetailsAsync(id);
        return insp is null ? NotFound() : Ok(insp);
    }
}
