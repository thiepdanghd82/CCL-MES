using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// In-process QC inspection read surface (IPQC / FQC / OQC types selected
/// via query). <c>QcRead</c> policy mirrors the legacy page-level gate.
/// Mutation (Create / Approve) is the natural P10.5 surface — capture is
/// append-offline; approval is online-only per Q4.
/// </summary>
[ApiController]
[Authorize(Policy = "QcRead")]
[Route(ApiVersion.Prefix + "/qc")]
public sealed class QcController : ControllerBase
{
    private readonly QcService _svc;
    public QcController(QcService svc) => _svc = svc;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] QcType type = QcType.IPQC,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
        => Ok(await _svc.ListAsync(type, search, page, pageSize));
}
