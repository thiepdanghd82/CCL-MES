using System.Security.Claims;
using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Audit;
using Microsoft.Data.Sqlite;

namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 6 Bước 2B — manual backup workflow for the Settings → Backup tab.
/// Behavior intentionally limited to non-destructive operations:
///   - SQLite: snapshot the live DB file to &lt;DATA_DIR&gt;/Backup/SQLite/
///     and enumerate existing snapshots from the same folder.
///   - SQL Server: every operation returns <see cref="BackupOutcome.SqlServerUnsupported"/>;
///     the UI surfaces a localized message pointing operators at SSMS /
///     maintenance plans.
/// Restore is shipped Bước 5 — `scripts/BackupRestore/` console script.
/// The Backup.razor tab shows guidance card with the literal command.
///
/// Phase 6 Bước 6.5 — backup folder moved from "next to DB" (flat) to
/// nested `&lt;DATA_DIR&gt;/Backup/SQLite/` mirror of Ops Control v1.2's
/// `server/data/Backup/SQLite/` convention. Old snapshots sitting next
/// to the live DB are migrated by a one-shot helper at boot via
/// <see cref="MigrateLegacySnapshots"/>.
/// </summary>
public class BackupService
{
    private readonly IConfiguration _config;
    private readonly IAuditWriter _audit;
    public BackupService(IConfiguration config, IAuditWriter audit)
    {
        _config = config;
        _audit = audit;
    }

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
    public async Task<BackupResult> CreateSnapshotAsync(ClaimsPrincipal actor)
    {
        if (!IsSqlite) return new BackupResult(BackupOutcome.SqlServerUnsupported, null);

        var (backupDir, dbFile) = ResolveSqlitePath();
        if (string.IsNullOrEmpty(dbFile) || !File.Exists(dbFile))
            return new BackupResult(BackupOutcome.SourceMissing, null);

        // Idempotent — Program.cs Bước 6.5 init already mkdirs, but cover
        // operators who pointed MES_DB_PATH outside the canonical data dir.
        Directory.CreateDirectory(backupDir);

        var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var name = $"ccl_mes.db.bak.snapshot-{ts}";
        var target = Path.Combine(backupDir, name);

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

        // Phase 6 Bước 5 — emit audit AFTER success. Detail captures the
        // resulting filename + size for cross-reference with the snapshot
        // list. NO file contents serialized.
        var (actorName, actorRole) = actor.AuditIdentity();
        var sizeBytes = new FileInfo(target).Length;
        await _audit.EmitAsync(
            AuditAction.BackupCreate, actorName, actorRole,
            targetType: "Backup", targetId: name,
            detail: JsonSerializer.Serialize(new { file = name, size_bytes = sizeBytes }));

        return new BackupResult(BackupOutcome.Success, name);
    }

    /// <summary>
    /// Enumerate snapshots from &lt;DATA_DIR&gt;/Backup/SQLite/. Sort newest
    /// first by file mtime.
    /// </summary>
    public IReadOnlyList<BackupFile> ListSnapshots()
    {
        if (!IsSqlite) return Array.Empty<BackupFile>();

        var (backupDir, dbFile) = ResolveSqlitePath();
        if (string.IsNullOrEmpty(backupDir) || !Directory.Exists(backupDir)) return Array.Empty<BackupFile>();

        var prefix = Path.GetFileName(dbFile) + ".bak";
        var files = Directory.EnumerateFiles(backupDir, $"{prefix}*", SearchOption.TopDirectoryOnly)
            .Select(p =>
            {
                var info = new FileInfo(p);
                return new BackupFile(info.Name, info.Length, info.LastWriteTimeUtc);
            })
            .OrderByDescending(f => f.LastWriteUtc)
            .ToList();
        return files;
    }

    /// <summary>
    /// One-shot migration: move legacy <c>*.db.bak.snapshot-*</c> files
    /// sitting next to the live DB (Bước 2B layout) into the new
    /// <c>&lt;DATA_DIR&gt;/Backup/SQLite/</c> folder (Bước 6.5 layout).
    /// Idempotent — safe to invoke at every boot. Returns count of files
    /// migrated for the boot log.
    /// </summary>
    public int MigrateLegacySnapshots()
    {
        if (!IsSqlite) return 0;

        var (backupDir, dbFile) = ResolveSqlitePath();
        if (string.IsNullOrEmpty(dbFile)) return 0;

        var legacyDir = Path.GetDirectoryName(dbFile);
        if (string.IsNullOrEmpty(legacyDir) || legacyDir == backupDir) return 0;

        var prefix = Path.GetFileName(dbFile) + ".bak";
        var legacyFiles = Directory.EnumerateFiles(legacyDir, $"{prefix}*", SearchOption.TopDirectoryOnly).ToList();
        if (legacyFiles.Count == 0) return 0;

        Directory.CreateDirectory(backupDir);
        var moved = 0;
        foreach (var src in legacyFiles)
        {
            var target = Path.Combine(backupDir, Path.GetFileName(src));
            if (File.Exists(target)) continue;   // already migrated, skip
            File.Move(src, target);
            moved++;
        }
        return moved;
    }

    // Resolve the SQLite paths from the connection string.
    //  - Live DB file: absolute path from "Data Source=<path>".
    //  - Backup dir : &lt;dirname(dbFile)&gt;/Backup/SQLite/ (Bước 6.5,
    //    mirror of Ops Control v1.2's server/data/Backup/SQLite/).
    // Program.cs Bước 6.5 forces an absolute path for the SQLite
    // connection string at boot, so Path.GetFullPath here returns the
    // operator-intended path regardless of process CWD.
    private (string BackupDir, string DbFile) ResolveSqlitePath()
    {
        var cs = _config.GetConnectionString("Default");
        if (string.IsNullOrEmpty(cs)) return ("", "");
        var builder = new SqliteConnectionStringBuilder(cs);
        var path = builder.DataSource;
        if (string.IsNullOrEmpty(path)) return ("", "");
        var full = Path.GetFullPath(path);
        var dataDir = Path.GetDirectoryName(full) ?? "";
        var backupDir = Path.Combine(dataDir, "Backup", "SQLite");
        return (backupDir, full);
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
