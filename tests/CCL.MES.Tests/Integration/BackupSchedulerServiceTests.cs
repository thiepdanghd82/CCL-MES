using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Infrastructure;
using CCL.MES.Tests.Integration._Support;
using CCL.MES.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CCL.MES.Tests.Integration;

/// <summary>
/// P-Backup — end-to-end verification of the automated backup cycle
/// (BackupSchedulerService.RunBackupCycleAsync). Mirrors the Ops Control
/// v1.3 backupScheduler.js contract on the .NET port:
///   - a snapshot lands in &lt;DATA_DIR&gt;/Backup/SQLite/,
///   - the snapshot passes integrity_check with no row-count anomaly,
///   - a BACKUP_CYCLE audit row fires (Source=Scheduler), and
///   - the summary reports success.
///
/// Uses an isolated /tmp SQLite DB (never touches live ccl_mes.db — the
/// A→B→C lesson) and a real DI scope factory so the Scoped-service
/// resolution inside the cycle is exercised exactly as in production.
/// </summary>
public sealed class BackupSchedulerServiceTests : IClassFixture<IsolatedDbFixture>
{
    private readonly IsolatedDbFixture _fx;

    public BackupSchedulerServiceTests(IsolatedDbFixture fx) => _fx = fx;

    private ServiceProvider BuildProvider(InMemoryAuditWriter audit)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:Default"] = $"Data Source={_fx.DbPath}",
                ["Ops:Backup:Enabled"] = "false", // RunBackupCycleAsync bypasses the gate
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<IAuditWriter>(audit);
        services.AddScoped<IMesDbContext>(_ => new MesDbContext(_fx.Options));
        services.AddScoped<BackupService>();
        services.AddScoped<BackupVerifier>();
        services.AddSingleton<BackupScheduleStore>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RunBackupCycle_creates_verified_snapshot_and_audits()
    {
        var audit = new InMemoryAuditWriter();
        using var provider = BuildProvider(audit);

        var scheduler = new BackupSchedulerService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<BackupScheduleStore>(),
            provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BackupSchedulerService>>());

        var summary = await scheduler.RunBackupCycleAsync(force: true);

        // Cycle succeeded overall.
        Assert.True(summary.Ok, $"cycle not ok: {summary.Error} / integrity={summary.Integrity}");

        // A snapshot file was written under <DATA_DIR>/Backup/SQLite/.
        Assert.NotNull(summary.SqliteFile);
        Assert.NotNull(summary.SqlitePath);
        Assert.True(File.Exists(summary.SqlitePath!), "snapshot file missing on disk");
        Assert.True(summary.SqliteMb >= 0);

        // Verify passed cleanly (integrity ok, no >10% row-count drop).
        Assert.True(summary.VerifyOk);
        Assert.Equal("ok", summary.Integrity, ignoreCase: true);
        Assert.Empty(summary.Drops);

        // Core tables were counted and match live (seed has 1 product + 1 rev).
        Assert.True(summary.RowCounts.ContainsKey("Products"));
        Assert.True(summary.RowCounts.ContainsKey("ProductRevisions"));

        // A BACKUP_CYCLE audit row fired from the scheduler.
        var cycleRows = audit.ByAction(AuditAction.BackupCycle).ToList();
        Assert.Single(cycleRows);
        Assert.Equal("Scheduler", cycleRows[0].Source);
        Assert.Equal("system", cycleRows[0].Actor);
        Assert.DoesNotContain(audit.ByAction(AuditAction.BackupFailed),
            _ => true); // no failure rows on the happy path

        // The snapshot itself also emitted a BACKUP_CREATE via BackupService.
        Assert.NotEmpty(audit.ByAction(AuditAction.BackupCreate));
    }

    [Fact]
    public async Task RunBackupCycle_detects_row_count_anomaly_vs_live()
    {
        var audit = new InMemoryAuditWriter();
        using var provider = BuildProvider(audit);

        var scheduler = new BackupSchedulerService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IHttpClientFactory>(),
            provider.GetRequiredService<BackupScheduleStore>(),
            provider.GetRequiredService<IConfiguration>(),
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BackupSchedulerService>>());

        // First snapshot at baseline.
        var first = await scheduler.RunBackupCycleAsync(force: true);
        Assert.True(first.VerifyOk);

        // Now blow up the live row count so a NEW snapshot (taken after the
        // growth) has far more rows than... wait — anomaly is live > backup.
        // Add many ProductRevisions to live, THEN verify the FIRST (smaller)
        // snapshot against the now-larger live DB via a fresh verifier.
        // Unique RevisionCode per row — the (ProductId, RevisionCode) index
        // is unique, so reusing "A" 50× would violate it.
        for (var i = 0; i < 50; i++)
            await _fx.SeedRevisionAsync($"SPEC-GROW-{i}", $"R{i}");

        using var scope = provider.CreateScope();
        var verifier = scope.ServiceProvider.GetRequiredService<BackupVerifier>();
        var verify = await verifier.VerifyAsync(first.SqlitePath!);

        Assert.True(verify.Ok); // integrity still fine — it's a valid older snapshot
        Assert.True(verify.Drops.ContainsKey("ProductRevisions"),
            "expected a >10% row-count drop on ProductRevisions vs grown live DB");
    }

    [Fact]
    public async Task SetSchedule_persists_and_status_reflects_it()
    {
        var audit = new InMemoryAuditWriter();
        using var provider = BuildProvider(audit);
        var scheduler = NewScheduler(provider);

        // Default (from in-memory config Ops:Backup:Enabled=false).
        Assert.False(scheduler.GetStatus().Enabled);

        var status = await scheduler.SetScheduleAsync(
            enabled: true, hour: 4, retentionDays: 14, minKeep: 7,
            actor: "admin", audit: audit);

        // Status reflects the edit immediately + computes a next-run preview.
        Assert.True(status.Enabled);
        Assert.Equal(4, status.Hour);
        Assert.Equal(14, status.RetentionDays);
        Assert.Equal(7, status.MinKeep);
        Assert.NotNull(status.NextRunAtUtc);

        // Persisted to backup-schedule.json — survives a fresh scheduler
        // instance (simulating a process restart).
        Assert.True(File.Exists(status.PersistedAt));
        var reloaded = NewScheduler(provider).GetStatus();
        Assert.True(reloaded.Enabled);
        Assert.Equal(4, reloaded.Hour);

        // BACKUP_SCHEDULE_CHANGE audit row fired.
        Assert.Single(audit.ByAction(AuditAction.BackupScheduleChange));
    }

    [Fact]
    public async Task SetSchedule_rejects_invalid_hour()
    {
        var audit = new InMemoryAuditWriter();
        using var provider = BuildProvider(audit);
        var scheduler = NewScheduler(provider);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            scheduler.SetScheduleAsync(enabled: true, hour: 99, retentionDays: null,
                minKeep: null, actor: "admin", audit: audit));

        // Nothing persisted / audited on a rejected edit.
        Assert.Empty(audit.ByAction(AuditAction.BackupScheduleChange));
    }

    private static BackupSchedulerService NewScheduler(ServiceProvider provider) => new(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<IHttpClientFactory>(),
        provider.GetRequiredService<BackupScheduleStore>(),
        provider.GetRequiredService<IConfiguration>(),
        provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BackupSchedulerService>>());
}
