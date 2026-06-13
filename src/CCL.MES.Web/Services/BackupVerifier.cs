using CCL.MES.Application;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Web.Services;

/// <summary>
/// P-Backup — verify a freshly-created SQLite snapshot before we trust it.
/// Port of Ops Control v1.3 backupScheduler.js <c>verifyBackup()</c> +
/// <c>compareToLive()</c>:
///   1. Open the snapshot read-only, run <c>PRAGMA integrity_check</c>.
///   2. Count rows in a handful of core tables.
///   3. Compare those counts against the LIVE DB — a backup that lost
///      &gt;10% of rows on a core table is "suspicious" (e.g. taken mid-
///      truncate) and gets flagged so the scheduler can alert.
///
/// Why a separate Scoped service (not inline in the BackgroundService):
/// the live-count comparison needs <see cref="IMesDbContext"/>, which is
/// Scoped. The scheduler already creates a DI scope per cycle, so it can
/// resolve this verifier from that same scope.
/// </summary>
public sealed class BackupVerifier
{
    private readonly IMesDbContext _db;

    public BackupVerifier(IMesDbContext db) => _db = db;

    // Core tables checked for integrity + row-count anomaly. Mirrors the
    // 6-table set the console restore tool (scripts/BackupRestore) prints
    // before a restore — the rows an operator cares most about not losing.
    private static readonly string[] CoreTables =
    {
        "WorkOrders",
        "ProductRevisions",
        "Products",
        "Users",
        "AuditLogs",
    };

    /// <summary>
    /// Verify <paramref name="snapshotPath"/> (absolute path to a .sqlite
    /// snapshot file). Returns integrity result + per-table counts +
    /// suspicious drops vs live. Never throws — failures surface as
    /// <see cref="BackupVerifyResult.Ok"/> = false.
    /// </summary>
    public async Task<BackupVerifyResult> VerifyAsync(string snapshotPath, CancellationToken ct = default)
    {
        var counts = new Dictionary<string, long>();
        string integrity;

        try
        {
            // Mode=ReadOnly so we never mutate the snapshot during verify.
            await using var conn = new SqliteConnection($"Data Source={snapshotPath};Mode=ReadOnly");
            await conn.OpenAsync(ct);

            await using (var pragma = conn.CreateCommand())
            {
                pragma.CommandText = "PRAGMA integrity_check;";
                integrity = (await pragma.ExecuteScalarAsync(ct))?.ToString() ?? "unknown";
            }

            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
                return BackupVerifyResult.Failed($"integrity_check: {integrity}");

            foreach (var table in CoreTables)
            {
                try
                {
                    await using var cmd = conn.CreateCommand();
                    // Table names are a hardcoded whitelist above — no
                    // injection surface despite the string concat.
                    cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
                    var n = await cmd.ExecuteScalarAsync(ct);
                    counts[table] = Convert.ToInt64(n);
                }
                catch
                {
                    // Table may not exist in an older snapshot schema —
                    // record null-equivalent (skip) rather than fail.
                }
            }
        }
        catch (Exception ex)
        {
            return BackupVerifyResult.Failed(ex.Message);
        }

        // Compare to live — flag any core table that dropped >10%.
        var drops = new Dictionary<string, RowCountDrop>();
        foreach (var (table, backupCount) in counts)
        {
            try
            {
                var liveCount = await CountLiveAsync(table, ct);
                if (liveCount <= 0) continue;
                var drop = liveCount - backupCount;
                if ((double)drop / liveCount > 0.10)
                {
                    drops[table] = new RowCountDrop(
                        Backup: backupCount,
                        Live: liveCount,
                        DropPct: Math.Round(drop * 100.0 / liveCount, 1));
                }
            }
            catch
            {
                // Live count unavailable for this table — skip comparison.
            }
        }

        return new BackupVerifyResult(true, integrity, counts, drops);
    }

    // EF Core has no generic "count by table name" — map the whitelist to
    // the typed DbSets so the LINQ provider builds a SELECT COUNT(*).
    private Task<int> CountLiveAsync(string table, CancellationToken ct) => table switch
    {
        "WorkOrders"       => _db.WorkOrders.CountAsync(ct),
        "ProductRevisions" => _db.ProductRevisions.CountAsync(ct),
        "Products"         => _db.Products.CountAsync(ct),
        "Users"            => _db.Users.CountAsync(ct),
        "AuditLogs"        => _db.AuditLogs.CountAsync(ct),
        _                  => Task.FromResult(-1),
    };
}

/// <summary>Outcome of a snapshot verification.</summary>
public sealed record BackupVerifyResult(
    bool Ok,
    string Integrity,
    IReadOnlyDictionary<string, long> Counts,
    IReadOnlyDictionary<string, RowCountDrop> Drops)
{
    public static BackupVerifyResult Failed(string error) =>
        new(false, error,
            new Dictionary<string, long>(),
            new Dictionary<string, RowCountDrop>());

    /// <summary>True when verify passed AND no core table lost &gt;10% of rows.</summary>
    public bool IsClean => Ok && Drops.Count == 0;
}

/// <summary>A core table whose snapshot count is suspiciously below live.</summary>
public sealed record RowCountDrop(long Backup, long Live, double DropPct);
