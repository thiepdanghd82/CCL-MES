using Microsoft.Data.Sqlite;

namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 6 Bước 2B — manual backup workflow for the Settings → Backup tab.
/// Behavior intentionally limited to non-destructive operations:
///   - SQLite: snapshot the live DB file to a sibling .db.bak.snapshot-&lt;ts&gt;
///     and enumerate existing snapshots.
///   - SQL Server: every operation returns <see cref="BackupOutcome.SqlServerUnsupported"/>;
///     the UI surfaces a localized message pointing operators at SSMS /
///     maintenance plans.
/// Restore is deliberately out of scope this Bước — replacing the live DB
/// file is a destructive operation that would warrant its own confirmation
/// gate + audit trail (filed as a Bước 5 follow-up).
/// </summary>
public class BackupService
{
    private readonly IConfiguration _config;
    public BackupService(IConfiguration config) => _config = config;

    public string Provider =>
        _config["Database:Provider"]?.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) == true
            ? "SqlServer"
            : "Sqlite";

    public bool IsSqlite => Provider == "Sqlite";

    /// <summary>
    /// Take a snapshot of the live SQLite DB. Returns the resulting filename
    /// (basename only) on success, or an outcome enum + null filename
    /// otherwise. Uses SQLite's online backup API so a snapshot is safe to
    /// take while the server is serving traffic.
    /// </summary>
    public BackupResult CreateSnapshot()
    {
        if (!IsSqlite) return new BackupResult(BackupOutcome.SqlServerUnsupported, null);

        var (dir, dbFile) = ResolveSqlitePath();
        if (string.IsNullOrEmpty(dbFile) || !File.Exists(dbFile))
            return new BackupResult(BackupOutcome.SourceMissing, null);

        var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var name = $"ccl_mes.db.bak.snapshot-{ts}";
        var target = Path.Combine(dir, name);

        try
        {
            // SQLite online backup — safer than File.Copy because it
            // guarantees a consistent snapshot even if other connections
            // are mid-write.
            using var src = new SqliteConnection($"Data Source={dbFile};Mode=ReadOnly");
            using var dst = new SqliteConnection($"Data Source={target}");
            src.Open();
            dst.Open();
            src.BackupDatabase(dst);
        }
        catch (Exception ex)
        {
            return new BackupResult(BackupOutcome.Error, ex.Message);
        }

        return new BackupResult(BackupOutcome.Success, name);
    }

    /// <summary>
    /// Enumerate snapshots + any other <c>*.db.bak*</c> files sitting next
    /// to the live DB. Sort newest first by file mtime.
    /// </summary>
    public IReadOnlyList<BackupFile> ListSnapshots()
    {
        if (!IsSqlite) return Array.Empty<BackupFile>();

        var (dir, dbFile) = ResolveSqlitePath();
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return Array.Empty<BackupFile>();

        var prefix = Path.GetFileName(dbFile) + ".bak";
        var files = Directory.EnumerateFiles(dir, $"{prefix}*", SearchOption.TopDirectoryOnly)
            .Select(p =>
            {
                var info = new FileInfo(p);
                return new BackupFile(info.Name, info.Length, info.LastWriteTimeUtc);
            })
            .OrderByDescending(f => f.LastWriteUtc)
            .ToList();
        return files;
    }

    // Resolve the SQLite file path from the connection string. The
    // connection string is "Data Source=<path>" — relative paths resolve
    // against process CWD (src/CCL.MES.Web for `dotnet run`).
    private (string Dir, string DbFile) ResolveSqlitePath()
    {
        var cs = _config.GetConnectionString("Default");
        if (string.IsNullOrEmpty(cs)) return ("", "");
        var builder = new SqliteConnectionStringBuilder(cs);
        var path = builder.DataSource;
        if (string.IsNullOrEmpty(path)) return ("", "");
        var full = Path.GetFullPath(path);
        return (Path.GetDirectoryName(full) ?? "", full);
    }
}

public record BackupResult(BackupOutcome Outcome, string? FileName);

public record BackupFile(string Name, long SizeBytes, DateTime LastWriteUtc);

public enum BackupOutcome
{
    Success,
    SqlServerUnsupported,
    SourceMissing,
    Error,
}
