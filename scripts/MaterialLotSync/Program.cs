using System.Security.Cryptography;
using System.Text.Json;
using CCL.MES.Domain;
using CCL.MES.Domain.Audit;
using CCL.MES.Domain.Entities;
using CCL.MES.Domain.StateMachine;
using CCL.MES.Infrastructure;
using Microsoft.EntityFrameworkCore;

// ── Nối mạch IQC → MaterialLot để IPQC tra được lô ────────────────────────
//   dotnet run --project scripts/MaterialLotSync -- --db <path> [--commit]
//
//   KHÔNG có --commit  → chạy khô: đọc, quy đổi, ĐẾM đầy đủ, KHÔNG chạm DB.
//   Có    --commit     → ghi thật + một dòng AuditLog (Source=Console).
//   Chạy lại lần hai   → phải ra thêm=0 sửa=0.
//
// VÌ SAO CẦN. Màn IPQC dựng cột "LOT IQC GỐC" bằng chuỗi FK
//   WoMaterial.MaterialLotId → MaterialLot.IqcInspectionId → IqcInspection
// nhưng mạch đó ĐỨT ở mắt giữa: đo 2026-09-09, MaterialLots có 28 dòng và
// 0 dòng nào có IqcInspectionId, trong khi IqcInspections có 5334 phiếu.
// Nên cột đó toàn dấu "—" và MỌI dòng vật tư bị đánh DIVERGENT.
//
// KHÔNG chữa bằng cách tra chuỗi lúc đọc. Gate A1 (gate-material-lot-fk.sh)
// cấm nối theo chuỗi lô, và số đo của nó vẫn đúng hôm nay: 5 dòng
// WoMaterials có LotNo, 0 dòng khớp lô bên IQC. Chuỗi chỉ là NHÃN; nối phải
// đi bằng khoá số. Việc đúng là ĐỔ DỮ LIỆU vào MaterialLots để mạch liền.
//
// LUẬT CHỌN PHIẾU CHỦ: một lô có thể có nhiều phiếu IQC trái ngược nhau
// (đo được: 4819 cặp (mã,lô), 387 cặp nhiều phiếu, 34 cặp vừa Pass vừa
// Fail). Thiệp chốt 2026-09-09 phương án 2 — "hễ có Fail thì báo Fail".
// Luật nằm ở IqcLotVerdict, có 18 test khoá; ở đây chỉ gọi.

string? Arg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
bool Flag(string name) => Array.IndexOf(args, name) >= 0;

// Dấu chủ sở hữu của các dòng do sync tạo — để lần sau phân biệt được dòng
// nào máy đẻ ra với dòng nào người nhập.
const string SyncActor = "iqc-lot-sync";

var dbPath = Arg("--db");
var commit = Flag("--commit");
var actor = Arg("--actor") ?? "console";

if (string.IsNullOrWhiteSpace(dbPath))
{
    Console.Error.WriteLine("--db <path> là bắt buộc.");
    return 2;
}

var abs = Path.GetFullPath(dbPath);
if (!File.Exists(abs))
{
    Console.Error.WriteLine($"Không thấy DB: {abs}");
    return 2;
}
Console.WriteLine($"[db] {abs}  sha8={Sha8(abs)}");
Console.WriteLine(commit ? "[mode] COMMIT — sẽ GHI vào DB" : "[mode] dry-run — KHÔNG chạm DB");

var options = new DbContextOptionsBuilder<MesDbContext>().UseSqlite($"Data Source={abs}").Options;
using var db = new MesDbContext(options);

// KHÔNG tự chạy migration ở đây — migration lên DB thật là STOP-gate của dự án
// (CLAUDE.md §0), phải đi Phase A→B→C có backup, không đi ké lệnh sync.
var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
if (pending.Count > 0)
{
    Console.Error.WriteLine($"[migrate] DB còn {pending.Count} migration CHƯA áp: {string.Join(", ", pending)}");
    Console.Error.WriteLine("[migrate] Áp theo Phase A→B→C trước rồi chạy lại. Sync KHÔNG tự áp.");
    return 3;
}

var beforeLots = await db.MaterialLots.CountAsync();
var beforeLinked = await db.MaterialLots.CountAsync(x => x.IqcInspectionId != null);
var totalIqc = await db.IqcInspections.CountAsync();
Console.WriteLine($"[before] material_lots={beforeLots} (có nối IQC={beforeLinked}) · iqc_inspections={totalIqc}");

// ── Đọc phiếu IQC ──────────────────────────────────────────────────────────
var tickets = await db.IqcInspections.AsNoTracking()
    .Select(i => new
    {
        i.Id, i.CodeIfs, i.PartNo, i.LotNumber, i.BatchNumber, i.ReceiptNo,
        i.Result, i.ReceivedDate, i.RawMaterialId, i.SupplierName, i.Quantity, i.UomQty,
    })
    .ToListAsync();

