using System.Security.Claims;
using CCL.MES.Application.Services;
using CCL.MES.Application.Storage;
using CCL.MES.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CCL.MES.Web.Controllers;

/// <summary>
/// Phase 8 PR-D-5b — Drawing blob download endpoint.
///
/// Route: <c>GET /api/specs/{revisionId:long}/drawings/{versionId:long}/file</c>
///   - <b>Path-segment only</b> per Lesson #33 — NO dot-extension in route
///     template. Content-Type + Content-Disposition response headers carry
///     the original filename + MIME so browsers download correctly.
///   - <b>Revision-scoped</b> — versionId resolved via
///     <see cref="DrawingsService.GetForDownloadAsync"/> which verifies the
///     version's parent drawing belongs to the supplied
///     <paramref name="revisionId"/>. Defends against forged links that
///     attempt to read a drawing under a revision the user didn't open.
///
/// RBAC: <c>[Authorize(Roles = "Admin,Supervisor,Engineer")]</c> mirrors
/// the NpiSpecRead policy (same role set; matrix per Program.cs:195).
/// Pattern matches <see cref="SpecsExportController"/> — Roles attribute
/// over Policy so API auth returns 401/403 instead of SPA cookie-auth
/// redirect to <c>_Host.cshtml</c>.
/// </summary>
[ApiController]
[Route("api/specs/{revisionId:long}/drawings")]
[Authorize(Roles = "Admin,Supervisor,Engineer")]
public class DrawingsController : ControllerBase
{
    // Whitelisted extension → MIME map. Mirrors the BlobStoreOptions allowlist
    // — every allowed extension MUST resolve to a sensible Content-Type or
    // we emit application/octet-stream as a safe fallback (browser still
    // downloads via Content-Disposition).
    private static readonly Dictionary<string, string> _mime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pdf"]  = "application/pdf",
        ["png"]  = "image/png",
        ["jpg"]  = "image/jpeg",
        ["jpeg"] = "image/jpeg",
        ["svg"]  = "image/svg+xml",
        ["gif"]  = "image/gif",
        ["webp"] = "image/webp",
        ["dwg"]  = "application/acad",
        ["dxf"]  = "application/dxf",
        ["ai"]   = "application/postscript",
    };

    private readonly DrawingsService _svc;
    private readonly IBlobStore _blobs;

    public DrawingsController(DrawingsService svc, IBlobStore blobs)
    {
        _svc = svc;
        _blobs = blobs;
    }

    [HttpGet("{versionId:long}/file")]
    public async Task<IActionResult> Download(long revisionId, long versionId, CancellationToken ct)
    {
        var info = await _svc.GetForDownloadAsync(versionId, revisionId, ct);
        if (info is null)
        {
            return NotFound(new { error = "drawing_version_not_found" });
        }

        Stream stream;
        try
        {
            stream = await _blobs.GetAsync(info.StorageKey, ct);
        }
        catch (FileNotFoundException)
        {
            // Blob file missing on disk while DB row exists — orphan. Surface
            // a distinct error so an operator/admin can investigate.
            return NotFound(new { error = "blob_missing", info.StorageKey });
        }
        catch (InvalidOperationException ex)
        {
            // Stored key failed regex/containment check — DB row was tampered
            // with. Treat as 404 to the caller; log via Audit later if needed.
            return NotFound(new { error = "blob_key_invalid", message = ex.Message });
        }

        var ext = Path.GetExtension(info.FileName).TrimStart('.').ToLowerInvariant();
        var mime = _mime.TryGetValue(ext, out var m) ? m : "application/octet-stream";

        // Inline disposition lets browsers preview PDF / images in-tab;
        // operator still hits Cmd+S for explicit download. Original filename
        // included so a save reflects what was uploaded.
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{SanitizeFilename(info.FileName)}\"";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Cache-Control"] = "private, max-age=300";
        return File(stream, mime, fileDownloadName: null, enableRangeProcessing: false);
    }

    private static string SanitizeFilename(string s)
    {
        // Strip quote + CR/LF that could break the Content-Disposition header.
        // Operator-uploaded filenames are otherwise rendered verbatim so the
        // download dialog matches what they see in the card.
        var chars = s.Where(c => c != '"' && c != '\r' && c != '\n').ToArray();
        return new string(chars);
    }

    /// <summary>
    /// Phase 8 PR-D-5c — chip decide endpoint. Body shape:
    ///   { role: "Npi"|"Production"|"Qc",
    ///     decision: "Approved"|"Rejected",
    ///     comment: string? }
    /// Service enforces RBAC (Role + Department per Option (a)) +
    /// comment-required-on-Reject. Path is pure segments (no dot-extension
    /// per Lesson #33).
    /// </summary>
    [HttpPost("{versionId:long}/decide")]
    public async Task<IActionResult> Decide(
        long revisionId,
        long versionId,
        [FromBody] DecideRequest req,
        CancellationToken ct)
    {
        if (req is null) return BadRequest(new { error = "body_required" });
        if (!Enum.TryParse<DrawingApprovalRole>(req.Role, ignoreCase: true, out var role))
            return BadRequest(new { error = "invalid_role", req.Role });
        if (!Enum.TryParse<DrawingApprovalStatus>(req.Decision, ignoreCase: true, out var decision))
            return BadRequest(new { error = "invalid_decision", req.Decision });

        var actor = User.Identity?.Name ?? "anonymous";
        var actorRole = User.FindFirstValue(ClaimTypes.Role) ?? "";
        var actorDepartment = User.FindFirstValue("department");

        try
        {
            var result = await _svc.DecideAsync(
                revisionId, versionId, role, decision,
                req.Comment, actor, actorRole, actorDepartment, ct);
            return Ok(new
            {
                version_id          = result.VersionId,
                version_status      = result.VersionStatus.ToString(),
                drawing_status      = result.DrawingStatus.ToString(),
                superseded_count    = result.SupersededCount,
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "invalid_state", message = ex.Message });
        }
    }

    public sealed record DecideRequest(string Role, string Decision, string? Comment);
}
