using System.Security.Cryptography;
using System.Text.Json;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Infrastructure.QcLibrary;
using Microsoft.EntityFrameworkCore;

// ── QC Library v5 importer ───────────────────────────────────────────────
//   dotnet run --project scripts/QcLibraryV5Import -- --db <path> [--src <v5.xlsx>] [--commit]
//
//   Dry-run (no --commit): parse v5 + print counts by Line/Group. No DB touch.
//   --commit: apply pending migrations → seed v5 (upsert by ItemId) → audit.
//   Idempotent: re-running yields 0 inserted / 0 updated.

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
bool Flag(string name) => Array.IndexOf(args, name) >= 0;

var dbPath = Arg("--db");
var commit = Flag("--commit");
if (string.IsNullOrWhiteSpace(dbPath) && commit)
{
    Console.Error.WriteLine("--db <path> required with --commit.");
    return 2;
}

// Resolve v5 source (walk-up from cwd if --src not given).
var src = Arg("--src");
if (string.IsNullOrWhiteSpace(src))
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
    {
        var c = Path.Combine(dir.FullName, "IPQC_Library_CMES_v5.xlsx");
        if (File.Exists(c)) { src = c; break; }
    }
}
if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
{
    Console.Error.WriteLine("IPQC_Library_CMES_v5.xlsx not found (use --src).");
    return 2;
}

Console.WriteLine($"[src] {src}");
IReadOnlyList<CCL.MES.Application.Services.QcCheckLibraryRow> rows;
using (var fs = File.OpenRead(src)) rows = QcLibraryV5Parser.Parse(fs);
Console.WriteLine($"[parse] {rows.Count} items");

// Breakdown by Line / Group.
foreach (var lg in rows.GroupBy(r => r.ProcessLine).OrderBy(g => g.Key))
{
    Console.WriteLine($"  Line {lg.Key}: {lg.Count()}");
    foreach (var g in lg.GroupBy(r => r.GroupLabel).OrderBy(g => g.Key))
        Console.WriteLine($"      {g.Key}: {g.Count()}");
}

if (!commit)
{
    Console.WriteLine("[dry-run] no --commit → DB untouched. Re-run with --commit to write.");
    return 0;
}

var abs = Path.GetFullPath(dbPath!);
Console.WriteLine($"[db] {abs}  sha8={Sha8(abs)}");
var options = new DbContextOptionsBuilder<MesDbContext>()
    .UseSqlite($"Data Source={abs}")
    .Options;
using var db = new MesDbContext(options);

// Apply pending migrations (RemodelCheckItemLibraryV5 etc.).
var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
if (pending.Count > 0)
{
    Console.WriteLine($"[migrate] applying {pending.Count}: {string.Join(", ", pending)}");
    await db.Database.MigrateAsync();
}
else Console.WriteLine("[migrate] up-to-date.");

var before = await db.CheckItemLibraries.CountAsync();
// --replace: thay TOÀN BỘ thư viện (xoá hạng mục cũ trước khi seed v5). Master
// data — WoIpqcCheckItems là snapshot copy (không FK), ReasonCode giữ nguyên.
if (Flag("--replace") && before > 0)
{
    var deleted = await db.CheckItemLibraries.ExecuteDeleteAsync();
    Console.WriteLine($"[replace] deleted {deleted} legacy items");
}
var result = await DbSeeder.SeedCheckItemLibraryAsync(db, rows);
var after = await db.CheckItemLibraries.CountAsync();

// Audit row (Source=Console).
db.AuditLogs.Add(new AuditLog
{
    Timestamp = DateTime.UtcNow,
    ActorUsername = "console",
    ActorRole = "System",
    Action = AuditAction.QcLibraryImport,
    TargetType = "CheckItemLibrary",
    TargetId = Path.GetFileName(src),
    Source = "Console",
    Detail = JsonSerializer.Serialize(new
    {
        src = Path.GetFileName(src),
        inserted = result.LibInserted,
        updated = result.LibUpdated,
        reason_added = result.ReasonAdded,
        total = after,
    }),
});
await db.SaveChangesAsync();

Console.WriteLine($"[import] inserted={result.LibInserted} updated={result.LibUpdated} reason_added={result.ReasonAdded}");
Console.WriteLine($"[rowcount] CheckItemLibraries {before} → {after}");
Console.WriteLine("[done]");
return 0;

static string Sha8(string path)
{
    if (!File.Exists(path)) return "(none)";
    using var s = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(s))[..8].ToLowerInvariant();
}