var vt = tickets.Select(t => new IqcLotVerdict.Ticket(
    t.Id, t.CodeIfs, t.PartNo, t.LotNumber, t.BatchNumber, t.ReceiptNo, t.Result, t.ReceivedDate)).ToList();
var byId = tickets.ToDictionary(t => t.Id);

// ── Nhóm theo (PartNo, lô) ─────────────────────────────────────────────────
// Nhóm theo PartNo chứ không CodeIfs: PartNo là cột KHÔNG rỗng dòng nào
// (đo: 0/5334 trống) và là cột MaterialLot.PartNo yêu cầu bắt buộc. Hai cột
// bằng nhau ở 5331/5334 dòng nên khác biệt là nhiễu, không phải ngữ nghĩa.
var groups = new Dictionary<(string Part, string Lot), List<long>>();
var skipNoLot = 0;
var skipNoPart = 0;
foreach (var t in vt)
{
    var part = (t.PartNo ?? "").Trim();
    var lot = IqcLotVerdict.EffectiveLot(t);
    if (part.Length == 0) { skipNoPart++; continue; }
    if (string.IsNullOrWhiteSpace(lot)) { skipNoLot++; continue; }
    var key = (part.ToUpperInvariant(), lot!.ToUpperInvariant());
    if (!groups.TryGetValue(key, out var lst)) groups[key] = lst = new();
    lst.Add(t.Id);
}
Console.WriteLine($"[nhóm] cặp (mã,lô) = {groups.Count}   bỏ: thiếu-lô={skipNoLot} thiếu-mã={skipNoPart}");

// ── Chốt kết luận từng cặp ─────────────────────────────────────────────────
var plans = new List<Plan>();
int conflict = 0, multi = 0;
var byStatus = new Dictionary<string, int>();
foreach (var ((partU, lotU), ids) in groups)
{
    var any = byId[ids[0]];
    var part = (any.PartNo ?? "").Trim();
    var lot = IqcLotVerdict.EffectiveLot(vt.First(x => x.Id == ids[0]))!;

    var v = IqcLotVerdict.Resolve(part, lot, vt.Where(x => ids.Contains(x.Id)));
    if (v is null) continue;                       // không thể xảy ra, nhóm đã lọc
    var gov = byId[v.Value.GoverningId];

    if (v.Value.HasConflict) conflict++;
    if (v.Value.TicketCount > 1) multi++;

    // Ánh xạ theo ĐÚNG sơ đồ trạng thái ghi trong MaterialLot.cs:
    //   IQC Pass → Released · IQC Fail → Rejected (terminal) · chưa kết luận → Quarantine
    var status = v.Value.Result switch
    {
        QcResult.Pass => nameof(MaterialLotStatus.Released),
        QcResult.Fail => nameof(MaterialLotStatus.Rejected),
        _ => nameof(MaterialLotStatus.Quarantine),
    };
    byStatus[status] = byStatus.GetValueOrDefault(status) + 1;

    // RawMaterialId lấy từ phiếu chủ; chỉ 1 cặp (mã,lô) trên toàn bộ dữ liệu
    // có RawMaterialId không đồng nhất giữa các phiếu nên không cần luật riêng.
    plans.Add(new Plan(part, lot, gov.RawMaterialId, v.Value.GoverningId, gov.ReceiptNo,
        status, gov.SupplierName, gov.ReceivedDate, gov.Quantity, gov.UomQty,
        v.Value.TicketCount, v.Value.HasConflict));
}

Console.WriteLine($"[chốt] {plans.Count} lô   nhiều-phiếu={multi}  MÂU THUẪN(lấy Fail)={conflict}");
Console.WriteLine($"[trạng thái] {string.Join(" · ", byStatus.OrderByDescending(x => x.Value).Select(x => $"{x.Key}={x.Value}"))}");

// ── Đối chiếu với MaterialLots đang có ─────────────────────────────────────
var existing = await db.MaterialLots.ToListAsync();
var idx = new Dictionary<(string, string), MaterialLot>();
foreach (var l in existing)
{
    var k = ((l.PartNo ?? "").Trim().ToUpperInvariant(), (l.LotNo ?? "").Trim().ToUpperInvariant());
    idx.TryAdd(k, l);
}

int insert = 0, update = 0, same = 0, keepHuman = 0, keepLinked = 0;
var sampleIns = new List<string>();
var sampleUpd = new List<string>();

