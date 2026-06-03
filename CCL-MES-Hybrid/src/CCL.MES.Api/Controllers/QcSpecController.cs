using CCL.MES.Application.Services;
using CCL.MES.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// Read surface for the two QC-spec helpers:
///   <see cref="SpecQcWindowService"/> — tolerance bands per QC stage on
///     a given product revision, and
///   <see cref="SpecQcCaptureService"/> — captured QC results plus the
///     reason-code lookup the capture UI needs.
/// Both grouped under <c>/api/v2/qc-specs</c> so the MAUI client only
/// learns one route family.
/// </summary>
[ApiController]
[Authorize(Policy = "QcRead")]
[Route(ApiVersion.Prefix + "/qc-specs")]
public sealed class QcSpecController : ControllerBase
{
    private readonly SpecQcWindowService _windows;
    private readonly SpecQcCaptureService _captures;

    public QcSpecController(SpecQcWindowService windows, SpecQcCaptureService captures)
    {
        _windows = windows;
        _captures = captures;
    }

    [HttpGet("windows/by-revision/{revisionId:long}")]
    public async Task<IActionResult> Windows(long revisionId) =>
        Ok(await _windows.ListByRevisionAsync(revisionId));

    [HttpGet("captures/by-revision/{revisionId:long}")]
    public async Task<IActionResult> Captures(long revisionId) =>
        Ok(await _captures.ListByRevisionAsync(revisionId));

    [HttpGet("reason-codes")]
    public async Task<IActionResult> ReasonCodes() =>
        Ok(await _captures.ListReasonCodesAsync());
}
