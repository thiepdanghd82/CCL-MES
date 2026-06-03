using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Application.Storage;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Infrastructure;
using CCL.MES.Infrastructure.Storage;
using CCL.MES.Tests.Integration._Support;
using CCL.MES.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// Phase 9 T2a — Real-prod-logic integration test for
/// <see cref="SpecTrashPurgeService.RunCycleAsync"/>. Locks in the
/// 6-rule SAFETY CONTRACT documented in the service header against a
/// fresh /tmp SQLite + a real <see cref="FilesystemBlobStore"/> on
/// disk + a real <see cref="DrawingsService"/> for blob-bearing seeds.
/// Henry's hard constraint: <i>"không mirror"</i> — the predicate is
/// not extracted, the EF query is what's tested.
///
/// <para>
/// Retention is forced via <c>IConfiguration["Ops:SpecTrashRetentionDays"] = "30"</c>;
/// the env var <c>OPS_SPEC_TRASH_RETENTION_DAYS</c> is cleared + restored
/// across the test lifetime so a developer's host machine config can't
/// leak in.
/// </para>
///
/// <para>
/// Boundary verification on 29d KEEP / 31d PURGE uses clear margins
/// (24h either side of cutoff) so the inevitable few-ms drift between
/// test setup and service execution can't change the outcome. The exact
/// 30-day boundary semantics — <c>&lt;</c> strict — are covered as a
/// pure unit in T1 <c>SpecTrashPurgeEligibilityTests</c>.
/// </para>
/// </summary>
public sealed class SpecTrashPurgeServiceTests : IDisposable
{
    private const string EnvVar = "OPS_SPEC_TRASH_RETENTION_DAYS";

    private readonly IsolatedDbFixture _fx;
    private readonly string _blobRoot;
    private readonly FilesystemBlobStore _blobStore;
    private readonly InMemoryAuditWriter _audit;
    private readonly ServiceProvider _provider;
    private readonly SpecTrashPurgeService _svc;
    private readonly string? _originalEnvValue;

