# Runbook — sửa `WorkOrders.CurrentStep = 'Done'` (2026-08-19)

> Ghi vào **DB sản xuất**. Henry duyệt trước khi chạy. Tài liệu này là hồ sơ, không phải kế hoạch.

## Vấn đề

11/27 WO (41%) mang `CurrentStep='Done'` — giá trị **không tồn tại** trong
`ProcessStepCode` (`src/CCL.MES.Domain/Enums.cs`, 8 thành viên:
`PrePressCheck · OpSetting · IpqcApproval · ReadyToRun · Running · Fqc · Oqc · Closed`).

`MesDbContext.cs:89` cấu hình `HasConversion<string>()`, sinh
`EnumToStringConverter<ProcessStepCode>`. Chiều **đọc** ném
`InvalidOperationException` trong shaper của EF khi chuỗi không map được — nên
**mọi truy vấn materialise entity `WorkOrder` đều chết**, không phụ thuộc controller.

Endpoint chiếu sang DTO (`.Select(...)`) sống sót vì EF không sinh SQL đọc cột đó.

### Phạm vi (chứng minh bằng phương pháp vi sai: cùng binary, hai DB)

**10 route API hỏng.** Nặng nhất là route **danh sách**
`/api/v2/work-orders/shop-orders` — một dòng độc giết cả truy vấn, mất **toàn bộ
27 WO** cho mọi người dùng, không riêng 11.

Màn hình chính app Blazor legacy cũng chết: `Dashboard.razor:111`,
`WorkOrders.razor:393`.

### Đã được biết từ 2026-07-20 và đi vòng thay vì sửa

```
TraceFreezeService.cs:44  // whose legacy CurrentStep enum has bad rows ('Done') that throw on load.
TraceIndexService.cs:51   // entity (its legacy CurrentStep enum column has bad rows like 'Done'
                             commit fa5355a  2026-07-20
```
Hai chỗ **mới** né dữ liệu hỏng; 10 đường **cũ** để nguyên.

## Nguồn gốc

Chứng minh được:
- `Done` **chưa bao giờ** là thành viên enum (`git log -S"Done" -- Enums.cs` rỗng).
- Không script nào trong lịch sử git từng ghi giá trị đó.
- **0 audit** cho 11 WO, trong khi cùng cửa sổ thời gian có 926 bản ghi audit khác
  ⇒ ghi vào **ngoài đường có audit của app**, tức SQL trực tiếp.
- Dấu vân tay lô riêng: id liên tục 19–29, `WoNo` liên tục `WO-26-7201`…`7211`,
  `CreatedBy`/`UpdatedBy` NULL, `UpdatedAt` rơi vào giờ tròn.
- Từ vựng đến từ `docs/PHASE8-WORKORDER-PARITY-PLAN.md:114`, tài liệu này mô tả
  **sai cả hai** enum. Đã cắm cảnh báo tại chỗ ngày 2026-08-19.

**Không** chứng minh được ai chạy và lúc nào — backup cũ nhất còn giữ (2026-07-20)
đã có sẵn 11 dòng. Không đặt tên người.

## Là dữ liệu demo, không phải sản xuất

Cả 11 đều terminal (`Status=Finished`, `MesPhase` = SHIPPED ×9 hoặc CANCELLED ×2)
và khai đã làm ra 1.180–19.377 sản phẩm, **nhưng 0 dòng con ở mọi bảng nghiệp vụ**:

| Bảng | 11 WO này | WO khác |
|---|---|---|
| `WoMaterials` | 0 | 82 |
| `WoPlateChecks` | 0 | 17 |
| `WoCutterChecks` | 0 | 17 |
| `WoIpqcChecks` | 0 | 7 |
| `WoQcChecks` | 0 | 8 |

Không thể shipped 19.377 cái mà không nạp một cuộn vật tư nào.

## Phương án đã chọn — PA-B (22/25 điểm)

Sửa 11 dòng về `Closed`. **Không chế giá trị mới** — `Closed` là thứ
`WorkOrderStateMachine.ProjectToLegacy` đã quy định:

```csharp
MesPhase.DONE => ProcessStepCode.Closed,
MesPhase.CANCELLED => ProcessStepCode.Closed,
MesPhase.SHIPPED => ProcessStepCode.Closed,
```

