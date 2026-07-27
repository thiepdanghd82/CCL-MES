# P11 — Per-leg BẮT BUỘC check-flow (Pre-press → OP Setting → IPQC → Running)

> **Status: APPROVED (Henry 2026-07-25) — đang triển khai.**
> ## ✅ Quyết định
> - **Q1 Setting store** → **Reuse `WoRunSession`** (WoLegId + StartedAt/EndedAt), **KHÔNG migration**.
> - **Q2 Gate** → **CHẶN (blocking)** + cập nhật ~8–12 routing test churn. Gate chỉ áp
>   khi leg CÓ surface materialized (multi-leg) → WO 1-leg parity nguyên.
> - **Q3 Drill-in** → **Nhúng inline** trong thẻ leg (tái dùng 3 dashboard + `[Parameter] WoLegId`).
>
> ---
>
> **(SCOPE PROPOSAL gốc bên dưới.)**
> Verify trên COPY (`/tmp/p11-checkflow-inspect.db`) — live NEVER written.
> Nối tiếp [`p11-per-leg-qc-scope.md`](p11-per-leg-qc-scope.md) (đã APPROVED).

Mục tiêu: MỖI `WoLeg` đi đủ **Pre-press → Setting → IPQC → Running** với dữ liệu
kiểm tra RIÊNG theo `leg.ProcessLine`, tái dùng 3 màn RICH của mã 1 nhánh
(PrepressDashboard / SettingDashboard / IpqcDashboard) — KHÔNG fork bản sao.

---

## 0. ĐÃ XONG (5 commit trên `feat/p11-per-leg-qc`)
| Phần | Trạng thái |
|---|---|
| Migration partial-index (4 bảng) | ✅ /tmp + demo copy; **live chưa áp** (Phase C) |
| **① Pre-press per-leg** materialize (full BOM + plate/cutter theo LegKind) | ✅ `PrepressBomSnapshotService.MaterializeForLegAsync` + test |
| **① IPQC per-leg** materialize (theo `leg.ProcessLine`) | ✅ `IpqcLegMaterializer` + test |
| Wire fork `/legs/materialize` → materialize per-leg mọi leg | ✅ + T3 e2e test |
| UI readiness chips (IPQC x/y, Vật tư x/y) trên LegsDashboard | ✅ `GET /legs` + bUnit |

⇒ **CÒN LẠI**: ① Setting per-leg · ② **GATE per-leg** · ③ wire read/write theo
`legId` · ④ **UI drill-in** (nhúng 3 màn rich scoped leg).

---

## 1. GOLDEN STRUCTURE — 81091753 (WO 30, product 80640004, ảnh Henry)
PREPRESS, `WoLegId` NULL: **5 WoMaterial + 1 WoPlateCheck + 1 WoCutterCheck**;
IPQC lazy (chưa tạo tới IPQC_WAIT). Setting: cột **WO-level** `SettingStartAt /
SettingEndAt / SettingDurationSec` + checklist 6 mục (client-side, chốt bằng
`/setting/done`). Đây là "golden" mỗi leg PHẢI tái tạo (scoped WoLegId).

---

## 2. ⚠ QUYẾT ĐỊNH #1 — Setting per-leg CHƯA có chỗ lưu
`WoLeg` **KHÔNG có** cột setting timer; timer là **WO-level** (`SettingStartAt/
EndAt/DurationSec` trên `WorkOrders`). ⇒ Setting per-leg cần 1 trong 2:

- **A — Tái dùng `WoRunSession` làm "setting session" per leg** (KHÔNG migration):
  `WoRunSession` đã có `WoLegId` + `StartedAt/EndedAt`. 1 session `StartedAt` =
  bắt đầu setup leg, `EndedAt` = finish setting → duration suy ra. Không cột mới.
  ⚠ hơi lệch ngữ nghĩa (RunSession vốn cho RUNNING) nhưng đủ dùng + 0 schema.
- **B — Thêm cột `SettingStartAt/EndAt/DurationSec` vào `WoLeg`** (migration):
  sạch ngữ nghĩa nhưng **đụng schema** → STOP-gate (Phase A→C, Henry duyệt).

**Đề xuất: A** (không migration, đúng HARD CONSTRAINT). Checklist 6 mục vốn
client-side → per-leg chỉ cần scope timer + chốt. **❓ Henry chọn A/B?**

---

## 3. ② GATE MATRIX per-leg (gắn điều kiện DỮ LIỆU theo leg vào LegFlow đã có)

| Transition (LegFlow) | Điều kiện MỚI per-leg | Nguồn rollup |
|---|---|---|
| PREPRESS → SETTING | materials-ready + plate OK + cutter OK **của leg** | `MaterialsReadinessRollup` scoped WoLegId |
| SETTING → IPQC_WAIT | setting đã "done" **của leg** (timer chốt) | Setting session/timer per leg (§2) |
| IPQC_WAIT → IPQC_APPROVED | IPQC items **của leg** AllOk (rollup scoped) | `IpqcReadinessRollup.Compute` scoped WoLegId |
| (giữ nguyên) → RUNNING | HARD/SOFT hội tụ (ASSEMBLY chờ PRINT+TAPE) **đã có** | `RoutingLegGate` |
| terminal → LEG_DONE → JOIN → FQC | **đã có** (L21) | — |