    public SpecTrashPurgeServiceTests()
    {
        _originalEnvValue = Environment.GetEnvironmentVariable(EnvVar);
        Environment.SetEnvironmentVariable(EnvVar, null);

        _fx = new IsolatedDbFixture();
        _blobRoot = Path.Combine(Path.GetTempPath(), $"ccl-purge-blob-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_blobRoot);
        _blobStore = new FilesystemBlobStore(new BlobStoreOptions
        {
            DataDir  = _blobRoot,
            MaxBytes = 1024 * 1024,
        });
        _audit = new InMemoryAuditWriter();

        // Wire a scoped DI graph the service can resolve from. IMesDbContext
        // MUST be Scoped because the service creates its own scope per cycle
        // and resolves a fresh context — matches prod registration.
        var services = new ServiceCollection();
        services.AddDbContext<MesDbContext>(opt => opt.UseSqlite($"Data Source={_fx.DbPath}"));
        services.AddScoped<IMesDbContext>(sp => sp.GetRequiredService<MesDbContext>());
        services.AddSingleton<IAuditWriter>(_audit);
        services.AddSingleton<IBlobStore>(_blobStore);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddLogging();
        _provider = services.BuildServiceProvider();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new[]
            {
                new KeyValuePair<string, string?>("Ops:SpecTrashRetentionDays", "30"),
            })
            .Build();

        _svc = new SpecTrashPurgeService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            config,
            _provider.GetRequiredService<ILogger<SpecTrashPurgeService>>());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EnvVar, _originalEnvValue);
        _provider.Dispose();
        _fx.Dispose();
        try { Directory.Delete(_blobRoot, recursive: true); } catch { /* best effort */ }
    }

    // ── Rule #1 — Eligibility boundary 29d KEEP / 31d PURGE ───────────

    [Fact]
    public async Task Cycle_keeps_29_day_old_trashed_revision()
    {
        var keepId = await SeedTrashedRevAsync("SPEC-KEEP-29", "B", DateTime.UtcNow.AddDays(-29));

        var stats = await _svc.RunCycleAsync();

        Assert.Equal(0, stats.EligibleCount);
        Assert.Equal(0, stats.PurgedCount);
        await AssertRevExistsAsync(keepId, expected: true);
    }

    [Fact]
    public async Task Cycle_purges_31_day_old_trashed_revision()
    {
        var purgeId = await SeedTrashedRevAsync("SPEC-PURGE-31", "B", DateTime.UtcNow.AddDays(-31));

        var stats = await _svc.RunCycleAsync();

        Assert.Equal(1, stats.EligibleCount);
        Assert.Equal(1, stats.PurgedCount);
        Assert.Equal(0, stats.SkippedCount);
        await AssertRevExistsAsync(purgeId, expected: false);

        // Rule #3 — audit row emitted before delete.
        var audit = Assert.Single(_audit.ByAction(AuditAction.SpecPurge));
        Assert.Equal("system", audit.Actor);
        Assert.Equal("ProductRevision", audit.TargetType);
        Assert.Equal(purgeId.ToString(), audit.TargetId);
    }

    [Fact]
    public async Task Cycle_keeps_non_trashed_revisions()
    {
        // Seeded rev is NOT trashed → ignored regardless of TrashedAt being null.
        var stats = await _svc.RunCycleAsync();

        Assert.Equal(0, stats.EligibleCount);
        await AssertRevExistsAsync(_fx.SeedRevisionId, expected: true);
    }

    [Fact]
    public async Task Cycle_purges_mixed_batch_only_eligible_rows()
    {
        var keep = await SeedTrashedRevAsync("SPEC-K", "B", DateTime.UtcNow.AddDays(-29));
        var purge1 = await SeedTrashedRevAsync("SPEC-P1", "C", DateTime.UtcNow.AddDays(-31));
        var purge2 = await SeedTrashedRevAsync("SPEC-P2", "D", DateTime.UtcNow.AddDays(-90));

        var stats = await _svc.RunCycleAsync();

        Assert.Equal(2, stats.EligibleCount);
        Assert.Equal(2, stats.PurgedCount);
        await AssertRevExistsAsync(keep, expected: true);
        await AssertRevExistsAsync(purge1, expected: false);
        await AssertRevExistsAsync(purge2, expected: false);
    }

    // ── Rule #2 — WO RETAIN (any WO link → silent retain, no FK throw) ─

    [Fact]
    public async Task Cycle_retains_silently_when_active_WO_references_trashed_rev()
    {
        // Trash-side blocker normally prevents this state — but an active
        // WO can race in AFTER trashing. Purge pre-checks ANY WO reference
        // and retains silently instead of attempting a delete that the FK
        // would reject.
        var revId = await SeedTrashedRevAsync("SPEC-WO-ACTIVE", "B", DateTime.UtcNow.AddDays(-45));
        await _fx.SeedWorkOrderAsync(revId, WoStatus.InProgress, "WO-RACE-IN");

        var stats = await _svc.RunCycleAsync();

        Assert.Equal(1, stats.EligibleCount);
        Assert.Equal(0, stats.PurgedCount);
        Assert.Equal(1, stats.RetainedCount);
        Assert.Equal(0, stats.SkippedCount);
        Assert.Equal(0, stats.FailedCount);

        // Spec still in DB + still trashed (retained, not restored).
        await AssertRevExistsAsync(revId, expected: true);
        await AssertRevIsTrashedAsync(revId, expected: true);

        // No audit emit per-retained-spec (would noisy-flood the audit log
        // across daily cycles).
        Assert.Empty(_audit.ByAction(AuditAction.SpecPurge));
    }

    [Fact]
    public async Task Cycle_retains_silently_when_only_terminal_WOs_reference_trashed_rev()
    {
        // WorkOrders.ProductRevisionId is ON DELETE RESTRICT — even a
        // Closed/Finished/Cancelled WO blocks the delete. Pre-count avoids
        // the delete-then-catch-FK loop that used to noisy-spam audit +
        // FailedCount every 24h. This locks in the fix per Henry's
        // directive (was: "Cycle_with_only_terminal_WOs_hits_RESTRICT_FK_
        // and_records_failure"). NO FK throw, NO FailedCount, NO audit.
        var revId = await SeedTrashedRevAsync("SPEC-DONE-WO", "B", DateTime.UtcNow.AddDays(-45));
        await _fx.SeedWorkOrderAsync(revId, WoStatus.Closed,    "WO-CLOSED");
        await _fx.SeedWorkOrderAsync(revId, WoStatus.Finished,  "WO-FINISHED");
        await _fx.SeedWorkOrderAsync(revId, WoStatus.Cancelled, "WO-CANCELLED");

        var stats = await _svc.RunCycleAsync();

        Assert.Equal(1, stats.EligibleCount);
        Assert.Equal(0, stats.PurgedCount);
        Assert.Equal(1, stats.RetainedCount);
        Assert.Equal(0, stats.SkippedCount);
        Assert.Equal(0, stats.FailedCount);

        await AssertRevExistsAsync(revId, expected: true);
        await AssertRevIsTrashedAsync(revId, expected: true);
        Assert.Empty(_audit.ByAction(AuditAction.SpecPurge));
    }

    [Fact]
    public async Task Cycle_classifies_mixed_batch_into_purged_and_retained()
    {
        // 3 specs eligible by date; 1 has no WO link → purged, 2 have WO
        // links → retained. Stats split cleanly across counters.
        var purgeId = await SeedTrashedRevAsync("SPEC-MIX-PURGE", "B", DateTime.UtcNow.AddDays(-31));
        var keepIdActive = await SeedTrashedRevAsync("SPEC-MIX-RETAIN-ACTIVE", "C", DateTime.UtcNow.AddDays(-40));
        await _fx.SeedWorkOrderAsync(keepIdActive, WoStatus.InProgress, "WO-A-1");
        var keepIdClosed = await SeedTrashedRevAsync("SPEC-MIX-RETAIN-CLOSED", "D", DateTime.UtcNow.AddDays(-50));
        await _fx.SeedWorkOrderAsync(keepIdClosed, WoStatus.Closed, "WO-C-1");

        var stats = await _svc.RunCycleAsync();

        Assert.Equal(3, stats.EligibleCount);
        Assert.Equal(1, stats.PurgedCount);
        Assert.Equal(2, stats.RetainedCount);
        Assert.Equal(0, stats.FailedCount);

        await AssertRevExistsAsync(purgeId,      expected: false);
        await AssertRevExistsAsync(keepIdActive, expected: true);
        await AssertRevExistsAsync(keepIdClosed, expected: true);

        // Only the actually-purged spec produces an audit row.
        var audit = Assert.Single(_audit.ByAction(AuditAction.SpecPurge));
        Assert.Equal(purgeId.ToString(), audit.TargetId);
    }

    // ── Rule #5 — Blob cleanup after EF cascade ───────────────────────

    [Fact]
    public async Task Cycle_deletes_blobs_associated_with_purged_revisions_drawings()
    {
        // Build a real revision with a drawing upload so a blob exists on disk.
        var (revId, storageKey) = await SeedTrashedRevWithDrawingAsync(
            specCode: "SPEC-BLOB-CLEANUP",
            revCode:  "B",
            trashedAt: DateTime.UtcNow.AddDays(-45));

        // Blob exists pre-cycle.
        Assert.True(await _blobStore.ExistsAsync(storageKey));

        var stats = await _svc.RunCycleAsync();

        Assert.Equal(1, stats.PurgedCount);
        Assert.Equal(1, stats.BlobsRemoved);
        Assert.Equal(0, stats.BlobsFailed);
        Assert.False(await _blobStore.ExistsAsync(storageKey));
        await AssertRevExistsAsync(revId, expected: false);
    }

    // ── Rule #6 — Idempotent ─────────────────────────────────────────

    [Fact]
    public async Task Cycle_is_idempotent_second_run_finds_nothing()
    {
        await SeedTrashedRevAsync("SPEC-IDEM-A", "B", DateTime.UtcNow.AddDays(-31));
        await SeedTrashedRevAsync("SPEC-IDEM-B", "C", DateTime.UtcNow.AddDays(-31));

        var first = await _svc.RunCycleAsync();
        var second = await _svc.RunCycleAsync();

        Assert.Equal(2, first.PurgedCount);
        Assert.Equal(0, second.EligibleCount);
        Assert.Equal(0, second.PurgedCount);
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private async Task<long> SeedTrashedRevAsync(string specCode, string revCode, DateTime trashedAtUtc)
        => await _fx.SeedRevisionAsync(specCode, revCode, isTrashed: true, trashedAt: trashedAtUtc);

    private async Task<(long revId, string storageKey)> SeedTrashedRevWithDrawingAsync(
        string specCode, string revCode, DateTime trashedAt)
    {
        // Direct seed to avoid dangling DbContext lifetimes that DrawingsService
        // would otherwise leave hanging until GC — SQLite's per-connection
        // locks intermittently bite the purge cycle if a writer connection is
        // still open.
        var revId = await _fx.SeedRevisionAsync(specCode, revCode);

        // 1. Persist a real Drawing + DrawingVersion row + put the blob on disk.
        using var db = _fx.NewContext();

        var drawing = new CCL.MES.Domain.Entities.Drawing
        {
            ProductRevisionId = revId,
            Kind              = DrawingKind.CustomerDrawing,
            Status            = DrawingStatus.Draft,
        };
        db.Drawings.Add(drawing);
        await db.SaveChangesAsync();

        var bytes = new byte[2048];
        new Random(7).NextBytes(bytes);
        using (var stream = new MemoryStream(bytes))
        {
            var put = await _blobStore.PutAsync(
                stream,
                suggestedKey: $"drawings/{revId}/{drawing.Id}/v1.pdf",
                contentType:  "application/pdf");

            var version = new CCL.MES.Domain.Entities.DrawingVersion
            {
                DrawingId   = drawing.Id,
                VersionNo   = 1,
                StorageKey  = put.Key,
                FileName    = "seed.pdf",
                FileSize    = put.SizeBytes,
                FileHash    = put.Sha256Hex,
                UploadedAt  = DateTime.UtcNow,
                UploadedBy  = "test-seed",
                Status      = DrawingVersionStatus.Draft,
            };
            db.DrawingVersions.Add(version);
            await db.SaveChangesAsync();

            // 2. Mark the rev trashed with the backdated TrashedAt.
            var rev = await db.ProductRevisions.FirstAsync(r => r.Id == revId);
            rev.IsTrashed = true;
            rev.TrashedAt = trashedAt;
            await db.SaveChangesAsync();

            return (revId, put.Key);
        }
    }

    private async Task AssertRevExistsAsync(long revId, bool expected)
    {
        using var db = _fx.NewContext();
        var exists = await db.ProductRevisions.AnyAsync(r => r.Id == revId);
        Assert.Equal(expected, exists);
    }

    private async Task AssertRevIsTrashedAsync(long revId, bool expected)
    {
        // Retain is a runtime classification — the spec's IsTrashed bit MUST
        // stay set (it didn't get restored; it just couldn't be purged).
        using var db = _fx.NewContext();
        var rev = await db.ProductRevisions.AsNoTracking().FirstAsync(r => r.Id == revId);
        Assert.Equal(expected, rev.IsTrashed);
    }
}
