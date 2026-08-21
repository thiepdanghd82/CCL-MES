using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Api.Policies;
using CCL.MES.Application.Storage;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Shared;
using CCL.MES.Shared.Envelopes;
using CCL.MES.Shared.WoQcReview;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Controllers;

/// <summary>
/// A2 thin-controller lát 8 — QC photo evidence write/read surface,
/// extracted VERBATIM from <see cref="WoQcReviewController"/> so the review
/// controller stays lean. Shares the WoQc mutation infrastructure
/// (prelude / conflict / kind normalise / check materialise) via
/// <see cref="WoQcMutationControllerBase"/>. Route prefix + endpoint
/// templates are IDENTICAL to the original — byte-identical behaviour,
/// error codes, audit detail, and ETag headers.
///
/// Endpoints:
///   POST   /api/v2/work-orders/{id}/qc/{kind}/items/{itemKey}/photos                       Upload one JPEG/PNG
///   GET    /api/v2/work-orders/{id}/qc/{kind}/items/{itemKey}/photos                       List photo metadata
///   GET    /api/v2/work-orders/{id}/qc/{kind}/items/{itemKey}/photos/{photoId}/content     Stream one blob
///   DELETE /api/v2/work-orders/{id}/qc/{kind}/items/{itemKey}/photos/{photoId}             Delete one photo
/// </summary>
[ApiController]
[Route(ApiVersion.Prefix + "/work-orders")]
public sealed class WoQcPhotoController : WoQcMutationControllerBase
{
    private readonly IBlobStore _blobs;

    public WoQcPhotoController(
        IMesDbContext db,
        IAuditWriter audit,
        IBlobStore blobs)
        : base(db, audit)
    {
        _blobs = blobs;
    }

    // ═══════════════════════════════════════════════════════════════
    // POST — photo upload (Q6 file-picker; camera defers to 7f)
    // ═══════════════════════════════════════════════════════════════

    /// <summary>P10.7e-3 Q6 — upload a single JPEG/PNG photo as evidence
    /// for one QC check item. Multipart form with single "file" field;
    /// 5 MiB cap; "image/jpeg" + "image/png" only. The same If-Match +
    /// Idempotency-Key prelude as the mutation routes — uploading evidence
    /// is a state mutation that bumps RowVersion via the SQLite trigger.
    /// Audit: WoQcPhotoAdd carries SHA-256 + size + item key so the
    /// auditor can prove the on-disk file matches what the controller stamped.
    /// </summary>
    [HttpPost("{id:long}/qc/{kind}/items/{itemKey}/photos"), Authorize(Policy = "QcEdit")]
    [RequestSizeLimit(6 * 1024 * 1024)]  // 5 MiB photo + ~1 MiB headers + form overhead
    public async Task<IActionResult> PostPhoto(
        long id, string kind, string itemKey, IFormFile? file, CancellationToken ct = default)
    {
        var normKind = NormaliseKind(kind);
        if (normKind is null)
            return Invalid("qc.invalid_kind", $"Kind must be \"fqc\" or \"oqc\"; got \"{kind}\".");
        if (string.IsNullOrWhiteSpace(itemKey) || itemKey.Length > 64)
            return Invalid("qc.invalid_item_key", "ItemKey must be 1-64 chars.");

        if (file is null || file.Length == 0)
            return Invalid("qc.invalid_photo", "Photo file is required.");
        if (file.Length > 5 * 1024 * 1024)
            return Invalid("qc.photo_too_large",
                $"Photo size {file.Length} exceeds 5 MiB cap.");

        var mime = (file.ContentType ?? "").ToLowerInvariant().Trim();
        if (mime != "image/jpeg" && mime != "image/png")
            return Invalid("qc.invalid_photo_mime",
                $"MIME must be image/jpeg or image/png; got \"{file.ContentType}\".");

        var actor = ActorName();
        var role = ActorRole();
        var pre = await PreludeAsync(id, actor, role, $"qc_{normKind.ToLowerInvariant()}_add_photo");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        var expectedPhase = WoQcJudgmentPolicy.ExpectedPhaseForKind(normKind);
        if (wo.MesPhase != expectedPhase)
            return Invalid("wo.invalid_phase",
                $"qc/{normKind}/photos requires MesPhase = {expectedPhase}; current = {wo.MesPhase}.");

        var check = await GetOrCreateCheckAsync(id, normKind, wo.ProductId);
        var item = check.Items.FirstOrDefault(i => i.ItemKey == itemKey);
        if (item is null)
        {
            // Lazy materialise the item row (operator may upload before
            // tapping Ok/Ng — order isn't enforced).
            item = new WoQcCheckItem
            {
                WoQcCheckId = check.Id,
                ItemKey = itemKey,
                Status = IpqcCheckStatus.Pending,
            };
            check.Items.Add(item);
            await _db.SaveChangesAsync(ct);
        }

        // Stream to blob store + capture SHA-256 + size from BlobPutResult
        // (single-pass write — see FilesystemBlobStore docs).
        var ext = mime == "image/png" ? ".png" : ".jpg";
        var safeFileName = SanitiseFileName(file.FileName) ?? ("photo" + ext);
        var suggestedKey = $"wo-qc-photos/{id}/{normKind.ToLowerInvariant()}/{itemKey}/{Guid.NewGuid():N}{ext}";
        BlobPutResult blobResult;
        await using (var stream = file.OpenReadStream())
        {
            blobResult = await _blobs.PutAsync(stream, suggestedKey, mime, ct);
        }

        var now = DateTime.UtcNow;
        var photo = new WoQcPhoto
        {
            WoQcCheckItemId = item.Id,
            Sha256 = blobResult.Sha256Hex,
            MimeType = mime,
            SizeBytes = blobResult.SizeBytes,
            OriginalFileName = safeFileName,
            RelativePath = blobResult.Key,
            UploadedBy = actor,
            UploadedAt = now,
        };
        _db.WoQcPhotos.Add(photo);
        // Touch parent so the trigger bumps RowVersion + ETag changes.
        wo.UpdatedAt = now;
        wo.UpdatedBy = actor;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Race lost on WO row — operator must retry.
            return await HandleWoStateConflictAsync(id, actor, role, "qc.photo", ct);
        }

