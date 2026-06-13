using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace CCL.MES.Web.Services;

/// <summary>
/// P-Backup (P2) — persistence for the admin-editable backup schedule.
/// Port of Ops Control v1.3 backupScheduler.js
/// <c>readPersistedConfig()</c>/<c>writePersistedConfig()</c>:
/// the schedule lives in a small JSON file under the data dir so edits via
/// Settings → Backup survive a process restart, and env/appsettings remain
/// the first-boot fallback.
///
/// File: <c>&lt;DATA_DIR&gt;/Library/SystemConfig/backup-schedule.json</c>
/// (same folder Ops Control uses). Written atomically (tmp + rename) so a
/// crash mid-write can't leave a half-parsed config.
/// </summary>
public sealed class BackupScheduleStore
{
    private readonly IConfiguration _config;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public BackupScheduleStore(IConfiguration config) => _config = config;

    /// <summary>Absolute path of the persisted config file.</summary>
    public string ConfigPath()
    {
        var dataDir = ResolveDataDir();
        return Path.Combine(dataDir, "Library", "SystemConfig", "backup-schedule.json");
    }

    /// <summary>
    /// Read the persisted config. Returns <c>null</c> when the file is
    /// absent or unparseable (caller then falls back to env/appsettings).
    /// </summary>
    public BackupSchedulePersisted? Read()
    {
        try
        {
            var path = ConfigPath();
            if (!File.Exists(path)) return null;
            var txt = File.ReadAllText(path);
            return JsonSerializer.Deserialize<BackupSchedulePersisted>(txt);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Persist the config atomically.</summary>
    public void Write(BackupSchedulePersisted cfg)
    {
        var path = ConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = $"{path}.tmp.{Environment.ProcessId}";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    // Resolve <DATA_DIR> the same way BackupService does — dirname of the
    // SQLite "Data Source" — so the config file sits beside the DB + backups.
    private string ResolveDataDir()
    {
        var cs = _config.GetConnectionString("Default");
        if (string.IsNullOrEmpty(cs)) return Directory.GetCurrentDirectory();
        var source = new SqliteConnectionStringBuilder(cs).DataSource;
        if (string.IsNullOrEmpty(source)) return Directory.GetCurrentDirectory();
        return Path.GetDirectoryName(Path.GetFullPath(source)) ?? Directory.GetCurrentDirectory();
    }
}

/// <summary>
/// The persisted shape. Nullable so "field absent → use env/appsettings
/// fallback" is distinguishable from an explicit value. Mirrors the
/// editable subset of Ops Control's schedule (enabled/hour/retention);
/// webhook + blobs stay env/appsettings-only (operational, not per-edit).
/// </summary>
public sealed class BackupSchedulePersisted
{
    public bool? Enabled { get; set; }
    public int? Hour { get; set; }
    public int? RetentionDays { get; set; }
    public int? MinKeep { get; set; }
    public string? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}
