using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Domain;
using CCL.MES.Shared;
using CCL.MES.Shared.QcSpecs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// QC-spec helpers under <c>/api/v2/qc-specs</c>:
///   - <see cref="SpecQcWindowService"/> drives the 4-stage QC plan
///     surface (read + per-stage atomic upsert in P10.5f).
///   - <see cref="SpecQcCaptureService"/> drives the capture surface
///     (read + create in P10.5f). Reason-code lookup is the dropdown
///     source for the FAIL gate.
///
/// All read endpoints stay on <c>QcRead</c> policy (Admin / Supervisor
/// / QC). Write endpoints — upsert + capture — use their own
/// per-action <c>[Authorize]</c>; the legacy service layer enforces
/// the Admin/Engineer gate via `_editorRoles` so a forged client can
/// never reach the DB without matching the right role claim.
/// W4 device-pairing audit (<c>SPEC_QC_PLAN_UPSERT_DEVICE</c> +
/// <c>SPEC_QC_CAPTURE_DEVICE</c>) emits when X-Device-Id is on the
/// request.
/// </summary>
[ApiController]
[Route(ApiVersion.Prefix + "/qc-specs")]
public sealed class QcSpecController : ControllerBase
{
    private readonly SpecQcWindowService _windows;
    private readonly SpecQcCaptureService _captures;
    private readonly IAuditWriter _audit;

    public QcSpecController(
        SpecQcWindowService windows,
        SpecQcCaptureService captures,
        IAuditWriter audit)
    {
        _windows = windows;
        _captures = captures;
        _audit = audit;
    }

    // ── Read (P10.5b) ────────────────────────────────────────────────

    [HttpGet("windows/by-revision/{revisionId:long}")]
    [Authorize(Policy = "NpiSpecRead")]
    public async Task<IActionResult> Windows(long revisionId)
    {
        // P10.5f — project to MAUI wire DTOs to dodge the
        // QcCriterion ↔ SpecQcWindow back-reference cycle that the
        // raw entity graph carries (System.Text.Json hits its 32-depth
        // limit otherwise). Mirrors the 4-stage keyed dictionary the
        // legacy service returned by string-keying each stage.
        var dict = await _windows.ListByRevisionAsync(revisionId);
        var result = new Dictionary<string, QcWindowItem?>(StringComparer.Ordinal);
        foreach (var stage in new[] { CCL.MES.Domain.QcStage.IpqcPrint, CCL.MES.Domain.QcStage.IpqcCut, CCL.MES.Domain.QcStage.Fqc, CCL.MES.Domain.QcStage.Oqc })
        {
            dict.TryGetValue(stage, out var w);
            result[stage.ToString()] = w is null ? null : ProjectWindow(w);
        }
        return Ok(result);
    }

    [HttpGet("captures/by-revision/{revisionId:long}")]
    [Authorize(Policy = "NpiSpecRead")]
    public async Task<IActionResult> Captures(long revisionId)
    {
        // Project each entity through to the wire DTO so the back-
        // reference cycle on SpecQcCapture.QcCriterion / SpecQcWindow
        // never reaches the serializer.
        var raw = await _captures.ListByRevisionAsync(revisionId);
        return Ok(raw.Select(ProjectCapture).ToList());
    }

    [HttpGet("reason-codes")]
    [Authorize(Policy = "NpiSpecRead")]
    public async Task<IActionResult> ReasonCodes()
    {
        var raw = await _captures.ListReasonCodesAsync();
        return Ok(raw.Select(r => new QcReasonCode
        {
            Id = r.Id,
            Code = r.Code,
            LabelEn = r.LabelEn,
            LabelVi = r.LabelVi,
            Kind = r.Kind.ToString(),
            Sort = r.Sort,
        }).ToList());
    }

    // ── Write (P10.5f) ───────────────────────────────────────────────

