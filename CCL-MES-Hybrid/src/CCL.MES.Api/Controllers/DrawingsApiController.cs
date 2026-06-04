using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Application.Storage;
using CCL.MES.Domain;
using CCL.MES.Infrastructure.Storage;
using CCL.MES.Shared;
using CCL.MES.Shared.Drawings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// P10.5e-1 — Drawings upload + download surface for the MAUI Hybrid
/// client. 1:1 forwards to the legacy
/// <see cref="CCL.MES.Application.Services.DrawingsService"/> — does
/// NOT re-implement the upload pipeline. The legacy already enforces:
///   - Role gate (Admin / Engineer) inside UploadAsync
///   - 6 security guards (extension allowlist + size cap + path-segment
///     regex + containment + atomic write + sha256) in
///     <see cref="FilesystemBlobStore"/>
///   - Audit emit (<c>DRAWING_UPLOAD</c>)
///   - Atomic transaction wrapping Drawing-find-or-create +
///     DrawingVersion + 3 DrawingApproval rows + DrawingsService
///     supersede semantics
///
/// New surface this PR adds:
///   - <c>POST /api/v2/specs/{revisionId}/drawings/upload?kind=…</c>
///     (multipart, NpiSpecWrite, RequestSizeLimit aligned with the
///      operator UX promise of 10 MB)
///   - <c>GET /api/v2/specs/{revisionId}/drawings/{versionId}/file</c>
///     (binary stream, NpiSpecRead, revision-scoped guard inherited
///      from <c>GetForDownloadAsync</c>)
///
/// W4 device-pairing audit (<c>DRAWING_UPLOAD_DEVICE</c>) emits when
/// <c>X-Device-Id</c> is on the request, mirroring the
/// <see cref="SpecsController"/> pattern. The legacy
/// <c>DRAWING_UPLOAD</c> row still emits internally from the
/// orchestrator.
///
/// Decide / 3-role approval surface lands in P10.5e-2.
/// </summary>
[ApiController]
[Route(ApiVersion.Prefix + "/specs/{revisionId:long}/drawings")]
public sealed class DrawingsApiController : ControllerBase
{
    private readonly DrawingsService _svc;
    private readonly IBlobStore _blobs;
    private readonly IAuditWriter _audit;
    private readonly BlobStoreOptions _blobOpts;

    /// <summary>API contract cap. Mirrors operator UX promise (10 MB)
    /// and the blob store's default <c>MaxBytes</c>. Multipart-form
    /// limits + RequestSizeLimit align so an oversize body short-
    /// circuits at the framework layer with a typed 422 instead of a
    /// generic 413.</summary>
    private const long UploadMaxBytes = 10 * 1024 * 1024;

    public DrawingsApiController(
        DrawingsService svc,
        IBlobStore blobs,
        IAuditWriter audit,
        IOptions<BlobStoreOptions> blobOpts)
    {
        _svc = svc;
        _blobs = blobs;
        _audit = audit;
        _blobOpts = blobOpts.Value;
    }

    // ── Upload ──────────────────────────────────────────────────────

