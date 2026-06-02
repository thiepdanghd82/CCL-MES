using CCL.MES.Application;
using CCL.MES.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

/// <summary>
/// Phase 8 PR #28 — REWORKED sau Spec → ProductRevision clean rewrite.
///   - GET /api/specs               → SpecsAsync paginated DTO
///   - POST /api/specs              → CreateAsync trả ProductRevision
///   - POST /api/specs/revisions/{revisionId}/approve → ApproveAsync trả ProductRevision
///
/// Compatibility note: cũ route `/api/specs/versions/{versionId}/approve` đổi
/// thành `revisions/{revisionId}`; KHÔNG có external API consumer hiện tại
/// (chỉ internal Blazor circuit). Nếu tương lai cần stable contract, alias
/// đường cũ thêm dòng `[HttpPost("versions/{versionId:long}/approve")]`.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SpecsController : ControllerBase
{
    private readonly SpecService _svc;
    public SpecsController(SpecService svc) => _svc = svc;

    /// <summary>
    /// GET grid paginated. Optional `?search=&page=&pageSize=`. KHÔNG có
    /// auth middleware ở đây — Web SPA dùng SpecService trực tiếp qua DI.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50)
        => Ok(await _svc.SpecsAsync(search, page, pageSize));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSpecRequest r, [FromQuery] string? user)
        => Ok(await _svc.CreateAsync(r, user));

    [HttpPost("revisions/{revisionId:long}/approve")]
    public async Task<IActionResult> Approve(long revisionId, [FromQuery] string? user)
    {
        var rev = await _svc.ApproveAsync(revisionId, user);
        return rev is null ? NotFound() : Ok(rev);
    }

    // Phase 8 PR-L1 — Spec lifecycle Copy + Edit. RBAC: Admin/Engineer only
    // (Q9 mirror — QC/Operator cannot mutate spec content). Audit emit
    // happens inside the service inside the same transaction as the writes.
    //
    // Result envelopes (CopyResult, UpdateResult) are mapped to HTTP status
    // codes here:
    //   - Copy:   Ok → 201, SourceNotFound → 404, DuplicateCode → 422,
    //             ValidationError → 422
    //   - Update: Ok / NoChanges → 200, NotFound → 404, Trashed → 422,
    //             ImmutableStatus → 422 with current status in detail

    [HttpPost("{revisionId:long}/copy")]
    [Authorize(Roles = "Admin,Engineer")]
    public async Task<IActionResult> Copy(long revisionId, [FromBody] CopySpecRequest r)
    {
        try
        {
            var user = User?.Identity?.Name;
            var result = await _svc.CopyAsync(revisionId, r, user);
            return result.Kind switch
            {
                CopyResultKind.Ok               => StatusCode(201, new
                {
                    id          = result.Revision!.Id,
                    specCode    = result.Revision.SpecCode,
                    revision    = result.Revision.RevisionCode,
                    productId   = result.Revision.ProductId,
                }),
                CopyResultKind.SourceNotFound   => NotFound(new { error = result.Error }),
                CopyResultKind.DuplicateCode    => UnprocessableEntity(new
                {
                    code  = "duplicate_spec_code",
                    error = result.Error,
                }),
                CopyResultKind.ValidationError  => UnprocessableEntity(new
                {
                    code  = "validation",
                    error = result.Error,
                }),
                _                               => Problem("Unexpected copy result"),
            };
        }
        catch (System.Exception ex)
        {
            return Problem(title: "Spec copy failed", detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("{revisionId:long}/revise")]
    [Authorize(Roles = "Admin,Engineer")]
    public async Task<IActionResult> Revise(long revisionId, [FromBody] ReviseSpecRequest r)
    {
        try
        {
            var user = User?.Identity?.Name;
            var result = await _svc.ReviseAsync(revisionId, r, user);
            return result.Kind switch
            {
                ReviseResultKind.Ok                  => StatusCode(201, new
                {
                    id          = result.Revision!.Id,
                    specCode    = result.Revision.SpecCode,
                    revision    = result.Revision.RevisionCode,
                    parentId    = result.Revision.ParentRevisionId,
                }),
                ReviseResultKind.SourceNotFound      => NotFound(new { error = result.Error }),
                ReviseResultKind.SourceTrashed       => UnprocessableEntity(new
                {
                    code  = "trashed",
                    error = result.Error,
                }),
                ReviseResultKind.InvalidSourceStatus => UnprocessableEntity(new
                {
                    code           = "invalid_source_status",
                    error          = result.Error,
                    currentStatus  = result.CurrentStatus?.ToString(),
                }),
                ReviseResultKind.ReasonRequired      => UnprocessableEntity(new
                {
                    code  = "reason_required",
                    error = result.Error,
                }),
                _                                    => Problem("Unexpected revise result"),
            };
        }
        catch (System.Exception ex)
        {
            return Problem(title: "Spec revise failed", detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("{revisionId:long}/supersede")]
    [Authorize(Roles = "Admin,Engineer")]
    public async Task<IActionResult> Supersede(long revisionId, [FromBody] SupersedeSpecRequest r)
    {
        try
        {
            var user = User?.Identity?.Name;
            var result = await _svc.SupersedeAsync(revisionId, r, user);
            return result.Kind switch
            {
                SupersedeResultKind.Ok               => Ok(new
                {
                    id          = result.Revision!.Id,
                    specCode    = result.Revision.SpecCode,
                    revision    = result.Revision.RevisionCode,
                    status      = result.Revision.Status.ToString(),
                }),
                SupersedeResultKind.NotFound         => NotFound(new { error = result.Error }),
                SupersedeResultKind.Trashed          => UnprocessableEntity(new
                {
                    code  = "trashed",
                    error = result.Error,
                }),
                SupersedeResultKind.InvalidStatus    => UnprocessableEntity(new
                {
                    code           = "invalid_status",
                    error          = result.Error,
                    currentStatus  = result.CurrentStatus?.ToString(),
                }),
                SupersedeResultKind.ConfirmMismatch  => UnprocessableEntity(new
                {
                    code  = "confirm_mismatch",
                    error = result.Error,
                }),
                _                                    => Problem("Unexpected supersede result"),
            };
        }
        catch (System.Exception ex)
        {
            return Problem(title: "Spec supersede failed", detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPut("{revisionId:long}")]
    [Authorize(Roles = "Admin,Engineer")]
    public async Task<IActionResult> Update(long revisionId, [FromBody] UpdateSpecRequest r)
    {
        try
        {
            var user = User?.Identity?.Name;
            var result = await _svc.UpdateAsync(revisionId, r, user);
            return result.Kind switch
            {
                UpdateResultKind.Ok or UpdateResultKind.NoChanges => Ok(new
                {
                    id              = result.Revision!.Id,
                    specCode        = result.Revision.SpecCode,
                    revision        = result.Revision.RevisionCode,
                    title           = result.Revision.Title,
                    status          = result.Revision.Status.ToString(),
                    noChanges       = result.Kind == UpdateResultKind.NoChanges,
                }),
                UpdateResultKind.NotFound        => NotFound(new { error = result.Error }),
                UpdateResultKind.Trashed         => UnprocessableEntity(new
                {
                    code  = "trashed",
                    error = result.Error,
                }),
                UpdateResultKind.ImmutableStatus => UnprocessableEntity(new
                {
                    code           = "immutable_status",
                    error          = result.Error,
                    currentStatus  = result.CurrentStatus?.ToString(),
                }),
                _                                => Problem("Unexpected update result"),
            };
        }
        catch (System.Exception ex)
        {
            return Problem(title: "Spec update failed", detail: ex.Message, statusCode: 500);
        }
    }
}