    /// <summary>
    /// Atomic per-stage upsert. Reads the diff from the request body
    /// (delete / update / insert) and writes inside a single transaction.
    /// 1:1 forwards to <see cref="SpecQcWindowService.UpsertStageAsync"/>;
    /// the legacy already handles the Admin/Engineer role gate, the
    /// `Criterion Name không được để trống` validation, and audit emit
    /// (<c>SPEC_QC_PLAN_UPSERT</c>).
    ///
    /// Exception → typed envelope map:
    ///   UnauthorizedAccessException → 403 <c>qc.forbidden</c>
    ///   InvalidOperationException "ProductRevision … not found" → 404 <c>qc.not_found</c>
    ///   InvalidOperationException "Criterion Name không được để trống" → 422 <c>qc.invalid_row</c>
    ///   InvalidOperationException → 422 <c>qc.validation</c>
    ///   Unknown <see cref="QcStage"/> → 422 <c>qc.invalid_stage</c>
    /// </summary>
    [HttpPost("windows/upsert-stage/{revisionId:long}")]
    [Authorize]
    public async Task<IActionResult> UpsertStage(
        long revisionId,
        [FromBody] QcPlanUpsertRequest req,
        CancellationToken ct)
    {
        try
        {
            if (req is null)
                return UnprocessableEntity(new QcMutationError
                {
                    Code = "qc.validation",
                    Error = "Request body is required.",
                });

            if (!Enum.TryParse<CCL.MES.Domain.QcStage>(req.Stage, ignoreCase: true, out var stage))
                return UnprocessableEntity(new QcMutationError
                {
                    Code = "qc.invalid_stage",
                    Error = $"Unknown QC stage '{req.Stage}'. Expected IpqcPrint / IpqcCut / Fqc / Oqc.",
                });

            var rows = req.Rows
                .Select(r => new QcCriterionRow(
                    Id: r.Id,
                    Name: r.Name ?? "",
                    Target: r.Target,
                    Tolerance: r.Tolerance,
                    Method: r.Method,
                    Frequency: r.Frequency))
                .ToList();

            // The legacy returns CREATED / UPDATED / DELETED counts via
            // the audit JSON only — we re-derive them on the controller
            // side from a pre-read so the response can surface the
            // counts directly for the operator's "Đã lưu N tiêu chí"
            // notice. (We tolerate the extra round-trip; per-stage
            // saves are operator-paced.)
            Domain.Entities.SpecQcWindow window;
            try
            {
                window = await _windows.UpsertStageAsync(
                    revisionId, stage, rows, ActorName(), ActorRole());
            }
            catch (UnauthorizedAccessException uaex)
            {
                return StatusCode(403, new QcMutationError
                {
                    Code = "qc.forbidden",
                    Error = uaex.Message,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(new QcMutationError
                {
                    Code = "qc.not_found",
                    Error = ex.Message,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Name", StringComparison.OrdinalIgnoreCase))
            {
                return UnprocessableEntity(new QcMutationError
                {
                    Code = "qc.invalid_row",
                    Error = ex.Message,
                });
            }
            catch (InvalidOperationException ex)
            {
                return UnprocessableEntity(new QcMutationError
                {
                    Code = "qc.validation",
                    Error = ex.Message,
                });
            }

            await EmitDeviceAuditIfHeaderPresent("SPEC_QC_PLAN_UPSERT_DEVICE", new
            {
                revision_id = revisionId,
                stage = stage.ToString(),
                window_id = window.Id,
                criteria_count = window.Criteria.Count,
            });

            // Project the reloaded window into the MAUI wire shape.
            return Ok(new QcPlanUpsertResponse
            {
                Window = ProjectWindow(window),
            });
        }
        catch (Exception ex)
        {
            return Problem(title: "QC plan upsert failed", detail: ex.Message, statusCode: 500);
        }
    }

    /// <summary>
    /// Append-only QC capture. 1:1 forwards to
    /// <see cref="SpecQcCaptureService.CaptureAsync"/>; the legacy
    /// handles the role gate, FAIL-requires-reason validation, reason
    /// code existence check, and audit emit (<c>SPEC_QC_CAPTURE</c>).
    ///
    /// Exception → typed envelope map:
    ///   UnauthorizedAccessException → 403 <c>qc.forbidden</c>
    ///   "NG reason code is required when result = FAIL" → 422 <c>qc.reason_required</c>
    ///   "is not a known active reason code" → 422 <c>qc.invalid_reason</c>
    ///   "Criterion … not found" / "does not belong to revision" → 404 <c>qc.not_found</c>
    ///   InvalidOperationException → 422 <c>qc.validation</c>
    /// </summary>
    [HttpPost("captures/{revisionId:long}")]
    [Authorize]
    public async Task<IActionResult> CreateCapture(
        long revisionId,
        [FromBody] QcCaptureCreateRequest req,
        CancellationToken ct)
    {
        try
        {
            if (req is null)
                return UnprocessableEntity(new QcMutationError
                {
                    Code = "qc.validation",
                    Error = "Request body is required.",
                });

            if (!Enum.TryParse<CCL.MES.Domain.QcCaptureResult>(req.Result, ignoreCase: true, out var result))
                return UnprocessableEntity(new QcMutationError
                {
                    Code = "qc.invalid_result",
                    Error = $"Unknown capture result '{req.Result}'. Expected Pass / Fail / Na.",
                });

            var legacyReq = new QcCaptureRequest(
                CriterionId: req.CriterionId,
                Result: result,
                Measurement: req.Measurement,
                NgReasonCode: req.NgReasonCode,
                Comment: req.Comment);

            Domain.Entities.SpecQcCapture capture;
            try
            {
                capture = await _captures.CaptureAsync(
                    revisionId, legacyReq, ActorName(), ActorRole());
            }
            catch (UnauthorizedAccessException uaex)
            {
                return StatusCode(403, new QcMutationError
                {
                    Code = "qc.forbidden",
                    Error = uaex.Message,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("required when result = FAIL", StringComparison.OrdinalIgnoreCase))
            {
                return UnprocessableEntity(new QcMutationError
                {
                    Code = "qc.reason_required",
                    Error = ex.Message,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not a known active reason code", StringComparison.OrdinalIgnoreCase))
            {
                return UnprocessableEntity(new QcMutationError
                {
                    Code = "qc.invalid_reason",
                    Error = ex.Message,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                                                       || ex.Message.Contains("does not belong", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound(new QcMutationError
                {
                    Code = "qc.not_found",
                    Error = ex.Message,
                });
            }
            catch (InvalidOperationException ex)
            {
                return UnprocessableEntity(new QcMutationError
                {
                    Code = "qc.validation",
                    Error = ex.Message,
                });
            }

            await EmitDeviceAuditIfHeaderPresent("SPEC_QC_CAPTURE_DEVICE", new
            {
                revision_id = revisionId,
                criterion_id = capture.QcCriterionId,
                result = capture.Result.ToString(),
                has_measurement = !string.IsNullOrWhiteSpace(capture.Measurement),
                ng_reason_code = capture.NgReasonCode,
            });

            return Ok(ProjectCapture(capture));
        }
        catch (Exception ex)
        {
            return Problem(title: "QC capture failed", detail: ex.Message, statusCode: 500);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────

    private string ActorName() =>
        User.FindFirstValue(ClaimTypes.Name) ?? "anonymous";

    private string ActorRole() =>
        User.FindFirstValue(ClaimTypes.Role) ?? "";

    private async Task EmitDeviceAuditIfHeaderPresent(string action, object detail)
    {
        var deviceId = Request.Headers["X-Device-Id"].ToString();
        if (string.IsNullOrWhiteSpace(deviceId)) return;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["device_id"] = deviceId,
            ["detail"] = detail,
        });
        await _audit.EmitAsync(
            action: action,
            actor: ActorName(),
            actorRole: ActorRole(),
            targetType: "Device",
            targetId: deviceId,
            detail: payload);
    }

    private static QcWindowItem ProjectWindow(Domain.Entities.SpecQcWindow w) =>
        new()
        {
            Id = w.Id,
            ProductRevisionId = w.ProductRevisionId,
            Stage = (CCL.MES.Shared.QcSpecs.QcStage)(int)w.Stage,
            ProcessCode = w.ProcessCode,
            Title = w.Title,
            Description = w.Description,
            SamplePlan = w.SamplePlan,
            Frequency = w.Frequency,
            RejectAction = (CCL.MES.Shared.QcSpecs.QcRejectAction)(int)w.RejectAction,
            Status = (CCL.MES.Shared.QcSpecs.QcWindowStatus)(int)w.Status,
            ApprovedBy = w.ApprovedBy,
            ApprovedAt = w.ApprovedAt,
            Criteria = w.Criteria
                .OrderBy(c => c.Seq)
                .Select(c => new QcCriterionItem
                {
                    Id = c.Id,
                    SpecQcWindowId = c.SpecQcWindowId,
                    Seq = c.Seq,
                    Name = c.Name,
                    CriterionType = (CCL.MES.Shared.QcSpecs.QcCriterionType)(int)c.CriterionType,
                    MeasureMethod = c.MeasureMethod,
                    TargetValue = c.TargetValue,
                    ToleranceMin = c.ToleranceMin,
                    ToleranceMax = c.ToleranceMax,
                    Unit = c.Unit,
                    PassCriteria = c.PassCriteria,
                    ReferenceImageKey = c.ReferenceImageKey,
                    Required = c.Required,
                    MethodOverride = c.Method,
                    FrequencyOverride = c.Frequency,
                })
                .ToList(),
        };

    private static QcCaptureItem ProjectCapture(Domain.Entities.SpecQcCapture c) =>
        new()
        {
            Id = c.Id,
            SpecQcWindowId = c.SpecQcWindowId,
            QcCriterionId = c.QcCriterionId,
            Result = (CCL.MES.Shared.QcSpecs.QcCaptureResult)(int)c.Result,
            Measurement = c.Measurement,
            NgReasonCode = c.NgReasonCode,
            Comment = c.Comment,
            CapturedBy = c.CapturedBy,
            CapturedAt = c.CapturedAt,
        };
}