### ⚠ QUYẾT ĐỊNH #2 — Gate strictness + test churn
Gate chỉ áp **khi leg CÓ bộ surface materialized** (multi-leg WO). WO 1-leg
(WoLegId NULL, 81091753-style) KHÔNG có gate mới → **parity tuyệt đối**.

NHƯNG multi-leg WO nay materialize per-leg tại fork → **các test routing hiện có**
(RoutingControllerTests advance T2/T3 tự do qua PREPRESS→...→RUNNING) **sẽ bị gate
chặn** → phải cập nhật (seed per-leg data OK trước khi advance). Ước tính ~8–12
fixture cần sửa. **❓ Henry chấp nhận churn test này** (đúng bản chất "bắt buộc qua
kiểm tra")? Hay muốn gate **advisory** (trả cờ, không chặn) ở đợt này?

---

## 4. ③ WIRE — endpoint nhận `legId` (thêm optional, KHÔNG đổi contract mã 1 nhánh)
- Prepress: `GET/PUT /work-orders/{id}/prepress...` + `?legId=` optional → đọc/ghi
  theo `WoLegId`; **path WO-level thêm filter `WoLegId IS NULL`** (correctness:
  không nhặt row per-leg). Atomic + `WoLeg.RowVersion` If-Match cho per-leg.
- Setting: `/setting/enter|done` + `?legId=` → session/timer per leg (§2).
- IPQC: `/ipqc + items + judgment` + `?legId=` → check/items/rollup per leg;
  judgment của leg → advance leg (không đổi WO-level).
- Audit: đính `WoLegId` vào Detail JSON (tái dùng `WO_PREPRESS_*/WO_IPQC_*`).

## 5. ④ UI DRILL-IN — LegsDashboard nhúng 3 màn rich scoped leg
- Thêm `[Parameter] long? WoLegId` vào PrepressDashboard/SettingDashboard/
  IpqcDashboard (+ WoMaterialsList/WoPlateCheck/WoCutterCheck) → client method
  truyền legId → endpoint §4. Mặc định `null` = luồng WO cũ (KHÔNG đổi mã 1 nhánh).
- LegsDashboard: thẻ leg mở panel drill-in render đúng màn theo `LegPhase`
  (PREPRESS→Prepress, SETTING→Setting, IPQC_WAIT→Ipqc). Xong bước → nút advance
  (Start Setting / Finish Setting → IPQC / judgment) theo LegFlow. Giữ readiness
  chips đã có làm tóm tắt.

### ⚠ QUYẾT ĐỊNH #3 — cách drill-in
- **A — Nhúng inline** trong thẻ leg (expand panel) — tái dùng component trực tiếp,
  1 màn LegsDashboard. (đề xuất)
- **B — Route riêng** `/legs/{legId}/prepress...` — điều hướng rời, nặng hơn.

---

## 6. PARITY (bất biến)
WO 1-leg (81091753/80640004): materialize WO-level (WoLegId NULL), 3 màn cũ,
gate cũ, **cùng số row + màn** → LegacyParity + toàn bộ test cũ xanh. Chỉ
multi-leg (SPLIT) đi luồng per-leg.

## 7. COMMIT STACK (sau duyệt) — trên `feat/p11-per-leg-qc`
1. **domain**: Setting per-leg materializer (§2) + rollup scoped leg + gate predicates.
2. **wire**: 3 controller nhận `legId` + WO-level NULL-filter + advance-gate; cập nhật test churn (§3).
3. **UI**: `WoLegId?` param cho 3 dashboard + drill-in LegsDashboard + i18n + bUnit.

## 8. TEST (COPY — live NEVER written)
- Unit: Setting per-leg materializer golden + idempotent (Prepress/IPQC đã có).
- Integration: WO T3 → mỗi leg qua Prepress→Setting→IPQC gated; chỉ RUNNING khi
  IPQC leg AllOk; join→FQC. Gate chặn advance khi bước chưa xong.
- bUnit: drill-in render đúng 3 màn theo LegPhase; advance disabled tới khi xong.
- LegacyParity: WO 1-leg không đổi. Full suite 0 fail.

---

## 9. ❓ CẦN HENRY (chốt trước khi code)
- **Q1 Setting storage** → A (reuse WoRunSession, no migration) hay B (WoLeg columns, migration)?
- **Q2 Gate + test churn** → gate CHẶN (bắt buộc, churn ~8–12 test) hay advisory (đợt này)?
- **Q3 Drill-in UI** → A (nhúng inline) hay B (route riêng)?
