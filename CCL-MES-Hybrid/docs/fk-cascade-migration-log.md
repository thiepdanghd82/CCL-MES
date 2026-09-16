# Nhật ký migration — `AddWorkOrderChildForeignKeys` (2026-09-16)

> Bắt buộc theo skill `cmes-migration-abc`: số liệu Phase A → B → C phải nằm
> trong một file **commit được**. File `.db` không vào git; nhật ký thì có.

## Tờ trình gốc ĐO SAI — đính chính trước khi làm bất cứ gì

Tờ trình 10-09 ghi *"khoá ngoại đang được thực thi qua đường EF
(`PRAGMA foreign_keys = 1`), cascade nổ thật trên bản sao"*, và cả ba câu hỏi
gửi Henry đều dựng trên tiền đề đó. Đo lại bằng lệnh trên chính `data/ccl_mes.db`:

```
pragma_foreign_key_list của 5 bảng:
  WoIpqcChecks 0 · WoPlateChecks 0 · WoCutterChecks 0
  WoTraceIndexes 0 · WoTraceSnapshots 0
Bảng KHÁC trong cùng DB có FK: 32
Quan hệ khai trong MesDbContext cho 5 entity: KHÔNG có dòng nào
  (không HasOne, không WithMany, không OnDelete, không navigation property)
```

**Không có khoá ngoại nào để đặt `ON DELETE`.** Câu hỏi thật không phải "cascade
hay RESTRICT" mà là "có thêm ràng buộc tham chiếu vào năm bảng này không".
Đây là chỗ **sót**, không phải lập trường của dự án — 32 bảng khác đều có FK.

Chi tiết chưa ai ghi: tên cột lệch nhau — `WoIpqcChecks` · `WoPlateChecks` ·
`WoCutterChecks` dùng `WorkOrderId`; `WoTraceIndexes` · `WoTraceSnapshots`
dùng `WoId`.

## STOP-gate

Chạm DB thật là STOP-gate theo `CLAUDE.md` §0. Thiệp duyệt tường minh
16-09-2026 sau khi đã được báo rằng việc này **lớn hơn** thứ tờ trình mô tả
(thêm FK = rebuild bảng, không phải đổi một thuộc tính). Không phải agent tự
vượt cổng.

## Thiết kế

| Bảng | Cột | `ON DELETE` | Vì sao |
|---|---|---|---|
| `WoIpqcChecks` | `WorkOrderId` | `CASCADE` | dữ liệu thao tác |
| `WoPlateChecks` | `WorkOrderId` | `CASCADE` | dữ liệu thao tác |
| `WoCutterChecks` | `WorkOrderId` | `CASCADE` | dữ liệu thao tác |
| `WoTraceIndexes` | `WoId` | `CASCADE` | chỉ mục sống, WO mất thì vô nghĩa |
| `WoTraceSnapshots` | `WoId` | **`RESTRICT`** | **bằng chứng bất biến** — DB phải chặn |

Khai **không navigation** (`HasOne<WorkOrder>().WithMany()`): thêm FK là để DB
gác, không phải để đổi hình dạng đối tượng.

Comment sẵn có *"No FK to source entities by design"* vẫn giữ nguyên hiệu lực và
KHÔNG mâu thuẫn — nó nói về FK tới các bảng NGUỒN mà snapshot đóng băng từ đó;
đây là FK tới `WorkOrders`, tức bảng CHA.

## Phase A — baseline

```
backup : data/Backup/SQLite/ccl_mes.db.before-fk-cascade.20260916-132927
sha256 live   a806451f44c84f7f3ed04f22ab8611edbfdc042fcb692a268b0aa3d00c790fee
sha256 backup 350f05d846e054c504c5bd1cd9483c59afb2e19263fd2f9699de9b688d6d1640
```

Hai hash **lệch nhau là bình thường** — SQLite ở chế độ WAL, `.backup` tạo bản
đã checkpoint. Đối chiếu bằng rowcount, không bằng hash (cùng luật với nhật ký
`userperm`).

```
WorkOrders            2
WoIpqcChecks          1
WoPlateChecks         2
WoCutterChecks        2
WoTraceIndexes        2
WoTraceSnapshots      0
AuditLogs          3543
__EFMigrationsHistory 54

integrity_check    ok
foreign_key_check  0 dòng lỗi
dòng mồ côi        0 trên cả 5 bảng
trigger trên 5 bảng 0   ← bẫy L38 KHÔNG dính
snapshot mã        /tmp/snapshot-pre-fkcascade.cs
```

**Đây là lúc rẻ nhất để làm.** Sau đợt purge 10-09 chỉ còn 2 WO, mỗi bảng con
0–2 dòng. Rebuild càng để lâu càng đắt.

## Phase B — sinh + kiểm trên DB CÔ LẬP

```
MES_CONNSTR="Data Source=/tmp/fkcascade-design.db"   ← KHÔNG chạm live
20260916063100_AddWorkOrderChildForeignKeys.cs
5 × AddForeignKey (Up) · 5 × DropForeignKey (Down)
type-affinity: 0 chỗ cần strip
```

**Hai lần hỏng giữa đường, ghi lại vì cả hai đều im lặng:**

1. **Migration đầu tiên RỖNG** — `Up()` và `Down()` đều trống. Nguyên nhân:
   `--no-build` khiến EF nạp `CCL.MES.Infrastructure.dll` trong `bin` của
   startup project, bản **15-09**, nên không thấy thay đổi model. Nếu áp nó lên
   live thì migration chạy xong, `__EFMigrationsHistory` +1, mà schema không đổi
   gì — "thành công" mà không làm gì. Dọn bằng `rm` thủ công (**không**
   `ef migrations remove`), build lại startup project, sinh lại.
