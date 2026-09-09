using System.Security.Cryptography;
using System.Text.Json;
using CCL.MES.Application.Services;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Infrastructure.IqcMaster;
using Microsoft.EntityFrameworkCore;

// ── P13 bước 5 — nạp sheet NG của file master vào tab NG / claim ─────────
//   dotnet run --project scripts/IqcNgImport -- \
//       --src "<IQC report 2026.xlsx>" [--db <path>] [--commit]
//
//   KHÔNG có --commit  → chạy khô: đọc, quy đổi, ĐẾM đầy đủ, KHÔNG chạm DB.
//   Có    --commit     → ghi thật + một dòng AuditLog (Source=Console).
//   Chạy lại lần hai   → phải ra inserted=0 updated=0.
//
// Idempotent theo ImportSource = "xlsx:NG Material:h{hash}-{n}" (xem
// IqcNgImport): mã băm của NỘI DUNG dòng, cộng số thứ tự cho các dòng trùng
// hệt nhau. Bản trước khoá theo vị trí dòng và vỡ khi file chèn/xoá dòng ở
// giữa; importer tự NÂNG CẤP các bản ghi khoá cũ tại chỗ, không nhân đôi.

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
List<IqcNgSheetRow> rows;
using (var fs = File.OpenRead(src)) rows = IqcNgSheetReader.Read(fs);
Console.WriteLine($"[parse] {rows.Count} dòng có dữ liệu");

var mapped = IqcNgImport.MapAll(rows).Select(x => (Row: x.Row, M: x.Mapped)).ToList();
var ok = mapped.Where(x => x.M.Record is not null).ToList();
var skipped = mapped.Where(x => x.M.Record is null).ToList();
var dupKeys = ok.GroupBy(x => x.M.Record!.ImportSource).Count(g => g.Count() > 1);
if (dupKeys > 0)
{
    Console.Error.WriteLine($"[bug] {dupKeys} khoá bị trùng sau MapAll — dừng, không ghi.");
    return 4;
}
Console.WriteLine($"[map]  quy đổi được={ok.Count}  bỏ={skipped.Count}");
foreach (var g in skipped.GroupBy(x => x.M.SkipReason).OrderByDescending(g => g.Count()))
    Console.WriteLine($"       bỏ {g.Count(),3} × {g.Key} (dòng {string.Join(",", g.Take(6).Select(x => x.Row.RowNumber))}…)");

Console.WriteLine($"[stage] {string.Join(" · ", ok.GroupBy(x => x.M.Record!.DetectedStage).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={g.Count()}"))}");
Console.WriteLine($"[status] {string.Join(" · ", ok.GroupBy(x => x.M.Record!.Status).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={g.Count()}"))}");
Console.WriteLine($"[settle] {string.Join(" · ", ok.GroupBy(x => x.M.Record!.Settlement).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={g.Count()}"))}");
Console.WriteLine($"[qty]   có-m²={ok.Count(x => x.M.Record!.NgAreaM2 is not null)} có-pcs/m={ok.Count(x => x.M.Record!.NgQty is not null)} có-cuộn={ok.Count(x => x.M.Record!.NgRolls is not null)}");

if (string.IsNullOrWhiteSpace(dbPath))
{
    Console.WriteLine("[dry-run] không có --db → chỉ đọc file. Thêm --db để xem sẽ ghi những gì.");
    return 0;
}

var abs = Path.GetFullPath(dbPath);
Console.WriteLine($"[db] {abs}  sha8={Sha8(abs)}");

var options = new DbContextOptionsBuilder<MesDbContext>().UseSqlite($"Data Source={abs}").Options;
using var db = new MesDbContext(options);

// KHÔNG tự chạy migration ở đây — migration lên DB thật là STOP-gate của dự án
// (CLAUDE.md §0), phải đi Phase A→B→C có backup, không đi ké lệnh import.
var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
if (pending.Count > 0)
{
    Console.Error.WriteLine($"[migrate] DB còn {pending.Count} migration CHƯA áp: {string.Join(", ", pending)}");
    Console.Error.WriteLine("[migrate] Áp theo Phase A→B→C trước rồi chạy lại. Import KHÔNG tự áp.");
    return 3;
}

var before = await db.IqcNgRecords.CountAsync();
var beforeImported = await db.IqcNgRecords.CountAsync(x => x.ImportSource != null);
Console.WriteLine($"[before] ng_records={before} (trong đó nạp-từ-excel={beforeImported})");

// Khoá nối vật liệu là PartNo (đo được 84%), KHÔNG phải mã mẹ.
var partNos = await db.RawMaterials.AsNoTracking()
    .Select(x => x.PartNo).Where(x => x != null).Distinct().ToListAsync();
var partSet = new HashSet<string>(partNos!, StringComparer.OrdinalIgnoreCase);
var matched = ok.Count(x => x.M.Record!.PartNo is { } p && partSet.Contains(p));
Console.WriteLine($"[link] PartNo khớp RawMaterials = {matched}/{ok.Count}"
                + $" ({(ok.Count == 0 ? 0 : 100.0 * matched / ok.Count):0.#}%)");

var existing = await db.IqcNgRecords
    .Where(x => x.ImportSource != null && x.ImportSource.StartsWith(IqcNgImport.SourcePrefix))
    .ToDictionaryAsync(x => x.ImportSource!, x => x);

