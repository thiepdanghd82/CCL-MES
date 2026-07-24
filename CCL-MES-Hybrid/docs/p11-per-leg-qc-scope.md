# P11 — Per-leg QC materialization (Pre-press / Setting / IPQC scoped by WoLeg)

> **Status: APPROVED (Henry 2026-07-24) — đang triển khai.**
>
> ## ✅ Quyết định đã chốt (Henry)
> - **Q-A BOM split** → **Option A: full BOM mỗi leg** (mỗi leg snapshot đủ 6 dòng
>   BOM, chỉ khác `WoLegId`; đánh dấu `⚠ Ops-confirm`, nâng cấp Option B sau).
> - **Q-B Plate/Cutter** → **chỉ leg liên quan**: rule `LegKind→tool` —
>   **PRINT→Plate**, **CUT→Cutter**, **PRINT_CUT→cả hai**, **TAPE→Cutter** (cắt tape
>   dùng khuôn), **ASSEMBLY→không**. Rule này `⚠ Ops-confirm` (mirror precedence
>   RoutingLegMapSeed). Leg không có tool → không tạo row (không Pending rỗng).
> - **Q-C/Q-D Scope** → **ĐẦY ĐỦ chuỗi: Pre-press → Setting → IPQC → Running** per-leg
>   (không tách Setting/Running sang đợt sau). Trigger: **eager tại `/legs/materialize`**
>   (materialize Prepress+IPQC surface cho mọi leg lúc fork); Setting/Running state
>   scoped `WoLegId` khi leg chạy qua flow.
>
> ---
>
> ---
>
> ## 🛑 STOP-GATE (phát hiện khi implement 2026-07-24) — CẦN HENRY DUYỆT MIGRATION
>
> Ground-truth ban đầu chỉ verify **cột** `WoLegId` tồn tại — **BỎ SÓT** rằng 4
> bảng có **UNIQUE INDEX key theo `WorkOrderId`** chặn hoàn toàn per-leg (nhiều
> row cùng WO). Unit test parity đã lộ ra: `UNIQUE constraint failed:
> WoMaterials.WorkOrderId, WoMaterials.BomLineIdx`.
>
> | Bảng | UNIQUE index hiện tại | Vì sao chặn per-leg |
> |---|---|---|
> | `WoMaterials` | `(WorkOrderId, BomLineIdx)` | Option A full-BOM: 4 leg × BomLineIdx 0..5 cùng WorkOrderId → đụng |
> | `WoPlateChecks` | `(WorkOrderId)` | >1 leg cần plate/WO → đụng |
> | `WoCutterChecks` | `(WorkOrderId)` | T3 có CUT **và** TAPE (đều cần cutter theo Q-B) → 2 row/WO → đụng |
> | `WoIpqcChecks` | `(WorkOrderId)` | per-leg IPQC = 1 check/leg = tới 4/WO → đụng |
> | `WoIpqcCheckItems` | `(WoIpqcCheckId, ItemKey)` | ✅ OK — scoped theo check |
>
> ⇒ **Per-leg BẮT BUỘC đổi 4 unique index** để thêm `WoLegId`. Đây là **migration
> schema** → theo HARD CONSTRAINT tôi **DỪNG, chưa tạo migration, chưa áp live**.
>
> ### Đề xuất migration (chờ Henry duyệt — Phase A→B→C, forward-only)
> Giữ **1:1 cho WO 1-nhánh** (WoLegId NULL) + cho phép per-leg → dùng **2 partial
> index** mỗi bảng (SQLite hỗ trợ `CREATE UNIQUE INDEX … WHERE`):
> ```
> -- WoMaterials
> UNIQUE (WorkOrderId, BomLineIdx)            WHERE WoLegId IS NULL      -- 1-nhánh cũ giữ nguyên
> UNIQUE (WorkOrderId, WoLegId, BomLineIdx)   WHERE WoLegId IS NOT NULL  -- per-leg
> -- WoPlateChecks / WoCutterChecks / WoIpqcChecks
> UNIQUE (WorkOrderId)                        WHERE WoLegId IS NULL
> UNIQUE (WorkOrderId, WoLegId)               WHERE WoLegId IS NOT NULL
> ```
> - **Parity tuyệt đối**: index `WHERE WoLegId IS NULL` giữ y hệt ràng buộc cũ cho
>   81092000-style → LegacyParity không đổi.
> - EF Core: `.HasIndex(...).IsUnique().HasFilter("WoLegId IS NULL")` +
>   `.HasFilter("WoLegId IS NOT NULL")` (drop 4 index cũ, tạo 8 index mới).
> - Migration generate trên `/tmp` DB (§4.3), verify `.schema`, **KHÔNG áp live**
>   tới khi Henry chạy Phase C.
>
> **Trạng thái code**: domain `LegPrepressTools` + `PrepressBomSnapshotService.
> MaterializeForLegAsync` + unit test ĐÃ viết (đúng logic), nhưng **1 parity test
> đỏ vì index cũ** — chưa commit. Chờ Henry duyệt migration để: tạo migration →
> test xanh → tiếp IPQC/Setting/Running per-leg + wire.
>
> **✅ HENRY DUYỆT (2026-07-24)** — migration `AddPerLegPartialUniqueIndexes`
> đã tạo (partial-index, forward-only): drop 4 unique cũ → tạo 8 partial (4×
> `WHERE WoLegId IS NULL` giữ parity + 4× `WHERE WoLegId IS NOT NULL` per-leg).
> **Phase B done**: generate + apply trên `/tmp` (§4.3), verify `.schema`; suite
> Domain **1108** + Hybrid Api **495** + per-leg unit **23** = xanh; **live DB
> KHÔNG áp** (0 row PerLegPartial trong `__EFMigrationsHistory` của live).
> **Phase C (áp live) = Henry chạy khi sẵn sàng** (`dotnet ef database update`
> hoặc để boot Web migrate). Domain per-leg Prepress đã unblock + commit.
>
> ---
>
> **(Bản gốc SCOPE PROPOSAL bên dưới — giữ nguyên làm căn cứ.)**
> Mọi số liệu dưới đây verify trên **COPY** live DB (`/tmp/p11-perleg-inspect.db`,
> `cp` từ `data/ccl_mes.db`) — **live NEVER written**. Không đụng schema.

