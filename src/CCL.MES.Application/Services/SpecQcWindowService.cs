using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Application.Services;

/// <summary>
/// Phase 8 PR-D-3 — QC Plans tab service. CRUD cho <see cref="SpecQcWindow"/>
/// + child <see cref="QcCriterion"/> per <see cref="ProductRevision"/>.
///
/// UI contract — operator nhập text vào 5 cột:
///     Criterion / Target / Tolerance / Method / Frequency
///
/// Entity mapping (preserve existing schema, no breaking rename):
///     Criterion  → <see cref="QcCriterion.Name"/>           (required)
///     Target     → <see cref="QcCriterion.PassCriteria"/>   (existing nullable string)
///     Tolerance  → <see cref="QcCriterion.MeasureMethod"/>  (repurposed — original
///                                                            "how to measure" semantic
///                                                            now covered by new Method)
///     Method     → <see cref="QcCriterion.Method"/>         (NEW PR-D-3 col, max 200)
///     Frequency  → <see cref="QcCriterion.Frequency"/>      (NEW PR-D-3 col, max 120)
///
/// Per-stage atomic upsert: each save reads current rows, computes diff (deleted /
/// updated / created), writes via single transaction, audits SpecQcPlanUpsert.
///
/// RBAC: server-side role check — only Admin or Engineer can mutate.
/// Throws <see cref="UnauthorizedAccessException"/> otherwise (caller catches +
/// renders error banner per hotfix #27 pattern).
/// </summary>
public class SpecQcWindowService
{
    private static readonly HashSet<string> _editorRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Admin",
        "Engineer",
    };

    private readonly IMesDbContext _db;
    private readonly IAuditWriter _audit;

    public SpecQcWindowService(IMesDbContext db, IAuditWriter audit)
    {
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Read-only — returns the 4 stage windows for a revision keyed by
    /// <see cref="QcStage"/>. Missing stage = null entry (operator chưa tạo).
    /// Criteria eager-loaded + ordered by Seq.
    /// </summary>
    public async Task<Dictionary<QcStage, SpecQcWindow?>> ListByRevisionAsync(long revisionId)
    {
        var windows = await _db.SpecQcWindows
            .AsNoTracking()
            .Where(w => w.ProductRevisionId == revisionId)
            .Include(w => w.Criteria.OrderBy(c => c.Seq))
            .ToListAsync();

        var result = new Dictionary<QcStage, SpecQcWindow?>
        {
            { QcStage.IpqcPrint, null },
            { QcStage.IpqcCut,   null },
            { QcStage.Fqc,       null },
            { QcStage.Oqc,       null },
        };

        foreach (var w in windows)
        {
            // Last-write-wins if duplicates exist (shouldn't, but defensive).
            result[w.Stage] = w;
        }

        return result;
    }

    /// <summary>
    /// Atomic per-stage upsert. Find or create SpecQcWindow for (revisionId, stage),
    /// replace all criteria with the provided list, audit emit.
    ///
    /// RBAC: only Admin or Engineer can mutate. Other roles throw
    /// <see cref="UnauthorizedAccessException"/>.
    ///
    /// Criteria semantics:
    ///   - Rows are processed in order — Seq assigned as row index (0..N-1).
    ///   - Existing criteria with Id matching an input row → UPDATE in place.
    ///   - Existing criteria with no matching input Id → HARD DELETE (no FK
    ///     children on QcCriterion as of PR-D-3 schema, so safe).
    ///   - Input rows without Id → INSERT new.
    ///   - Empty list → window stays (with 0 criteria), all rows deleted.
    ///
    /// Returns the reloaded window (criteria included, ordered by Seq) so the UI
    /// can rebind without a separate round-trip.
    /// </summary>
    public async Task<SpecQcWindow> UpsertStageAsync(
        long revisionId,
        QcStage stage,
        IReadOnlyList<QcCriterionRow> rows,
        string actor,
        string actorRole)
    {
        if (!_editorRoles.Contains(actorRole))
        {
            throw new UnauthorizedAccessException(
                $"Role '{actorRole}' không có quyền chỉnh sửa QC plans (cần Admin hoặc Engineer).");
        }

        // Validate revision exists — guards against UI tampering with a fake id.
        var revisionExists = await _db.ProductRevisions
            .AsNoTracking()
            .AnyAsync(r => r.Id == revisionId);
        if (!revisionExists)
        {
            throw new InvalidOperationException($"ProductRevision {revisionId} not found.");
        }

        // Validate per-row Name non-empty (UI guards, but defensive).
        foreach (var r in rows)
        {
            if (string.IsNullOrWhiteSpace(r.Name))
            {
                throw new InvalidOperationException(
                    "Criterion Name không được để trống.");
            }
        }

        // IMesDbContext abstraction không expose Database property; cast xuống
        // DbContext qua interface (pattern khớp SpecImportService).
        var dbCtx = (DbContext)_db;
        await using var tx = await dbCtx.Database.BeginTransactionAsync();

        try
        {
            // Find-or-create window. Index (ProductRevisionId, Stage) is non-unique
            // by schema design — defensive: use FirstOrDefault.
            var window = await _db.SpecQcWindows
                .Include(w => w.Criteria)
                .FirstOrDefaultAsync(w => w.ProductRevisionId == revisionId && w.Stage == stage);

            int createdCount = 0, updatedCount = 0, deletedCount = 0;

            if (window is null)
            {
                window = new SpecQcWindow
                {
                    ProductRevisionId = revisionId,
                    Stage = stage,
                    Title = StageDefaultTitle(stage),
                    Status = SpecQcWindowStatus.Draft,
                    RejectAction = QcRejectAction.Escalate,
                    Criteria = new List<QcCriterion>(),
                };
                _db.SpecQcWindows.Add(window);
                // SaveChanges to materialize Id for FK on criteria.
                await _db.SaveChangesAsync();
            }

            // Build set of incoming criterion Ids for diff.
            var incomingIds = rows
                .Where(r => r.Id.HasValue)
                .Select(r => r.Id!.Value)
                .ToHashSet();

            // Delete criteria whose Id is not in incoming.
            var toDelete = window.Criteria.Where(c => !incomingIds.Contains(c.Id)).ToList();
            foreach (var c in toDelete)
            {
                _db.QcCriteria.Remove(c);
                deletedCount++;
            }

            // Apply incoming rows.
            short seq = 0;
            foreach (var row in rows)
            {
                if (row.Id.HasValue)
                {
                    var existing = window.Criteria.FirstOrDefault(c => c.Id == row.Id.Value);
                    if (existing is null)
                    {
                        // Id provided but not in this window — likely tampering;
                        // treat as new row instead of throwing (idempotent UX).
                        existing = new QcCriterion
                        {
                            SpecQcWindowId = window.Id,
                        };
                        _db.QcCriteria.Add(existing);
                        createdCount++;
                    }
                    else
                    {
                        updatedCount++;
                    }
                    ApplyRow(existing, row, seq);
                }
                else
                {
                    var fresh = new QcCriterion
                    {
                        SpecQcWindowId = window.Id,
                    };
                    ApplyRow(fresh, row, seq);
                    _db.QcCriteria.Add(fresh);
                    createdCount++;
                }
                seq++;
            }

            await _db.SaveChangesAsync();

            await _audit.EmitAsync(
                AuditAction.SpecQcPlanUpsert,
                actor: string.IsNullOrWhiteSpace(actor) ? "anonymous" : actor,
                actorRole: actorRole ?? "",
                targetType: "SpecQcWindow",
                targetId: window.Id.ToString(),
                detail: JsonSerializer.Serialize(new
                {
                    revision_id = revisionId,
                    stage = stage.ToString(),
                    criteria_count = rows.Count,
                    created = createdCount,
                    updated = updatedCount,
                    deleted = deletedCount,
                }));

            await tx.CommitAsync();

            // Reload with ordered criteria for response.
            return await _db.SpecQcWindows
                .AsNoTracking()
                .Include(w => w.Criteria.OrderBy(c => c.Seq))
                .FirstAsync(w => w.Id == window.Id);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private static void ApplyRow(QcCriterion target, QcCriterionRow row, short seq)
    {
        target.Seq = seq;
        target.Name = row.Name.Trim();
        // CriterionType + Required stay at entity defaults (Visual + true).
        // PR-D-3 keeps the UI 5-col free-form text + leaves structured numeric
        // fields (TargetValue / ToleranceMin / ToleranceMax) untouched.
        target.PassCriteria  = NormalizeOptional(row.Target);
        target.MeasureMethod = NormalizeOptional(row.Tolerance); // repurposed — see service summary
        target.Method        = NormalizeOptional(row.Method);
        target.Frequency     = NormalizeOptional(row.Frequency);
    }

    private static string? NormalizeOptional(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return s.Trim();
    }

    private static string StageDefaultTitle(QcStage stage) => stage switch
    {
        QcStage.IpqcPrint => "IPQC · Print",
        QcStage.IpqcCut   => "IPQC · Cut",
        QcStage.Fqc       => "FQC · Final",
        QcStage.Oqc       => "OQC · Outgoing",
        _                 => stage.ToString(),
    };
}

/// <summary>
/// PR-D-3 UI row DTO — matches the 5 editable cells in the QC Plans table.
/// Id null = new row (insert); Id provided = existing row (update or hard
/// delete if missing from next save).
/// </summary>
public record QcCriterionRow(
    long? Id,
    string Name,
    string? Target,
    string? Tolerance,
    string? Method,
    string? Frequency);
