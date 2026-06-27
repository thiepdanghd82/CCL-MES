using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using CCL.MES.Shared.Backup;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CCL.MES.Api.Services;

/// <summary>
/// P-Backup (Hybrid) — automated nightly backup worker. Port of the legacy
/// CCL.MES.Web BackupSchedulerService onto the Hybrid API surface (the only
/// shipping app). Covers the LOCAL "3 copies" of the IBM 3-2-1 rule; the
/// off-site copy stays a separate cron job (scripts/backup-offsite.{sh,ps1}).
///
/// Each cycle (daily at OPS_BACKUP_HOUR ICT, default 02:00):
///   1. Snapshot live SQLite via the existing online-safe
///      <see cref="BackupApiService.CreateSnapshot"/> (WAL — no lock).
///   2. Tarball &lt;DATA_DIR&gt;/blobs/ → &lt;DATA_DIR&gt;/Backup/Blobs/blobs_YYYYMMDD.tar.gz.
///   3. Verify the snapshot (<see cref="BackupVerifier"/>): integrity_check
///      + row-count anomaly vs live.
///   4. Prune snapshots + tarballs older than OPS_BACKUP_RETENTION_DAYS
///      (default 30) while always keeping OPS_BACKUP_MIN_KEEP (default 10).
///   5. Audit (BACKUP_CYCLE / BACKUP_FAILED) ALWAYS + optional webhook alert.
///
/// Activation: OPS_BACKUP_SCHEDULE=1 / Settings → Backup (default OFF).
///
/// DI: BackgroundService is a singleton; BackupApiService is a singleton too,
/// but IAuditWriter + BackupVerifier + IMesDbContext are Scoped, so each
/// cycle resolves them from a fresh scope.
/// </summary>
public sealed class BackupSchedulerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _httpFactory;
    private readonly BackupScheduleStore _store;
    private readonly IConfiguration _config;
    private readonly ILogger<BackupSchedulerService> _logger;

    private readonly bool _fbEnabled;
    private readonly int _fbHour;
    private readonly int _fbRetentionDays;
    private readonly int _fbMinKeep;
    private readonly string? _webhookUrl;
    private readonly bool _backupBlobs;
    private readonly TimeZoneInfo _tz;

    private BackupRunResultDto? _lastRun;
    private DateTime? _lastRunAtUtc;
    private string? _lastError;

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
        _config = config;
        _logger = logger;

        _fbEnabled = ReadFlag("OPS_BACKUP_SCHEDULE", config["Ops:Backup:Enabled"], false);
        _fbHour = ReadInt("OPS_BACKUP_HOUR", config["Ops:Backup:Hour"], 2, 0, 23);
        _fbRetentionDays = ReadInt("OPS_BACKUP_RETENTION_DAYS", config["Ops:Backup:RetentionDays"], 30, 1, 3650);
        _fbMinKeep = ReadInt("OPS_BACKUP_MIN_KEEP", config["Ops:Backup:MinKeep"], 10, 1, 1000);
        _backupBlobs = ReadFlag("OPS_BACKUP_BLOBS", config["Ops:Backup:Blobs"], true);
        _webhookUrl = Environment.GetEnvironmentVariable("OPS_BACKUP_WEBHOOK") ?? config["Ops:Backup:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(_webhookUrl)) _webhookUrl = null;

        _tz = ResolveIctTimeZone();
    }

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
                    await WaitForRescheduleOrStop(ct);
                    continue;
                }

                var delay = DelayUntilNextRun(eff.Hour);
                _logger.LogInformation(
                    "[backup] enabled — next cycle in {Minutes:0} min (target {Hour:00}:00 {Tz}, retention {Days}d keep ≥{Keep}, blobs={Blobs}, webhook={Webhook}).",
                    delay.TotalMinutes, eff.Hour, _tz.Id, eff.RetentionDays, eff.MinKeep,
                    _backupBlobs, _webhookUrl is null ? "off" : "on");

                var rescheduled = await DelayOrReschedule(delay, ct);
                if (rescheduled) continue;

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

    private async Task<bool> DelayOrReschedule(TimeSpan delay, CancellationToken ct)
    {
        var rescheduleTask = _reschedule.Task;
        var delayTask = Task.Delay(delay, ct);
        var done = await Task.WhenAny(delayTask, rescheduleTask);
        if (done == rescheduleTask)
        {
            _reschedule = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return true;
        }
        await delayTask;
        return false;
    }

    private async Task WaitForRescheduleOrStop(CancellationToken ct)
    {
        var rescheduleTask = _reschedule.Task;
        var stopTask = Task.Delay(Timeout.Infinite, ct);
        var done = await Task.WhenAny(rescheduleTask, stopTask);
        if (done == rescheduleTask)
            _reschedule = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        else
            await stopTask;
    }

    /// <summary>
    /// Run exactly one backup cycle against a fresh DI scope. Exposed for the
    /// "run now" endpoint + tests. Never throws — failures land in the result.
    /// </summary>
    public async Task<BackupRunResultDto> RunBackupCycleAsync(bool force = false, CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new BackupRunResultDto();

        using var scope = _scopes.CreateScope();
        var backup = scope.ServiceProvider.GetRequiredService<BackupApiService>();
        var verifier = scope.ServiceProvider.GetRequiredService<BackupVerifier>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();

        if (!backup.IsSqlite)
        {
            result.Ok = false;
            result.Error = "Provider is not SQLite — scheduler skips (use SSMS/maintenance plans for SQL Server).";
            _lastError = result.Error;
            _logger.LogWarning("[backup] {Error}", result.Error);
            return Finish(result, sw);
        }

        var eff = EffectiveSettings();
        var (backupDir, dbFile) = ResolveSqlitePaths();

        // ── Step 1: SQLite snapshot ──────────────────────────────────────
        var snap = backup.CreateSnapshot();
        if (snap is null)
        {
            result.Ok = false;
            result.Error = "sqlite snapshot failed — check server logs.";
            _lastError = result.Error;
            await audit.EmitAsync(AuditAction.BackupFailed, "system", "",
                targetType: "Backup", source: "Scheduler",
                detail: JsonSerializer.Serialize(new { step = "sqlite", error = result.Error }));
            await AlertAsync($"🚨 CCL-MES backup FAILED (sqlite): {result.Error}");
            return Finish(result, sw);
        }
        result.SqliteFile = snap.FileName;
        result.SqliteMb = Math.Round(snap.SizeBytes / 1024.0 / 1024.0, 2);
        var snapPath = Path.Combine(backupDir, snap.FileName);

        // ── Step 2: blob tarball — non-fatal ─────────────────────────────
        if (_backupBlobs)
        {
            try
            {
                result.BlobFile = TarBlobs(dbFile, backupDir, force);
                if (result.BlobFile is not null)
                    result.BlobMb = SizeMb(Path.Combine(BlobsBackupDir(backupDir), result.BlobFile));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[backup] blob tarball failed (non-fatal).");
            }
        }

        // ── Step 3: verify ───────────────────────────────────────────────
        var verify = await verifier.VerifyAsync(snapPath, ct);
        result.VerifyOk = verify.Ok;
        result.Integrity = verify.Integrity;
        if (!verify.Ok)
            await AlertAsync($"🚨 CCL-MES backup verify FAILED: {verify.Integrity}");
        else if (verify.Drops.Count > 0)
            await AlertAsync($"⚠️ CCL-MES backup row-count anomaly: {JsonSerializer.Serialize(verify.Drops)}");

        // ── Step 4: prune ────────────────────────────────────────────────
        try
        {
            result.Pruned = PruneOld(backupDir, eff.RetentionDays, eff.MinKeep)
                          + PruneOld(BlobsBackupDir(backupDir), eff.RetentionDays, eff.MinKeep);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[backup] prune failed (non-fatal).");
        }

        result.Ok = verify.Ok;
        Finish(result, sw);
        _lastError = result.Ok ? null : $"verify: {result.Integrity}";

        // ── Step 5: audit ALWAYS ─────────────────────────────────────────
        await audit.EmitAsync(
            result.Ok ? AuditAction.BackupCycle : AuditAction.BackupFailed,
            "system", "", targetType: "Backup", targetId: result.SqliteFile,
            source: "Scheduler",
            detail: JsonSerializer.Serialize(new
            {
                sqlite_file = result.SqliteFile,
                sqlite_mb = result.SqliteMb,
                blob_file = result.BlobFile,
                blob_mb = result.BlobMb,
                verify_ok = result.VerifyOk,
                integrity = result.Integrity,
                row_counts = verify.Counts,
                drops = verify.Drops.Count > 0 ? verify.Drops : null,
                pruned = result.Pruned,
                duration_ms = result.DurationMs,
            }));

        _logger.LogInformation(
            "[backup] cycle {Status} in {Ms}ms — sqlite={Sqlite} ({Mb}MB), blob={Blob}, pruned={Pruned}.",
            result.Ok ? "✓" : "✗", result.DurationMs, result.SqliteFile, result.SqliteMb,
            result.BlobFile ?? "—", result.Pruned);

        return result;
    }

    public BackupScheduleStatusDto GetStatus()
    {
        var eff = EffectiveSettings();
        DateTime? nextRunAtUtc = eff.Enabled ? DateTime.UtcNow.Add(DelayUntilNextRun(eff.Hour)) : null;
        return new BackupScheduleStatusDto
        {
            Enabled = eff.Enabled,
            Hour = eff.Hour,
            RetentionDays = eff.RetentionDays,
            MinKeep = eff.MinKeep,
            BlobsEnabled = _backupBlobs,
            WebhookConfigured = _webhookUrl is not null,
            TimeZone = _tz.Id,
            NextRunAtUtc = nextRunAtUtc,
            LastRunAtUtc = _lastRunAtUtc,
            LastRunOk = _lastRun?.Ok,
            LastSqliteFile = _lastRun?.SqliteFile,
            LastError = _lastError,
            PersistedAt = _store.ConfigPath(),
        };
    }

    public async Task<BackupScheduleStatusDto> SetScheduleAsync(
        BackupScheduleUpdateRequest req, string actor, IAuditWriter audit)
    {
        if (req.Hour is < 0 or > 23)
            throw new ArgumentOutOfRangeException(nameof(req), req.Hour, "Hour must be 0–23.");
        if (req.RetentionDays is < 1 or > 3650)
            throw new ArgumentOutOfRangeException(nameof(req), req.RetentionDays, "RetentionDays must be 1–3650.");
        if (req.MinKeep is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(req), req.MinKeep, "MinKeep must be 1–1000.");

        var cfg = _store.Read() ?? new BackupSchedulePersisted();
        if (req.Enabled.HasValue) cfg.Enabled = req.Enabled.Value;
        if (req.Hour.HasValue) cfg.Hour = req.Hour.Value;
        if (req.RetentionDays.HasValue) cfg.RetentionDays = req.RetentionDays.Value;
        if (req.MinKeep.HasValue) cfg.MinKeep = req.MinKeep.Value;
        cfg.UpdatedAtUtc = DateTime.UtcNow.ToString("o");
        cfg.UpdatedBy = actor;
        _store.Write(cfg);

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

    private BackupRunResultDto Finish(BackupRunResultDto r, System.Diagnostics.Stopwatch sw)
    {
        sw.Stop();
        r.DurationMs = sw.ElapsedMilliseconds;
        _lastRun = r;
        _lastRunAtUtc = DateTime.UtcNow;
        return r;
    }

    private TimeSpan DelayUntilNextRun(int hour)
    {
        var nowIct = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _tz);
        var targetIct = new DateTimeOffset(nowIct.Year, nowIct.Month, nowIct.Day, hour, 0, 0, nowIct.Offset);
        if (targetIct <= nowIct) targetIct = targetIct.AddDays(1);
        var delay = targetIct - nowIct;
        return delay < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : delay;
    }

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

        var tmp = outFile + ".tmp";
        using (var fs = File.Create(tmp))
        using (var gz = new GZipStream(fs, CompressionLevel.Optimal))
        {
            TarFile.CreateFromDirectory(blobsDir, gz, includeBaseDirectory: false);
        }
        File.Move(tmp, outFile, overwrite: true);
        return name;
    }

    private static int PruneOld(string dir, int retentionDays, int minKeep)
    {
        if (!Directory.Exists(dir)) return 0;
        var files = Directory.EnumerateFiles(dir)
            .Where(f => f.Contains(".bak", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            // Never prune a pre-restore safety snapshot automatically.
            .Where(f => !Path.GetFileName(f).StartsWith("pre-restore-", StringComparison.Ordinal))
            .Select(f => new FileInfo(f))
            .OrderByDescending(fi => fi.LastWriteTimeUtc)
            .ToList();

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var pruned = 0;
        foreach (var fi in files.Skip(minKeep))
        {
            if (fi.LastWriteTimeUtc >= cutoff) continue;
            try { fi.Delete(); pruned++; } catch { /* best-effort */ }
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
            using var content = new StringContent(
                JsonSerializer.Serialize(new { text }), System.Text.Encoding.UTF8, "application/json");
            await client.PostAsync(_webhookUrl, content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("[backup] webhook alert failed: {Msg}", ex.Message);
        }
    }

    private (string BackupDir, string DbFile) ResolveSqlitePaths()
    {
        var cs = _config.GetConnectionString("Default") ?? "";
        const string prefix = "Data Source=";
        if (!cs.StartsWith(prefix, StringComparison.Ordinal)) return ("", "");
        var dbPath = Path.GetFullPath(cs[prefix.Length..]);
        var dataDir = Path.GetDirectoryName(dbPath) ?? "";
        return (Path.Combine(dataDir, "Backup", "SQLite"), dbPath);
    }

    private static string BlobsBackupDir(string sqliteBackupDir)
    {
        var backupRoot = Path.GetDirectoryName(sqliteBackupDir) ?? sqliteBackupDir;
        return Path.Combine(backupRoot, "Blobs");
    }

    private static double SizeMb(string path)
    {
        try { return Math.Round(new FileInfo(path).Length / 1024.0 / 1024.0, 2); }
        catch { return 0; }
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
        return raw is "1" or "true" or "True" or "on" or "yes";
    }

    private static int ReadInt(string envKey, string? configValue, int def, int min, int max)
    {
        var raw = Environment.GetEnvironmentVariable(envKey) ?? configValue;
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return def;
        return Math.Clamp(v, min, max);
    }
}

/// <summary>Resolved schedule (persisted JSON layered over env/appsettings).</summary>
public readonly record struct EffectiveBackupSettings(
    bool Enabled, int Hour, int RetentionDays, int MinKeep);
