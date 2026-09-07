using CCL.MES.Application.Audit;
using CCL.MES.Application.Services;
using CCL.MES.Infrastructure;
using CCL.MES.Infrastructure.IqcMaster;
using Microsoft.EntityFrameworkCore;

// ── Import folder tiêu chuẩn Form → IqcMaterialSpecs (header) ────────────
//   dotnet run --project scripts/IqcStandardSpecFolderImport -- \
//       --src "<folder IQC TIÊU CHUẨN … 2026>" [--db <path>] [--commit]

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
bool Flag(string name) => Array.IndexOf(args, name) >= 0;

var src = Arg("--src")
    ?? Environment.GetEnvironmentVariable(IqcStandardSpecCatalogService.DefaultEnvDir);
var dbPath = Arg("--db");
var commit = Flag("--commit");
var actor = Arg("--actor") ?? "console";

if (string.IsNullOrWhiteSpace(src) || !Directory.Exists(src))
{
    Console.Error.WriteLine("--src <folder> hoặc MES_IQC_STANDARD_SPEC_DIR là bắt buộc.");
    return 2;
}
if (string.IsNullOrWhiteSpace(dbPath) && commit)
{
    Console.Error.WriteLine("--db <path> bắt buộc khi --commit.");
    return 2;
}

Console.WriteLine($"[src] {src}");
var scanner = new IqcStandardSpecFolderScanner();
var rows = scanner.Scan(src);
Console.WriteLine($"[parse] files={rows.Count}");

if (string.IsNullOrWhiteSpace(dbPath))
{
    Console.WriteLine("[dry-run] không --db → chỉ đọc folder.");
    foreach (var sample in rows.Take(5))
        Console.WriteLine($"  {sample.SpecNo} · {sample.MaterialCode} · {sample.Revision} · {sample.FileName}");
    return 0;
}

var abs = Path.GetFullPath(dbPath);
Console.WriteLine($"[db] {abs}");
var options = new DbContextOptionsBuilder<MesDbContext>().UseSqlite($"Data Source={abs}").Options;
await using var db = new MesDbContext(options);

var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
if (pending.Count > 0)
{
    Console.Error.WriteLine($"[migrate] còn {pending.Count} migration — KHÔNG tự migrate.");
    return 3;
}

var before = await db.IqcMaterialSpecs.CountAsync(s => s.SpecNo.StartsWith("CCL-SPEC-QC"));
var svc = new IqcStandardSpecCatalogService(db, new ConsoleAudit());
var result = await svc.ImportParsedAsync(rows, actor, "System", src, commit);
Console.WriteLine($"[write] seen={result.FilesSeen} skip={result.FilesSkipped} insert={result.Inserted} update={result.Updated} present={result.AlreadyPresent} items+={result.ItemsInserted} items↻={result.ItemsUpdated}");

if (!commit)
{
    Console.WriteLine("[dry-run] không --commit → DB không đổi.");
    return 0;
}

var after = await db.IqcMaterialSpecs.CountAsync(s => s.SpecNo.StartsWith("CCL-SPEC-QC"));
Console.WriteLine($"[after] CCL-SPEC specs {before} → {after}");
Console.WriteLine("[done]");
return 0;

sealed class ConsoleAudit : IAuditWriter
{
    public Task EmitAsync(string action, string actor, string actorRole,
        string? targetType = null, string? targetId = null, string? detail = null,
        string? source = null)
    {
        Console.WriteLine($"[audit] {action} {actor} {targetType}/{targetId}");
        return Task.CompletedTask;
    }
}
