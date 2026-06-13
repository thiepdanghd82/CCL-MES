using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CCL.MES.Web.Services;

/// <summary>
/// P-Backup — automated nightly backup worker. .NET port of Ops Control
/// v1.3 <c>server/services/backupScheduler.js</c> (the LOCAL "3 copies"
/// portion of the IBM 3-2-1 rule). Off-site rsync stays a separate cron
/// job (scripts/backup-offsite.{sh,ps1}).
///
/// Each cycle (daily at <c>OPS_BACKUP_HOUR</c> ICT, default 02:00):
///   1. Snapshot live SQLite via the existing online-safe
///      <see cref="BackupService.CreateSnapshotAsync"/> (WAL — no lock).
///   2. Tarball <c>&lt;DATA_DIR&gt;/blobs/</c> (drawings/CAD) →
///      <c>&lt;DATA_DIR&gt;/Backup/Blobs/blobs_YYYYMMDD.tar.gz</c>.
///   3. Verify the snapshot (<see cref="BackupVerifier"/>): integrity_check
///      + row-count anomaly vs live.
///   4. Prune snapshots + tarballs older than <c>OPS_BACKUP_RETENTION_DAYS</c>
///      (default 30) while always keeping the <c>OPS_BACKUP_MIN_KEEP</c>
///      (default 10) most recent — so a quiet month never wipes the only
///      backups on disk (Ops Control Sprint 1.7 lesson).
///   5. Audit the outcome (BACKUP_CYCLE / BACKUP_FAILED) ALWAYS, plus a
///      best-effort webhook alert when <c>OPS_BACKUP_WEBHOOK</c> is set.
///
/// Activation: <c>OPS_BACKUP_SCHEDULE=1</c> (default OFF for dev), mirroring
/// Ops Control. When disabled the worker logs once and exits its loop.
///
/// DI lifetime: a <see cref="BackgroundService"/> is a singleton, but
/// <see cref="BackupService"/>/<see cref="IAuditWriter"/>/<see cref="BackupVerifier"/>
/// are Scoped — so every cycle resolves them from a fresh
/// <see cref="IServiceScope"/> (same pattern as SpecTrashPurgeService).
/// </summary>
public sealed class BackupSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _httpFactory;
    private readonly BackupScheduleStore _store;
    private readonly ILogger<BackupSchedulerService> _logger;

    // First-boot fallbacks from env/appsettings. Persisted JSON (edited via
    // Settings → Backup) takes precedence — see EffectiveSettings().
    private readonly bool _fbEnabled;
    private readonly int _fbHour;
    private readonly int _fbRetentionDays;
    private readonly int _fbMinKeep;
    private readonly string? _webhookUrl;
    private readonly bool _backupBlobs;
    private readonly TimeZoneInfo _tz;

    private BackupCycleSummary? _lastRun;
    private string? _lastError;

    // Reschedule signal — completed when the schedule changes so ExecuteAsync
    // re-arms its timer WITHOUT a process restart (Ops Control stop+start).
    private volatile TaskCompletionSource _reschedule =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BackupSchedulerService(
        IServiceScopeFactory scopes,
        IHttpClientFactory httpFactory,
        BackupScheduleStore store,
        IConfiguration config,
        ILogger<BackupSchedulerService> logger)
    {
        _scopes = scopes;
        _httpFactory = httpFactory;
        _store = store;
        _logger = logger;

        // env var first, config (Ops:Backup:*) fallback — same precedence
        // SpecTrashPurgeService uses for OPS_SPEC_TRASH_RETENTION_DAYS.
        _fbEnabled = ReadFlag("OPS_BACKUP_SCHEDULE", config["Ops:Backup:Enabled"], defaultValue: false);
        _fbHour = ReadInt("OPS_BACKUP_HOUR", config["Ops:Backup:Hour"], def: 2, min: 0, max: 23);
        _fbRetentionDays = ReadInt("OPS_BACKUP_RETENTION_DAYS", config["Ops:Backup:RetentionDays"], def: 30, min: 1, max: 3650);
        _fbMinKeep = ReadInt("OPS_BACKUP_MIN_KEEP", config["Ops:Backup:MinKeep"], def: 10, min: 1, max: 1000);
        _backupBlobs = ReadFlag("OPS_BACKUP_BLOBS", config["Ops:Backup:Blobs"], defaultValue: true);
        _webhookUrl = Environment.GetEnvironmentVariable("OPS_BACKUP_WEBHOOK") ?? config["Ops:Backup:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(_webhookUrl)) _webhookUrl = null;

        // Backup hour is in LOCAL business time (ICT). .NET 10 ships ICU so
        // the IANA id resolves on Windows too; fall back to a fixed +07:00.
        _tz = ResolveIctTimeZone();
    }

    /// <summary>
    /// Effective schedule = persisted JSON (admin edits) layered over the
    /// env/appsettings first-boot fallback. Recomputed every loop iteration
    /// + on demand so a SetSchedule() edit takes effect immediately.
    /// </summary>
    public EffectiveBackupSettings EffectiveSettings()
    {
        var p = _store.Read();
        return new EffectiveBackupSettings(
            Enabled: p?.Enabled ?? _fbEnabled,
            Hour: Math.Clamp(p?.Hour ?? _fbHour, 0, 23),
            RetentionDays: Math.Clamp(p?.RetentionDays ?? _fbRetentionDays, 1, 3650),
            MinKeep: Math.Clamp(p?.MinKeep ?? _fbMinKeep, 1, 1000));
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var eff = EffectiveSettings();

                if (!eff.Enabled)
                {
                    _logger.LogInformation(
                        "[backup] scheduler disabled — idle until enabled via Settings → Backup or OPS_BACKUP_SCHEDULE=1.");
                    // Park until the schedule changes (or shutdown). No timer
                    // spinning while disabled.
                    await WaitForRescheduleOrStop(ct);
                    continue;
                }

                var delay = DelayUntilNextRun(eff.Hour);
                _logger.LogInformation(
                    "[backup] enabled — next cycle in {Minutes:0} min (target {Hour:00}:00 {Tz}, retention {Days}d keep ≥{Keep}, blobs={Blobs}, webhook={Webhook}).",
                    delay.TotalMinutes, eff.Hour, _tz.Id, eff.RetentionDays, eff.MinKeep,
                    _backupBlobs, _webhookUrl is null ? "off" : "on");

                // Wait for the timer OR a schedule change, whichever first.
                var rescheduled = await DelayOrReschedule(delay, ct);
                if (rescheduled) continue; // settings changed — recompute

                try
                {
                    await RunBackupCycleAsync(force: false, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // One bad cycle must never tank the worker — log + retry
                    // on the next daily tick.
                    _lastError = ex.Message;
                    _logger.LogError(ex, "[backup] cycle threw — will retry next interval.");
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _logger.LogInformation("[backup] scheduler stopping.");
        }
    }

    // Await Task.Delay(delay) OR a reschedule signal. Returns true when a
    // reschedule fired (caller recomputes), false when the timer elapsed.
    private async Task<bool> DelayOrReschedule(TimeSpan delay, CancellationToken ct)
    {
        var rescheduleTask = _reschedule.Task;
        var delayTask = Task.Delay(delay, ct);
        var done = await Task.WhenAny(delayTask, rescheduleTask);
        if (done == rescheduleTask)
        {
            // Re-arm the signal for the next wait.
            _reschedule = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
        await delayTask; // observe cancellation if it was the delay
        return false;
    }

    // Park indefinitely until a reschedule signal or shutdown.
    private async Task WaitForRescheduleOrStop(CancellationToken ct)
    {
        var rescheduleTask = _reschedule.Task;
        var stopTask = Task.Delay(Timeout.Infinite, ct);
        var done = await Task.WhenAny(rescheduleTask, stopTask);
        if (done == rescheduleTask)
            _reschedule = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        else
            await stopTask; // propagate cancellation
    }

    /// <summary>
    /// Run exactly one backup cycle against a fresh DI scope. Exposed so a
    /// future admin "run now" endpoint (P2) and tests can trigger it
    /// directly. Never throws — failures are captured in the summary +
    /// audited + alerted.
    /// </summary>
    public async Task<BackupCycleSummary> RunBackupCycleAsync(bool force = false, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var summary = new BackupCycleSummary { StartedAtUtc = DateTime.UtcNow };

        using var scope = _scopes.CreateScope();
        var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
        var verifier = scope.ServiceProvider.GetRequiredService<BackupVerifier>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

        if (!backup.IsSqlite)
        {
            summary.Ok = false;
            summary.Error = "Provider is not SQLite — scheduler skips (use SSMS/maintenance plans for SQL Server).";
            _lastError = summary.Error;
            _logger.LogWarning("[backup] {Error}", summary.Error);
            return summary;
        }

        var eff = EffectiveSettings();
        var (backupDir, dbFile) = backup.ResolveSqlitePaths();

        // ── Step 1: SQLite snapshot (reuse the proven online-backup path) ─
        try
        {
            var result = await backup.CreateSnapshotAsync(SystemActor());
            if (result.Outcome != BackupOutcome.Success || result.FileName is null)
            {
                summary.Ok = false;
                summary.Error = $"sqlite snapshot: {result.Outcome} {result.FileName}";
                _lastError = summary.Error;
                await audit.EmitAsync(AuditAction.BackupFailed, "system", "",
                    targetType: "Backup", source: "Scheduler",
                    detail: JsonSerializer.Serialize(new { step = "sqlite", error = summary.Error }));
                await AlertAsync($"🚨 CCL-MES backup FAILED (sqlite): {summary.Error}");
                return summary;
            }
            summary.SqliteFile = result.FileName;
            summary.SqlitePath = Path.Combine(backupDir, result.FileName);
            summary.SqliteMb = SizeMb(summary.SqlitePath);
        }
        catch (Exception ex)
        {
            summary.Ok = false;
            summary.Error = $"sqlite snapshot threw: {ex.Message}";
            _lastError = summary.Error;
            await audit.EmitAsync(AuditAction.BackupFailed, "system", "",
                targetType: "Backup", source: "Scheduler",
                detail: JsonSerializer.Serialize(new { step = "sqlite", error = ex.Message }));
            await AlertAsync($"🚨 CCL-MES backup FAILED (sqlite): {ex.Message}");
            return summary;
        }

        // ── Step 2: blob tarball (drawings/CAD) — non-fatal on failure ────
        if (_backupBlobs)
        {
            try
            {
                summary.BlobFile = TarBlobs(dbFile, backupDir, force);
                if (summary.BlobFile is not null)
                    summary.BlobMb = SizeMb(Path.Combine(BlobsBackupDir(backupDir), summary.BlobFile));
            }
            catch (Exception ex)
            {
                summary.BlobError = ex.Message;
                _logger.LogWarning(ex, "[backup] blob tarball failed (non-fatal).");
            }
        }

        // ── Step 3: verify the fresh snapshot ─────────────────────────────
        var verify = await verifier.VerifyAsync(summary.SqlitePath!, ct);
        summary.VerifyOk = verify.Ok;
        summary.Integrity = verify.Integrity;
        summary.RowCounts = verify.Counts;
        summary.Drops = verify.Drops;
        if (!verify.Ok)
            await AlertAsync($"🚨 CCL-MES backup verify FAILED: {verify.Integrity}");
        else if (verify.Drops.Count > 0)
            await AlertAsync($"⚠️ CCL-MES backup row-count anomaly: {JsonSerializer.Serialize(verify.Drops)}");

        // ── Step 4: prune old backups ─────────────────────────────────────
        try
        {
            summary.Pruned = PruneOld(backupDir, eff.RetentionDays, eff.MinKeep)
                           + PruneOld(BlobsBackupDir(backupDir), eff.RetentionDays, eff.MinKeep);
        }
        catch (Exception ex)
        {
            summary.PruneError = ex.Message;
            _logger.LogWarning(ex, "[backup] prune failed (non-fatal).");
        }

        sw.Stop();
        summary.DurationMs = sw.ElapsedMilliseconds;
        summary.Ok = verify.Ok; // sqlite already succeeded; gate overall on verify
        _lastRun = summary;
        _lastError = summary.Ok ? null : $"verify: {summary.Integrity}";

        // ── Step 5: audit ALWAYS (even with no webhook configured) ────────
        await audit.EmitAsync(
            summary.Ok ? AuditAction.BackupCycle : AuditAction.BackupFailed,
            "system", "", targetType: "Backup", targetId: summary.SqliteFile,
            source: "Scheduler",
            detail: JsonSerializer.Serialize(new
            {
                sqlite_file = summary.SqliteFile,
                sqlite_mb = summary.SqliteMb,
                blob_file = summary.BlobFile,
                blob_mb = summary.BlobMb,
                verify_ok = summary.VerifyOk,
                integrity = summary.Integrity,
                row_counts = summary.RowCounts,
                drops = summary.Drops.Count > 0 ? summary.Drops : null,
                pruned = summary.Pruned,
                duration_ms = summary.DurationMs,
            }));

        _logger.LogInformation(
            "[backup] cycle {Status} in {Ms}ms — sqlite={Sqlite} ({Mb}MB), blob={Blob}, pruned={Pruned}.",
            summary.Ok ? "✓" : "✗", summary.DurationMs, summary.SqliteFile, summary.SqliteMb,
            summary.BlobFile ?? "—", summary.Pruned);

        return summary;
    }

    /// <summary>Last cycle summary (for the status endpoint / health check).</summary>
    public BackupCycleSummary? LastRun => _lastRun;

    /// <summary>
    /// Current scheduler status for the admin UI: effective settings +
    /// next-run preview (computed independent of timer state so the UI can
    /// show "next at 02:00 in ~7h" right after a restart) + last cycle.
    /// </summary>
    public BackupScheduleStatus GetStatus()
    {
        var eff = EffectiveSettings();
        DateTime? nextRunAtUtc = null;
        if (eff.Enabled)
            nextRunAtUtc = DateTime.UtcNow.Add(DelayUntilNextRun(eff.Hour));
        return new BackupScheduleStatus(
            Enabled: eff.Enabled,
            Hour: eff.Hour,
            RetentionDays: eff.RetentionDays,
            MinKeep: eff.MinKeep,
            BlobsEnabled: _backupBlobs,
            WebhookConfigured: _webhookUrl is not null,
            TimeZone: _tz.Id,
            NextRunAtUtc: nextRunAtUtc,
            LastRun: _lastRun,
            LastError: _lastError,
            PersistedAt: _store.ConfigPath());
    }

    /// <summary>
    /// Admin edit of the schedule. Validates, persists to backup-schedule.json,
    /// re-arms the running worker (no restart), and audits the change.
    /// Any null arg leaves that field unchanged. Returns the new status.
    /// </summary>
    public async Task<BackupScheduleStatus> SetScheduleAsync(
        bool? enabled, int? hour, int? retentionDays, int? minKeep,
        string actor, IAuditWriter audit)
    {
        if (hour is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(hour), hour, "Hour must be 0–23.");
        if (retentionDays is < 1 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(retentionDays), retentionDays, "RetentionDays must be 1–3650.");
        if (minKeep is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(minKeep), minKeep, "MinKeep must be 1–1000.");

        var cfg = _store.Read() ?? new BackupSchedulePersisted();
        if (enabled.HasValue) cfg.Enabled = enabled.Value;
        if (hour.HasValue) cfg.Hour = hour.Value;
        if (retentionDays.HasValue) cfg.RetentionDays = retentionDays.Value;
        if (minKeep.HasValue) cfg.MinKeep = minKeep.Value;
        cfg.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
        cfg.UpdatedBy = actor;
        _store.Write(cfg);

        // Re-arm the worker loop so the new schedule takes effect now.
        _reschedule.TrySetResult();

        var eff = EffectiveSettings();
        await audit.EmitAsync(
            AuditAction.BackupScheduleChange, actor, "", targetType: "Backup", source: "Web",
            detail: JsonSerializer.Serialize(new
            {
                enabled = eff.Enabled,
                hour = eff.Hour,
                retention_days = eff.RetentionDays,
                min_keep = eff.MinKeep,
            }));
        return GetStatus();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private TimeSpan DelayUntilNextRun(int hour)
    {
        var nowIct = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tz);
        var targetIct = new DateTimeOffset(
            nowIct.Year, nowIct.Month, nowIct.Day, hour, 0, 0, nowIct.Offset);
        if (targetIct <= nowIct) targetIct = targetIct.AddDays(1);
        var delay = targetIct - nowIct;
        // Floor at 1 minute so a clock-edge race can't spin a tight loop.
        return delay < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay;
    }

    // tar czf the blobs dir. One tarball per day; skip if today's exists
    // (unless force). Returns the basename, or null when there's nothing to
    // back up. Excludes nothing (no secrets live under blobs/).
    private string? TarBlobs(string dbFile, string backupDir, bool force)
    {
        var dataDir = Path.GetDirectoryName(dbFile);
        if (string.IsNullOrEmpty(dataDir)) return null;
        var blobsDir = Path.Combine(dataDir, "blobs");
        if (!Directory.Exists(blobsDir)) return null;

        var outDir = BlobsBackupDir(backupDir);
        Directory.CreateDirectory(outDir);
        var ts = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var name = $"blobs_{ts}.tar.gz";
        var outFile = Path.Combine(outDir, name);
        if (File.Exists(outFile) && !force) return name;

        // Write to a temp file then atomic-rename so a crash mid-tar never
        // leaves a half-written tarball that looks complete.
        var tmp = outFile + ".tmp";
        using (var fs = File.Create(tmp))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        {
            TarFile.CreateFromDirectory(blobsDir, gz, includeBaseDirectory: false);
        }
        File.Move(tmp, outFile, overwrite: true);
        return name;
    }

    // Prune *.sqlite snapshots / *.tar.gz tarballs older than retention,
    // always keeping the most-recent minKeep files (Ops Control rule).
    private static int PruneOld(string dir, int retentionDays, int minKeep)
    {
        if (!Directory.Exists(dir)) return 0;
        var files = Directory.EnumerateFiles(dir)
            .Where(f => f.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
                     || f.Contains(".bak", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            .Select(f => new FileInfo(f))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .ToList();

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var pruned = 0;
        // Skip the first minKeep (newest) — only consider the rest.
        foreach (var fi in files.Skip(minKeep))
        {
            if (fi.LastWriteTimeUtc >= cutoff) continue;
            try { fi.Delete(); pruned++; }
            catch { /* best-effort */ }
        }
        return pruned;
    }

    private async Task AlertAsync(string text)
    {
        if (_webhookUrl is null) return;
        try
        {
            using var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var payload = JsonSerializer.Serialize(new { text });
            using var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            await client.PostAsync(_webhookUrl, content);
        }
        catch (Exception ex)
        {
            // Webhook failure must never abort the cycle (it already
            // happened) — log and move on.
            _logger.LogWarning("[backup] webhook alert failed: {Msg}", ex.Message);
        }
    }

    private static string BlobsBackupDir(string sqliteBackupDir)
    {
        // sqliteBackupDir = <DATA_DIR>/Backup/SQLite → sibling Backup/Blobs.
        var backupRoot = Path.GetDirectoryName(sqliteBackupDir) ?? sqliteBackupDir;
        return Path.Combine(backupRoot, "Blobs");
    }

    private static double SizeMb(string path)
    {
        try { return Math.Round(new FileInfo(path).Length / 1024.0 / 1024.0, 2); }
        catch { return 0; }
    }

    // A synthetic authenticated principal so BackupService.CreateSnapshotAsync
    // records actor="system" (its AuditIdentity() returns "anonymous" for an
    // unauthenticated principal).
    private static ClaimsPrincipal SystemActor()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, "system"),
            new Claim(ClaimTypes.Role, ""),
        }, authenticationType: "Scheduler");
        return new ClaimsPrincipal(identity);
    }

    private static TimeZoneInfo ResolveIctTimeZone()
    {
        foreach (var id in new[] { "Asia/Ho_Chi_Minh", "SE Asia Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch { /* try next */ }
        }
        return TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "ICT", "ICT");
    }

    private static bool ReadFlag(string envKey, string? configValue, bool defaultValue)
    {
        var raw = Environment.GetEnvironmentVariable(envKey) ?? configValue;
        if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
        raw = raw.Trim();
        // Treat "1"/"true"/"on"/"yes" as enabled (mirrors Ops Control "===1").
        return raw is "1" or "true" or "True" or "on" or "yes";
    }

    private static int ReadInt(string envKey, string? configValue, int def, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envKey) ?? configValue;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return def;
        return Math.Clamp(v, min, max);
    }
}

