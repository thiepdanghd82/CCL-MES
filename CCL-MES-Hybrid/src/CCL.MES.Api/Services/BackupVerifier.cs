using CCL.MES.Application;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CCL.MES.Api.Services;

/// <summary>
/// P-Backup (Hybrid) — verify a freshly-created SQLite snapshot before we
/// trust it. Port of the legacy CCL.MES.Web BackupVerifier:
///   1. Open the snapshot read-only, run <c>PRAGMA integrity_check</c>.
///   2. Count rows in a handful of core tables.
///   3. Compare against the LIVE DB — a backup that lost &gt;10% of rows
///      on a core table is "suspicious" and gets flagged.
///
/// Scoped — the live-count comparison needs <see cref="IMesDbContext"/>.
/// </summary>
public sealed class BackupVerifier
{
    private readonly IMesDbContext _db;

    public BackupVerifier(IMesDbContext db) => _db = db;

    private static readonly string[] CoreTables =
    {
        "WorkOrders",
        "ProductRevisions",
        "Products",
        "Users",
        "AuditLogs",
    };

    public async Task<BackupVerifyResult> VerifyAsync(string snapshotPath, CancellationToken ct = default)
    {
        var counts = new Dictionary<string, long>();
        string integrity;

        try
        {
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
                    // Whitelisted table names above — no injection surface.
                    cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\";";
                    counts[table] = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
                }
                catch
                {
                    // Table may be absent in an older snapshot schema — skip.
                }
            }
        }
        catch (Exception ex)
        {
            return BackupVerifyResult.Failed(ex.Message);
        }

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
                // Live count unavailable — skip comparison for this table.
            }
        }

        return new BackupVerifyResult(true, integrity, counts, drops);
    }

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

public sealed record BackupVerifyResult(
    bool Ok,
    string Integrity,
    IReadOnlyDictionary<string, long> Counts,
    IReadOnlyDictionary<string, RowCountDrop> Drops)
{
    public static BackupVerifyResult Failed(string error) =>
        new(false, error, new Dictionary<string, long>(), new Dictionary<string, RowCountDrop>());

    public bool IsClean => Ok && Drops.Count == 0;
}

public sealed record RowCountDrop(long Backup, long Live, double DropPct);