2. **`database update` lần đầu ném `PendingModelChangesWarning`** — DLL đang nạp
   có model MỚI (đã thêm FK) nhưng snapshot CŨ, vì migration + snapshot vừa sinh
   ra dưới dạng *source* chưa được biên dịch. Phải build LẠI sau khi
   `migrations add` rồi mới `database update`.

Kiểm chứng schema trên `/tmp/fkcascade-design.db`:

```
WoIpqcChecks      → WorkOrders(Id)  ON DELETE CASCADE
WoPlateChecks     → WorkOrders(Id)  ON DELETE CASCADE
WoCutterChecks    → WorkOrders(Id)  ON DELETE CASCADE
WoTraceIndexes    → WorkOrders(Id)  ON DELETE CASCADE
WoTraceSnapshots  → WorkOrders(Id)  ON DELETE RESTRICT
trigger toàn DB sau rebuild: 8 (còn nguyên)
```

Kiểm chứng **HÀNH VI** (schema đúng chưa chứng minh hành vi đúng):

```
CA 1 — WO 9001, chỉ có dữ liệu thao tác:
  DELETE thành công · WO còn 0 · WoPlateChecks còn 0   ⇒ CASCADE cuốn theo ✓
CA 2 — WO 9002, CÓ hồ sơ truy xuất đóng băng:
  DELETE bị chặn: "FOREIGN KEY constraint failed (19)"
  WO còn 1 · snapshot còn 1 · WoPlateChecks còn 1      ⇒ RESTRICT chặn ✓
```

Lần dựng dữ liệu thử ĐẦU tiên không hợp lệ và đã bị vứt: `INSERT` fail vì
`WorkOrders` có FK riêng sang `Customers`/`Products` mà DB cô lập chưa có dòng
nào — kết quả "còn 0" chỉ vì chưa bao giờ có gì, không chứng minh được cascade.
Làm lại với `foreign_keys=OFF` lúc dựng, `ON` lúc thử.

## ⚠ Rủi ro phải biết trước khi áp live

EF cảnh báo nguyên văn:

```
The migration operation 'PRAGMA foreign_keys = 0;' from migration
'AddWorkOrderChildForeignKeys' cannot be executed in a transaction. If the app
is terminated or an unrecoverable error occurs while this operation is being
executed then the migration will be left in a partially applied state and would
need to be reverted manually.
```

Thêm FK trong SQLite = **rebuild bảng**, và rebuild không chạy trong transaction
được. Đứt giữa chừng ⇒ schema nửa vời, phải khôi phục tay từ backup Phase A.

Giảm rủi ro: **dừng API trước khi áp** (không ai ghi giữa chừng), backup Phase A
đã có, và dữ liệu đang cực ít nên rebuild xong trong tích tắc.

## Phase C — áp live (16-09-2026, Thiệp duyệt)

API **dừng trước khi áp** để không ai ghi giữa chừng (rebuild không chạy trong
transaction được — xem mục rủi ro ở trên). Xác nhận `lsof data/ccl_mes.db` = 0
tiến trình, cổng 5100 đã đóng, app Catalyst không chạy.

```
Applying migration '20260916063206_AddWorkOrderChildForeignKeys'.
Done.
```

### FK trước → sau

```
TRƯỚC:  cả 5 bảng = 0 khoá ngoại
SAU:
  WoIpqcChecks      → WorkOrders(Id)  ON DELETE CASCADE
  WoPlateChecks     → WorkOrders(Id)  ON DELETE CASCADE
  WoCutterChecks    → WorkOrders(Id)  ON DELETE CASCADE
  WoTraceIndexes    → WorkOrders(Id)  ON DELETE CASCADE
  WoTraceSnapshots  → WorkOrders(Id)  ON DELETE RESTRICT
```

### Rowcount trước → sau — KHÔNG suy suyển

```
                    trước   sau
WorkOrders              2     2
WoIpqcChecks            1     1
WoPlateChecks           2     2
WoCutterChecks          2     2
WoTraceIndexes          2     2
WoTraceSnapshots        0     0
AuditLogs            3543  3543
__EFMigrationsHistory  54    55   ← +1, đúng một migration

integrity_check    ok
foreign_key_check  0 dòng lỗi
trigger toàn DB    8 (còn nguyên — rebuild KHÔNG làm mất trigger nào)
migration mới nhất 20260916063206_AddWorkOrderChildForeignKeys
```

### Bật lại API

`api-service.sh install` **từ chối lần đầu**: *"binary Release CŨ HƠN mã nguồn"*.
Cổng gác này đúng và đáng giá — binary cũ không mang thay đổi `MesDbContext`,
chạy nó là chạy một model không khớp schema vừa áp. Build `-c Release` rồi cài lại:

```
tiến trình : 71313
cổng 5100  : đang lắng nghe
/health    : HTTP 200
/api/v2/work-orders không token → HTTP 401   (cổng xác thực còn gác)
```

### Còn nợ sau đợt này

`purge-applied.sql` (quét tay 13 bảng con) nay đã **thừa cho 5 bảng này** — DB tự
cuốn 4 bảng và tự chặn ở bảng thứ 5. Nhưng 8 bảng con còn lại trong script đó vẫn
chưa rà; chưa biết bảng nào còn thiếu FK. Việc tiếp theo là quét nốt.

## Đường lùi

```
rm -f src/CCL.MES.Infrastructure/Migrations/*AddWorkOrderChildForeignKeys*
cp /tmp/snapshot-pre-fkcascade.cs \
   src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs
# DB: khôi phục từ data/Backup/SQLite/ccl_mes.db.before-fk-cascade.20260916-132927
```

**Không** dùng `ef migrations remove` — lệnh đó connect live DB và chạy `Down()`
thật (sự cố 31-05: DROP TABLE AuditLogs).