/// <summary>Outcome of one backup cycle — logged, audited, and (P2) served via status API.</summary>
public sealed class BackupCycleSummary
{
    public DateTime StartedAtUtc { get; set; }
    public bool Ok { get; set; }
    public string? Error { get; set; }

    public string? SqliteFile { get; set; }
    public string? SqlitePath { get; set; }
    public double SqliteMb { get; set; }

    public string? BlobFile { get; set; }
    public double BlobMb { get; set; }
    public string? BlobError { get; set; }

    public bool VerifyOk { get; set; }
    public string Integrity { get; set; } = "";
    public IReadOnlyDictionary<string, long> RowCounts { get; set; } = new Dictionary<string, long>();
    public IReadOnlyDictionary<string, RowCountDrop> Drops { get; set; } = new Dictionary<string, RowCountDrop>();

    public int Pruned { get; set; }
    public string? PruneError { get; set; }
    public long DurationMs { get; set; }
}

/// <summary>Resolved schedule (persisted JSON layered over env/appsettings).</summary>
public readonly record struct EffectiveBackupSettings(
    bool Enabled, int Hour, int RetentionDays, int MinKeep);

/// <summary>Scheduler status surfaced to the admin Settings → Backup page.</summary>
public sealed record BackupScheduleStatus(
    bool Enabled,
    int Hour,
    int RetentionDays,
    int MinKeep,
    bool BlobsEnabled,
    bool WebhookConfigured,
    string TimeZone,
    DateTime? NextRunAtUtc,
    BackupCycleSummary? LastRun,
    string? LastError,
    string PersistedAt);