Loại bỏ:
- **Thêm `Done=9` vào enum** (13/25) — hợp thức hoá giá trị rác; thành viên thứ 9
  mà state machine không bao giờ sinh ra, trùng nghĩa `Closed`, và badge sẽ hiện
  "NEW" xám cho 11 WO đã shipped. Contract impact = 1 ⇒ STOP-gate.
- **Xoá 11 dòng** (16/25) — sạch nhưng làm rỗng tab "closed" của shop-orders và khó đảo hơn.
- **Converter khoan dung** (11/25) — biến hỏng-ồn-ào thành hỏng-im-lặng trên cả 37
  cột enum-string. Đi sai hướng.

## Đã chạy

```bash
# backup
sqlite3 data/ccl_mes.db ".backup 'data/Backup/SQLite/ccl_mes.before-currentstep-repair-20260819-115750.db'"

# sửa
BEGIN;
UPDATE WorkOrders
   SET CurrentStep='Closed', UpdatedAt=datetime('now'),
       UpdatedBy='data-repair-currentstep-2026-08-19'
 WHERE CurrentStep='Done' AND MesPhase IN ('SHIPPED','CANCELLED');
-- changes() = 11
COMMIT;
```

Điều kiện `AND MesPhase IN ('SHIPPED','CANCELLED')` là cố ý: nếu có dòng `Done`
nào **không** ở trạng thái kết thúc thì câu lệnh bỏ qua nó và `changes()` sẽ khác
11 — chuông báo, không phải sửa mù.

| | trước | sau |
|---|---|---|
| SHA256 live | `cc82e24745d301fb644419bf3244cc22597450228d53dffe9e296c0255b959a9` | `d485daa94dc7373270cd65a5c55285002d2232deface2c8934c2f36ca25631e2` |
| SHA256 backup | — | `2fc8d42d4477f3b90dfd82a6ac809c8da8cda6cfb966d501bf15f8f78381f62d` |
| `WorkOrders` | 27 | 27 |
| `CurrentStep='Done'` | 11 | **0** |
| `CurrentStep='Closed'` | 0 | **11** |
| `AuditLogs` | 2379 | 2379 |
| `integrity_check` | — | `ok` |

Phân bố sau khi sửa, **mọi giá trị đều là thành viên hợp lệ**:
`Closed 11 · PrePressCheck 5 · OpSetting 3 · Fqc 3 · IpqcApproval 2 · Running 1 · ReadyToRun 1 · Oqc 1`

## Đường lui

```bash
# lùi chính xác (nghịch đảo đã biết)
sqlite3 data/ccl_mes.db "UPDATE WorkOrders SET CurrentStep='Done' WHERE Id BETWEEN 19 AND 29;"

# hoặc restore nguyên trạng byte-identical
cp data/Backup/SQLite/ccl_mes.before-currentstep-repair-20260819-115750.db data/ccl_mes.db
shasum -a 256 data/ccl_mes.db   # phải ra cc82e247…b959a9
```

## Không làm, và vì sao

**Không ghi dòng `AuditLogs`.** Dự án chưa có từ vựng audit cho việc sửa dữ liệu
tay (`SELECT DISTINCT Action WHERE Action LIKE '%REPAIR%'` → rỗng). Chế một chuỗi
mới bằng SQL trực tiếp **chính là bug class vừa sửa**. Hồ sơ nằm ở tài liệu này +
tên file backup + cột `UpdatedBy='data-repair-currentstep-2026-08-19'` trên đúng
11 dòng.

**Nợ để lại:** cần thêm hằng `AuditAction.AdminDataRepair` **trong mã** rồi mới
dùng, không phải ngược lại.

## Còn treo

- **Lỗi riêng, KHÔNG do `Done`:** `GET /api/v2/work-orders` và
  `/api/v2/work-orders/{id}` trả 500 cho **cả 27 WO** vì
  `JsonException: A possible object cycle was detected` — serialize thẳng entity
  thay vì DTO. Cần ticket riêng.
- **3 vi phạm FK có sẵn** `WoQcCheckItems → WoQcChecks` (có trong backup 2026-07-20,
  không do lần sửa này).
