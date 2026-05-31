using System.Diagnostics;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using CCL.MES.Application.Audit;
using CCL.MES.Application.Services.NpiImport;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Auth;
using CCL.MES.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Web.Services;

/// <summary>
/// Phase 7 hạng mục 1 — Web-layer engine cho NPI CSV replace-all import.
/// Tách khỏi <see cref="CCL.MES.Application.Services.NpiImport.NpiCsvParser"/>
/// (pure parse, no DB) vì engine cần concrete <see cref="MesDbContext"/>
/// (<c>Database</c> property + <c>Set&lt;T&gt;()</c> + <c>Entry</c>) +
/// <see cref="BackupService"/> + <see cref="IAuditWriter"/>.
///
/// Tổng quát hoá pattern cho 5 tab NPI (Structure / Routine /
/// RawMaterials / Spec / WorkCenter). Hạng mục 1 ship cùng
/// <c>StructureCsvTarget</c>. Hạng mục sau add target + dùng lại
/// engine + modal generic.
///
/// Trình tự ApplyAsync (replace-all atomic):
///   1. RBAC validate — role ∈ {Admin, Engineer}
///   2. BackupService.CreateSnapshotAsync → file + SHA256
///   3. BeginTransactionAsync (SQLite atomic)
///   4. DELETE FROM "<TableName>"
///   5. AddRange chunked (500) + SaveChangesAsync + Detach để clear tracker
///   6. CommitAsync
///   7. AuditWriter.EmitAsync(NPI_IMPORT) với detail JSON shape stable
/// Lỗi bước 3–6 → rollback, backup file vẫn còn (restore tay).
/// </summary>
public class NpiImportService
{
    private readonly MesDbContext _db;
    private readonly BackupService _backup;
    private readonly IAuditWriter _audit;

    public NpiImportService(MesDbContext db, BackupService backup, IAuditWriter audit)
    {
        _db = db;
        _backup = backup;
        _audit = audit;
    }

    public static bool IsImportRole(string role) =>
        role == UserRole.Admin || role == UserRole.Engineer;

    public async Task<CsvImportResult> ApplyAsync<TEntity>(
        IReadOnlyList<TEntity> rows,
        ICsvImportTarget<TEntity> target,
        ClaimsPrincipal actor,
        int skipped,
        CancellationToken ct = default)
        where TEntity : class
    {
        var (actorName, actorRole) = actor.AuditIdentity();
        if (!IsImportRole(actorRole))
            throw new CsvImportException("forbidden_role", $"Role '{actorRole}' is not allowed to import NPI master data.");
        if (rows.Count == 0)
            throw new CsvImportException("no_rows", "Nothing to import (0 rows parsed).");

        var sw = Stopwatch.StartNew();

        // Step 2 — backup BEFORE any mutation. If this fails, no DB change happens.
        var backupResult = await _backup.CreateSnapshotAsync(actor);
        if (backupResult.Outcome != BackupOutcome.Success || backupResult.FileName is null)
            throw new CsvImportException("backup_failed",
                $"Pre-import backup failed (outcome={backupResult.Outcome}). Import aborted to keep DB safe.");

        var backupFile = backupResult.FileName;
        var backupSha = ComputeBackupSha256(backupFile) ?? "(sha-unavailable)";
        var backupShaShort = backupSha.Length >= 12 ? backupSha[..12] : backupSha;

        var dbSet = _db.Set<TEntity>();
        var oldCount = await dbSet.CountAsync(ct);

        // Step 3-6 — atomic DELETE + INSERT.
        // Table name is interpolated raw — EF1002 warning suppressed because:
        //   1. ICsvImportTarget.TableName là hardcoded literal trong concrete
        //      class (StructureCsvTarget / RoutineCsvTarget / …), KHÔNG đến
        //      từ user input.
        //   2. Defense-in-depth: validate khớp identifier-only pattern
        //      [A-Za-z_][A-Za-z0-9_]* để chặn 100% injection vector kể cả
        //      khi tương lai có ai cho TableName đến từ config.
        if (!System.Text.RegularExpressions.Regex.IsMatch(target.TableName, @"^[A-Za-z_][A-Za-z0-9_]*$"))
            throw new CsvImportException("invalid_table_name", $"Refusing to DELETE — table name '{target.TableName}' is not a plain identifier.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
#pragma warning disable EF1002 // identifier-only TableName, validated above
            await _db.Database.ExecuteSqlRawAsync($"DELETE FROM \"{target.TableName}\"", ct);
#pragma warning restore EF1002

            const int chunk = 500;
            for (int off = 0; off < rows.Count; off += chunk)
            {
                var slice = rows.Skip(off).Take(chunk).ToList();
                dbSet.AddRange(slice);
                await _db.SaveChangesAsync(ct);
                foreach (var e in slice)
                {
                    _db.Entry(e).State = EntityState.Detached;
                }
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (ex is not CsvImportException)
        {
            await tx.RollbackAsync(ct);
            throw new CsvImportException("apply_failed",
                $"Replace-all rolled back ({ex.GetType().Name}: {ex.Message}). Backup at {backupFile} still available for restore.", ex);
        }

        sw.Stop();
        var newCount = await dbSet.CountAsync(ct);

        // Step 7 — audit emit. Detail JSON shape stable cho 5 tab NPI sau.
        var detail = JsonSerializer.Serialize(new
        {
            table = target.TableName,
            old_count = oldCount,
            new_count = newCount,
            skipped,
            backup_file = backupFile,
            backup_sha256 = backupSha,
            elapsed_ms = (int)sw.ElapsedMilliseconds,
        });
        await _audit.EmitAsync(
            AuditAction.NpiImport,
            actorName,
            actorRole,
            targetType: target.TableName,
            targetId: backupFile,
            detail: detail);

        return new CsvImportResult
        {
            Table = target.TableName,
            OldCount = oldCount,
            NewCount = newCount,
            Skipped = skipped,
            BackupFile = backupFile,
            BackupSha256Short = backupShaShort,
            ElapsedMs = (int)sw.ElapsedMilliseconds,
        };
    }

    /// <summary>
    /// Compute SHA256 of backup file. BackupService creates the snapshot
    /// at <c>&lt;DATA_DIR&gt;/Backup/SQLite/&lt;file&gt;</c> via online backup
    /// API. We resolve the path via the same helper BackupService uses.
    /// Returns null silently if the file disappeared (rare race) so the
    /// import still proceeds with audit having a placeholder sha.
    /// </summary>
    private string? ComputeBackupSha256(string fileName)
    {
        try
        {
            var (dir, _) = _backup.ResolveSqlitePaths();
            var full = Path.Combine(dir, fileName);
            if (!File.Exists(full)) return null;
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(full);
            var hash = sha.ComputeHash(fs);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch
        {
            return null;
        }
    }
}
