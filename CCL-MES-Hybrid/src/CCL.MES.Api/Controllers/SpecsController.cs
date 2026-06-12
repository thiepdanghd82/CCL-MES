using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Application.SpecImport;
using CCL.MES.Domain;
using CCL.MES.Shared;
using CCL.MES.Shared.Specs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
    private readonly SpecImportService _import;
    private readonly IMesDbContext _db;

    /// <summary>P10.5c-2 — API contract cap. Mirrors operator UX promise
    /// (10 MB per Henry's spec). Legacy <c>SpecImportService.MaxFileSizeBytes</c>
    /// stays at 5 MB; files between 5 MB and 10 MB will surface as
    /// <c>parse_error</c> with the legacy "File quá lớn" message — that's
    /// still operator-actionable.</summary>
    private const long ImportMaxFileSizeBytes = 10 * 1024 * 1024;

    public SpecsController(SpecService svc, IAuditWriter audit, SpecImportService import, IMesDbContext db)
    {
        _svc = svc;
        _audit = audit;
        _import = import;
        _db = db;
    }

    // ── Read (P10.1) ────────────────────────────────────────────────

    [HttpGet]
    [Authorize(Policy = "NpiSpecRead")]
    public async Task<IActionResult> List(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] SpecListView view = SpecListView.Active,
        [FromQuery] string? planner = null)
    {
        // P10.5c-3 — planner chip filter forwarded to legacy service.
        // Accepts SILK / FLEXO / LETTER / INDIGO / DIECUT (canonical
        // planner codes used by SpecShowcardVm). Empty / unknown = no filter.
        var result = await _svc.SpecsAsync(search, page, pageSize, view, planner);
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
                IfsCode = r.IfsCode,
                SpecCode = r.SpecCode,
                Title = r.Title,
                Customer = r.Customer,
                Spec = r.Spec,
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
        catch (CCL.MES.Application.Services.DuplicateSpecException dup)
        {
            // The (Product, Rev A) unique constraint — the duplicate is the
            // Part No, not the Spec code. Surfaces as duplicate_part_no so the
            // UI can highlight the right field.
            return UnprocessableEntity(new SpecMutationError { Code = "duplicate_part_no", Error = dup.Message });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException)
        {
            return UnprocessableEntity(new SpecMutationError
            {
                Code = "duplicate_part_no",
                Error = "This Part No already has a spec (Rev A). Use Copy or Revise to add a revision.",
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
                // P10.10 — inline-edit fields
                SubstrateType = r.SubstrateType,
                AdhesiveType = r.AdhesiveType,
                ThicknessUm = r.ThicknessUm,
                PrintingCavity = r.PrintingCavity,
                LengthPitchMm = r.LengthPitchMm,
                ProductSizeWmm = r.ProductSizeWmm,
                ProductSizeHmm = r.ProductSizeHmm,
                RemarksText = r.RemarksText,
                RemarksCutText = r.RemarksCutText,
                Colors = r.Colors?.Select(c => new SpecColorRowInput
                {
                    Surface = c.Surface, Color = c.Color, InkName = c.InkName, InkCode = c.InkCode,
                    Maker = c.Maker, Retarder = c.Retarder, Viscosity = c.Viscosity, Speed = c.Speed,
                    Squeegee = c.Squeegee, Dry = c.Dry, TemperatureC = c.TemperatureC, TimeMin = c.TimeMin,
                    Uv = c.Uv, EmulsionUm = c.EmulsionUm, PlateSize = c.PlateSize, Mesh = c.Mesh,
                    AngleDeg = c.AngleDeg, PlateCode = c.PlateCode, ControlNo = c.ControlNo, Remark = c.Remark,
                }).ToList(),
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

    // ── Import (P10.5c-2) ───────────────────────────────────────────

    /// <summary>
    /// Multipart xlsx upload → parse → return preview + dup info.
    /// Stateless: the parsed DTO rides back in the response as an opaque
    /// JSON blob (<see cref="SpecImportPreviewResponse.ParsedJson"/>) which
    /// the client echoes verbatim on save. No server-side temp-file cache.
    ///
    /// Validation chain:
    ///   1. File part present + non-empty.
    ///   2. .xlsx extension (case-insensitive).
    ///   3. Size ≤ <see cref="ImportMaxFileSizeBytes"/> (10 MB).
    ///   4. Content sniff: first 2 bytes = "PK" (xlsx is a zip).
    ///   5. Planner category whitelist (silkscreen / flexo / letterpress
    ///      / indigo / diecut) — invalid falls through to silkscreen with
    ///      a warning per legacy parser factory behavior.
    /// </summary>
    [HttpPost("import/preview")]
    [Authorize(Policy = "NpiSpecWrite")]
    [RequestSizeLimit(ImportMaxFileSizeBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ImportMaxFileSizeBytes + 64 * 1024)]
    public async Task<IActionResult> ImportPreview(
        IFormFile? file,
        [FromForm] string? plannerCategory,
        CancellationToken ct)
    {
        try
        {
            // Nullable IFormFile is required so model-binding doesn't 400
            // ahead of our 422 guard when the multipart 'file' part is
            // missing — we want a typed error envelope, not the framework's
            // bare BadRequest.
            if (file is null || file.Length == 0)
                return UnprocessableEntity(new SpecMutationError { Code = "import.no_file", Error = "Multipart 'file' part is missing or empty." });

            if (!HasXlsxExtension(file.FileName))
                return UnprocessableEntity(new SpecMutationError { Code = "import.invalid_extension", Error = $"File '{file.FileName}' is not .xlsx." });

            if (file.Length > ImportMaxFileSizeBytes)
                return UnprocessableEntity(new SpecMutationError
                {
                    Code = "import.oversize",
                    Error = $"File size {file.Length} bytes exceeds the {ImportMaxFileSizeBytes}-byte cap.",
                });

            // Sniff the first 2 bytes for the ZIP "PK" magic. xlsx is a
            // zip container; a renamed .txt / .docx slips past extension
            // alone. We read from a buffered stream so the legacy parser
            // can still read from byte 0 afterwards.
            await using var raw = file.OpenReadStream();
            var sniff = new byte[2];
            var sniffed = await raw.ReadAsync(sniff.AsMemory(0, 2), ct);
            if (sniffed != 2 || sniff[0] != (byte)'P' || sniff[1] != (byte)'K')
                return UnprocessableEntity(new SpecMutationError { Code = "import.invalid_content", Error = "File is not a valid zip/xlsx container." });

            // Rewind to byte 0 so the legacy parser reads the full file.
            // IFormFile.OpenReadStream() returns a seekable stream for
            // small (≤ memory-buffer-threshold) bodies — true here per
            // the 10 MB cap above.
            if (raw.CanSeek)
                raw.Seek(0, SeekOrigin.Begin);
            else
                // Fallback: copy into a MemoryStream. Bounded by the
                // 10 MB cap so heap pressure is acceptable.
                return await ImportPreviewBuffered(file, plannerCategory, ct);

            var category = NormalizePlannerCategory(plannerCategory);
            var previewDto = await _import.PreviewAsync(raw, file.FileName, file.Length, category);
            var response = BuildPreviewResponse(previewDto);
            // Enrich dup info with status — legacy returns only id+code.
            if (response.DuplicateRevisionId is not null)
                response = response with { DuplicateStatus = await ResolveDuplicateStatusAsync(response.DuplicateRevisionId.Value, ct) };
            return Ok(response);
        }
        catch (Exception ex)
        {
            // Surface as parse_error so the modal banner reads cleanly;
            // the underlying ex.Message is included for ops triage.
            return UnprocessableEntity(new SpecMutationError
            {
                Code = "import.parse_error",
                Error = ex.Message,
            });
        }
    }

    /// <summary>
    /// Save endpoint — echoes the parsed payload from <see cref="ImportPreview"/>
    /// and applies the operator-chosen dup-handling decision.
    ///
    /// Modes:
    ///   - <c>SaveNew</c>: legacy save with overwriteRefNo=false; server
    ///     rejects via "duplicate_ref_no" if a dup appeared since preview.
    ///   - <c>UpgradeRev</c>: legacy save with overwriteRefNo=true →
    ///     soft-trashes the matching existing rev + creates the new draft.
    ///     Revision letter increment is deferred to a follow-up PR (the
    ///     legacy still hardcodes "A").
    ///   - <c>SaveAsCopy</c>: requires <see cref="SpecImportSaveRequest.SpecCodeOverride"/>;
    ///     mutates parsed.PartNo to the override and clears parsed.RefNo
    ///     so the new spec has no dup conflict.
    /// </summary>
    [HttpPost("import/save")]
    [Authorize(Policy = "NpiSpecWrite")]
    public async Task<IActionResult> ImportSave([FromBody] SpecImportSaveRequest req, CancellationToken ct)
    {
        try
        {
            if (req is null || string.IsNullOrWhiteSpace(req.ParsedJson))
                return UnprocessableEntity(new SpecMutationError { Code = "import.no_parsed_payload", Error = "ParsedJson is required (echo from /preview)." });

            ParsedSpecDto? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ParsedSpecDto>(req.ParsedJson);
            }
            catch (JsonException ex)
            {
                return UnprocessableEntity(new SpecMutationError { Code = "import.invalid_parsed_payload", Error = ex.Message });
            }
            if (parsed is null)
                return UnprocessableEntity(new SpecMutationError { Code = "import.invalid_parsed_payload", Error = "ParsedJson deserialized to null." });

            var mode = req.Mode ?? SpecImportSaveMode.SaveNew;
            bool overwrite;
            switch (mode)
            {
                case SpecImportSaveMode.SaveNew:
                    overwrite = false;
                    break;

                case SpecImportSaveMode.UpgradeRev:
                    overwrite = true;
                    break;

                case SpecImportSaveMode.SaveAsCopy:
                    if (string.IsNullOrWhiteSpace(req.SpecCodeOverride))
                        return UnprocessableEntity(new SpecMutationError { Code = "import.spec_code_override_required", Error = "SpecCodeOverride is required for SaveAsCopy mode." });
                    // Mutate parsed payload: SaveAsCopy creates a parallel
                    // spec with operator-supplied SpecCode + no RefNo.
                    parsed.PartNo = req.SpecCodeOverride!.Trim();
                    parsed.RefNo = "";
                    overwrite = false;
                    break;

                default:
                    return UnprocessableEntity(new SpecMutationError { Code = "import.invalid_mode", Error = $"Unknown mode '{req.Mode}'." });
            }

            // P10.5c-3 — the prior rename-suffix workaround here was
            // removed. Legacy SpecImportService.SaveAsync now handles the
            // UpgradeRev flow correctly: when overwriteRefNo=true and a
            // matching non-trashed rev exists, it supersedes that rev
            // (Status=Superseded + EffectiveTo=now) AND bumps the new
            // rev's letter via SpecRevisionHelpers.NextAvailableRev,
            // preserving lineage via ParentRevisionId. No more
            // (ProductId, "A") UNIQUE collisions, no trashed-suffix
            // noise in the Trash view.

            SpecImportSaveResult save;
            try
            {
                save = await _import.SaveAsync(parsed, source: "maui_import", fileName: req.FileName ?? "", user: ActorName(), overwriteRefNo: overwrite);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("đã tồn tại", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("RefNo", StringComparison.OrdinalIgnoreCase))
            {
                return UnprocessableEntity(new SpecMutationError { Code = "import.duplicate_ref_no", Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return UnprocessableEntity(new SpecMutationError { Code = "import.validation", Error = ex.Message });
            }

            await EmitDeviceAuditIfHeaderPresent("SPEC_IMPORT_DEVICE", new
            {
                rev_id = save.ProductRevisionId,
                spec_code = save.SpecCode,
                ref_no = save.RefNo,
                category = parsed.Category,
                mode,
                filename = req.FileName,
                rows_parsed = save.RowsParsed,
            });

            return Ok(new SpecImportSaveResponse
            {
                ProductRevisionId = save.ProductRevisionId,
                ProductId = save.ProductId,
                SpecCode = save.SpecCode,
                RefNo = save.RefNo,
                RowsParsed = save.RowsParsed,
                CreatedNewProduct = save.CreatedNewProduct,
                Warnings = save.Warnings,
                Mode = mode,
            });
        }
        catch (Exception ex)
        {
            return Problem(title: "Spec import save failed", detail: ex.Message, statusCode: 500);
        }
    }

    private async Task<IActionResult> ImportPreviewBuffered(IFormFile file, string? plannerCategory, CancellationToken ct)
    {
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        ms.Position = 0;
        var category = NormalizePlannerCategory(plannerCategory);
        var previewDto = await _import.PreviewAsync(ms, file.FileName, file.Length, category);
        var response = BuildPreviewResponse(previewDto);
        if (response.DuplicateRevisionId is not null)
            response = response with { DuplicateStatus = await ResolveDuplicateStatusAsync(response.DuplicateRevisionId.Value, ct) };
        return Ok(response);
    }

    private async Task<string?> ResolveDuplicateStatusAsync(long revisionId, CancellationToken ct)
    {
        var status = await _db.ProductRevisions
            .AsNoTracking()
            .Where(r => r.Id == revisionId)
            .Select(r => (CCL.MES.Domain.ProductRevisionStatus?)r.Status)
            .FirstOrDefaultAsync(ct);
        return status?.ToString();
    }

    private static bool HasXlsxExtension(string? filename) =>
        !string.IsNullOrEmpty(filename) && filename.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePlannerCategory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "silkscreen";
        var lower = raw.Trim().ToLowerInvariant();
        return lower switch
        {
            "silkscreen" or "flexo" or "letterpress" or "indigo" or "diecut" => lower,
            _ => "silkscreen",
        };
    }

    private static SpecImportPreviewResponse BuildPreviewResponse(SpecImportPreviewDto dto)
    {
        SpecImportParsedSummary? summary = null;
        string? parsedJson = null;
        if (dto.Parsed is not null)
        {
            var p = dto.Parsed;
            var isFlexo = string.Equals(p.Category, "flexo", StringComparison.OrdinalIgnoreCase);
            int? cavity = isFlexo
                ? p.FlexoCuttingRows.FirstOrDefault()?.CuttingCavity
                : p.PrintingCavity;
            double? pitchMm = isFlexo
                ? p.FlexoCuttingRows.FirstOrDefault()?.PitchMm
                : p.LengthPitchMm;
            int numColors = isFlexo ? p.FlexoInkRows.Count : p.PrintRows.Count;
            summary = new SpecImportParsedSummary
            {
                Category = p.Category,
                RefNo = string.IsNullOrWhiteSpace(p.RefNo) ? null : p.RefNo,
                Customer = string.IsNullOrWhiteSpace(p.Customer) ? null : p.Customer,
                PartNo = string.IsNullOrWhiteSpace(p.PartNo) ? null : p.PartNo,
                PartName = string.IsNullOrWhiteSpace(p.PartName) ? null : p.PartName,
                MaterialType = string.IsNullOrWhiteSpace(p.MaterialType) ? null : p.MaterialType,
                ProductSizeW = p.ProductSizeW,
                ProductSizeH = p.ProductSizeH,
                NumColors = numColors,
                Cavity = cavity,
                PitchMm = pitchMm,
                InspectionLevel = string.IsNullOrWhiteSpace(p.InspectionLevel) ? null : p.InspectionLevel,
                RemarksLeft = p.RemarksLeft,
                RemarksRight = p.RemarksRight,
                PrintRowsCount = p.PrintRows.Count,
                FlexoCuttingRowsCount = p.FlexoCuttingRows.Count,
                FlexoInkRowsCount = p.FlexoInkRows.Count,
                FlexoPrintRowsCount = p.FlexoPrintRows.Count,
            };
            // Opaque echo — serialize once on preview, client passes back
            // verbatim on save. Keeps both endpoints stateless.
            parsedJson = JsonSerializer.Serialize(p);
        }

        return new SpecImportPreviewResponse
        {
            FileName = dto.FileName,
            FileSizeBytes = dto.FileSizeBytes,
            PlannerCategory = dto.PlannerCategory,
            ParseOk = dto.ParseOk,
            ParseError = dto.ParseError,
            Warnings = dto.Warnings.ToList(),
            Summary = summary,
            DuplicateRefNo = dto.DuplicateRefNo,
            DuplicateRevisionId = dto.DuplicateRevisionId,
            DuplicateSpecCode = dto.DuplicateSpecCode,
            ParsedJson = parsedJson,
        };
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
