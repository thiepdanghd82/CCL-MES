using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// Phase 8 PR-D-4 — QC Capture service. Append-only inspection results per
/// criterion within a stage window. Operator's "Capture" click creates ONE
/// new <see cref="SpecQcCapture"/> row; the latest-by-CapturedAt row is the
/// current displayed pill.
///
/// Distinction from Phase 6 <c>IqcInspection</c> (Iqc.cs): IqcInspection is
/// pre-WO raw-material inspection (vendor batch arrives → QC checks before
/// release to production). QC Capture is per-spec inline/checkpoint result
/// (operator at IPQC/FQC/OQC stage checks a criterion → records result).
/// Different semantic + different audit codes; do NOT merge.
///
/// RBAC: server-side role check — only Admin or Engineer can capture
/// (default per Q11, matches PR-D-3 contract). QC role bypassed because
/// route policy <c>NpiSpecRead</c> doesn't grant QC role anyway. Throws
/// <see cref="UnauthorizedAccessException"/> on other roles.
///
/// Validation:
///   - Criterion must exist + belong to a stage window of the revision.
///   - <see cref="QcCaptureResult.Fail"/> requires non-empty
///     <c>NgReasonCode</c> referencing an active <see cref="ReasonCode"/>
///     of <see cref="ReasonCodeKind.Scrap"/> kind.
///   - <see cref="QcCaptureResult.Pass"/> and <see cref="QcCaptureResult.Na"/>
///     allow <c>NgReasonCode</c> = null but if provided must still be valid
///     (defense-in-depth; UI guards against this anyway).
/// </summary>
public class SpecQcCaptureService
{
    private static readonly HashSet<string> _editorRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin",
        "Engineer",
    };

    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    public SpecQcCaptureService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Returns the active <see cref="ReasonCode"/> rows sorted by Kind + Sort
    /// for UI dropdown population. Inactive rows excluded.
    /// </summary>
    public async Task<List<ReasonCode>> ListReasonCodesAsync()
    {
        return await _db.ReasonCodes
            .AsNoTracking()
            .Where(r => r.Active)
            .OrderBy(r => r.Kind)
            .ThenBy(r => r.Sort)
            .ThenBy(r => r.Code)
            .ToListAsync();
    }

    /// <summary>
    /// Returns all captures for a revision grouped by (Stage, CriterionId)
    /// with the LATEST capture last so UI can grab the latest per pair easily.
    /// Ordered by CapturedAt ASC to support both latest-only and timeline views.
    /// </summary>
    public async Task<List<SpecQcCapture>> ListByRevisionAsync(long revisionId)
    {
        // Resolve windows of this revision (small set — max 4 stages).
        var windowIds = await _db.SpecQcWindows
            .AsNoTracking()
            .Where(w => w.ProductRevisionId == revisionId)
            .Select(w => w.Id)
            .ToListAsync();

        if (windowIds.Count == 0) return new();

        return await _db.SpecQcCaptures
            .AsNoTracking()
            .Where(c => windowIds.Contains(c.SpecQcWindowId))
            .OrderBy(c => c.CapturedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Atomic capture write. Validate inputs, append one new
    /// <see cref="SpecQcCapture"/> row, emit audit. Returns the persisted row
    /// (with server-assigned Id + CapturedAt) for UI rebind.
    /// </summary>
    public async Task<SpecQcCapture> CaptureAsync(
        long revisionId,
        QcCaptureRequest request,
        string actor,
        string actorRole)
    {
        if (!_editorRoles.Contains(actorRole))
        {
            throw new UnauthorizedAccessException(
                $"Role '{actorRole}' không có quyền capture QC (cần Admin hoặc Engineer).");
        }

        // Find the criterion + parent window in one query — ensures both exist
        // AND criterion's window belongs to the requested revision.
        var criterion = await _db.QcCriteria
            .Include(c => c.SpecQcWindow)
            .FirstOrDefaultAsync(c => c.Id == request.CriterionId);

        if (criterion is null || criterion.SpecQcWindow is null)
        {
            throw new InvalidOperationException($"Criterion {request.CriterionId} not found.");
        }
        if (criterion.SpecQcWindow.ProductRevisionId != revisionId)
        {
            throw new InvalidOperationException(
                $"Criterion {request.CriterionId} does not belong to revision {revisionId}.");
        }

        // FAIL requires reason code referencing active Scrap-kind row.
        if (request.Result == QcCaptureResult.Fail)
        {
            if (string.IsNullOrWhiteSpace(request.NgReasonCode))
            {
                throw new InvalidOperationException(
                    "NG reason code is required when result = FAIL.");
            }
        }

        // If reason code provided (any result), validate it exists + is active.
        if (!string.IsNullOrWhiteSpace(request.NgReasonCode))
        {
            var code = request.NgReasonCode.Trim();
            var rc = await _db.ReasonCodes
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.Code == code && r.Active);
            if (rc is null)
            {
                throw new InvalidOperationException(
                    $"NG reason code '{code}' is not a known active reason code.");
            }
        }

        var dbCtx = (DbContext)_db;
        await using var tx = await dbCtx.Database.BeginTransactionAsync();
        try
        {
            var capture = new SpecQcCapture
            {
                SpecQcWindowId = criterion.SpecQcWindowId,
                QcCriterionId  = criterion.Id,
                Result         = request.Result,
                Measurement    = Normalize(request.Measurement),
                NgReasonCode   = Normalize(request.NgReasonCode),
                Comment        = Normalize(request.Comment),
                CapturedBy     = string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor,
                CapturedAt     = DateTime.UtcNow,
            };
            _db.SpecQcCaptures.Add(capture);
            await _db.SaveChangesAsync();

            await _audit.EmitAsync(
                AuditAction.SpecQcCapture,
                actor: capture.CapturedBy,
                actorRole: actorRole ?? "",
                targetType: "SpecQcCapture",
                targetId: capture.Id.ToString(),
                detail: JsonSerializer.Serialize(new
                {
                    revision_id     = revisionId,
                    stage           = criterion.SpecQcWindow.Stage.ToString(),
                    criterion_id    = criterion.Id,
                    result          = capture.Result.ToString(),
                    has_measurement = !string.IsNullOrWhiteSpace(capture.Measurement),
                    ng_reason_code  = capture.NgReasonCode,
                    has_comment     = !string.IsNullOrWhiteSpace(capture.Comment),
                }));

            await tx.CommitAsync();
            return capture;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static string? Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim();
    }
}

/// <summary>
/// PR-D-4 — capture write request DTO. Operator-facing modal collects these
/// 4 fields per criterion. <c>CriterionId</c> identifies the row; the service
/// resolves the parent window + revision-ownership in one query.
/// </summary>
public record QcCaptureRequest(
    long CriterionId,
    QcCaptureResult Result,
    string? Measurement,
    string? NgReasonCode,
    string? Comment);