Bài toán: WO nhiều nhánh (fork-join P11) hiện **không có bộ kiểm tra per-leg** →
worker scan 1 leg mà không thấy Pre-press/Setting/IPQC riêng. Mục tiêu: MỖI
`WoLeg` tự materialize bộ check **giống hệt cấu trúc mã 1 nhánh** (chuẩn 81092000),
nhưng **scoped theo leg** (`WoLegId = leg.Id`).

---

## 0. Ground truth — đã kiểm chứng lại (không giả định)

### 0.1 Cột shadow `WoLegId` — TỒN TẠI trên cả 8 bảng ✅
`pragma_table_info` trên copy:

| Bảng | `WoLegId` | Index |
|---|---|---|
| WoMaterials · WoPlateChecks · WoCutterChecks · WoRunSessions · WoPauseEvents · WoQtyEntries · WoIpqcChecks · WoIpqcCheckItems | ✅ có | ✅ có |

⇒ **KHÔNG cần migration** (playbook §4 không kích hoạt). Chỉ POPULATE + đọc theo `WoLegId`.

### 0.2 Entry point materialize (cấp WO hiện tại)
- **Pre-press**: `PrepressBomSnapshotService.MaterializeAsync(woId)` — key `WorkOrderId`,
  existence check `AnyAsync(x => x.WorkOrderId == woId)`, insert `WoPlateCheck`(1:1) +
  `WoCutterCheck`(1:1) + `WoMaterial`(N dòng BOM từ `ManufacturingStructures`).
  Idempotent (chỉ insert khi thiếu; refresh cột BOM, giữ cột operator).
- **IPQC**: `IpqcLibraryMaterializer.Build(libraryRows, resolvedLines)` — **thuần**,
  nhận library rows **đã lọc** + `resolvedLines`. Caller (IPQC controller) lọc
  `CheckItemLibrary` theo `QcLineResolver.Classify(routing)` → materialize
  `WoIpqcCheck` + N `WoIpqcCheckItem` + freeze `ItemsProfileSnapshotJson`.
- **Setting**: `WoRunSessionService` / setting timer — hiện cấp WO (`WoRunSessions`
  key `WorkOrderId`, có cột `WoLegId` nullable).

---

## 1. GOLDEN STRUCTURE — mã 81092000 (WO id 31) + IPQC ref 81092002 (WO id 33)

81092000 (WO 31) đang ở **PREPRESS** nên **IPQC chưa materialize**; lấy IPQC golden
từ **81092002 (WO 33, cùng nhóm sản phẩm, IPQC_APPROVED, 57 item)**.

### 1.1 Pre-press (WO 31 — `WoLegId` toàn NULL)
| Surface | Rows | Chi tiết |
|---|---|---|
| `WoMaterials` | **6** | BomLineIdx 0..5 (30030193 m2 · 80641180 pcs · 30120109 kg · 20211969 kg · 30120109 kg · 30120101 kg). Status: 1 Ok + 5 Pending |
| `WoPlateChecks` | **1** | Status=Pending |
| `WoCutterChecks` | **1** | Status=Pending |

### 1.2 IPQC (WO 33 — 1 `WoIpqcCheck`, `WoLegId` NULL)
- `WoIpqcChecks` = **1 row**: 4 slot legacy (Material/PrintA/PrintB/PrintC) +
  Judgment=GoRun + **`ResolvedLines = "SILK,PRESS_CNC,FINISHING"`** + `ItemsProfileSnapshotJson`(~16.6 KB).