foreach (var p in plans)
{
    var k = (p.PartNo.ToUpperInvariant(), p.LotNo.ToUpperInvariant());
    if (!idx.TryGetValue(k, out var cur))
    {
        insert++;
        if (sampleIns.Count < 5) sampleIns.Add($"{p.PartNo}/{p.LotNo} → {p.Status} (phiếu {p.ReceiptNo})");
        if (commit)
        {
            db.MaterialLots.Add(new MaterialLot
            {
                LotNo = p.LotNo, PartNo = p.PartNo, RawMaterialId = p.RawMaterialId,
                IqcInspectionId = p.GoverningId, SupplierName = p.SupplierName,
                ReceivedAt = p.ReceivedAt, QtyReceived = p.Qty ?? 0, QtyAvailable = p.Qty ?? 0,
                Uom = p.Uom, Status = p.Status,
                StatusReason = p.HasConflict
                    ? $"iqc-lot-sync: {p.TicketCount} phiếu mâu thuẫn, lấy kết luận chặn"
                    : "iqc-lot-sync",
                CreatedBy = SyncActor, UpdatedBy = SyncActor,
            });
        }
        continue;
    }

    // KHÔNG đè quyết định của người. Status do người đổi (StatusChangedBy có
    // giá trị) là một hành động có chủ ý — sync là máy, không được ghi đè.
    if (!string.IsNullOrWhiteSpace(cur.StatusChangedBy)) { keepHuman++; continue; }
    // Đã nối sẵn phiếu khác thì để nguyên: sync chỉ LẤP chỗ trống, không đổi neo.
    if (cur.IqcInspectionId is not null && cur.IqcInspectionId != p.GoverningId) { keepLinked++; continue; }

    var changed = cur.IqcInspectionId != p.GoverningId || !string.Equals(cur.Status, p.Status, StringComparison.Ordinal);
    if (!changed) { same++; continue; }

    update++;
    if (sampleUpd.Count < 5)
        sampleUpd.Add($"{p.PartNo}/{p.LotNo}: {cur.Status}→{p.Status}, IQC {cur.IqcInspectionId?.ToString() ?? "—"}→{p.GoverningId}");
    if (commit)
    {
        cur.IqcInspectionId = p.GoverningId;
        cur.Status = p.Status;
        cur.UpdatedBy = SyncActor;
        cur.UpdatedAt = DateTime.UtcNow;
    }
}

Console.WriteLine();
Console.WriteLine($"[kế hoạch] thêm={insert}  sửa={update}  không-đổi={same}"
                + $"  giữ-vì-người-đã-đổi={keepHuman}  giữ-vì-đã-neo-phiếu-khác={keepLinked}");
foreach (var s in sampleIns) Console.WriteLine($"    + {s}");
foreach (var s in sampleUpd) Console.WriteLine($"    ~ {s}");

// ── Lô sẽ dùng được tới đâu cho IPQC ───────────────────────────────────────
var woCodes = await db.WoMaterials.AsNoTracking()
    .Select(m => m.MaterialCode).Distinct().ToListAsync();
var planParts = plans.Select(p => p.PartNo).ToHashSet(StringComparer.OrdinalIgnoreCase);
var covered = woCodes.Count(c => !string.IsNullOrWhiteSpace(c) && planParts.Contains(c.Trim()));
Console.WriteLine($"[phủ] mã NVL bên WoMaterials có lô sau sync = {covered}/{woCodes.Count}");

if (!commit)
{
    Console.WriteLine();
    Console.WriteLine("[dry-run] KHÔNG có gì được ghi. Thêm --commit để ghi thật.");
    return 0;
}

await db.SaveChangesAsync();
db.AuditLogs.Add(new AuditLog
{
    Action = AuditAction.MaterialLotSync,
    ActorUsername = actor,
    ActorRole = "Console",
    TargetType = nameof(MaterialLot),
    TargetId = "iqc-lot-sync",
    Source = "Console",
    Detail = JsonSerializer.Serialize(new
    {
        inserted = insert, updated = update, unchanged = same,
        keptHumanStatus = keepHuman, keptOtherLink = keepLinked,
        lots = plans.Count, conflicts = conflict, rule = "fail-wins",
    }),
});
await db.SaveChangesAsync();

var afterLots = await db.MaterialLots.CountAsync();
var afterLinked = await db.MaterialLots.CountAsync(x => x.IqcInspectionId != null);
Console.WriteLine($"[after] material_lots={afterLots} (có nối IQC={afterLinked})  sha8={Sha8(abs)}");
return 0;

static string Sha8(string path)
{
    using var fs = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(fs))[..8].ToLowerInvariant();
}

internal sealed record Plan(
    string PartNo, string LotNo, long? RawMaterialId, long GoverningId, string? ReceiptNo,
    string Status, string? SupplierName, DateTime ReceivedAt, double? Qty, string? Uom,
    int TicketCount, bool HasConflict);
