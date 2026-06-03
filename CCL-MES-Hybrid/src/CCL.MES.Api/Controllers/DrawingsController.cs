using CCL.MES.Application.Services;
using CCL.MES.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// Drawing read surface. Per the plan §6.4 Drawing upload is online-only
/// (blob payloads too heavy for the offline queue) so the upload endpoint
/// is deferred — the MAUI client routes drawing actions through the
/// legacy Web UI until the MAUI screen ships in P10.5+. P10.1 covers the
/// list endpoint so the MAUI pilot can show what drawings exist per
/// product revision.
/// </summary>
[ApiController]
[Authorize(Policy = "NpiSpecRead")]
[Route(ApiVersion.Prefix + "/drawings")]
public sealed class DrawingsController : ControllerBase
{
    private readonly DrawingsService _svc;
    public DrawingsController(DrawingsService svc) => _svc = svc;

    [HttpGet("by-revision/{revisionId:long}")]
    public async Task<IActionResult> ListByRevision(long revisionId) =>
        Ok(await _svc.ListByRevisionAsync(revisionId));
}
