# A1 — Mạch lô nguyên vật liệu (hợp đồng thiết kế ĐÃ DUYỆT)

> Trạng thái: **Henry duyệt 2026-08-19.** Thi công thuộc **Đợt 2**, bắt đầu sau
> khi Đợt 1 đóng STOP-gate. Tài liệu này là hợp đồng — implementer không được
> đổi hình dạng, thấy sai thì dừng và báo.

## 0. Vì sao A1 tồn tại

Hôm nay `WoMaterials.LotNo` là **chuỗi tự do**, ghi tại
`CCL-MES-Hybrid/src/CCL.MES.Api/Controllers/PrepressController.cs:133` không qua
một lớp kiểm nào. Đo trên live 2026-08-19:

| Đo | Kết quả |
|---|---|
| `WoMaterials` tổng | 82 |
| có `LotNo` khác rỗng | **5** |
| trong đó khớp `RawMaterials.PartNo` | **0** |
| trong đó khớp `IqcInspections.LotNumber` | **0** |

Nghĩa là: **chuỗi lô hiện tại không nối được với bất cứ thứ gì.** Khách hàng hỏi
"cuộn màng nào đã vào đơn này, phiếu IQC đâu" — hệ không trả lời được.

## 1. Sáu quyết định của Henry (không thương lượng)

| # | Quyết định | Hệ quả kỹ thuật |
|---|---|---|
| **Đ1** | **Nới luật baseline read-only** thành: `src/CCL.MES.*` read-only **trừ** (a) file MỚI thuần thêm, (b) dòng đăng ký `DbSet`/config, (c) migration | `MaterialLot.cs` + `WoMaterialConsumption.cs` đặt trong `src/CCL.MES.Domain/Entities/`. Đúng tiền lệ `SemiLot.cs` (commit `b66bdb4`) |
| **Đ2** | **`ExpiryAt` nhập tay khi làm IQC** | **KHÔNG** đụng `RawMaterials` (2127 dòng). Phạm vi migration hẹp: 2 bảng mới + 1 cột mới |
| **Đ3** | **Supervisor đảo tiêu thụ · QC được gia hạn lô hết hạn sau kiểm lại** | +4 cột vòng đời; `Expired → Released` là transition hợp lệ có điều kiện |
| **Đ4** | **Mỗi lần quét sinh một dòng tiêu hao riêng** | **BỎ** unique index chống quét lặp. Chống bấm nhầm chuyển sang `Idempotency-Key` |
| **Đ5** | Giữ tên `IqcInspection` (orchestrator quyết — đổi tên phá luật additive) | FK trỏ `IqcInspectionId`. `IqcReceipt` trong báo cáo docx là **sai**, không tồn tại |
| **Đ6** | Lô backfill không khớp → tạo lô `Quarantine` (PA-1, 23/25 điểm) | Mạch lô liền cho mọi WO; đảo bằng một câu `DELETE` |

## 2. Hệ quả của Đ4 — đọc kỹ, đây là chỗ dễ làm sai

Đặc tả gốc dựa vào
`UNIQUE(WoMaterialId, MaterialLotId) WHERE ReversedAt IS NULL` để quét lặp không
sinh dòng thứ hai. **Đ4 xoá bỏ index đó.** Ba thứ phải đổi theo:

1. **Chống bấm nhầm chuyển hoàn toàn sang `Idempotency-Key`** (middleware đã có,
   đã test). Cùng key → 1 dòng. Khác key → 2 dòng, cả hai hiện trong hồ sơ.
   Đây là đánh đổi Henry chọn có ý thức: **hồ sơ chi tiết hơn, chống bấm nhầm
   yếu hơn**. Ghi rõ trong tài liệu vận hành, đừng để operator ngạc nhiên.
2. **Backfill mất chỗ dựa idempotent.** Thay bằng dấu `CreatedBy = 'backfill-a1'`
   và điều kiện `NOT EXISTS (... WHERE CreatedBy='backfill-a1' AND WoMaterialId=?)`.
   Chạy hai lần vẫn phải ra cùng rowcount — có test bắt buộc.
3. **Hai khung nhìn, không phải một.** Màn hình truy xuất hiển thị **từng lần
   quét** (đúng ý Đ4). Con số "đã tiêu hao bao nhiêu" phải `SUM(QtyUsed)` theo
   `(WoMaterialId, MaterialLotId)` — không được lấy dòng cuối.

## 3. Schema

### 3.1 `MaterialLots`

