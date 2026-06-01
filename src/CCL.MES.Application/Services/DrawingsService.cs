using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Storage;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// Phase 8 PR-D-5b — Drawing list / upload / download glue. Builds on the
/// PR-D-5a <see cref="IBlobStore"/> infrastructure (path scheme + 6 security
/// guards) and on the PR #28 <see cref="Drawing"/> + <see cref="DrawingVersion"/>
/// entity scaffold (no schema migration needed in PR-D-5b).
///
/// One-drawing-per-kind convention: <c>Title</c> defaults to the
/// <see cref="DrawingKind"/> string. Future PR can introduce multi-drawing
/// per kind (different artwork variants under one Customer Drawing slot)
/// by surfacing a Title field in the upload modal.
///
/// 3-role approval (PR-D-5c) NOT wired here. Newly-uploaded versions land in
/// <see cref="DrawingVersionStatus.Draft"/>; the UI renders a "Pending /
/// No-approval" pill until the approval flow ships.
///
/// RBAC: server-side <see cref="UploadAsync"/> rejects anyone not in
/// {Admin, Engineer}. Download is page/endpoint-level — NpiSpecRead policy
/// (Admin / Supervisor / Engineer) covers read; this service does NOT
/// re-check the role on download because the controller already gates it.
/// </summary>
public class DrawingsService
{
    private static readonly HashSet<string> _editorRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin",
        "Engineer",
    };

    private readonly IMesDbContext _db;
    private readonly IBlobStore _blobs;
    private readonly IAuditWriter _audit;

    public DrawingsService(IMesDbContext db, IBlobStore blobs, IAuditWriter audit)
    {
        _db = db;
        _blobs = blobs;
        _audit = audit;
    }

    /// <summary>
    /// Returns one entry per <see cref="DrawingKind"/> (9 entries total).
    /// Entry kind is present even when no drawing has been uploaded yet —
    /// the UI renders the "empty section" placeholder for those.
    /// </summary>
    public async Task<List<DrawingKindView>> ListByRevisionAsync(long revisionId)
    {
        var drawings = await _db.Drawings
            .AsNoTracking()
            .Where(d => d.ProductRevisionId == revisionId)
            .Include(d => d.Versions.OrderByDescending(v => v.VersionNo))
            .ToListAsync();

        // Group by kind. Each kind may have 0..1 Drawing in v1 (one-drawing-per-kind
        // convention). Future PR can lift to N.
        var byKind = drawings
            .GroupBy(d => d.Kind)
            .ToDictionary(g => g.Key, g => g.First()); // pick first if any future
                                                        // dup; deterministic-by-Id.

        var result = new List<DrawingKindView>(9);
        foreach (DrawingKind k in Enum.GetValues<DrawingKind>())
        {
            byKind.TryGetValue(k, out var dr);
            var versions = dr?.Versions
                .OrderByDescending(v => v.VersionNo)
                .Select(v => new DrawingVersionView(
                    v.Id,
                    v.VersionNo,
                    v.FileName,
                    v.FileHash,
                    v.FileSize,
                    v.Status,
                    v.ChangeReason,
                    v.UploadedAt,
                    v.UploadedBy))
                .ToList() ?? new List<DrawingVersionView>();
            result.Add(new DrawingKindView(
                Kind: k,
                DrawingId: dr?.Id,
                Title: dr?.Title ?? "",
                CurrentVersionId: dr?.CurrentVersionId,
                Versions: versions));
        }
        return result;
    }

    /// <summary>
    /// Atomic upload of one new <see cref="DrawingVersion"/> for the given
    /// (revision, kind). Find-or-creates the parent <see cref="Drawing"/>
    /// row, computes the next version number from the existing chain,
    /// streams the content through <see cref="IBlobStore.PutAsync"/>,
    /// persists metadata, advances <see cref="Drawing.CurrentVersionId"/>,
    /// emits the <see cref="AuditAction.DrawingUpload"/> audit row, and
    /// commits — all under a single EF transaction.
    /// </summary>
    public async Task<DrawingUploadResult> UploadAsync(
        long revisionId,
        DrawingKind kind,
        string originalFileName,
        string contentType,
        Stream content,
        string? changeReason,
        string actor,
        string actorRole,
        CancellationToken ct = default)
    {
        if (!_editorRoles.Contains(actorRole))
        {
            throw new UnauthorizedAccessException(
                $"Role '{actorRole}' không có quyền upload drawing (cần Admin hoặc Engineer).");
        }

        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new InvalidOperationException("originalFileName is required.");

        var ext = Path.GetExtension(originalFileName).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext))
            throw new InvalidOperationException("File must have an extension (e.g. .pdf, .png).");

        // Revision exists.
        var revisionExists = await _db.ProductRevisions
            .AsNoTracking()
            .AnyAsync(r => r.Id == revisionId, ct);
        if (!revisionExists)
            throw new InvalidOperationException($"ProductRevision {revisionId} not found.");

        var dbCtx = (DbContext)_db;
        await using var tx = await dbCtx.Database.BeginTransactionAsync(ct);
        try
        {
            // Find-or-create Drawing (one-per-kind convention; Title = kind name).
            var title = kind.ToString();
            var drawing = await _db.Drawings
                .Include(d => d.Versions)
                .FirstOrDefaultAsync(
                    d => d.ProductRevisionId == revisionId && d.Kind == kind && d.Title == title,
                    ct);
            if (drawing is null)
            {
                drawing = new Drawing
                {
                    ProductRevisionId = revisionId,
                    Kind = kind,
                    Title = title,
                    Status = DrawingStatus.Draft,
                    Versions = new List<DrawingVersion>(),
                };
                _db.Drawings.Add(drawing);
                await _db.SaveChangesAsync(ct);
            }

            // Next version number from existing chain (or 1 if empty).
            var nextVersion = drawing.Versions.Count == 0
                ? 1
                : drawing.Versions.Max(v => v.VersionNo) + 1;

            // Suggest key for blob store — store appends sha8 + returns the
            // final path back. Store enforces extension allowlist + size cap;
            // we surface the InvalidOperationException unchanged so callers
            // can render "extension not allowed" + "size exceeds cap" inline.
            var suggestedKey = $"drawings/{revisionId}/{drawing.Id}/v{nextVersion}.{ext}";
            BlobPutResult put;
            try
            {
                put = await _blobs.PutAsync(content, suggestedKey, contentType, ct);
            }
            catch (InvalidOperationException)
            {
                // Roll back the drawing-row create on failed upload — we
                // don't want orphan Drawing rows when the first version
                // can't be persisted (rare but possible: oversize / bad
                // ext caught only at write time).
                throw;
            }

            var version = new DrawingVersion
            {
                DrawingId = drawing.Id,
                VersionNo = nextVersion,
                FileName = originalFileName,
                StorageKey = put.Key,
                FileHash = put.Sha256Hex,
                FileSize = put.SizeBytes,
                ChangeReason = string.IsNullOrWhiteSpace(changeReason) ? null : changeReason.Trim(),
                Status = DrawingVersionStatus.Draft,
                UploadedAt = DateTime.UtcNow,
                UploadedBy = string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor,
            };
            _db.DrawingVersions.Add(version);
            await _db.SaveChangesAsync(ct);

            // Advance current pointer.
            drawing.CurrentVersionId = version.Id;
            drawing.UpdatedAt = DateTime.UtcNow;
            drawing.UpdatedBy = version.UploadedBy;
            await _db.SaveChangesAsync(ct);

            await _audit.EmitAsync(
                AuditAction.DrawingUpload,
                actor: version.UploadedBy,
                actorRole: actorRole ?? "",
                targetType: "DrawingVersion",
                targetId: version.Id.ToString(),
                detail: JsonSerializer.Serialize(new
                {
                    revision_id     = revisionId,
                    drawing_id      = drawing.Id,
                    kind            = kind.ToString(),
                    version_no      = nextVersion,
                    filename        = originalFileName,
                    sha256_short    = put.Sha256Hex[..8],
                    size_bytes      = put.SizeBytes,
                    has_change_reason = !string.IsNullOrWhiteSpace(version.ChangeReason),
                }));

            await tx.CommitAsync(ct);

            return new DrawingUploadResult(
                DrawingId: drawing.Id,
                VersionId: version.Id,
                VersionNo: nextVersion,
                StorageKey: put.Key,
                Sha256Hex: put.Sha256Hex,
                SizeBytes: put.SizeBytes);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Resolve a <see cref="DrawingVersion"/> for download. Verifies that the
    /// version's parent drawing belongs to <paramref name="expectedRevisionId"/>
    /// — defends against forged links that attempt to read drawings from a
    /// different revision (defense-in-depth alongside the controller's
    /// NpiSpecRead policy). Returns <c>null</c> if version not found OR the
    /// revision check fails.
    /// </summary>
    public async Task<DrawingDownloadInfo?> GetForDownloadAsync(
        long versionId,
        long expectedRevisionId,
        CancellationToken ct = default)
    {
        var version = await _db.DrawingVersions
            .AsNoTracking()
            .Include(v => v.Drawing)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null || version.Drawing is null) return null;
        if (version.Drawing.ProductRevisionId != expectedRevisionId) return null;
        return new DrawingDownloadInfo(
            StorageKey: version.StorageKey,
            FileName: version.FileName,
            FileSize: version.FileSize,
            Sha256Hex: version.FileHash,
            UploadedAt: version.UploadedAt);
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────

public sealed record DrawingKindView(
    DrawingKind Kind,
    long? DrawingId,
    string Title,
    long? CurrentVersionId,
    List<DrawingVersionView> Versions);

public sealed record DrawingVersionView(
    long Id,
    int VersionNo,
    string FileName,
    string Sha256Hex,
    long FileSize,
    DrawingVersionStatus Status,
    string? ChangeReason,
    DateTime UploadedAt,
    string? UploadedBy);

public sealed record DrawingUploadResult(
    long DrawingId,
    long VersionId,
    int VersionNo,
    string StorageKey,
    string Sha256Hex,
    long SizeBytes);

public sealed record DrawingDownloadInfo(
    string StorageKey,
    string FileName,
    long FileSize,
    string Sha256Hex,
    DateTime UploadedAt);
