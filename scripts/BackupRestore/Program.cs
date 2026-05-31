using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using Microsoft.EntityFrameworkCore;

// Phase 6 Bước 5 — DB restore lifeline. Console only. Requires both
// CONFIRM-RESTORE typed at the prompt AND a successful pre-restore
// auto-backup of the current DB before any byte is overwritten.
//
// Usage (run from repo root):
//   cd scripts/BackupRestore
//   dotnet run -- --from <snapshot-filename>
//
// Optional env:
//   MES_DB_PATH       Override the active DB file
//                     (default: ../../src/CCL.MES.Web/ccl_mes.db)
//
// The script does NOT support SQL Server — for that provider use SSMS /
// RESTORE DATABASE / maintenance plans. The script prints a clear
// message and exits if asked to operate on a non-SQLite DB.

if (args.Length < 2 || args[0] != "--from" || string.IsNullOrWhiteSpace(args[1]))
{
    PrintUsage();
    return 2;
}

var snapshotName = args[1];

var dbPath = Environment.GetEnvironmentVariable("MES_DB_PATH")
             ?? Path.GetFullPath(Path.Combine("..", "..", "src", "CCL.MES.Web", "ccl_mes.db"));
var dbDir = Path.GetDirectoryName(dbPath) ?? "";

// If the user passed just a basename, resolve next to the live DB.
var snapshotPath = Path.IsPathRooted(snapshotName) || snapshotName.Contains(Path.DirectorySeparatorChar)
    ? Path.GetFullPath(snapshotName)
    : Path.Combine(dbDir, snapshotName);

Console.WriteLine($"Active DB:      {dbPath}");
Console.WriteLine($"Snapshot file:  {snapshotPath}");

if (!File.Exists(dbPath))
{
    Console.Error.WriteLine($"Active DB not found at {dbPath}.");
    return 3;
}
if (!File.Exists(snapshotPath))
{
    Console.Error.WriteLine($"Snapshot not found at {snapshotPath}.");
    return 4;
}

// Row count of snapshot — printed BEFORE confirmation so the operator
// can sanity-check what they're about to replace with.
var preCounts = await ReadRowCounts(snapshotPath, "snapshot");
foreach (var (table, count) in preCounts)
    Console.WriteLine($"  snapshot.{table,-25} = {count,8:N0}");

var liveCounts = await ReadRowCounts(dbPath, "active");
foreach (var (table, count) in liveCounts)
    Console.WriteLine($"  active.{table,-25}   = {count,8:N0}");

Console.Write("Type CONFIRM-RESTORE to proceed (anything else aborts): ");
var confirm = Console.ReadLine();
if (confirm != "CONFIRM-RESTORE")
{
    Console.Error.WriteLine("Aborted (confirmation did not match).");
    return 5;
}

// Pre-restore auto-backup of the current live DB — non-negotiable. If
// the copy fails we DO NOT proceed; the operator still has the original
// DB intact.
var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
var preRestoreBackup = Path.Combine(dbDir, $"ccl_mes.db.bak.pre-restore-{ts}");
try
{
    File.Copy(dbPath, preRestoreBackup, overwrite: false);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Pre-restore backup failed — aborting BEFORE any overwrite: {ex.Message}");
    return 6;
}
Console.WriteLine($"Pre-restore backup taken: {preRestoreBackup}");

// Overwrite the live DB with the snapshot. SqliteConnection.BackupDatabase
// is the read-path — for a wholesale replace, File.Copy is the right
// primitive (and matches the symmetry: snapshot was taken via online
// backup which produced a regular .db file on disk).
try
{
    File.Copy(snapshotPath, dbPath, overwrite: true);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Copy failed: {ex.Message}");
    Console.Error.WriteLine($"The pre-restore backup at {preRestoreBackup} is still valid.");
    return 7;
}

// Post-restore audit row written directly to the (now-restored) DB.
// The Web app is assumed offline; the script becomes the audit emitter
// for this one row, Source = "Console".
var options = new DbContextOptionsBuilder<MesDbContext>()
    .UseSqlite($"Data Source={dbPath}")
    .Options;
using (var db = new MesDbContext(options))
{
    db.AuditLogs.Add(new AuditLog
    {
        Timestamp = DateTime.UtcNow,
        ActorUsername = Environment.UserName,
        ActorRole = "Console",
        Action = AuditAction.BackupRestore,
        TargetType = "Backup",
        TargetId = Path.GetFileName(snapshotPath),
        Detail = System.Text.Json.JsonSerializer.Serialize(new
        {
            snapshot = Path.GetFileName(snapshotPath),
            pre_restore_backup = Path.GetFileName(preRestoreBackup),
            os_user = Environment.UserName,
            host = Environment.MachineName,
        }),
        Source = "Console",
    });
    await db.SaveChangesAsync();
}

// Print post-restore row counts so the operator can verify the swap.
Console.WriteLine();
Console.WriteLine("Post-restore row counts:");
var postCounts = await ReadRowCounts(dbPath, "restored");
foreach (var (table, count) in postCounts)
    Console.WriteLine($"  {table,-25} = {count,8:N0}");

Console.WriteLine();
Console.WriteLine("Restore complete. Restart the web server.");
return 0;

static void PrintUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  dotnet run -- --from <snapshot-filename>");
    Console.Error.WriteLine("Env:");
    Console.Error.WriteLine("  MES_DB_PATH   Override active DB file");
    Console.Error.WriteLine("                (default: ../../src/CCL.MES.Web/ccl_mes.db)");
}

static async Task<IReadOnlyList<(string Table, long Count)>> ReadRowCounts(string path, string label)
{
    var options = new DbContextOptionsBuilder<MesDbContext>()
        .UseSqlite($"Data Source={path};Mode=ReadOnly")
        .Options;
    using var db = new MesDbContext(options);
    return new (string, long)[]
    {
        ("WorkCenters",             await db.WorkCenters.LongCountAsync()),
        ("RawMaterials",            await db.RawMaterials.LongCountAsync()),
        ("RoutingOperations",       await db.RoutingOperations.LongCountAsync()),
        ("ManufacturingStructures", await db.ManufacturingStructures.LongCountAsync()),
        ("Users",                   await db.Users.LongCountAsync()),
        ("WorkOrders",              await db.WorkOrders.LongCountAsync()),
    };
}