Khoá tự nhiên `LotNo` + (`RawMaterialId` khi resolve được, `PartNo` khi chưa).
Cột: `Id · LotNo(64) · RawMaterialId? · PartNo(64) · IqcInspectionId? ·
SupplierName(120)? · SupplierLotNo(64)? · ReceivedAt · ExpiryAt? ·
QtyReceived · QtyAvailable · Uom(16)? · Status(16) · StatusReason(500)? ·
StatusChangedBy(80)? · StatusChangedAt? · RowVersion · BaseEntity`

**Bốn cột vòng đời từ Đ3:**
`RetestedAt? · RetestedBy(80)? · ExpiryExtendedTo? · ExpiryExtendedBy(80)?`

**Enum `MaterialLotStatus` lưu dạng CHUỖI** (bài học L27 — thêm trạng thái về
sau là dữ liệu, 0 migration): `Quarantine · Released · Rejected · Consumed · Expired`

```
Quarantine ──IQC Pass──▶ Released ──QtyAvailable=0──▶ Consumed
     │                      │  ▲                          │
     │                      │  └──── đảo tiêu thụ ─────────┘  (Supervisor, Đ3)
     └──IQC Fail──▶ Rejected│                                  QtyAvailable>0 ⇒ về Released
        (terminal)          └──ExpiryAt<now──▶ Expired
                                                  │
                                                  └─ QC kiểm lại + gia hạn ─▶ Released  (Đ3)
```

**Chữ ký (orchestrator quyết, Henry có quyền lật):** Release lần đầu = **một**
chữ ký QC. **Gia hạn lô hết hạn = hai vai khác nhau** (người kiểm lại ≠ người
duyệt) — theo đúng tiền lệ `OqcSignaturePolicy` đã có và đã test. Lý do: gia hạn
là quyết định rủi ro cao hơn release lần đầu, vì vật tư đã quá hạn ghi trên bao bì.

**Khoá tự nhiên chuỗi — ba lớp, đặt ở SCHEMA không rải trong C#:**
1. `UseCollation("NOCASE")` trên `LotNo`, `PartNo`, `SupplierLotNo`.
   ❌ **Cấm** `EF.Functions.Collate(...)` trong query — thừa và phá cổng SQL Server.
2. `CHECK ("LotNo" = TRIM("LotNo") AND LENGTH("LotNo") > 0)` — NOCASE không lo khoảng trắng.
3. `.Trim()` ở service, có unit test. Lớp 3 là tiện lợi; lớp 1+2 là bảo đảm.

**Index:**
```sql
CREATE UNIQUE INDEX IX_MaterialLots_LotNo_RawMaterialId
  ON MaterialLots(LotNo, RawMaterialId) WHERE RawMaterialId IS NOT NULL;
CREATE UNIQUE INDEX IX_MaterialLots_LotNo_PartNo_Unresolved
  ON MaterialLots(LotNo, PartNo) WHERE RawMaterialId IS NULL;
CREATE INDEX IX_MaterialLots_Status_ExpiryAt      ON MaterialLots(Status, ExpiryAt);
CREATE INDEX IX_MaterialLots_RawMaterialId_Status ON MaterialLots(RawMaterialId, Status);
CREATE INDEX IX_MaterialLots_IqcInspectionId      ON MaterialLots(IqcInspectionId);
```
> **Phải có CẢ HAI partial unique index.** Trong SQLite `NULL ≠ NULL` trong unique
> index — chỉ có `UNIQUE(LotNo, RawMaterialId)` thì mọi lô chưa resolve lọt hết,
> unique vô hiệu đúng ở chỗ dễ trùng nhất.

### 3.2 `WoMaterialConsumptions` (append-only)

`Id · WoId(CASCADE) · LegId?(RESTRICT) · WoMaterialId(CASCADE) ·
MaterialLotId(RESTRICT) · QtyUsed · Uom(16)? · ScannedBy(80) · ScannedAt ·
ReversedAt? · ReversedBy(80)? · ReversedReason(500)? · BaseEntity`

`LegId` **bắt buộc nullable** — hôm nay `WoLegs` có **0 dòng**.

```sql
CREATE INDEX IX_WoMaterialConsumptions_WoId_LegId    ON WoMaterialConsumptions(WoId, LegId);
CREATE INDEX IX_WoMaterialConsumptions_MaterialLotId ON WoMaterialConsumptions(MaterialLotId);
CREATE INDEX IX_WoMaterialConsumptions_WoMaterialId  ON WoMaterialConsumptions(WoMaterialId);
-- KHÔNG có unique index chống quét lặp — xem §2, quyết định Đ4
```
`IX_..._MaterialLotId` phục vụ **genealogy ngược**: lô này đã vào những WO nào —
câu hỏi đầu tiên khi phải thu hồi.

**Đảo tiêu thụ = đánh dấu `ReversedAt/By/Reason`, TUYỆT ĐỐI KHÔNG `DELETE`,
KHÔNG `UPDATE QtyUsed`.**

