using System.Security.Cryptography;
using CCL.MES.Shared.Backup;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CCL.MES.Api.Services;

/// <summary>
/// P10.6h — admin Backup/Restore service for the Hybrid API.
///
/// Ports the legacy <c>CCL.MES.Web.Services.BackupService</c> create +
/// list behaviours over to the API surface (which doesn't reference
/// CCL.MES.Web — that DI graph is Blazor Server). Adds the Restore
/// endpoint the legacy stack never shipped from the UI (it lived in
/// <c>scripts/BackupRestore/</c> as a console tool).
///
/// Restore safety contract (the only WRITE path in this PR):
///   1. Save upload bytes to a temp file inside
///      &lt;DATA_DIR&gt;/Backup/SQLite/_upload-{ts}.tmp.
///   2. Validate SQLite header — first 16 bytes must be the documented
///      "SQLite format 3\0" magic. Fail-fast on truncated /
///      wrong-type uploads BEFORE the live DB is touched.
///   3. Open the temp file as a ReadOnly SQLite connection + sanity-
///      check the schema (Users + Customers + Products tables must
///      exist). Catches the case where an admin pastes some other
///      SQLite DB by accident.
///   4. Take a PRE-RESTORE snapshot of the CURRENT live DB before any
///      destructive write. Filename
///      <c>pre-restore-{ts}.db.bak.snapshot-{ts}</c> so the list
///      endpoint marks it distinctly + the audit row references it
///      by name. Admin can roll back by downloading + uploading the
///      pre-restore snapshot.
///   5. Use SQLite online <see cref="SqliteConnection.BackupDatabase"/>
///      to clone the validated temp file INTO the live DB. SQLite's
///      transaction layer handles concurrent reader locking; we don't
///      need to stop the API process. Open connections will see the
///      new state after their next read.
///   6. Delete the temp upload file.
///
/// Failure paths return one of the <see cref="RestoreOutcome"/>
/// non-Success values; the controller maps to RFC-7807 with stable
/// <c>backup.*</c> codes for the VN mapper.
/// </summary>
public sealed class BackupApiService
{
    private readonly IConfiguration _config;
    private readonly ILogger<BackupApiService> _log;
    private const string FilePrefix = "ccl_mes.db.bak";
    private const string SnapshotInfix = ".snapshot-";
    private const string PreRestorePrefix = "pre-restore-";
    // SQLite's documented file-header magic. 16 bytes including the
    // trailing NUL terminator. See https://www.sqlite.org/fileformat.html
    private static readonly byte[] SqliteMagic = System.Text.Encoding.ASCII
        .GetBytes("SQLite format 3\0");

    public BackupApiService(IConfiguration config, ILogger<BackupApiService> log)
    {
        _config = config;
        _log = log;
    }

    public string Provider =>
        _config["Database:Provider"]?.Equals("SqlServer", StringComparison.OrdinalIgnoreCase) == true
            ? "SqlServer"
            : "Sqlite";

    public bool IsSqlite => Provider == "Sqlite";