- `WoIpqcCheckItems` = **57 item**, phân bố theo process line:

| ProcessLine | Items | Groups |
|---|---|---|
| SILK | 25 | 4 |
| PRESS_CNC | 27 | 4 |
| FINISHING | 5 | 2 |
| **Σ** | **57** | 10 |

### 1.3 💡 Phát hiện then chốt (định hình toàn bộ thiết kế per-leg)
IPQC của **1 WO 1 nhánh** = **bó (bundle) items của TẤT CẢ process line** mà routing
resolve ra (ở đây 3 line = 57 item trong 1 check). Khi WO **fork thành nhiều leg**,
mỗi `WoLeg` chỉ mang **1 `ProcessLine`** (PRINT=SILK · CUT=PRESS_CNC · ASSEMBLY/TAPE=FINISHING).

⇒ **IPQC per-leg = PHÂN HOẠCH (partition) bó 1-nhánh theo `leg.ProcessLine`**:
```
WO 1-nhánh:  1× WoIpqcCheck  → 57 item  {SILK25 + PRESS_CNC27 + FINISHING5}
WO T3 4-leg: 4× WoIpqcCheck  → PRINT(SILK)=25 · CUT(PRESS_CNC)=27 ·
                                ASSEMBLY(FINISHING)=5 · TAPE(FINISHING)=5
             (∪ các leg = tập item của 1-nhánh cho các line hiện diện)
```
Đây **đúng Q6** ("IPQC riêng từng khu vực") và **không mơ hồ**: chỉ cần caller lọc
`CheckItemLibrary` theo `line == leg.ProcessLine` (thay vì multi-line resolve của WO)
rồi `IpqcLibraryMaterializer.Build(filtered, [leg.ProcessLine])`. **Materializer thuần
KHÔNG đổi** — chỉ tập input khác + stamp `WoLegId`.

---

## 2. Phạm vi materialize per-leg (đề xuất)

Khi `RoutingController /legs/materialize` fork (hoặc khi 1 leg vào PREPRESS), với **MỖI leg**:

| Surface | Quy tắc per-leg | `WoLegId` | Idempotent key |
|---|---|---|---|
| **Pre-press** | `WoPlateCheck`+`WoCutterCheck` (1:1 per leg) + `WoMaterial` (xem §3 ⚠) | `leg.Id` | `(WoLegId)` cho plate/cutter · `(WoLegId, BomLineIdx)` cho material |
| **IPQC** | lọc library `line == leg.ProcessLine` → `WoIpqcCheck`(1 per leg) + `WoIpqcCheckItem`(N của line đó), freeze snapshot | `leg.Id` | `(WoLegId)` cho check · `(WoLegId, ItemKey)` cho item |
| **Setting** | `WoRunSession`/timer scoped leg (mirror running-surface cấp WO) | `leg.Id` | `(WoLegId)` phiên đang mở |

**Rollup readiness THEO LEG**: `MaterialsReadinessRollup` / `IpqcReadinessRollup` tính
trên tập rows `WHERE WoLegId = leg.Id`. **Join/gate**: leg qua `IPQC_APPROVED` chỉ khi
IPQC của **chính leg** AllOk.

---

## 3. ⚠ OPS-CONFIRM — Quy tắc chia BOM (`WoMaterial`) theo leg

**Chưa có quy tắc material→leg.** BOM 81092000 có 6 dòng (băng keo 3M9471LE, PC10,
mực VIC trắng/đen, Pantone…) nhưng **không có cột nào map material→công đoạn/leg**.
Câu hỏi: leg PRINT lấy mực+substrate; leg TAPE lấy băng keo; leg CUT/ASSEMBLY lấy gì?

**Đề xuất (chờ Ops chốt):**

- **Option A — FULL BOM per leg (fallback mặc định, làm ngay):** mỗi leg snapshot
  **toàn bộ** 6 dòng BOM (giống hệt mã 1 nhánh, chỉ khác `WoLegId`). An toàn, parity,
  worker mọi leg thấy đủ vật tư. Nhược: trùng lặp, không phản ánh "leg này chỉ dùng vật tư X".
  → **Khuyến nghị dùng trước** để unblock, đánh dấu `⚠ Ops confirm` trong code.
- **Option B — chia theo `MaterialCode`→ProcessLine map (khi Ops cấp bảng):** thêm
  master-data `MaterialLineMap` (mirror `ProcessLineMap`) → mỗi dòng BOM gán về leg
  có `ProcessLine` khớp; dòng "chung" (vd substrate) nhân bản hoặc gán leg đầu.
  → cần Ops cung cấp mapping thật; **KHÔNG tự đoán**.