### 3.3 `WoMaterials` — additive thuần

Thêm `MaterialLotId long?` (FK RESTRICT) + index. **KHÔNG đổi kiểu/nghĩa cột
`LotNo`** — `AlterColumn` trên SQLite rebuild bảng, sẽ **xoá mất 2 partial unique
index** `IX_WoMaterials_WorkOrderId_BomLineIdx` và `..._WoLegId_BomLineIdx`.
`LotNo` hạ cấp thành **mirror hiển thị**, server ghi từ `MaterialLot.LotNo`.
Siết thành read-only là **Phase 3, sau go-live**.

## 4. Migration A→B→C — `AddMaterialLotGenealogy`

**Phase A — baseline (số thật đo 2026-08-19):**
```
WoMaterials 82 · IqcInspections 3 · IqcResultDetails 7
RawMaterials 2127 · WorkOrders 27 · WoLegs 0 · SemiLots 6 · SemiAllocations 0
```
Backup `data/ccl_mes.db` + SHA256 + copy `MesDbContextModelSnapshot.cs` sang /tmp.

**Phase B — generate + verify trên DB CÔ LẬP `/tmp`, không chạm live.**
Đọc file migration TRƯỚC khi apply. **Strip type-affinity**: xoá mọi
`type: "TEXT|INTEGER|REAL|BLOB"` trong `table.Column<>(...)` và mọi
`.HasColumnType(...)`. Giữ `maxLength:`, `nullable:`, `defaultValue:`,
`Annotation("Sqlite:Autoincrement", true)`.

**Trigger RowVersion phải viết tay trong `Up()`** (mirror
`20260723081701_AddSemiStock.cs`) — `randomblob(8)` on INSERT và on UPDATE.

Round-trip trên **bản copy dữ liệu thật ở /tmp**, chạy backfill **hai lần** để
chứng minh idempotent.

**Phase C — chỉ apply live sau khi B xanh VÀ Henry cho phép** (STOP-gate riêng,
chưa mở).

**Rollback:**
- Chưa apply live → `rm` file migration + `git checkout` snapshot.
  ❌ **TUYỆT ĐỐI KHÔNG `dotnet ef migrations remove`** (sự cố 2026-05-31 DROP TABLE AuditLogs).
- Đã apply live → **restore file backup Phase A byte-identical**. Không dùng
  `Down()`: `DropColumn MaterialLotId` rebuild `WoMaterials` ⇒ mất 2 partial unique index.
- Rollback mềm → tắt `Mes:MaterialLot:EnforceReleased`, hai bảng mới nằm im,
  đường đọc cũ không đổi. **Đây là lý do feature flag bắt buộc.**

## 5. Điểm chặn khi quét

`POST /api/v2/work-orders/{id}/materials/{bomLineIdx}/consume`
body `{ lot_no, qty_used, leg_id? }`. Logic ở `MaterialLotScanService`
(Application layer) — **không** ở controller.

**Thứ tự kiểm cố định:**
`cú pháp → prelude(phase/If-Match/Idempotency) → tìm lô → part mismatch →
Rejected → Expired → status≠Released → đủ số lượng → ghi + optimistic lock`

> **part mismatch kiểm TRƯỚC status** — quét nhầm lô của vật tư khác là lỗi phổ
> biến nhất trên sàn. Kiểm status trước thì operator đi tìm QC, trong khi vấn đề
> thật là cầm nhầm cuộn.

| Ca | HTTP | Mã lỗi | Audit |
|---|---|---|---|
| Lô không tồn tại | 404 | `lot.not_found` | ✓ `MATERIAL_LOT_SCAN_DENIED` |
| Lô Quarantine | 422 | `lot.not_released` | ✓ |
| Lô Rejected | 422 | `lot.rejected` | ✓ |
| Lô Expired (`Status='Expired'` HOẶC `ExpiryAt < now`) | 422 | `lot.expired` | ✓ |
| Lô thuộc vật tư khác BOM | 422 | `lot.part_mismatch` | ✓ |
| Lô đã cạn | 422 | `lot.depleted` | ✓ |
| Cú pháp sai | 422 | `lot.invalid_request` | ✗ (chưa chạm nghiệp vụ) |
| RowVersion đổi giữa chừng | 409 | `lot.conflict` | ✓ tái dùng `WO_STATE_CONFLICT` |
| Happy path | 200 | — | ✓ `MATERIAL_LOT_CONSUME` |

**Mã lỗi dùng chuỗi `lot.*`, KHÔNG mở rộng `WoErrorCode`** — enum đó là của
state machine WO; quét vật tư không phải transition WO. Đúng tiền lệ `prepress.*`/`semi.*`.