    /// <summary>
    /// Multipart upload of a new <see cref="DrawingVersion"/> for the
    /// (revision, kind) pair. The legacy <see cref="DrawingsService.UploadAsync"/>
    /// handles find-or-create of the parent Drawing, version-number
    /// bumping, blob storage with 6 security guards, and audit emit.
    ///
    /// Validation chain at this layer:
    ///   1. File part present + non-empty (returns typed 422 instead of
    ///      framework BadRequest).
    ///   2. Size ≤ <see cref="UploadMaxBytes"/> (matches the blob store
    ///      cap so we never hand oversize content downstream).
    ///   3. Kind is a valid <see cref="DrawingKind"/> enum value.
    ///
    /// On <see cref="UnauthorizedAccessException"/> from the orchestrator
    /// (operator's role lacks edit rights), we return 403 — that matches
    /// the role-gate semantics from PR-D-5b. On
    /// <see cref="InvalidOperationException"/> we return 422 with a typed
    /// error code so the modal banner reads cleanly.
    /// </summary>
    [HttpPost("upload")]
    [Authorize(Policy = "NpiSpecWrite")]
    [RequestSizeLimit(UploadMaxBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = UploadMaxBytes + 64 * 1024)]
    public async Task<IActionResult> Upload(
        long revisionId,
        [FromQuery] string kind,
        IFormFile? file,
        [FromForm] string? changeReason,
        CancellationToken ct)
    {
        try
        {
            if (file is null || file.Length == 0)
                return UnprocessableEntity(new DrawingMutationError
                {
                    Code = "drawing.no_file",
                    Error = "Multipart 'file' part is missing or empty.",
                });

            if (file.Length > UploadMaxBytes)
                return UnprocessableEntity(new DrawingMutationError
                {
                    Code = "drawing.oversize",
                    Error = $"File size {file.Length} bytes exceeds the {UploadMaxBytes}-byte cap.",
                    MaxBytes = UploadMaxBytes,
                });

            if (!Enum.TryParse<DrawingKind>(kind, ignoreCase: true, out var parsedKind))
                return UnprocessableEntity(new DrawingMutationError
                {
                    Code = "drawing.invalid_kind",
                    Error = $"Unknown drawing kind '{kind}'.",
                });

            // Forward to the legacy orchestrator. Stream the file content
            // directly — IFormFile.OpenReadStream() is seekable for small
            // bodies (≤ 10 MB cap above) so the blob store can compute
            // sha256 + write in one pass.
            await using var content = file.OpenReadStream();
            DrawingUploadResult result;
            try
            {
                result = await _svc.UploadAsync(
                    revisionId: revisionId,
                    kind: parsedKind,
                    originalFileName: file.FileName,
                    contentType: file.ContentType ?? "application/octet-stream",
                    content: content,
                    changeReason: changeReason,
                    actor: ActorName(),
                    actorRole: ActorRole(),
                    ct: ct);
            }
            catch (UnauthorizedAccessException uaex)
            {
                return StatusCode(403, new DrawingMutationError
                {
                    Code = "drawing.forbidden",
                    Error = uaex.Message,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("vượt", StringComparison.OrdinalIgnoreCase)
                                                       || ex.Message.Contains("size", StringComparison.OrdinalIgnoreCase))
            {
                return UnprocessableEntity(new DrawingMutationError
                {
                    Code = "drawing.oversize",
                    Error = ex.Message,
                    MaxBytes = _blobOpts.MaxBytes,
                });
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("extension", StringComparison.OrdinalIgnoreCase)
                                                       || ex.Message.Contains("phần mở rộng", StringComparison.OrdinalIgnoreCase))
            {
                return UnprocessableEntity(new DrawingMutationError
                {
                    Code = "drawing.invalid_extension",
                    Error = ex.Message,
                    AllowedExtensions = string.Join(",", _blobOpts.AllowedExtensions),
                });
            }
            catch (InvalidOperationException ex)
            {
                return UnprocessableEntity(new DrawingMutationError
                {
                    Code = "drawing.validation",
                    Error = ex.Message,
                });
            }

            await EmitDeviceAuditIfHeaderPresent("DRAWING_UPLOAD_DEVICE", new
            {
                revision_id = revisionId,
                drawing_id = result.DrawingId,
                version_id = result.VersionId,
                version_no = result.VersionNo,
                kind = parsedKind.ToString(),
                filename = file.FileName,
                sha256_short = result.Sha256Hex[..8],
                size_bytes = result.SizeBytes,
            });

            return Ok(new DrawingUploadResponse
            {
                DrawingId = result.DrawingId,
                VersionId = result.VersionId,
                VersionNo = result.VersionNo,
                Kind = parsedKind.ToString(),
                FileName = file.FileName,
                Sha256Hex = result.Sha256Hex,
                SizeBytes = result.SizeBytes,
            });
        }
        catch (Exception ex)
        {
            return Problem(title: "Drawing upload failed", detail: ex.Message, statusCode: 500);
        }
    }

    // ── Download ────────────────────────────────────────────────────

    /// <summary>
    /// Stream a drawing version blob back to the client. Mirrors the
    /// legacy <c>DrawingsController.Download</c> (Phase 8 PR-D-5b) at
    /// <c>src/CCL.MES.Web/Controllers/DrawingsController.cs</c> — same
    /// revision-scoped guard (<see cref="DrawingsService.GetForDownloadAsync"/>
    /// returns null when the version's parent drawing belongs to a
    /// different revision), same MIME-map, same inline content
    /// disposition.
    ///
    /// Read-only — the legacy <c>NpiSpecRead</c> policy gates access
    /// (Admin / Supervisor / Engineer); QC is intentionally excluded
    /// to keep the read surface aligned with the rest of the Spec
    /// detail page.
    /// </summary>
    [HttpGet("{versionId:long}/file")]
    [Authorize(Policy = "NpiSpecRead")]
    public async Task<IActionResult> Download(
        long revisionId,
        long versionId,
        CancellationToken ct)
    {
        try
        {
            var info = await _svc.GetForDownloadAsync(versionId, revisionId, ct);
            if (info is null) return NotFound(new DrawingMutationError
            {
                Code = "drawing.not_found",
                Error = $"Drawing version {versionId} not found for revision {revisionId}.",
            });

            Stream stream;
            try
            {
                stream = await _blobs.GetAsync(info.StorageKey, ct);
            }
            catch (FileNotFoundException)
            {
                return NotFound(new DrawingMutationError
                {
                    Code = "drawing.blob_missing",
                    Error = $"Stored blob for version {versionId} is no longer on disk.",
                });
            }

            var mime = ResolveMime(info.FileName);
            // Inline disposition mirrors the legacy controller — image/pdf
            // preview happens in the browser/Catalyst WebView; non-preview
            // types still download cleanly because the client side reads
            // bytes regardless of disposition.
            return File(stream, mime, fileDownloadName: info.FileName, enableRangeProcessing: true);
        }
        catch (Exception ex)
        {
            return Problem(title: "Drawing download failed", detail: ex.Message, statusCode: 500);
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

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

    /// <summary>Same MIME table the legacy web controller uses. Kept
    /// in-controller (no shared helper) because the 10-entry list is
    /// short + matches the blob-store extension allowlist exactly.</summary>
    private static string ResolveMime(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "pdf"  => "application/pdf",
            "png"  => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "svg"  => "image/svg+xml",
            "gif"  => "image/gif",
            "webp" => "image/webp",
            "dwg"  => "image/vnd.dwg",
            "dxf"  => "image/vnd.dxf",
            "ai"   => "application/postscript",
            _      => "application/octet-stream",
        };
    }
}
