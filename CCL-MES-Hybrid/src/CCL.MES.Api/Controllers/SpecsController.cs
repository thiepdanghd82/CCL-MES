using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Shared;
using CCL.MES.Shared.Specs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// Spec (= ProductRevision) surface for the MAUI Hybrid client.
///
/// P10.1 shipped read-only (List / Detail / Products); P10.5c-1 adds the
/// full lifecycle write surface (Create / Approve / Copy / Update /
/// Revise / Supersede / Trash / Restore). Mutations gate on the new
/// <c>NpiSpecWrite</c> policy (Admin / Engineer — Supervisor + QC stay
/// read-only per Phase 8 Q9).
///
/// Result envelopes from <see cref="SpecService"/> map 1:1 to HTTP per
/// the legacy <c>CCL.MES.Web/Controllers/SpecsController.cs</c> pattern
/// (Phase 8 PR-L1/L2/L3 contract) — we mirror that mapping here so the
/// MAUI client sees the same status codes + error envelopes the web UI
/// has already shaken out.
///
/// W4 device-id pattern: when <c>X-Device-Id</c> is on the request, the
/// controller emits a paired <c>SPEC_*_DEVICE</c> audit row carrying the
/// device id. Legacy SpecService already emits the un-paired
/// SPEC_COPY / SPEC_REVISE / SPEC_TRASH / SPEC_RESTORE etc rows; the
/// DEVICE pairing lets the kiosk health dashboard filter mutations by
/// originating station without modifying the legacy emit shape.
/// </summary>
[ApiController]
[Route(ApiVersion.Prefix + "/specs")]
public sealed class SpecsController : ControllerBase
{
    private readonly SpecService _svc;
    private readonly IAuditWriter _audit;
    public SpecsController(SpecService svc, IAuditWriter audit)
    {
        _svc = svc;
        _audit = audit;
    }

    // ── Read (P10.1) ────────────────────────────────────────────────