    // ── List ────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<BackupSnapshotDto>> ListAsync(CancellationToken ct)
    {
        if (!IsSqlite) return Array.Empty<BackupSnapshotDto>();
        var (backupDir, dbFile) = ResolveSqlitePath();
        if (string.IsNullOrEmpty(backupDir) || !Directory.Exists(backupDir))
            return Array.Empty<BackupSnapshotDto>();

        var dbBase = Path.GetFileName(dbFile);
        var list = new List<BackupSnapshotDto>();
        foreach (var path in Directory.EnumerateFiles(backupDir, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(path);
            // Accept either operator-initiated or pre-restore snapshots.
            // Skip anything else (stale temp upload, .DS_Store, etc.).
            var isPreRestore = name.StartsWith(PreRestorePrefix, StringComparison.Ordinal);
            var isSnapshot = name.StartsWith(dbBase + ".bak", StringComparison.Ordinal);
            if (!isPreRestore && !isSnapshot) continue;

            var info = new FileInfo(path);
            string sha;
            try
            {
                sha = await Sha256OfAsync(path, ct);
            }
            catch
            {
                sha = "";  // unreadable mid-list — surface row without hash
            }
            list.Add(new BackupSnapshotDto
            {
                FileName = name,
                SizeBytes = info.Length,
                CreatedAtUtc = info.LastWriteTimeUtc,
                Sha256 = sha,
                IsPreRestore = isPreRestore,
            });
        }
        return list.OrderByDescending(s => s.CreatedAtUtc).ToList();
    }

    // ── Create ──────────────────────────────────────────────────────

    public BackupSnapshotDto? CreateSnapshot()
    {
        if (!IsSqlite) return null;
        var (backupDir, dbFile) = ResolveSqlitePath();
        if (string.IsNullOrEmpty(dbFile) || !File.Exists(dbFile)) return null;

        Directory.CreateDirectory(backupDir);
        var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var name = $"{Path.GetFileName(dbFile)}.bak{SnapshotInfix}{ts}";
        var target = Path.Combine(backupDir, name);

        try
        {
            using var src = new SqliteConnection($"Data Source={dbFile};Mode=ReadOnly");
            using var dst = new SqliteConnection($"Data Source={target}");
            src.Open();
            dst.Open();
            src.BackupDatabase(dst);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[backup] CreateSnapshot failed for {dbFile} → {target}", dbFile, target);
            try { if (File.Exists(target)) File.Delete(target); } catch { /* swallow */ }
            return null;
        }

        var info = new FileInfo(target);
        return new BackupSnapshotDto
        {
            FileName = name,
            SizeBytes = info.Length,
            CreatedAtUtc = info.LastWriteTimeUtc,
            Sha256 = Sha256OfFile(target),
            IsPreRestore = false,
        };
    }

    // ── Download (return absolute path so controller can stream) ───

    public string? ResolveDownloadPath(string fileName)
    {
        if (!IsSqlite) return null;
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        if (fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains("..", StringComparison.Ordinal))
            return null;   // path-traversal defence

        var (backupDir, _) = ResolveSqlitePath();
        var full = Path.Combine(backupDir, fileName);
        return File.Exists(full) ? full : null;
    }

    // ── Restore ────────────────────────────────────────────────────

    public async Task<RestoreResultDto> RestoreAsync(Stream uploadStream, CancellationToken ct)
    {
        if (!IsSqlite) return new RestoreResultDto { Outcome = RestoreOutcome.SqlServerUnsupported };
        var (backupDir, dbFile) = ResolveSqlitePath();
        if (string.IsNullOrEmpty(dbFile)) return new RestoreResultDto { Outcome = RestoreOutcome.Error };

        Directory.CreateDirectory(backupDir);
        var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        // Tên file tạm phải DUY NHẤT, không chỉ theo giây. Hai lần restore trong
        // cùng một giây dùng chung tên, và Microsoft.Data.Sqlite gộp kết nối
        // theo CHUỖI KẾT NỐI — nên lần sau lấy lại handle của lần trước, vốn
        // vẫn trỏ vào inode cũ đã bị xoá (macOS/Linux giữ inode sống chừng nào
        // còn handle mở). Kết quả: đọc ra schema của FILE CŨ, và người dùng bị
        // báo "nhầm file?" cho một bản backup hoàn toàn hợp lệ.
        var tempName = $"_upload-{ts}-{Guid.NewGuid():N}.tmp";
        var tempPath = Path.Combine(backupDir, tempName);

        try
        {
            // 1. Drain upload to temp file.
            long bytesWritten;
            await using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await uploadStream.CopyToAsync(fs, ct);
                bytesWritten = fs.Length;
            }
            if (bytesWritten == 0)
            {
                SafeDelete(tempPath);
                return new RestoreResultDto { Outcome = RestoreOutcome.EmptyUpload };
            }

            // 2. Header validation.
            if (!await HasSqliteHeaderAsync(tempPath, ct))
            {
                SafeDelete(tempPath);
                return new RestoreResultDto { Outcome = RestoreOutcome.InvalidHeader };
            }

            // 3. Schema sanity. ReadOnly so SQLite doesn't write WAL.
            if (!HasRequiredSchema(tempPath))
            {
                SafeDelete(tempPath);
                return new RestoreResultDto { Outcome = RestoreOutcome.SchemaMismatch };
            }

            // 4. Pre-restore snapshot of CURRENT live DB.
            var preName = $"{PreRestorePrefix}{ts}-{Path.GetFileName(dbFile)}.bak{SnapshotInfix}{ts}";
            var prePath = Path.Combine(backupDir, preName);
            try
            {
                using var live = new SqliteConnection($"Data Source={dbFile};Mode=ReadOnly");
                using var dst = new SqliteConnection($"Data Source={prePath}");
                live.Open();
                dst.Open();
                live.BackupDatabase(dst);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[restore] pre-restore snapshot failed; aborting");
                SafeDelete(tempPath);
                return new RestoreResultDto { Outcome = RestoreOutcome.Error };
            }

            // 5. Apply restore: clone validated temp INTO live DB.
            try
            {
                using var src = new SqliteConnection($"Data Source={tempPath};Mode=ReadOnly");
                using var dst = new SqliteConnection($"Data Source={dbFile}");
                src.Open();
                dst.Open();
                src.BackupDatabase(dst);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "[restore] apply step failed; pre-restore snapshot {preName} kept", preName);
                SafeDelete(tempPath);
                return new RestoreResultDto
                {
                    Outcome = RestoreOutcome.Error,
                    PreRestoreSnapshot = preName,
                };
            }

            SafeDelete(tempPath);
            return new RestoreResultDto
            {
                Outcome = RestoreOutcome.Success,
                PreRestoreSnapshot = preName,
                RestoredBytes = bytesWritten,
            };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "[restore] outer failure");
            SafeDelete(tempPath);
            return new RestoreResultDto { Outcome = RestoreOutcome.Error };
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static async Task<bool> HasSqliteHeaderAsync(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fs.Length < SqliteMagic.Length) return false;
        var buf = new byte[SqliteMagic.Length];
        var read = await fs.ReadAsync(buf.AsMemory(0, buf.Length), ct);
        if (read != buf.Length) return false;
        for (var i = 0; i < buf.Length; i++)
            if (buf[i] != SqliteMagic[i]) return false;
        return true;
    }

    private bool HasRequiredSchema(string path)
    {
        try
        {
            // Pooling=False: kết nối này chỉ sống vài mili-giây và trỏ vào một
            // file dùng một lần. Để nó vào pool là mời một lần restore sau nhận
            // lại handle đã chết.
            using var conn = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
            conn.Open();
            // Three core tables must be present. They map to entities every
            // version of the schema has shipped since Phase 1, so this
            // is robust across the migration history.
            foreach (var table in new[] { "Users", "Customers", "Products" })
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=$name LIMIT 1";
                cmd.Parameters.AddWithValue("$name", table);
                var result = cmd.ExecuteScalar();
                if (result is null) return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            // KHÔNG được nuốt im lặng. Mọi lỗi ở đây đều nổi lên thành
            // "backup.schema_mismatch — nhầm file?", nên một sự cố đọc file
            // đang được báo cho quản trị viên bằng một CHẨN ĐOÁN SAI, và họ sẽ
            // đi tìm bản backup khác thay vì tìm nguyên nhân thật.
            _log.LogError(ex, "[restore] không đọc được schema của {path}; " +
                "sẽ báo schema_mismatch — kiểm tra xem có phải lỗi đọc file không", path);
            return false;
        }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* swallow */ }
    }

    private static async Task<string> Sha256OfAsync(string path, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Sha256OfFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private (string BackupDir, string DbFile) ResolveSqlitePath()
    {
        var cs = _config.GetConnectionString("Default") ?? "";
        const string prefix = "Data Source=";
        if (!cs.StartsWith(prefix, StringComparison.Ordinal)) return ("", "");
        var dbPath = Path.GetFullPath(cs[prefix.Length..]);
        var dataDir = Path.GetDirectoryName(dbPath) ?? "";
        var backupDir = Path.Combine(dataDir, "Backup", "SQLite");
        return (backupDir, dbPath);
    }
}