// Bản nạp CŨ khoá theo vị trí dòng. Khớp lại theo ĐÚNG số dòng rồi đổi sang
// khoá nội dung — nếu bỏ qua bước này thì lần chạy đầu sau khi đổi khoá sẽ
// chèn thêm 139 bản ghi nữa và sổ nhân đôi.
var legacy = await db.IqcNgRecords
    .Where(x => x.ImportSource != null && x.ImportSource.StartsWith(IqcNgImport.LegacyRowPrefix))
    .ToDictionaryAsync(x => x.ImportSource!, x => x);
var rekeyed = 0;
if (legacy.Count > 0)
{
    Console.WriteLine($"[legacy] {legacy.Count} bản ghi còn khoá theo vị trí dòng — sẽ nâng cấp tại chỗ.");
    foreach (var (row, m) in ok)
    {
        var oldKey = IqcNgImport.LegacyRowPrefix + row.RowNumber;
        if (!legacy.TryGetValue(oldKey, out var cur)) continue;
        var newKey = m.Record!.ImportSource!;
        if (existing.ContainsKey(newKey)) continue;   // đã có bản mới, để nguyên
        cur.ImportSource = newKey;
        existing[newKey] = cur;
        rekeyed++;
    }
    Console.WriteLine($"[legacy] đổi khoá được {rekeyed}/{legacy.Count}"
                    + (rekeyed == legacy.Count ? "" : "  ⚠ phần còn lại sẽ thành bản ghi MỚI — file đã đổi so với lần nạp trước"));
}

int inserted = 0, updated = 0, unchanged = 0;
foreach (var (_, m) in ok)
{
    var rec = m.Record!;
    if (!existing.TryGetValue(rec.ImportSource!, out var cur))
    {
        if (commit) db.IqcNgRecords.Add(rec);
        inserted++;
        continue;
    }
    // So từng field rồi mới set: chạy lại phải ra 0 update, nếu không thì
    // không phân biệt được "file đổi" với "importer tự ghi đè mỗi lần".
    var changed =
        Set(() => cur.PartNo, v => cur.PartNo = v, rec.PartNo) |
        Set(() => cur.SupplierLotNo, v => cur.SupplierLotNo = v, rec.SupplierLotNo) |
        Set(() => cur.SupplierName, v => cur.SupplierName = v, rec.SupplierName) |
        Set(() => cur.MaterialName, v => cur.MaterialName = v, rec.MaterialName) |
        Set(() => cur.PoNo, v => cur.PoNo = v, rec.PoNo) |
        Set(() => cur.DefectName, v => cur.DefectName = v, rec.DefectName) |
        Set(() => cur.NgUom, v => cur.NgUom = v, rec.NgUom) |
        Set(() => cur.ClaimRef, v => cur.ClaimRef = v, rec.ClaimRef) |
        Set(() => cur.SupplierNote, v => cur.SupplierNote = v, rec.SupplierNote) |
        Set(() => cur.Remark, v => cur.Remark = v, rec.Remark) |
        SetV(() => cur.DetectedAt, v => cur.DetectedAt = v, rec.DetectedAt) |
        SetV(() => cur.DetectedStage, v => cur.DetectedStage = v, rec.DetectedStage) |
        SetV(() => cur.Status, v => cur.Status = v, rec.Status) |
        SetV(() => cur.Settlement, v => cur.Settlement = v, rec.Settlement) |
        SetV(() => cur.ClaimedAt, v => cur.ClaimedAt = v, rec.ClaimedAt) |
        SetV(() => cur.NgQty, v => cur.NgQty = v, rec.NgQty) |
        SetV(() => cur.NgAreaM2, v => cur.NgAreaM2 = v, rec.NgAreaM2) |
        SetV(() => cur.NgRolls, v => cur.NgRolls = v, rec.NgRolls);
    if (changed) updated++; else unchanged++;
}

Console.WriteLine($"[write] thêm={inserted} sửa={updated} không-đổi={unchanged} đổi-khoá={rekeyed}");

if (!commit)
{
    Console.WriteLine("[dry-run] KHÔNG có --commit → DB không bị chạm. Thêm --commit để ghi thật.");
    return 0;
}

var after = await db.IqcNgRecords.CountAsync();
db.AuditLogs.Add(new AuditLog
{
    Timestamp = DateTime.UtcNow,
    ActorUsername = actor,
    ActorRole = "System",
    Action = AuditAction.QcLibraryImport,
    TargetType = "IqcNgRecord",
    TargetId = Path.GetFileName(src),
    Source = "Console",
    Detail = JsonSerializer.Serialize(new
    {
        src = Path.GetFileName(src),
        rows_read = rows.Count,
        mapped = ok.Count,
        skipped = skipped.Count,
        inserted,
        updated,
        unchanged,
        rekeyed,
        partno_matched = matched,
        total_after = after,
    }),
});
await db.SaveChangesAsync();

Console.WriteLine($"[after] ng_records {before} → {await db.IqcNgRecords.CountAsync()}");
Console.WriteLine($"[db] sha8 sau = {Sha8(abs)}");
Console.WriteLine("[done]");
return 0;

static bool Set(Func<string?> get, Action<string?> set, string? v)
{
    if (string.Equals(get(), v, StringComparison.Ordinal)) return false;
    set(v); return true;
}

static bool SetV<T>(Func<T> get, Action<T> set, T v)
{
    if (EqualityComparer<T>.Default.Equals(get(), v)) return false;
    set(v); return true;
}

static string Sha8(string path)
{
    if (!File.Exists(path)) return "(none)";
    using var s = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(s))[..8].ToLowerInvariant();
}