    [HttpGet]
    [Authorize(Policy = "NpiSpecRead")]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] SpecListView view = SpecListView.Active)
    {
        var result = await _svc.SpecsAsync(search, page, pageSize, view);
        return Ok(result);
    }

    [HttpGet("{revisionId:long}")]
    [Authorize(Policy = "NpiSpecRead")]
    public async Task<IActionResult> Detail(long revisionId)
    {
        var detail = await _svc.SpecDetailAsync(revisionId);
        return detail is null ? NotFound() : Ok(detail);
    }

    [HttpGet("products")]
    [Authorize(Policy = "NpiSpecRead")]
    public async Task<IActionResult> Products() =>
        Ok(await _svc.ProductsForDropdownAsync());

    // ── Mutations (P10.5c-1) ────────────────────────────────────────

    [HttpPost]
    [Authorize(Policy = "NpiSpecWrite")]
    public async Task<IActionResult> Create([FromBody] CreateSpecMutation r)
    {
        try
        {
            var req = new CreateSpecRequest
            {
                ProductId = r.ProductId,
                SpecCode = r.SpecCode,
                Title = r.Title,
                ProcessCode = r.ProcessCode,
                Parameters = r.Parameters.Select(p => new SpecParamDto
                {
                    ParamName = p.ParamName,
                    Nominal = p.Nominal,
                    TolMin = p.TolMin,
                    TolMax = p.TolMax,
                    Uom = p.Uom,
                    IsCritical = p.IsCritical,
                }).ToList(),
            };

            var rev = await _svc.CreateAsync(req, ActorName());
            await EmitDeviceAuditIfHeaderPresent("SPEC_CREATE_DEVICE", new
            {
                rev_id = rev.Id,
                spec_code = rev.SpecCode,
                product_id = rev.ProductId,
                process_code = req.ProcessCode,
            });
            return StatusCode(201, new SpecMutationResponse
            {
                Id = rev.Id,
                SpecCode = rev.SpecCode,
                Revision = rev.RevisionCode,
                Status = rev.Status.ToString(),
                Title = rev.Title,
                ProductId = rev.ProductId,
            });
        }
        catch (Exception ex)
        {
            return Problem(title: "Spec create failed", detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("revisions/{revisionId:long}/approve")]
    [Authorize(Policy = "NpiSpecWrite")]
    public async Task<IActionResult> Approve(long revisionId)
    {
        try
        {
            var rev = await _svc.ApproveAsync(revisionId, ActorName());
            if (rev is null) return NotFound(new SpecMutationError { Code = "not_found", Error = "Spec not found" });
            await EmitDeviceAuditIfHeaderPresent("SPEC_APPROVE_DEVICE", new
            {
                rev_id = rev.Id,
                spec_code = rev.SpecCode,
                status = rev.Status.ToString(),
            });
            return Ok(new SpecMutationResponse
            {
                Id = rev.Id,
                SpecCode = rev.SpecCode,
                Revision = rev.RevisionCode,
                Status = rev.Status.ToString(),
            });
        }
        catch (Exception ex)
        {
            return Problem(title: "Spec approve failed", detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("{revisionId:long}/copy")]
    [Authorize(Policy = "NpiSpecWrite")]
    public async Task<IActionResult> Copy(long revisionId, [FromBody] CopySpecMutation r)
    {
        try
        {
            var req = new CopySpecRequest
            {
                ProductId = r.ProductId,
                SpecCode = r.SpecCode,
                Title = r.Title,
            };
            var result = await _svc.CopyAsync(revisionId, req, ActorName());
            switch (result.Kind)
            {
                case CopyResultKind.Ok:
                    await EmitDeviceAuditIfHeaderPresent("SPEC_COPY_DEVICE", new
                    {
                        source_rev_id = revisionId,
                        new_rev_id = result.Revision!.Id,
                        new_spec_code = result.Revision.SpecCode,
                    });
                    return StatusCode(201, new SpecMutationResponse
                    {
                        Id = result.Revision!.Id,
                        SpecCode = result.Revision.SpecCode,
                        Revision = result.Revision.RevisionCode,
                        ProductId = result.Revision.ProductId,
                    });
                case CopyResultKind.SourceNotFound:
                    return NotFound(new SpecMutationError { Code = "not_found", Error = result.Error ?? "" });
                case CopyResultKind.DuplicateCode:
                    return UnprocessableEntity(new SpecMutationError
                    {
                        Code = "duplicate_spec_code",
                        Error = result.Error ?? "",
                    });
                case CopyResultKind.ValidationError:
                    return UnprocessableEntity(new SpecMutationError
                    {
                        Code = "validation",
                        Error = result.Error ?? "",
                    });
                default:
                    return Problem("Unexpected copy result");
            }
        }
        catch (Exception ex)
        {
            return Problem(title: "Spec copy failed", detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("{revisionId:long}/revise")]
    [Authorize(Policy = "NpiSpecWrite")]
    public async Task<IActionResult> Revise(long revisionId, [FromBody] ReviseSpecMutation r)
    {
        try
        {
            var req = new ReviseSpecRequest { Reason = r.Reason };
            var result = await _svc.ReviseAsync(revisionId, req, ActorName());
            switch (result.Kind)
            {
                case ReviseResultKind.Ok:
                    await EmitDeviceAuditIfHeaderPresent("SPEC_REVISE_DEVICE", new
                    {
                        source_rev_id = revisionId,
                        new_rev_id = result.Revision!.Id,
                        new_revision_code = result.Revision.RevisionCode,
                        reason_len = r.Reason.Length,
                    });
                    return StatusCode(201, new SpecMutationResponse
                    {
                        Id = result.Revision!.Id,
                        SpecCode = result.Revision.SpecCode,
                        Revision = result.Revision.RevisionCode,
                        ParentId = result.Revision.ParentRevisionId,
                    });
                case ReviseResultKind.SourceNotFound:
                    return NotFound(new SpecMutationError { Code = "not_found", Error = result.Error ?? "" });
                case ReviseResultKind.SourceTrashed:
                    return UnprocessableEntity(new SpecMutationError { Code = "trashed", Error = result.Error ?? "" });
                case ReviseResultKind.InvalidSourceStatus:
                    return UnprocessableEntity(new SpecMutationError
                    {
                        Code = "invalid_source_status",
                        Error = result.Error ?? "",
                        CurrentStatus = result.CurrentStatus?.ToString(),
                    });
                case ReviseResultKind.ReasonRequired:
                    return UnprocessableEntity(new SpecMutationError { Code = "reason_required", Error = result.Error ?? "" });
                default:
                    return Problem("Unexpected revise result");
            }
        }
        catch (Exception ex)
        {
            return Problem(title: "Spec revise failed", detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("{revisionId:long}/supersede")]
    [Authorize(Policy = "NpiSpecWrite")]
    public async Task<IActionResult> Supersede(long revisionId, [FromBody] SupersedeSpecMutation r)
    {
        try
        {
            var req = new SupersedeSpecRequest { ConfirmSpecCode = r.ConfirmSpecCode };
            var result = await _svc.SupersedeAsync(revisionId, req, ActorName());
            switch (result.Kind)
            {
                case SupersedeResultKind.Ok:
                    await EmitDeviceAuditIfHeaderPresent("SPEC_SUPERSEDE_DEVICE", new
                    {
                        rev_id = result.Revision!.Id,
                        spec_code = result.Revision.SpecCode,
                        status = result.Revision.Status.ToString(),
                    });
                    return Ok(new SpecMutationResponse
                    {
                        Id = result.Revision!.Id,
                        SpecCode = result.Revision.SpecCode,
                        Revision = result.Revision.RevisionCode,
                        Status = result.Revision.Status.ToString(),
                    });
                case SupersedeResultKind.NotFound:
                    return NotFound(new SpecMutationError { Code = "not_found", Error = result.Error ?? "" });
                case SupersedeResultKind.Trashed:
                    return UnprocessableEntity(new SpecMutationError { Code = "trashed", Error = result.Error ?? "" });
                case SupersedeResultKind.InvalidStatus:
                    return UnprocessableEntity(new SpecMutationError
                    {
                        Code = "invalid_status",
                        Error = result.Error ?? "",
                        CurrentStatus = result.CurrentStatus?.ToString(),
                    });
                case SupersedeResultKind.ConfirmMismatch:
                    return UnprocessableEntity(new SpecMutationError { Code = "confirm_mismatch", Error = result.Error ?? "" });
                default:
                    return Problem("Unexpected supersede result");
            }
        }
        catch (Exception ex)
        {
            return Problem(title: "Spec supersede failed", detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("{revisionId:long}/trash")]
    [Authorize(Policy = "NpiSpecWrite")]
    public async Task<IActionResult> Trash(long revisionId)
    {
        try
        {
            var result = await _svc.TrashAsync(revisionId, ActorName());
            switch (result.Kind)
            {
                case TrashResultKind.Ok:
                    await EmitDeviceAuditIfHeaderPresent("SPEC_TRASH_DEVICE", new
                    {
                        rev_id = result.Revision!.Id,
                        spec_code = result.Revision.SpecCode,
                    });
                    return Ok(new SpecMutationResponse
                    {
                        Id = result.Revision!.Id,
                        SpecCode = result.Revision.SpecCode,
                        Revision = result.Revision.RevisionCode,
                        IsTrashed = result.Revision.IsTrashed,
                    });
                case TrashResultKind.NotFound:
                    return NotFound(new SpecMutationError { Code = "not_found", Error = result.Error ?? "" });
                case TrashResultKind.AlreadyTrashed:
                    return UnprocessableEntity(new SpecMutationError { Code = "already_trashed", Error = result.Error ?? "" });
                case TrashResultKind.ActiveWorkOrders:
                    return UnprocessableEntity(new SpecMutationError
                    {
                        Code = "active_work_orders",
                        Error = result.Error ?? "",
                        ActiveWoCount = result.ActiveWoCount,
                    });
                default:
                    return Problem("Unexpected trash result");
            }
        }
        catch (Exception ex)
        {
            return Problem(title: "Spec trash failed", detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPost("{revisionId:long}/restore")]
    [Authorize(Policy = "NpiSpecWrite")]
    public async Task<IActionResult> Restore(long revisionId)
    {
        try
        {
            var result = await _svc.RestoreAsync(revisionId, ActorName());
            switch (result.Kind)
            {
                case RestoreResultKind.Ok:
                    await EmitDeviceAuditIfHeaderPresent("SPEC_RESTORE_DEVICE", new
                    {
                        rev_id = result.Revision!.Id,
                        spec_code = result.Revision.SpecCode,
                    });
                    return Ok(new SpecMutationResponse
                    {
                        Id = result.Revision!.Id,
                        SpecCode = result.Revision.SpecCode,
                        Revision = result.Revision.RevisionCode,
                        IsTrashed = result.Revision.IsTrashed,
                    });
                case RestoreResultKind.NotFound:
                    return NotFound(new SpecMutationError { Code = "not_found", Error = result.Error ?? "" });
                case RestoreResultKind.NotTrashed:
                    return UnprocessableEntity(new SpecMutationError { Code = "not_trashed", Error = result.Error ?? "" });
                default:
                    return Problem("Unexpected restore result");
            }
        }
        catch (Exception ex)
        {
            return Problem(title: "Spec restore failed", detail: ex.Message, statusCode: 500);
        }
    }

    [HttpPut("{revisionId:long}")]
    [Authorize(Policy = "NpiSpecWrite")]
    public async Task<IActionResult> Update(long revisionId, [FromBody] UpdateSpecMutation r)
    {
        try
        {
            var req = new UpdateSpecRequest
            {
                Title = r.Title,
                RefNo = r.RefNo,
                InspectionLevel = r.InspectionLevel,
                ProcessCode = r.ProcessCode,
                ColorSpecJson = r.ColorSpecJson,
            };
            var result = await _svc.UpdateAsync(revisionId, req, ActorName());
            switch (result.Kind)
            {
                case UpdateResultKind.Ok:
                case UpdateResultKind.NoChanges:
                    if (result.Kind == UpdateResultKind.Ok)
                    {
                        await EmitDeviceAuditIfHeaderPresent("SPEC_UPDATE_DEVICE", new
                        {
                            rev_id = result.Revision!.Id,
                            spec_code = result.Revision.SpecCode,
                            title_changed = r.Title is not null,
                            refno_changed = r.RefNo is not null,
                            inspection_changed = r.InspectionLevel is not null,
                            process_changed = r.ProcessCode is not null,
                            color_changed = r.ColorSpecJson is not null,
                        });
                    }
                    return Ok(new SpecMutationResponse
                    {
                        Id = result.Revision!.Id,
                        SpecCode = result.Revision.SpecCode,
                        Revision = result.Revision.RevisionCode,
                        Status = result.Revision.Status.ToString(),
                        Title = result.Revision.Title,
                        NoChanges = result.Kind == UpdateResultKind.NoChanges,
                    });
                case UpdateResultKind.NotFound:
                    return NotFound(new SpecMutationError { Code = "not_found", Error = result.Error ?? "" });
                case UpdateResultKind.Trashed:
                    return UnprocessableEntity(new SpecMutationError { Code = "trashed", Error = result.Error ?? "" });
                case UpdateResultKind.ImmutableStatus:
                    return UnprocessableEntity(new SpecMutationError
                    {
                        Code = "immutable_status",
                        Error = result.Error ?? "",
                        CurrentStatus = result.CurrentStatus?.ToString(),
                    });
                default:
                    return Problem("Unexpected update result");
            }
        }
        catch (Exception ex)
        {
            return Problem(title: "Spec update failed", detail: ex.Message, statusCode: 500);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private string ActorName() =>
        User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";

    private async Task EmitDeviceAuditIfHeaderPresent(string action, object detail)
    {
        var deviceId = Request.Headers["X-Device-Id"].ToString();
        if (string.IsNullOrWhiteSpace(deviceId)) return;

        var actor = ActorName();
        var role = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["device_id"] = deviceId,
            ["detail"] = detail,
        });
        await _audit.EmitAsync(
            action: action,
            actor: actor,
            actorRole: role,
            targetType: "Device",
            targetId: deviceId,
            detail: payload);
    }
}
