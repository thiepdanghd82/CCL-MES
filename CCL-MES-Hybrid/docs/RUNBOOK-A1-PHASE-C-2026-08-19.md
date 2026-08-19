# Runbook — A1 Phase C: áp `AddMaterialLotGenealogy` lên DB sản xuất (2026-08-19)

> Henry duyệt. Thực hiện **sau** khi mã A1 đã merge vào `main` (PR #157, commit `34c1364`) —
> mã trước, schema sau. Áp migration khi mã chưa vào `main` sẽ khiến schema sản
> xuất đi trước mã đang chạy; nếu phải lùi thì `main` không có entity để xử lý.

## Tiền kiểm

```
lsof data/ccl_mes.db                       → không tiến trình nào giữ
lsof -p 33352 | grep ccl_mes               → rỗng (API cổng 5100 không giữ handle)
ls -l data/ccl_mes.db-wal                  → 0 byte
sqlite3 ... "BEGIN IMMEDIATE; ROLLBACK;"   → ghi được, không bị khoá
```

App Hybrid của Henry vẫn chạy ở cổng 5100 trong lúc áp. An toàn vì EF Core mở/đóng
connection theo request, không giữ handle thường trực; và mọi thay đổi đều
**additive** — EF sinh danh sách cột tường minh nên cột mới không làm hỏng bản
build cũ đang chạy.

## Phase A — baseline

| | |
|---|---|
| Backup | `data/Backup/SQLite/ccl_mes.before-a1-materiallot-20260819-121230.db` |
| SHA256 backup | `61508ce3d15b21ea52c0a104785f94014b82568b84ad301657eee9b1e262e99e` |
| SHA256 live TRƯỚC | `d485daa94dc7373270cd65a5c55285002d2232deface2c8934c2f36ca25631e2` |
| Model snapshot | copy sang `/tmp/snapshot-pre-a1-phasec.cs` |
| Migration cuối trước đó | `20260818083843_RemodelCheckItemLibraryV5` |

Rowcount TRƯỚC: `WoMaterials 82 · IqcInspections 3 · IqcResultDetails 7 ·
RawMaterials 2127 · WorkOrders 27 · WoLegs 0 · SemiLots 6 · AuditLogs 2379`

Index `WoMaterials` TRƯỚC — **bốn cái, phải còn đủ sau khi rebuild bảng**:
```
IX_WoMaterials_WorkOrderId
IX_WoMaterials_WoLegId
IX_WoMaterials_WorkOrderId_BomLineIdx                ← partial unique
IX_WoMaterials_WorkOrderId_WoLegId_BomLineIdx        ← partial unique
```

## Áp

```bash
MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=$(pwd)/data/ccl_mes.db" \
  dotnet ef database update -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web
# Build succeeded.
# Applying migration '20260819042948_AddMaterialLotGenealogy'.
# Done.   exit=0
```

EF cảnh báo `PRAGMA foreign_keys = 0` không chạy được trong transaction — đã biết
trước và đã đo trên bản copy dữ liệu thật ở Phase B. Đây là lý do **rollback là
restore file backup, không dùng `Down()`**.

## Nghiệm thu

**Index `WoMaterials` SAU — bốn cái cũ còn nguyên + một cái mới:**
```
IX_WoMaterials_MaterialLotId                         ← mới
IX_WoMaterials_WoLegId
IX_WoMaterials_WorkOrderId
IX_WoMaterials_WorkOrderId_BomLineIdx                ← partial unique, SỐNG SÓT
IX_WoMaterials_WorkOrderId_WoLegId_BomLineIdx        ← partial unique, SỐNG SÓT
```
Đây là điểm dễ mất nhất: `AddForeignKey` trên SQLite rebuild cả bảng. Kiểm bằng
mắt, không tin suy luận.

**Cột mới:** `PRAGMA table_info(WoMaterials)` → `9|MaterialLotId|INTEGER|0||0` (nullable).
`LotNo` giữ nguyên kiểu `TEXT` — **không** `AlterColumn`, đúng §3.3 hợp đồng.

**Khoá tự nhiên chuỗi đã siết ở schema:**
```sql
"LotNo"         TEXT COLLATE NOCASE NOT NULL,
"PartNo"        TEXT COLLATE NOCASE NOT NULL,
"SupplierLotNo" TEXT COLLATE NOCASE NULL,
CONSTRAINT "CK_MaterialLots_LotNo_Trimmed"  CHECK ("LotNo"  = TRIM("LotNo")  AND LENGTH("LotNo")  > 0),
CONSTRAINT "CK_MaterialLots_PartNo_Trimmed" CHECK ("PartNo" = TRIM("PartNo") AND LENGTH("PartNo") > 0)
```

**Cả hai partial unique index** (thiếu một cái thì lô chưa resolve lọt hết, vì
trong SQLite `NULL ≠ NULL` trong unique index):
```
IX_MaterialLots_LotNo_RawMaterialId        WHERE RawMaterialId IS NOT NULL
IX_MaterialLots_LotNo_PartNo_Unresolved    WHERE RawMaterialId IS NULL
```

**Trigger RowVersion:** `MaterialLots_RowVersion_OnInsert`, `MaterialLots_RowVersion_OnUpdate`

**Rowcount SAU — bảng cũ không đổi một dòng nào:**
```
WoMaterials 82 · IqcInspections 3 · RawMaterials 2127 · WorkOrders 27
SemiLots 6 · AuditLogs 2379
MaterialLots 0 · WoMaterialConsumptions 0        (bảng mới, chưa backfill)
```

`PRAGMA integrity_check` = `ok`
`__EFMigrationsHistory` cuối = `20260819042948_AddMaterialLotGenealogy`
**SHA256 live SAU** = `0a5e54ef5a626e417e346b9f495e2651cc6fe7322ab2577e7defca664136925c`

## Đường lui

```bash
cp data/Backup/SQLite/ccl_mes.before-a1-materiallot-20260819-121230.db data/ccl_mes.db
shasum -a 256 data/ccl_mes.db   # phải ra d485daa9…5631e2
```
**KHÔNG dùng `Down()`** — `DropColumn MaterialLotId` rebuild `WoMaterials` và sẽ
xoá mất hai partial unique index ở trên.
**KHÔNG dùng `dotnet ef migrations remove`** — sự cố 2026-05-31 đã DROP TABLE AuditLogs vì lệnh này.

## CHƯA XONG — backfill

**Backfill chưa chạy, vì `MaterialLotBackfillService` không có đường gọi nào
trong mã production.** Grep toàn repo: ngoài dòng đăng ký DI ở `Program.cs:445`,
service chỉ được dựng **trong test**. Không endpoint, không CLI, không hosted service.

Đây là mã chết lọt vào `main` qua PR #157 — đúng loại việc C3 vừa dọn. Đang bổ
sung endpoint theo tiền lệ `POST /api/v2/traceability/backfill` (AdminOnly).

Sau khi có endpoint:
1. Chạy backfill → kỳ vọng **5 lô `Quarantine`** (Phase B đã đo: 0/5 chuỗi lô live khớp IQC).
2. Chạy lần hai → rowcount phải không đổi (idempotent qua dấu `backfill-a1`).
3. Xem con số `quarantined` → **rồi mới** bàn ngày lật `Mes:MaterialLot:EnforceReleased`.

Cờ đang **tắt** (mặc định): vẫn resolve lô, vẫn ghi tiêu thụ, nhưng trả 200 +
warning thay vì 422; audit vẫn emit với `enforced:false`. Nghĩa là **đo được
chính xác bao nhiêu ca sẽ bị chặn trước khi chặn thật**.

## Nợ tách riêng

`SemiLots.LotNo` có `UNIQUE INDEX` nhưng **thiếu `COLLATE NOCASE`** — L28 tái phạm.
Hôm nay `LOT-001` và `lot-001` là hai lô khác nhau trong kho bán thành phẩm.
PR riêng: `AlterColumn` rebuild bảng sẽ xoá 2 trigger RowVersion của `SemiLots`,
phải dựng lại trong cùng migration.
