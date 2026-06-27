using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace CCL.MES.Api.Services;

/// <summary>
/// P-Backup (Hybrid) — persistence for the admin-editable backup schedule.
/// The schedule lives in a small JSON file under the data dir so edits via
/// Settings → Backup survive a process restart; env/appsettings are the
/// first-boot fallback.
///
/// File: <c>&lt;DATA_DIR&gt;/Library/SystemConfig/backup-schedule.json</c>
/// (same convention as the legacy Web port). Written atomically.
/// </summary>
public sealed class BackupScheduleStore
{
    private readonly IConfiguration _config;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public BackupScheduleStore(IConfiguration config) => _config = config;

    public string ConfigPath()
    {
        var dataDir = ResolveDataDir();
        return Path.Combine(dataDir, "Library", "SystemConfig", "backup-schedule.json");
    }

    public BackupSchedulePersisted? Read()
    {
        try
        {
            var path = ConfigPath();
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<BackupSchedulePersisted>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public void Write(BackupSchedulePersisted cfg)
    {
        var path = ConfigPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = $"{path}.tmp.{Environment.ProcessId}";
        File.WriteAllText(tmp, JsonSerializer.Serialize(cfg, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }

    // Resolve <DATA_DIR> the same way BackupApiService.ResolveSqlitePath does
    // (dirname of the "Data Source=" path) so the config sits beside the DB.
    private string ResolveDataDir()
    {
        var cs = _config.GetConnectionString("Default") ?? "";
        const string prefix = "Data Source=";
        if (!cs.StartsWith(prefix, StringComparison.Ordinal)) return Directory.GetCurrentDirectory();
        var dbPath = Path.GetFullPath(cs[prefix.Length..]);
        return Path.GetDirectoryName(dbPath) ?? Directory.GetCurrentDirectory();
    }
}

/// <summary>Persisted shape — nullable so "absent → fallback" is distinguishable.</summary>
public sealed class BackupSchedulePersisted
{
    public bool? Enabled { get; set; }
    public int? Hour { get; set; }
    public int? RetentionDays { get; set; }
    public int? MinKeep { get; set; }
    public string? UpdatedAtUtc { get; set; }
    public string? UpdatedBy { get; set; }
}