**Cần Henry/Ops trả lời trước khi ship Prepress per-leg:**
1. Dùng Option A (full BOM per leg) tạm thời? (khuyến nghị: **có**)
2. Có sẵn/định nghĩa được map `MaterialCode`/BOM-line → công đoạn không? (→ Option B)
3. Plate/Cutter: mỗi leg 1 cặp, hay chỉ leg CUT/PRINT có? (đề xuất: **mỗi leg 1 cặp** —
   parity với 1-nhánh, worker leg nào cũng có surface để thao tác; leg không dùng thì để Pending/skip).

---

## 4. Parity — WO 1-nhánh KHÔNG đổi

- Materialize per-leg CHỈ chạy cho WO có leg (`MesPhase=SPLIT`, ≥2 leg). WO 1-nhánh
  (81092000-style) vẫn đi `PrepressBomSnapshotService.MaterializeAsync(woId)` cũ →
  rows `WoLegId = NULL`, **số row + hành vi y hệt** (6 material + 1 plate + 1 cutter;
  IPQC bundle multi-line).
- Đọc per-leg CHỈ kích hoạt khi thao tác trong luồng 1 leg (endpoint có `legId`).
  Endpoint cấp WO cũ đọc `WoLegId IS NULL` → không trộn.
- LegacyParity test hiện có phải xanh (không đụng path `WoLegId=NULL`).

---

## 5. HARD constraints (từ brief)
- KHÔNG thêm migration (WoLegId đã có). Nếu phát sinh cần cột → DỪNG, báo Henry (Phase A→C).
- Additive + idempotent (upsert theo `(WoLegId, key)`), atomic (single SaveChanges +
  `WoLeg.RowVersion` If-Match per-leg concurrency, mirror 7c-2).
- Audit tái dùng `WO_PREPRESS_*` / `WO_IPQC_*` + đính `WoLegId` vào Detail JSON.
- KHÔNG đổi wording `*ErrorLocaliser`. UI (nếu chạm): Rule 4, L37 token, i18n T() parity, giữ data-testid.

---

## 6. Đề xuất commit stack (branch `feat/p11-routing-domain` hoặc nhánh mới) — SAU khi duyệt
1. **domain/app**: `PrepressBomSnapshotService.MaterializeForLegAsync(legId)` +
   IPQC per-leg filter helper (lọc `line==leg.ProcessLine`, stamp `WoLegId`) + rollup
   scoped leg. Unit test golden-structure so 81092000 + idempotent + IPQC-by-line.
2. **wire**: `RoutingController /legs/materialize` gọi materialize per-leg cho từng leg;
   Prepress/IPQC controller nhận `legId` optional → đọc/ghi `WoLegId`. Integration:
   seed T3 4-leg → mỗi leg có bộ check theo line + rollup + advance gated. LegacyParity xanh.
3. **UI (nếu cần)**: Prepress/IPQC dashboard nhận `legId` để render bộ check của leg.

---

## 7. Test plan (chạy trên COPY — live NEVER written)
- **Unit**: per-leg materializer sinh đúng golden (WoMaterial full-BOM×leg [Option A],
  WoIpqcCheckItem = items của `leg.ProcessLine`); idempotent (2 lần = 1 bộ); IPQC dùng
  `leg.ProcessLine` đúng khu vực (PRINT→SILK 25 item, CUT→PRESS_CNC 27, ASSEMBLY→FINISHING 5).
- **Integration/wire**: seed WO T3 (PRINT SILK / TAPE FINISHING / ASSEMBLY FINISHING /
  CUT PRESS_CNC) → mỗi leg có surface riêng; rollup per-leg; advance gated bởi IPQC của leg.
- **LegacyParity**: 81092000-style 1-leg — `WoLegId=NULL`, cùng số row, không đổi.
- `dotnet build` 0 error · full suite (Domain/Api/Client/Razor) 0 fail.
- **Live e2e (copy)**: scan WO nhiều nhánh → từng leg hiện bộ Prepress/IPQC riêng.

---

## 8. QUYẾT ĐỊNH CẦN HENRY (chốt trước khi code)
- **Q-A** BOM split: Option A (full BOM per leg, khuyến nghị) hay chờ map Option B? → §3
- **Q-B** Plate/Cutter: mỗi leg 1 cặp (khuyến nghị) hay chỉ leg liên quan? → §3.3
- **Q-C** Trigger materialize per-leg: tại `/legs/materialize` (fork) — làm luôn cho mọi leg —
  hay lazy khi từng leg vào PREPRESS? (đề xuất: **eager tại fork**, mirror 1-nhánh materialize-on-create)
- **Q-D** Setting per-leg: cần ngay đợt này hay tách sau (Prepress+IPQC trước)? (đề xuất: Prepress+IPQC trước, Setting theo sau)
