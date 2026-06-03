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
///   2. WO RETAIN: WorkOrders.ProductRevisionId is ON DELETE RESTRICT, so
///      a spec with ANY WO row referencing it (active OR terminal) cannot
///      be deleted without an FK violation. Pre-count via a single COUNT
///      query and RETAIN silently (no audit emit per-spec) when > 0 — this
///      avoids the failure-loop where each 24h cycle would attempt a
///      delete-then-catch-FK and pump up FailedCount + audit noise. The
///      Trash-side blocker still gates ACTIVE WOs (a spec with only
///      terminal WOs can enter retention; an active WO can also race in
///      after trash). Spec remains in Trash (IsTrashed=true); link to WO
///      preserved for traceability + compliance. NO FK relaxation,
///      NO migration, NO persisted "retained" field — the outcome is a
///      runtime classification on the cycle stats only.
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
                if      (purged.Retained) stats.RetainedCount++;
                else if (purged.Skipped)  stats.SkippedCount++;
                else                      stats.PurgedCount++;
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
            "SpecTrashPurge cycle: eligible={Eligible}, purged={Purged}, retained_wo={Retained}, skipped_other={Skipped}, failed={Failed}, blobs_removed={Blobs}, blobs_failed={BlobsFail}.",
            stats.EligibleCount, stats.PurgedCount, stats.RetainedCount, stats.SkippedCount,
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

        // Safety #2 — WO defence-in-depth. WorkOrders.ProductRevisionId is
        // ON DELETE RESTRICT (MesDbContext.cs ~ line 390), so a spec with
        // ANY WO row pointing at it (active OR terminal) cannot be purged
        // without an FK violation. Trash-side blocker only excludes ACTIVE
        // WOs, so terminal WOs (Closed/Finished/Cancelled) attached PRIOR
        // to trashing — and any active WO created AFTER trash through a
        // race window — survive into retention. Pre-count here and RETAIN
        // silently instead of attempting the delete-then-catch-FK loop
        // that used to noisy-spam audit + FailedCount each 24h cycle.
        //
        // Retention is a runtime outcome — the spec stays in Trash with
        // IsTrashed=true; no field persisted, no schema change, no FK
        // relaxation (link kept for WO traceability / compliance).
        var anyWoCount = await db.WorkOrders
            .Where(w => w.ProductRevisionId == revId)
            .CountAsync(ct);
        if (anyWoCount > 0)
        {
            // Per Henry's directive: no audit emit per-retained-spec (the
            // event repeats every 24h cycle and would flood the audit log).
            // Debug log captures the population for ops visibility.
            _logger.LogDebug(
                "SpecTrashPurge: retaining revId={RevId} ({Spec}) — {N} WO(s) reference it; FK RESTRICT prevents deletion.",
                rev.Id, rev.SpecCode, anyWoCount);
            return PurgeOneOutcome.OfRetained();
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

/// <summary>
/// Per-row outcome for cycle accounting + tests. Three terminal states:
///   - <see cref="Retained"/> = WO link blocks deletion; spec stays in Trash.
///   - <see cref="Skipped"/>  = rev disappeared / restored mid-cycle.
///   - default (neither flag) = purged successfully.
/// </summary>
public sealed class PurgeOneOutcome
{
    public bool Retained { get; set; }
    public bool Skipped { get; set; }
    public int BlobsRemoved { get; set; }
    public int BlobsFailed { get; set; }

    public static PurgeOneOutcome AlreadyGone() => new() { Skipped = true };
    public static PurgeOneOutcome Restored() => new() { Skipped = true };
    public static PurgeOneOutcome WasSkipped() => new() { Skipped = true };

    /// <summary>
    /// Spec stays in Trash because at least one WorkOrder still references
    /// it. No FK throw, no audit emit (would noisy-flood the trail across
    /// daily cycles). Cycle log tallies the population.
    /// </summary>
    public static PurgeOneOutcome OfRetained() => new() { Retained = true };
}

/// <summary>Per-cycle stats used by the log line and by tests.</summary>
public sealed class PurgeCycleStats
{
    public DateTime CutoffUtc { get; set; }
    public int EligibleCount { get; set; }
    public int PurgedCount { get; set; }
    /// <summary>
    /// Specs eligible by date but kept because a WorkOrder still references
    /// them (FK RESTRICT — see PurgeOneAsync Safety #2). Silent — no audit.
    /// </summary>
    public int RetainedCount { get; set; }
    public int SkippedCount { get; set; }
    public int FailedCount { get; set; }
    public int BlobsRemoved { get; set; }
    public int BlobsFailed { get; set; }
}
