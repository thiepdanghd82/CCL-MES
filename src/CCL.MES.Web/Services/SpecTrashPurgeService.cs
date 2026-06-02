using System.Globalization;
using System.Text.Json;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Storage;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 8 PR-L3 — Background hard-delete worker for soft-deleted spec
/// revisions. Cycle every 24h (configurable) and purge revisions that have
/// been in Trash longer than <c>OPS_SPEC_TRASH_RETENTION_DAYS</c> (default
/// 30). First run waits 30 seconds after boot to clear the DbSeeder + any
/// cold-start writes.
///
/// SAFETY CONTRACT (Henry's hard requirement — failure here is catastrophic
/// because it deletes data permanently):
///   1. ELIGIBILITY: a row is eligible ONLY when both
///        <c>IsTrashed = true</c>  AND  <c>TrashedAt &lt; UtcNow - retention</c>.
///      Exactly-30-day boundary keeps the row (`&lt;` strict).
///   2. WO BLOCKER DEFENCE-IN-DEPTH: even though Trash should have blocked
///      the soft-delete when active WOs existed, recount on each eligible
///      row. If any active WO still references the spec → skip + log a
///      <see cref="AuditAction.SpecPurge"/> SKIPPED row (so the trail
///      survives) and continue. Never crash the cycle on FK violations.
///   3. AUDIT BEFORE DELETE: emit SPEC_PURGE before EF Remove so the audit
///      row is durable after the spec row vanishes.
///   4. CASCADE QcCriterion RESTRICT: SpecQcCapture.QcCriterionId is
///      ON DELETE RESTRICT. The CASCADE chain ProductRevision →
///      SpecQcWindow → QcCriterion would race against
///      SpecQcWindow → SpecQcCapture CASCADE. To stay deterministic across
///      providers, we pre-delete SpecQcCaptures EXPLICITLY in the same
///      DbContext call before removing the ProductRevision.
///   5. BLOB CLEANUP: capture every <c>DrawingVersion.StorageKey</c> +
///      <c>PreviewKey</c> BEFORE the DB delete (the EF cascade wipes the
///      rows we'd query from). After the DB commits, call
///      <c>IBlobStore.DeleteAsync</c> for each captured key. Per-key
///      failures are logged + counted but do NOT roll back the DB delete
///      (orphan blobs are recoverable from filesystem cleanup; orphan DB
///      rows are not).
///   6. IDEMPOTENT: a re-run finds zero eligible rows and emits a single
///      summary log line, no audit row. Restart mid-cycle just resumes on
///      the next tick.
/// </summary>
public class SpecTrashPurgeService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SpecTrashPurgeService> _logger;
    private readonly int _retentionDays;
    private readonly TimeSpan _cycle;
    private readonly TimeSpan _firstRunDelay;

    public SpecTrashPurgeService(
        IServiceScopeFactory scopes,
        IConfiguration config,
        ILogger<SpecTrashPurgeService> logger)
    {
        _scopes = scopes;
        _logger = logger;
        // Per approved Q7 — env-overridable. Floor at 1 day so a misconfig
        // ('0' or negative) cannot wipe everything immediately.
        var raw = Environment.GetEnvironmentVariable("OPS_SPEC_TRASH_RETENTION_DAYS")
                  ?? config["Ops:SpecTrashRetentionDays"];
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) || days < 1)
        {
            days = 30;
        }
        _retentionDays = days;
        // Per approved Q8 — daily cycle. Per Q9 — first-run delay 30s.
        _cycle = TimeSpan.FromHours(24);
        _firstRunDelay = TimeSpan.FromSeconds(30);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation(
                "SpecTrashPurgeService starting. Retention={Days} day(s), cycle={Cycle}, first-run delay={Delay}.",
                _retentionDays, _cycle, _firstRunDelay);
            await Task.Delay(_firstRunDelay, ct);
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await RunCycleAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Never let one cycle's failure tank the service. Log and
                    // try again next tick.
                    _logger.LogError(ex, "SpecTrashPurgeService cycle threw — will retry on next interval.");
                }
                await Task.Delay(_cycle, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("SpecTrashPurgeService stopping.");
        }
    }

    /// <summary>
    /// Exposed for tests / ops manual triggers. Runs one purge cycle
    /// against a fresh DI scope so the DbContext lifetime is correct.
    /// </summary>
    public async Task<PurgeCycleStats> RunCycleAsync(CancellationToken ct = default)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IMesDbContext>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
        // IBlobStore may not be registered in unit-test contexts — tolerate.
        var blobStore = scope.ServiceProvider.GetService<IBlobStore>();

        var cutoff = DateTime.UtcNow.AddDays(-_retentionDays);

        // Strict `<` — exactly N days keeps the row.
        var eligibleIds = await db.ProductRevisions
            .AsNoTracking()
            .Where(r => r.IsTrashed && r.TrashedAt.HasValue && r.TrashedAt.Value < cutoff)
            .Select(r => r.Id)
            .ToListAsync(ct);

        var stats = new PurgeCycleStats
        {
            CutoffUtc = cutoff,
            EligibleCount = eligibleIds.Count,
        };

        if (eligibleIds.Count == 0)
        {
            _logger.LogInformation(
                "SpecTrashPurge cycle: 0 eligible (cutoff={Cutoff:o}).", cutoff);
            return stats;
        }

        foreach (var revId in eligibleIds)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                var purged = await PurgeOneAsync(db, audit, blobStore, revId, ct);
                if (purged.Skipped) stats.SkippedCount++;
                else stats.PurgedCount++;
                stats.BlobsRemoved += purged.BlobsRemoved;
                stats.BlobsFailed  += purged.BlobsFailed;
            }
            catch (Exception ex)
            {
                stats.FailedCount++;
                _logger.LogError(ex,
                    "SpecTrashPurge: unexpected failure purging revId={RevId} — skipping.", revId);
            }
        }

        _logger.LogInformation(
            "SpecTrashPurge cycle: eligible={Eligible}, purged={Purged}, skipped_fk={Skipped}, failed={Failed}, blobs_removed={Blobs}, blobs_failed={BlobsFail}.",
            stats.EligibleCount, stats.PurgedCount, stats.SkippedCount,
            stats.FailedCount, stats.BlobsRemoved, stats.BlobsFailed);
        return stats;
    }

    private async Task<PurgeOneOutcome> PurgeOneAsync(
        IMesDbContext db,
        IAuditWriter audit,
        IBlobStore? blobStore,
        long revId,
        CancellationToken ct)
    {
        // Fresh-load with full include for the data we need to (a) write the
        // audit detail + (b) capture blob keys.
        var rev = await db.ProductRevisions
            .Include(r => r.QcWindows)
            .Include(r => r.Drawings)
                .ThenInclude(d => d.Versions)
            .FirstOrDefaultAsync(r => r.Id == revId, ct);
        if (rev is null) return PurgeOneOutcome.AlreadyGone();
        if (!rev.IsTrashed)
        {
            // Restored between eligibility query and now — skip.
            return PurgeOneOutcome.Restored();
        }

        // Safety #2 — WO defence-in-depth. Trash should have blocked but
        // recount just in case.
        var activeWoCount = await db.WorkOrders
            .Where(w => w.ProductRevisionId == revId
                        && w.Status != WoStatus.Closed
                        && w.Status != WoStatus.Cancelled
                        && w.Status != WoStatus.Finished)
            .CountAsync(ct);
        if (activeWoCount > 0)
        {
            await audit.EmitAsync(
                AuditAction.SpecPurge, "system", actorRole: "",
                targetType: "ProductRevision", targetId: rev.Id.ToString(CultureInfo.InvariantCulture),
                detail: JsonSerializer.Serialize(new
                {
                    spec_code        = rev.SpecCode,
                    rev_id           = rev.Id,
                    rev_code         = rev.RevisionCode,
                    trashed_at       = rev.TrashedAt,
                    age_days         = AgeDays(rev.TrashedAt),
                    skipped          = true,
                    reason           = "active_work_orders",
                    active_wo_count  = activeWoCount,
                }));
            _logger.LogWarning(
                "SpecTrashPurge: skipping revId={RevId} ({Spec}) — {N} active WO(s) still reference it.",
                rev.Id, rev.SpecCode, activeWoCount);
            return PurgeOneOutcome.WasSkipped();
        }

        // Safety #5 — capture blob keys BEFORE EF cascade wipes the rows we'd
        // query from. Include both StorageKey and (optional) PreviewKey.
        var blobKeys = new List<string>();
        foreach (var d in rev.Drawings)
        {
            foreach (var v in d.Versions)
            {
                if (!string.IsNullOrWhiteSpace(v.StorageKey)) blobKeys.Add(v.StorageKey);
                if (!string.IsNullOrWhiteSpace(v.PreviewKey)) blobKeys.Add(v.PreviewKey!);
            }
        }

        // Safety #3 — audit emit BEFORE delete so the trail survives.
        await audit.EmitAsync(
            AuditAction.SpecPurge, "system", actorRole: "",
            targetType: "ProductRevision", targetId: rev.Id.ToString(CultureInfo.InvariantCulture),
            detail: JsonSerializer.Serialize(new
            {
                spec_code        = rev.SpecCode,
                rev_id           = rev.Id,
                rev_code         = rev.RevisionCode,
                trashed_at       = rev.TrashedAt,
                age_days         = AgeDays(rev.TrashedAt),
                blob_keys_count  = blobKeys.Count,
            }));

        // Safety #4 — defensively delete SpecQcCaptures for any
        // SpecQcWindow under this rev so the SpecQcWindow → QcCriterion
        // CASCADE doesn't race against SpecQcCapture.QcCriterionId RESTRICT.
        if (rev.QcWindows.Count > 0)
        {
            var windowIds = rev.QcWindows.Select(w => w.Id).ToList();
            var captures = await db.SpecQcCaptures
                .Where(c => windowIds.Contains(c.SpecQcWindowId))
                .ToListAsync(ct);
            if (captures.Count > 0)
            {
                db.SpecQcCaptures.RemoveRange(captures);
                _logger.LogInformation(
                    "SpecTrashPurge: pre-removed {N} SpecQcCapture(s) for revId={RevId}.",
                    captures.Count, rev.Id);
            }
        }

        db.ProductRevisions.Remove(rev);
        await db.SaveChangesAsync(ct);

        // Safety #5 (continued) — blob cleanup AFTER DB commit. Per-key
        // failures count but don't roll back.
        int blobsRemoved = 0, blobsFailed = 0;
        if (blobStore is not null)
        {
            foreach (var key in blobKeys)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    await blobStore.DeleteAsync(key, ct);
                    blobsRemoved++;
                }
                catch (Exception ex)
                {
                    blobsFailed++;
                    _logger.LogError(ex,
                        "SpecTrashPurge: blob delete failed key={Key} revId={RevId} — leaving orphan for ops cleanup.",
                        key, rev.Id);
                }
            }
        }

        _logger.LogInformation(
            "SpecTrashPurge: removed revId={RevId} spec={Spec} rev={Rev} (trashed_at={TrashedAt:o}, blobs={Blobs}/{Failed}).",
            rev.Id, rev.SpecCode, rev.RevisionCode, rev.TrashedAt, blobsRemoved, blobsFailed);
        return new PurgeOneOutcome
        {
            Skipped       = false,
            BlobsRemoved  = blobsRemoved,
            BlobsFailed   = blobsFailed,
        };
    }

    private static int AgeDays(DateTime? trashedAt)
    {
        if (!trashedAt.HasValue) return -1;
        return (int)Math.Floor((DateTime.UtcNow - trashedAt.Value).TotalDays);
    }
}

/// <summary>Per-row outcome for cycle accounting + tests.</summary>
public sealed class PurgeOneOutcome
{
    public bool Skipped { get; set; }
    public int BlobsRemoved { get; set; }
    public int BlobsFailed { get; set; }

    public static PurgeOneOutcome AlreadyGone() => new() { Skipped = true };
    public static PurgeOneOutcome Restored() => new() { Skipped = true };
    public static PurgeOneOutcome WasSkipped() => new() { Skipped = true };
}

/// <summary>Per-cycle stats used by the log line and by tests.</summary>
public sealed class PurgeCycleStats
{
    public DateTime CutoffUtc { get; set; }
    public int EligibleCount { get; set; }
    public int PurgedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public int BlobsRemoved { get; set; }
    public int BlobsFailed { get; set; }
}