**Mọi ca từ chối đều emit audit.** "Ai đã cố nạp lô chưa Released, lên WO nào,
lúc nào" là dữ liệu điều tra chất lượng, không phải noise. Tiền lệ đã có:
`WO_QA_APPROVE_DENIED`, `WO_OQC_APPROVE_DENIED`.

**AuditAction mới:** `MATERIAL_LOT_CONSUME · MATERIAL_LOT_REVERSE ·
MATERIAL_LOT_SCAN_DENIED · MATERIAL_LOT_STATUS_SET · MATERIAL_LOT_EXPIRY_EXTENDED`

**Grace period bắt buộc:** `Mes:MaterialLot:EnforceReleased`, **mặc định `false`**.
Khi tắt: vẫn resolve lô, vẫn ghi tiêu thụ, nhưng trả 200 + `warning` thay vì 422;
audit vẫn emit với `enforced:false`. ⇒ **đo được chính xác bao nhiêu ca sẽ bị
chặn trước khi chặn thật.** Ngày lật cờ là quyết định của Henry.

## 6. Nghiệm thu

**Truy vấn mạch lô — mọi JOIN qua khoá số, KHÔNG có `ON a.LotNo = b.LotNo` ở bất
kỳ đâu.** WO → WoMaterialConsumptions → MaterialLots → IqcInspections →
IqcResultDetails (đếm mục fail + `GROUP_CONCAT` mã lỗi).

**Test bắt buộc:**
- Happy path: ghi 1 dòng, trừ `QtyAvailable`, emit đúng 1 audit
- 6 ca chặn, mỗi ca khẳng định **đúng HTTP + đúng mã lỗi + đúng 1 audit row**
- `Part_mismatch_is_reported_before_status` (khoá thứ tự §5)
- `Expiry_in_past_blocks_even_when_status_is_Released`
- `Lot_lookup_is_case_insensitive` (Theory: lower/upper/mixed) + `..._trims_whitespace`
- **Đ4:** `Same_idempotency_key_twice_creates_one_row` **và**
  `Different_idempotency_keys_create_two_rows_both_visible_in_trace`
- **Đ3:** `Reversal_restores_lot_to_Released_when_qty_returns_above_zero` ·
  `Only_supervisor_can_reverse` · `Expired_lot_extension_requires_two_distinct_signers`
- Concurrency: N thread cùng lô → 1 winner + (N−1) × 409
- Backfill: `Backfill_is_idempotent_when_run_twice` (dùng dấu `backfill-a1`, §2) ·
  `Unresolved_lot_becomes_Quarantine` · `Resolved_lot_inherits_status_from_iqc_result`
- Schema: `MaterialLot_natural_key_columns_use_nocase` (đọc model snapshot)
- Bất biến bằng chứng: `Frozen_snapshot_unchanged_when_lot_status_later_changes` (L29)
- RBAC: Operator được consume; **chỉ QC/Supervisor/Admin** đổi trạng thái lô
- i18n: mọi key mới đủ `vi` + `en` trong `TranslationCatalog`

**Gate chống tái phát `gate-material-lot-fk.sh`:**
- **(A) HARD FAIL** — bất kỳ file nào chứa join theo chuỗi lô:
  `ON\s+\S*LotNo\s*=\s*\S*LotNo` hoặc `\.LotNo\s*==\s*\S+\.LotNo`
- **(B) RATCHET** — đếm nơi ghi `\.LotNo\s*=`. Baseline hiện tại = **1**
  (`PrepressController.cs:133`), hạ xuống **0** trong cùng PR
- **(C) HARD FAIL** — entity lô có mặt mà model snapshot thiếu `NOCASE` trên `LotNo`

Phải chứng minh chuỗi **PASS → FAIL (inject) → PASS**.

## 7. Nợ đã biết, tách PR riêng

**`SemiLots.LotNo` thiếu `COLLATE NOCASE`** — L28 đã tái phạm, phát hiện
2026-08-19. Hôm nay `LOT-001` và `lot-001` là hai lô khác nhau trong kho bán
thành phẩm. **PR riêng**, không gộp A1: `AlterColumn` rebuild bảng ⇒ xoá 2
trigger RowVersion của `SemiLots`, phải dựng lại trong cùng migration.

## 8. Bài học phải viết cùng PR (pha 6 LEARN)

> Khoá tự nhiên chuỗi giữa hai bảng = **không có khoá**. Truy xuất nguồn gốc phải
> là FK; chuỗi chỉ được tồn tại như nhãn hiển thị. Và mọi khoá tự nhiên chuỗi
> phải `NOCASE` + `TRIM` **ở schema** — L28 áp dụng lần hai, `SemiLots.LotNo` là
> bằng chứng đã bỏ sót một lần.
