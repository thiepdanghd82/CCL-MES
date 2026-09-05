using System.Security.Cryptography;
using System.Text.Json;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Infrastructure.IqcMaster;
using Microsoft.EntityFrameworkCore;

// ── P13 bước 3 — nạp tiêu chuẩn IQC từ sheet "Raw" của file master ───────
//   dotnet run --project scripts/IqcMasterImport -- \
//       --src "<IQC report 2026.xlsx>" [--db <path>] [--commit]
//
//   KHÔNG có --commit  → chạy khô: đọc, quy đổi, ĐẾM đầy đủ, KHÔNG chạm DB.
//   Có    --commit     → ghi thật + một dòng AuditLog (Source=Console).
//   Chạy lại lần hai   → phải ra 0 ở mọi cột inserted/updated.
//
// Vì sao là công cụ chạy tay chứ không phải seeder lúc boot: đây là MỘT LẦN
// nạp dữ liệu ngoài, không phải dữ liệu nền của app. Nhét vào boot thì mỗi lần
// khởi động phải đọc 2.319 dòng Excel, và con số 459/5961 mà
// IqcLibrarySeederTests đang khoá sẽ đổi theo một file nằm ngoài repo.

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
bool Flag(string name) => Array.IndexOf(args, name) >= 0;

var src = Arg("--src");
var dbPath = Arg("--db");
var commit = Flag("--commit");
var actor = Arg("--actor") ?? "console";

if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
{
    Console.Error.WriteLine("--src \"<IQC report 2026.xlsx>\" là bắt buộc (không tìm thấy file).");
    return 2;
}
if (string.IsNullOrWhiteSpace(dbPath) && commit)
{
    Console.Error.WriteLine("--db <path> là bắt buộc khi dùng --commit.");
    return 2;
}

Console.WriteLine($"[src] {src}");
List<IqcMasterRow> rows;
using (var fs = File.OpenRead(src)) rows = IqcMasterRawReader.Read(fs);

var codes = rows.Select(r => r.MotherCode.Trim().ToUpperInvariant())
                .Where(s => s.Length > 0).Distinct().Count();
Console.WriteLine($"[parse] {rows.Count} dòng · {codes} mã mẹ phân biệt");

// Chạy khô KHÔNG có --db thì dừng ở đây: không có DB để so thì mọi con số
// insert/update đều là bịa.
if (string.IsNullOrWhiteSpace(dbPath))
{
    Console.WriteLine("[dry-run] không có --db → chỉ đọc file. Thêm --db để xem sẽ ghi những gì.");
    return 0;
}

var abs = Path.GetFullPath(dbPath);
Console.WriteLine($"[db] {abs}  sha8={Sha8(abs)}");

var options = new DbContextOptionsBuilder<MesDbContext>().UseSqlite($"Data Source={abs}").Options;
using var db = new MesDbContext(options);

// KHÔNG tự chạy migration ở đây. Migration lên DB thật là STOP-gate của dự án
// (CLAUDE.md §0) và phải đi qua Phase A→B→C có backup, không đi ké một lệnh
// import. Thiếu migration thì dừng và nói rõ.
var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
if (pending.Count > 0)
{
    Console.Error.WriteLine(
        $"[migrate] DB còn {pending.Count} migration CHƯA áp: {string.Join(", ", pending)}");
    Console.Error.WriteLine("[migrate] Áp theo Phase A→B→C trước rồi chạy lại. Import KHÔNG tự áp.");
    return 3;
}

var specsBefore = await db.IqcMaterialSpecs.CountAsync();
var itemsBefore = await db.IqcSpecItems.CountAsync();
Console.WriteLine($"[before] specs={specsBefore} spec_items={itemsBefore}");

var svc = new IqcMasterImportService(db);
var r = await svc.ImportAsync(rows, actor, commit);

Console.WriteLine($"[map]  đọc={r.RowsRead} bỏ-vì-thiếu-mã={r.RowsSkippedNoCode}");
if (r.CodesWithDuplicateSpecs > 0)
    Console.WriteLine($"[warn] {r.CodesWithDuplicateSpecs} mã nguyên liệu đang có NHIỀU spec trong app — "
                    + "import ghi vào spec có SpecNo nhỏ nhất. Nên để QC dọn lại.");
Console.WriteLine($"[spec] tạo mới={r.SpecsInserted} làm-giàu={r.SpecsEnriched}");
Console.WriteLine($"[item] tạo mới={r.ItemsInserted} cập-nhật={r.ItemsUpdated}");
Console.WriteLine($"[limit] đọc-được-ngưỡng={r.LimitsParsed} phải-chấm-tay={r.LimitsUnparsed}");
if (r.TextConflicts > 0)
    Console.WriteLine($"[conflict] {r.TextConflicts} hạng mục có tiêu chuẩn khác nhau giữa Excel và spec "
                    + "trong app — GIỮ bản của app, cần QC đối chiếu.");

if (!commit)
{
    Console.WriteLine("[dry-run] KHÔNG có --commit → DB không bị chạm. Thêm --commit để ghi thật.");
    return 0;
}

var specsAfter = await db.IqcMaterialSpecs.CountAsync();
var itemsAfter = await db.IqcSpecItems.CountAsync();
var pendingQc = await db.IqcMaterialSpecs.CountAsync(x => x.Approval == IqcSpecApproval.PendingQc);

db.AuditLogs.Add(new AuditLog
{
    Timestamp = DateTime.UtcNow,
    ActorUsername = actor,
    ActorRole = "System",
    Action = AuditAction.QcLibraryImport,
    TargetType = "IqcMaterialSpec",
    TargetId = Path.GetFileName(src),
    Source = "Console",
    // Tóm tắt bằng con số, KHÔNG dump mảng: detail bị cắt cứng 4096 ký tự và
    // vết cắt rơi giữa token JSON ⇒ dòng audit thành JSON hỏng, không đọc được
    // đúng lúc cần điều tra.
    Detail = JsonSerializer.Serialize(new
    {
        src = Path.GetFileName(src),
        rows = r.RowsRead,
        specs_inserted = r.SpecsInserted,
        specs_enriched = r.SpecsEnriched,
        items_inserted = r.ItemsInserted,
        items_updated = r.ItemsUpdated,
        limits_parsed = r.LimitsParsed,
        limits_unparsed = r.LimitsUnparsed,
        specs_total = specsAfter,
        pending_qc = pendingQc,
    }),
});
await db.SaveChangesAsync();

Console.WriteLine($"[after] specs {specsBefore} → {specsAfter} · spec_items {itemsBefore} → {itemsAfter}");
Console.WriteLine($"[after] chờ QC duyệt = {pendingQc}");
Console.WriteLine($"[db] sha8 sau = {Sha8(abs)}");
Console.WriteLine("[done]");
return 0;

static string Sha8(string path)
{
    if (!File.Exists(path)) return "(none)";
    using var s = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(s))[..8].ToLowerInvariant();
}