- **Gate chống tái phát chưa cài.** Thiết kế `gate-enum-integrity` đã có và đã
  chứng minh PASS → FAIL → PASS: đọc EF model bằng reflection (không hard-code
  danh sách enum), quét 37 cột enum-string, mô phỏng đúng ngữ nghĩa converter —
  chấp nhận `'closed'`/`'CLOSED'`/`'8'`, nhưng bắt cả hạng **im lặng** (`''`, `'0'`
  cho ra giá trị không định nghĩa mà **không** ném).

  Ba tầng, tầng 3 là bắt buộc:

  | Tầng | Đỏ khi nào |
  |---|---|
  | 1. CI test | regression trong logic đọc |
  | 2. `gate-all.sh` trên DB fixture | PR đưa giá trị rác vào DB mẫu |
  | 3. **Preflight lúc boot + `/health/ready`** | **live DB nhiễm** |

  Defect này lọt **không phải** vì thiếu test code, mà vì **không ai kiểm tính
  toàn vẹn dữ liệu live**. Chỉ tầng 3 bắt được nó.

---

## Nghiệm thu — chạy thật sau khi sửa

Probe API trên cổng riêng, đọc snapshot `VACUUM INTO` từ connection `mode=ro`.
Instance sản xuất (PID 33352, cổng 5100) không bị đụng. SHA256 live đầu = cuối
phiên verify: `d485daa9…5631e2`.

**10/10 route từng hỏng đã về 200, 0 hồi quy:**

```
/api/v2/work-orders/shop-orders                500 -> 200   ← route DANH SÁCH
/api/v2/work-orders/19/ipqc                    500 -> 200
/api/v2/work-orders/19/prepress                500 -> 200
/api/v2/work-orders/19/running-surface         500 -> 200
/api/v2/work-orders/19/legs                    500 -> 200
/api/v2/work-orders/19/qc/fqc                  500 -> 200
/api/v2/work-orders/19/qc/oqc                  500 -> 200
/api/v2/work-orders/19/summary-report          500 -> 200
/api/v2/work-orders/by-no/WO-26-7201           500 -> 200
/api/v2/work-orders/by-no/WO-26-7201/summary   500 -> 200

FIXED=10   VẪN-500-DO-'Done'=0   HỒI-QUY=0
```

**Màn hình danh sách sống lại đủ tải** — trước đó trống trơn cho mọi người dùng:
```
/work-orders/shop-orders → active = 16 WO · closed = 11 WO · TỔNG = 27
```

**Vòng 27 WO: 27/27 = HTTP 200.** Tương quan 1:1 ở phiên RCA (11 hỏng / 16 xanh)
giờ thành 27 xanh tuyệt đối.

**5 đường service Blazor Web: OK cả 5.** `GetAllAsync()` trả 27 dòng;
`Dashboard.razor:111` và `WorkOrders.razor:393` hết THROW.

**`gate-enum-integrity`** — lần này không dùng vi phạm tiêm tay:
```
snapshot live ĐÃ SỬA :  columns scanned = 37 · PASS — no out-of-enum values   exit=0
backup TRƯỚC khi sửa :  VIOLATION WorkOrders.CurrentStep = 'Done' x11 · FAIL   exit=1
```
FAIL trên dữ liệu sản xuất thật trước khi sửa, PASS trên chính dữ liệu đó sau khi
sửa. Đồng thời xác nhận backup còn nguyên trạng tiền-sửa ⇒ đường lùi vẫn hợp lệ.

**Hai route còn 500 — chứng minh là defect riêng.** Loại ngoại lệ khác hẳn
(`JsonException` object-cycle, không phải `InvalidOperationException`), và quét
toàn bộ 24 route: **0 route** còn chuỗi `ProcessStepCode` trong thân lỗi.
Đáng chú ý: `/work-orders/19` trước chết ở **materialise**, giờ đi qua được
materialise và chết ở **serialize** — cùng chỗ WO 37 (dữ liệu vẫn luôn sạch) đã
chết từ đầu. Nguyên nhân: `WorkOrdersController.cs:118-127` trả thẳng entity có
navigation vòng.
