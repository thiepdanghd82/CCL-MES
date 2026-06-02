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
    ///
    /// PR-D-5c — each <see cref="DrawingVersionView"/> carries 3
    /// <see cref="DrawingApprovalView"/> entries (Npi/Production/Qc) so
    /// the UI can render chip state per version. Versions persisted
    /// before PR-D-5c had no approval rows; this method lazy-fills the
    /// missing 3 rows on first read so legacy data renders consistently.
    /// </summary>
    public async Task<List<DrawingKindView>> ListByRevisionAsync(long revisionId)
    {
        var drawings = await _db.Drawings
            .Where(d => d.ProductRevisionId == revisionId)
            .Include(d => d.Versions.OrderByDescending(v => v.VersionNo))
                .ThenInclude(v => v.Approvals)
            .ToListAsync();

        // Heal legacy versions (uploaded pre-PR-D-5c) that have < 3 approval rows.
        var needSave = false;
        foreach (var dr in drawings)
        {
            foreach (var v in dr.Versions)
            {
                foreach (var role in _approvalRoles)
                {
                    if (!v.Approvals.Any(a => a.Role == role))
                    {
                        v.Approvals.Add(new DrawingApproval
                        {
                            DrawingVersionId = v.Id,
                            Role = role,
                            Status = DrawingApprovalStatus.Pending,
                        });
                        needSave = true;
                    }
                }
            }
        }
        if (needSave) await _db.SaveChangesAsync();

        // Group by kind. Each kind may have 0..1 Drawing in v1 (one-drawing-per-kind
        // convention). Future PR can lift to N.
        var byKind = drawings
            .GroupBy(d => d.Kind)
            .ToDictionary(g => g.Key, g => g.First());

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
                    v.UploadedBy,
                    Approvals: BuildApprovalViews(v.Approvals)))
                .ToList() ?? new List<DrawingVersionView>();
            result.Add(new DrawingKindView(
                Kind: k,
                DrawingId: dr?.Id,
                Title: dr?.Title ?? "",
                DrawingStatus: dr?.Status ?? DrawingStatus.Draft,
                CurrentVersionId: dr?.CurrentVersionId,
                Versions: versions));
        }
        return result;
    }

    private static readonly DrawingApprovalRole[] _approvalRoles = new[]
    {
        DrawingApprovalRole.Npi,
        DrawingApprovalRole.Production,
        DrawingApprovalRole.Qc,
    };

    private static List<DrawingApprovalView> BuildApprovalViews(IEnumerable<DrawingApproval> rows)
    {
        var byRole = rows.ToDictionary(a => a.Role);
        var result = new List<DrawingApprovalView>(3);
        foreach (var role in _approvalRoles)
        {
            if (byRole.TryGetValue(role, out var a))
            {
                result.Add(new DrawingApprovalView(
                    a.Id, role, a.Status, a.ActedBy, a.ActedAt, a.Comment));
            }
            else
            {
                // Should never hit after heal, but defensive.
                result.Add(new DrawingApprovalView(
                    0, role, DrawingApprovalStatus.Pending, null, null, null));
            }
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

            // PR-D-5c — create 3 Pending approval rows deterministically at
            // upload time so the UI can always render Npi/Production/Qc chips
            // without lazy-create round-trips on read. Unique index
            // (DrawingVersionId, Role) enforces max 3.
            foreach (var role in _approvalRoles)
            {
                _db.DrawingApprovals.Add(new DrawingApproval
                {
                    DrawingVersionId = version.Id,
                    Role = role,
                    Status = DrawingApprovalStatus.Pending,
                });
            }
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

    /// <summary>
    /// PR-D-5c — chip-permission check (Option (a) Department-based).
    /// Admin always passes. Otherwise:
    ///   Npi chip:        Role=Engineer + Department=npi
    ///   Production chip: Role=Engineer + Department=production OR Role=Supervisor
    ///   Qc chip:         Role=Engineer + Department=qc
    /// Department string is case-insensitive.
    /// </summary>
    public static bool CanActAs(string actorRole, string? actorDepartment, DrawingApprovalRole chip)
    {
        if (string.Equals(actorRole, "Admin", StringComparison.OrdinalIgnoreCase))
            return true;
        var dept = (actorDepartment ?? "").Trim().ToLowerInvariant();
        return chip switch
        {
            DrawingApprovalRole.Npi =>
                string.Equals(actorRole, "Engineer", StringComparison.OrdinalIgnoreCase)
                && dept == "npi",
            DrawingApprovalRole.Production =>
                (string.Equals(actorRole, "Engineer", StringComparison.OrdinalIgnoreCase) && dept == "production")
                || string.Equals(actorRole, "Supervisor", StringComparison.OrdinalIgnoreCase),
            DrawingApprovalRole.Qc =>
                string.Equals(actorRole, "Engineer", StringComparison.OrdinalIgnoreCase)
                && dept == "qc",
            _ => false,
        };
    }

    /// <summary>
    /// Atomic decide. Find the approval row (versionId, role), validate
    /// caller can act as the chip role per <see cref="CanActAs"/>, enforce
    /// comment-required-on-Reject, write the row, recompute version status
    /// (all 3 Approved → Approved; any 1 Rejected → Rejected; else
    /// PendingApproval if any decided else Draft), then if newly-Approved,
    /// roll the older non-Rejected versions of the same drawing to Superseded
    /// and bump Drawing.CurrentVersionId. Audits DRAWING_DECIDE on the chip +
    /// DRAWING_SUPERSEDE per rolled version. All under a single EF transaction.
    /// </summary>
    public async Task<DrawingDecideResult> DecideAsync(
        long revisionId,
        long versionId,
        DrawingApprovalRole role,
        DrawingApprovalStatus decision,
        string? comment,
        string actor,
        string actorRole,
        string? actorDepartment,
        CancellationToken ct = default)
    {
        if (decision != DrawingApprovalStatus.Approved && decision != DrawingApprovalStatus.Rejected)
            throw new InvalidOperationException("Decision must be Approved or Rejected.");

        if (!CanActAs(actorRole, actorDepartment, role))
        {
            throw new UnauthorizedAccessException(
                $"Role '{actorRole}' / Department '{actorDepartment ?? "(none)"}' không có quyền action chip '{role}'.");
        }

        // Comment required on Reject (server-side guard mirrors client).
        if (decision == DrawingApprovalStatus.Rejected && string.IsNullOrWhiteSpace(comment))
        {
            throw new InvalidOperationException("Comment is required when rejecting.");
        }

        var version = await _db.DrawingVersions
            .Include(v => v.Drawing)
            .Include(v => v.Approvals)
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null || version.Drawing is null)
            throw new InvalidOperationException($"DrawingVersion {versionId} not found.");
        if (version.Drawing.ProductRevisionId != revisionId)
            throw new InvalidOperationException("Version does not belong to revision.");

        if (version.Status == DrawingVersionStatus.Superseded)
            throw new InvalidOperationException("Cannot decide on a superseded version.");

        var dbCtx = (DbContext)_db;
        await using var tx = await dbCtx.Database.BeginTransactionAsync(ct);
        try
        {
            // Find-or-create the chip row (heal pre-PR-D-5c legacy where 3 rows
            // might not exist yet — defensive; UploadAsync now creates them).
            var approval = version.Approvals.FirstOrDefault(a => a.Role == role);
            if (approval is null)
            {
                approval = new DrawingApproval
                {
                    DrawingVersionId = version.Id,
                    Role = role,
                };
                _db.DrawingApprovals.Add(approval);
                version.Approvals.Add(approval);
            }

            approval.Status = decision;
            approval.ActedBy = string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor;
            approval.ActedAt = DateTime.UtcNow;
            approval.Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();

            // Recompute version status from the 3 approvals.
            var newStatus = RecomputeVersionStatus(version.Approvals);
            version.Status = newStatus;
            version.UpdatedAt = DateTime.UtcNow;
            version.UpdatedBy = approval.ActedBy;

            await _db.SaveChangesAsync(ct);

            // Supersede logic — only when version newly becomes Approved.
            int supersededCount = 0;
            if (newStatus == DrawingVersionStatus.Approved)
            {
                var siblings = await _db.DrawingVersions
                    .Where(v => v.DrawingId == version.DrawingId
                                && v.Id != version.Id
                                && v.Status != DrawingVersionStatus.Superseded
                                && v.Status != DrawingVersionStatus.Rejected)
                    .ToListAsync(ct);
                foreach (var sib in siblings)
                {
                    sib.Status = DrawingVersionStatus.Superseded;
                    sib.UpdatedAt = DateTime.UtcNow;
                    sib.UpdatedBy = approval.ActedBy;
                    supersededCount++;

                    await _audit.EmitAsync(
                        AuditAction.DrawingSupersede,
                        actor: approval.ActedBy,
                        actorRole: actorRole ?? "",
                        targetType: "DrawingVersion",
                        targetId: sib.Id.ToString(),
                        detail: JsonSerializer.Serialize(new
                        {
                            revision_id              = revisionId,
                            drawing_id               = version.DrawingId,
                            superseded_version_id    = sib.Id,
                            superseded_version_no    = sib.VersionNo,
                            by_version_id            = version.Id,
                            by_version_no            = version.VersionNo,
                            by_decided_user          = approval.ActedBy,
                        }));
                }

                version.Drawing.CurrentVersionId = version.Id;
                version.Drawing.Status = DrawingStatus.Approved;
                version.Drawing.UpdatedAt = DateTime.UtcNow;
                version.Drawing.UpdatedBy = approval.ActedBy;
                await _db.SaveChangesAsync(ct);
            }
            else if (newStatus == DrawingVersionStatus.PendingApproval
                     && version.Drawing.Status == DrawingStatus.Draft)
            {
                // First decision lands → bump Drawing.Status to PendingApproval
                // (only if not already Approved/Superseded from older versions).
                version.Drawing.Status = DrawingStatus.PendingApproval;
                version.Drawing.UpdatedAt = DateTime.UtcNow;
                version.Drawing.UpdatedBy = approval.ActedBy;
                await _db.SaveChangesAsync(ct);
            }

            await _audit.EmitAsync(
                AuditAction.DrawingDecide,
                actor: approval.ActedBy,
                actorRole: actorRole ?? "",
                targetType: "DrawingApproval",
                targetId: approval.Id.ToString(),
                detail: JsonSerializer.Serialize(new
                {
                    revision_id            = revisionId,
                    drawing_id             = version.DrawingId,
                    version_id             = version.Id,
                    version_no             = version.VersionNo,
                    role                   = role.ToString(),
                    decision               = decision.ToString(),
                    has_comment            = !string.IsNullOrWhiteSpace(approval.Comment),
                    version_status_after   = newStatus.ToString(),
                    drawing_status_after   = version.Drawing.Status.ToString(),
                }));

            await tx.CommitAsync(ct);
            return new DrawingDecideResult(
                VersionId: version.Id,
                VersionStatus: newStatus,
                DrawingStatus: version.Drawing.Status,
                SupersededCount: supersededCount);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static DrawingVersionStatus RecomputeVersionStatus(IEnumerable<DrawingApproval> approvals)
    {
        var byRole = approvals.ToDictionary(a => a.Role, a => a.Status);
        bool Get(DrawingApprovalRole r, out DrawingApprovalStatus s)
            => byRole.TryGetValue(r, out s!);
        var hasNpi = Get(DrawingApprovalRole.Npi, out var npi);
        var hasProd = Get(DrawingApprovalRole.Production, out var prod);
        var hasQc = Get(DrawingApprovalRole.Qc, out var qc);

        var statuses = new[] { (hasNpi, npi), (hasProd, prod), (hasQc, qc) };
        var anyDecided = statuses.Any(t => t.Item1 && t.Item2 != DrawingApprovalStatus.Pending);
        var anyRejected = statuses.Any(t => t.Item1 && t.Item2 == DrawingApprovalStatus.Rejected);
        var allApproved = hasNpi && hasProd && hasQc
                          && npi == DrawingApprovalStatus.Approved
                          && prod == DrawingApprovalStatus.Approved
                          && qc == DrawingApprovalStatus.Approved;

        if (anyRejected) return DrawingVersionStatus.Rejected;
        if (allApproved) return DrawingVersionStatus.Approved;
        if (anyDecided)  return DrawingVersionStatus.PendingApproval;
        return DrawingVersionStatus.Draft;
    }
}

// ── DTOs ──────────────────────────────────────────────────────────────

public sealed record DrawingKindView(
    DrawingKind Kind,
    long? DrawingId,
    string Title,
    DrawingStatus DrawingStatus,
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
    string? UploadedBy,
    List<DrawingApprovalView> Approvals);

public sealed record DrawingApprovalView(
    long Id,
    DrawingApprovalRole Role,
    DrawingApprovalStatus Status,
    string? ActedBy,
    DateTime? ActedAt,
    string? Comment);

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

public sealed record DrawingDecideResult(
    long VersionId,
    DrawingVersionStatus VersionStatus,
    DrawingStatus DrawingStatus,
    int SupersededCount);