        // Re-read for the trigger-bumped RowVersion + emit audit + response.
        var freshWo = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new { w.RowVersion, w.MesPhase })
            .SingleAsync(ct);
        var newEtag = Convert.ToBase64String(freshWo.RowVersion);

        await _audit.EmitAsync(
            action: AuditAction.WoQcPhotoAdd,
            actor: actor,
            actorRole: role,
            targetType: "WorkOrder",
            targetId: id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                wo_id = id,
                wo_no = wo.WoNo,
                kind = normKind,
                item_key = itemKey,
                photo_id = photo.Id,
                sha256 = photo.Sha256,
                size_bytes = photo.SizeBytes,
                mime = photo.MimeType,
                file_name = photo.OriginalFileName,
            }));

        Response.Headers.ETag = $"\"{newEtag}\"";
        return Ok(new WoQcPhotoUploadResponse
        {
            Ok = true,
            ETag = newEtag,
            MesPhase = freshWo.MesPhase ?? "",
            Photo = new WoQcPhotoDto
            {
                Id = photo.Id,
                WoQcCheckItemId = photo.WoQcCheckItemId,
                Sha256 = photo.Sha256,
                MimeType = photo.MimeType,
                SizeBytes = photo.SizeBytes,
                OriginalFileName = photo.OriginalFileName,
                UploadedBy = photo.UploadedBy,
                UploadedAt = photo.UploadedAt,
            },
        });
    }

    /// <summary>P10.7e-3 — list metadata for all photos on a single item.
    /// Drives the FQC/OQC dashboard thumbnail strip. Read-only — no
    /// If-Match / Idempotency-Key requirement.</summary>
    [HttpGet("{id:long}/qc/{kind}/items/{itemKey}/photos"), Authorize(Policy = "QcRead")]
    public async Task<IActionResult> GetPhotos(
        long id, string kind, string itemKey, CancellationToken ct = default)
    {
        var normKind = NormaliseKind(kind);
        if (normKind is null)
            return UnprocessableEntity(ApiError.Of("qc.invalid_kind",
                $"Kind must be \"fqc\" or \"oqc\"; got \"{kind}\"."));

        var check = await _db.WoQcChecks.AsNoTracking()
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.WorkOrderId == id && c.QcKind == normKind, ct);
        if (check is null) return Ok(Array.Empty<WoQcPhotoDto>());

        var item = check.Items.FirstOrDefault(i => i.ItemKey == itemKey);
        if (item is null) return Ok(Array.Empty<WoQcPhotoDto>());

        var rows = await _db.WoQcPhotos.AsNoTracking()
            .Where(p => p.WoQcCheckItemId == item.Id)
            .OrderBy(p => p.Id)
            .Select(p => new WoQcPhotoDto
            {
                Id = p.Id,
                WoQcCheckItemId = p.WoQcCheckItemId,
                Sha256 = p.Sha256,
                MimeType = p.MimeType,
                SizeBytes = p.SizeBytes,
                OriginalFileName = p.OriginalFileName,
                UploadedBy = p.UploadedBy,
                UploadedAt = p.UploadedAt,
            })
            .ToListAsync(ct);
        return Ok(rows);
    }

    /// <summary>P10.7e-3 — stream a single photo blob. Caller must have
    /// QcRead. Returns 404 if the photo is missing OR if it doesn't belong
    /// to the WO+kind+item triple in the URL (path-traversal protection).</summary>
    [HttpGet("{id:long}/qc/{kind}/items/{itemKey}/photos/{photoId:long}/content"), Authorize(Policy = "QcRead")]
    public async Task<IActionResult> GetPhotoContent(
        long id, string kind, string itemKey, long photoId, CancellationToken ct = default)
    {
        var normKind = NormaliseKind(kind);
        if (normKind is null)
            return UnprocessableEntity(ApiError.Of("qc.invalid_kind",
                $"Kind must be \"fqc\" or \"oqc\"; got \"{kind}\"."));

        var photo = await (
            from p in _db.WoQcPhotos.AsNoTracking()
            join i in _db.WoQcCheckItems.AsNoTracking() on p.WoQcCheckItemId equals i.Id
            join c in _db.WoQcChecks.AsNoTracking() on i.WoQcCheckId equals c.Id
            where p.Id == photoId
                && c.WorkOrderId == id
                && c.QcKind == normKind
                && i.ItemKey == itemKey
            select new { p.RelativePath, p.MimeType, p.OriginalFileName }
        ).FirstOrDefaultAsync(ct);

        if (photo is null || string.IsNullOrEmpty(photo.RelativePath))
            return NotFound(ApiError.Of("qc.photo_not_found",
                $"Photo {photoId} not found on item {itemKey}."));

        var stream = await _blobs.GetAsync(photo.RelativePath, ct);
        return File(stream, photo.MimeType, photo.OriginalFileName);
    }

    /// <summary>P10.7e-3 — delete a single photo. If-Match required;
    /// audit WoQcPhotoDelete; blob delete best-effort (catalogue row
    /// is authoritative).</summary>
    [HttpDelete("{id:long}/qc/{kind}/items/{itemKey}/photos/{photoId:long}"), Authorize(Policy = "QcEdit")]
    public async Task<IActionResult> DeletePhoto(
        long id, string kind, string itemKey, long photoId, CancellationToken ct = default)
    {
        var normKind = NormaliseKind(kind);
        if (normKind is null)
            return Invalid("qc.invalid_kind", $"Kind must be \"fqc\" or \"oqc\"; got \"{kind}\".");

        var actor = ActorName();
        var role = ActorRole();
        var pre = await PreludeAsync(id, actor, role, $"qc_{normKind.ToLowerInvariant()}_delete_photo");
        if (pre.Error is not null) return pre.Error;
        var wo = pre.WoForUpdate!;

        var expectedPhase = WoQcJudgmentPolicy.ExpectedPhaseForKind(normKind);
        if (wo.MesPhase != expectedPhase)
            return Invalid("wo.invalid_phase",
                $"qc/{normKind}/photos requires MesPhase = {expectedPhase}; current = {wo.MesPhase}.");

        var photo = await (
            from p in _db.WoQcPhotos
            join i in _db.WoQcCheckItems.AsNoTracking() on p.WoQcCheckItemId equals i.Id
            join c in _db.WoQcChecks.AsNoTracking() on i.WoQcCheckId equals c.Id
            where p.Id == photoId
                && c.WorkOrderId == id
                && c.QcKind == normKind
                && i.ItemKey == itemKey
            select p
        ).FirstOrDefaultAsync(ct);

        if (photo is null)
            return NotFound(ApiError.Of("qc.photo_not_found",
                $"Photo {photoId} not found on item {itemKey}."));

        var deletedSha = photo.Sha256;
        var deletedKey = photo.RelativePath;
        _db.WoQcPhotos.Remove(photo);
        wo.UpdatedAt = DateTime.UtcNow;
        wo.UpdatedBy = actor;
        await _db.SaveChangesAsync(ct);

        // Blob delete is best-effort — catalogue row already gone.
        if (!string.IsNullOrEmpty(deletedKey))
        {
            try { await _blobs.DeleteAsync(deletedKey, ct); }
            catch { /* orphan blob is acceptable; audit + DB-row are authoritative */ }
        }

        var freshWo = await _db.WorkOrders.AsNoTracking()
            .Where(w => w.Id == id)
            .Select(w => new { w.RowVersion, w.MesPhase })
            .SingleAsync(ct);
        var newEtag = Convert.ToBase64String(freshWo.RowVersion);

        await _audit.EmitAsync(
            action: AuditAction.WoQcPhotoDelete,
            actor: actor,
            actorRole: role,
            targetType: "WorkOrder",
            targetId: id.ToString(),
            detail: JsonSerializer.Serialize(new
            {
                wo_id = id,
                wo_no = wo.WoNo,
                kind = normKind,
                item_key = itemKey,
                photo_id = photoId,
                sha256 = deletedSha,
            }));

        Response.Headers.ETag = $"\"{newEtag}\"";
        return Ok(new WoQcSetResponse
        {
            Ok = true,
            ETag = newEtag,
            MesPhase = freshWo.MesPhase ?? "",
        });
    }

    private static string? SanitiseFileName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = Path.GetFileName(raw.Trim());
        if (string.IsNullOrEmpty(trimmed)) return null;
        // Strip path-traversal bytes + odd unicode that breaks Content-Disposition.
        var safe = new string(trimmed.Where(c =>
            c >= 0x20 && c != '"' && c != '/' && c != '\\').ToArray());
        return safe.Length == 0 ? null : (safe.Length > 200 ? safe[..200] : safe);
    }
}
