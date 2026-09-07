using System.Security.Cryptography;
using System.Text.Json;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Infrastructure.IqcMaster;
using Microsoft.EntityFrameworkCore;

// ── Nạp sổ lịch sử IQC (Roll/PCS/Chem/Tool) → IqcInspections ─────────────
//   dotnet run --project scripts/IqcHistoryLedgerImport -- \
//       --src "<IQC report 2026.xlsx>" [--db <path>] [--commit] [--enrich]
//
//   --enrich  → nạp chi tiết Roll/PCS vào IqcResultDetails (xls-ledger).

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
bool Flag(string name) => Array.IndexOf(args, name) >= 0;

var src = Arg("--src");
var dbPath = Arg("--db");
var commit = Flag("--commit");
var enrich = Flag("--enrich");
var actor = Arg("--actor") ?? "console";

if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
{
    Console.Error.WriteLine("--src \"<IQC report 2026.xlsx>\" là bắt buộc.");
    return 2;
}
if (string.IsNullOrWhiteSpace(dbPath) && commit)
{
    Console.Error.WriteLine("--db <path> bắt buộc khi --commit.");
    return 2;
}

Console.WriteLine($"[src] {src}");
List<IqcHistoryLedgerRow> rows;
using (var fs = File.OpenRead(src))
    rows = IqcHistoryLedgerReader.Read(fs);

var bySheet = rows.GroupBy(r => r.Sheet).ToDictionary(g => g.Key, g => g.Count());
Console.WriteLine($"[parse] {rows.Count} dòng · " +
                  string.Join(" · ", bySheet.Select(kv => $"{kv.Key}={kv.Value}")));
Console.WriteLine($"[checks] có khối kiểm={rows.Count(r => r.Checks is not null)} enrich={(enrich ? "on" : "off")}");

if (string.IsNullOrWhiteSpace(dbPath))
{
    Console.WriteLine("[dry-run] không --db → chỉ đọc file.");
    return 0;
}

var abs = Path.GetFullPath(dbPath);
Console.WriteLine($"[db] {abs}  sha8={Sha8(abs)}");

var options = new DbContextOptionsBuilder<MesDbContext>().UseSqlite($"Data Source={abs}").Options;
await using var db = new MesDbContext(options);

var pendingMig = (await db.Database.GetPendingMigrationsAsync()).ToList();
if (pendingMig.Count > 0)
{
    Console.Error.WriteLine($"[migrate] còn {pendingMig.Count} migration chưa áp — import KHÔNG tự migrate.");
    return 3;
}

var before = await db.IqcInspections.CountAsync();
var beforeDet = await db.IqcResultDetails.CountAsync();
Console.WriteLine($"[before] IqcInspections={before} details={beforeDet}");

var svc = new IqcHistoryLedgerImportService(db);
var r = await svc.ImportAsync(rows, actor, commit, enrichDetails: enrich);

Console.WriteLine($"[map] đọc={r.RowsRead} bỏ-judgment={r.RowsSkippedNoJudgment} bỏ-pcs-cont={r.RowsSkippedPcsContinuation}");
Console.WriteLine($"[write] insert={r.Inserted} đã-có={r.AlreadyPresent} details-upsert={r.DetailsUpserted}");

if (!commit)
{
    Console.WriteLine("[dry-run] không --commit → DB không đổi.");
    return 0;
}

var after = await db.IqcInspections.CountAsync();
var afterDet = await db.IqcResultDetails.CountAsync();
db.AuditLogs.Add(new AuditLog
{
    Timestamp = DateTime.UtcNow,
    ActorUsername = actor,
    ActorRole = "System",
    Action = AuditAction.QcLibraryImport,
    TargetType = "IqcInspection",
    TargetId = Path.GetFileName(src),
    Source = "Console",
    Detail = JsonSerializer.Serialize(new
    {
        kind = "iqc_history_ledger",
        src = Path.GetFileName(src),
        rows = r.RowsRead,
        inserted = r.Inserted,
        already = r.AlreadyPresent,
        details_upserted = r.DetailsUpserted,
        enrich,
        total = after,
        details_total = afterDet,
    }),
});
await db.SaveChangesAsync();

Console.WriteLine($"[after] IqcInspections {before} → {after} · details {beforeDet} → {afterDet}");
Console.WriteLine($"[db] sha8 sau = {Sha8(abs)}");
Console.WriteLine("[done]");
return 0;

static string Sha8(string path)
{
    if (!File.Exists(path)) return "(none)";
    using var s = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(s))[..8].ToLowerInvariant();
}
