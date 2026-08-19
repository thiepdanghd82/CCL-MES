# PHASE 8 SHOP ORDER PARITY PLAN — port SpecHub Shop Order UI

> Anh chỉ thị: **nguồn = SpecHub Shop Order** (bỏ qua CMES sibling). Em đã
> survey lại + tìm đúng — SpecHub Shop Order = kiosk scan-product UI 3-
> section card-based (KHÁC HẲN table parity của Spec Library). Plan dưới
> đây map đầy đủ. STOP sau plan chờ anh chốt Q.

---

## Bước 0 — XÁC MINH NGUỒN (cập nhật sau correction)

**Bài học**: Em search "WorkOrder/work-order" ban đầu → KHÔNG tìm thấy
list view trong `spechub-prototype.html`. Anh chỉ thị đúng key: SpecHub
gọi là **"Shop Order"** (terminology riêng — gần workflow shop-floor).
Re-survey với keyword đúng → tìm thấy đầy đủ.

### Nguồn SpecHub Shop Order — confirmed surface

| Component | Location | Purpose |
|---|---|---|
| Sidebar nav | `spechub-prototype.html:7263` `data-i18n="nav.shop_order"` | "Shop order" entry trong PRODUCT OPERATION group |
| Empty-state render | `renderMesEmptyState()` HTML:18193-18290 | 3-section card-based main view |
| Status config | `WO_STATES` HTML:15042-15052 | 9 status (NEW/PREPRESS/SETTING/IPQC_WAIT/IPQC_APPROVED/QA_PENDING/RUNNING/PAUSED/DONE) với color + icon |
| Master data shape | `MES_WO_MASTER` HTML:15081+ | wo_code / qr_code / customer / product_code / product_name / machine_id / machine_desc / process / qty_plan / design_speed / bom_materials[] |
| Runtime state shape | `MES_WO_STATE` (separate localStorage) | status + run.qty_done + run.qty_ng |
| Shop Order History | `renderShopOrderHistory()` HTML:18870 | forensic record của closed/done WOs (separate view) |
| Detail drawer | `shop-order-detail-drawer` HTML:7379 | per-WO state machine flow view |

### UI layout (mirror anh screenshot)

```
┌─ Scan WO QR / type code [____________] · or [____________] ┬─ LOOKUP ┬─ + NEW SPEC ─┐
│                                                            │         │              │
│  📱  Scan Work Order QR code để bắt đầu                                                │
│      Demo: WO-26-2852, WO-26-3683, WO-26-5992                                          │
│                                                                                        │
│  ⚡ ACTIVE WORK ORDERS (N)                                                              │
│  ┌─ Card grid (auto-fit, gap 12px) ─────────────────────────────────────────────────┐ │
│  │  ┌─ WO-26-3683 ──── [① PRE-PRESS] ┐  ┌─ WO-26-2852 ──── [① PRE-PRESS] ┐         │ │
│  │  │ Customer   Brady Asia             │  │ Customer   Panasonic VN          │     │ │
│  │  │ Product    BRD-7656-D             │  │ Product    PAN-4548-F            │     │ │
│  │  │ Machine    ACNC3                  │  │ Machine    FBL01                 │     │ │
│  │  │ Process    Silkscreen + Diecut    │  │ Process    Flexo Print           │     │ │
│  │  │ Progress   0 / 12,000 pcs         │  │ Progress   0 / 9,000 pcs         │     │ │
│  │  └───────────────────────────────────┘  └───────────────────────────────────┘    │ │
│  └────────────────────────────────────────────────────────────────────────────────────┘ │
│                                                                                        │
│  📋 DEMO WORK ORDERS — click to open                                                   │
│  ┌─ 3 cards: WO-26-2852 (Continue) / WO-26-3683 (Continue) / WO-26-5992 (NEW) ──────┐ │
│  │  Body 5 rows: Customer / Product (code · name) / Machine (id · desc) /            │ │
│  │  Process / Target (qty · materials count)                                          │ │
│  └────────────────────────────────────────────────────────────────────────────────────┘ │
│                                                                                        │
│  ✔ CLOSED WORK ORDERS (N) — chỉ render khi có WO terminal                              │
│  Body 3 rows: Customer / Output (qty_done / qty_plan) / Reject (qty_ng · %)            │
└───────────────────────────────────────────────────────────────────────────────────────┘
```

**Khác biệt so với Spec Library**:
- KHÔNG phải table 14 cột; KHÔNG dùng rt-* sticky thead pattern
- Card-based grid (kiosk shop-floor focused)
- Scan QR input đầu trang (operator dùng máy quét)
- 3 section riêng (active/demo/closed) — KHÔNG cần Columns toggle

---

## 1. Map SpecHub Shop Order → CCL-MES WorkOrder entity

### 1.1 Card body fields mapping

**Active WO card** (body 5 rows mirror SpecHub HTML:18225-18231):

| SpecHub label | SpecHub source | CCL-MES WorkOrder field | Coupling? |
|---|---|---|---|
| Customer | `m.customer` (string) | `WorkOrder.Customer.Name` | navigation `Customer?` |
| Product | `m.product_code` (string) | `WorkOrder.Product.ProductCode` | navigation `Product?` |
| Machine | `m.machine_id` (string) | `WorkOrder.MachineCode` (string snapshot) | KHÔNG có Machine entity FK |
| Process | `m.process` (string e.g. "Silkscreen + Diecut") | **GAP** — CCL-MES không có `Process` field hiện | xem Q3 |
| Progress | `wo.run.qty_done / m.qty_plan pcs` | `WorkOrder.ProducedQty / WorkOrder.TargetQty` | ✓ |
| Status pill | `WO_STATES[wo.status]` color + icon + label | `WorkOrder.Status` (WoStatus enum) + `CurrentStep` (ProcessStepCode) | xem Q2 |

**Demo WO card** (body 5 rows mirror HTML:18250-18256):

| SpecHub label | SpecHub source | CCL-MES |
|---|---|---|
| Customer | `m.customer` | `Customer.Name` |
| Product | `m.product_code · m.product_name` | `Product.ProductCode · Product.Name` |
| Machine | `m.machine_id · m.machine_desc` | `WorkOrder.MachineCode · MachineName` |
| Process | `m.process` | **GAP** xem Q3 |
| Target | `m.qty_plan pcs · m.bom_materials.length materials` | `WorkOrder.TargetQty pcs · BOM material count` (xem Q4) |

**Closed WO card** (body 3 rows mirror HTML:18278-18280):

| SpecHub label | Source | CCL-MES |
|---|---|---|
| Customer | `m.customer` | `Customer.Name` |
| Output | `wo.run.qty_done.toLocaleString() / m.qty_plan` | `ProducedQty / TargetQty` |
| Reject | `wo.run.qty_ng · (qty_ng/qty_done*100).toFixed(1)%` | **GAP** — CCL-MES chưa track NG count riêng (xem Q5) |

### 1.2 Status mapping (Q2)

> ## ⚠️ CẢNH BÁO — PHẦN DƯỚI MÔ TẢ SAI CẢ HAI ENUM
>
> Thêm ngày **2026-08-19** sau sự cố dữ liệu. Giữ nguyên văn bản gốc bên dưới làm
> hồ sơ lịch sử, **nhưng đừng chép tên trạng thái từ đó** — chúng không tồn tại
> trong mã.
>
> | | Tài liệu này viết (SAI) | Thực tế trong `src/CCL.MES.Domain/Enums.cs` |
> |---|---|---|
> | `ProcessStepCode` | 7 state: `PrePressCheck/Setup/Running/QC/Pack/Ship/Done` | **8 state**: `PrePressCheck · OpSetting · IpqcApproval · ReadyToRun · Running · Fqc · Oqc · Closed` |
> | `WoStatus` | `Draft/Released/Running/Done/Cancelled` | `Draft · InProgress · OnHold · Finished · Closed · Cancelled` |
>
> Năm tên `Setup`, `QC`, `Pack`, `Ship`, `Done` **chưa bao giờ** là thành viên của
> `ProcessStepCode`. `Released`, `Running`, `Done` **chưa bao giờ** là thành viên
> của `WoStatus`.
>
> **Hậu quả thật:** một lô 11 WO demo (`WO-26-7201`…`7211`) được chèn bằng SQL
> trực tiếp với `CurrentStep='Done'` — từ vựng lấy từ chính tài liệu này. Giá trị
> đó làm `EnumToStringConverter` ném lỗi khi đọc, kéo sập **10 route API**, trong
> đó một route danh sách khiến mất toàn bộ 27 WO cho mọi người dùng. Xem
> `CCL-MES-Hybrid/docs/RUNBOOK-CURRENTSTEP-REPAIR-2026-08-19.md`.
>
> Trạng thái kết thúc **duy nhất đúng** do `WorkOrderStateMachine.ProjectToLegacy`
> quy định: `MesPhase.DONE|CANCELLED|SHIPPED → ProcessStepCode.Closed`.
>
> Trước khi viết bất kỳ giá trị enum nào vào DB: **đọc `Enums.cs`, đừng đọc tài liệu.**


SpecHub 9-state (`NEW/PREPRESS/SETTING/IPQC_WAIT/IPQC_APPROVED/QA_PENDING/RUNNING/PAUSED/DONE`)
↔ CCL-MES `ProcessStepCode` 7-state Phase 6 (`PrePressCheck/Setup/Running/QC/Pack/Ship/Done`)
+ `WoStatus` enum (Draft/Released/Running/Done/Cancelled).

Em đề xuất Q2 default: **derive status badge từ existing 2 enum** thay vì ADD field mới:
- `WoStatus.Draft` → SpecHub "NEW" badge (gray ○)
- `CurrentStep=PrePressCheck` → SpecHub "PRE-PRESS" badge (blue ①)
- `CurrentStep=Setup` → SpecHub "SETTING" badge (amber ③)
- `CurrentStep=Running` AND `WoStatus=Running` → SpecHub "RUNNING" badge (green ▶)
- `CurrentStep=Running` AND `WoStatus=Paused` (chưa có Paused enum trong CCL-MES — xem Q2b) → SpecHub "PAUSED" badge (red ⏸)
- `CurrentStep=QC` → SpecHub "IPQC_WAIT" (purple ④) → "IPQC_APPROVED" (green ✓) tùy `LastQc(IPQC).Result`
- `CurrentStep=Pack/Ship` → SpecHub "QA_PENDING" (amber ⏳)
- `CurrentStep=Done` HOẶC `WoStatus=Done` → SpecHub "DONE" badge (green ✔)

Mapping function pure C# `WorkOrderStatusBadge.From(wo)` → return `{ Code, Label, ColorHex, IconText }`.

### 1.3 Active/Demo/Closed classification (Q1)

SpecHub split (HTML:18200-18202):
```js
const TERMINAL = new Set(['SHIPPED', 'DONE', 'STOPPED']);
const active = filter !TERMINAL
const closed = filter TERMINAL
const demo = MES_WO_MASTER (all template WOs từ catalog)
```

CCL-MES port:
- **Active** = `WorkOrder.Where(w => w.Status != Done && w.Status != Cancelled)`
- **Closed** = `WorkOrder.Where(w => w.Status == Done || w.Status == Cancelled)`
- **Demo** = ⚠ GAP — SpecHub Demo WOs là **template** master data (chưa start), CCL-MES không có khái niệm template. Xem Q1.

---

## 2. Gap analysis — fields/entity CCL-MES còn thiếu

### 2.1 GAP — `Process` field (Q3)

SpecHub `MES_WO_MASTER.process` (string e.g. "Silkscreen + Diecut" / "Flexo Print")
= derive từ ProductRevision/SpecPrint.ProcessCode + SpecDiecut.CutProcessCode.

**Options**:
- **A — Derive runtime** từ `wo.ProductRevision.Print.ProcessCode +
  wo.ProductRevision.Diecut.CutProcessCode` (join string " + "). KHÔNG migration.
- **B — ADD `ProcessLabel` string field** trên WorkOrder. Migration nhỏ.

Em đề xuất **A — derive** (KHÔNG migration, semantic single-source-of-truth
từ ProductRevision).

### 2.2 GAP — Paused status (Q2b)

CCL-MES `WoStatus` hiện không có Paused enum. SpecHub có riêng. Phase 6
quản lý pause khác qua `CurrentStep = Running` + ProductionLog event
"PAUSE" — không state riêng.

**Options**:
- **A — Derive runtime** từ `wo.CurrentStep == Running` + last `ProductionLog`
  event PAUSE chưa có RESUME. Query phụ.
- **B — ADD `Paused` enum + DB field**. Migration + Phase 6 state machine
  edit (risk break test).

Em đề xuất **A — derive** (KHÔNG migration, KHÔNG đụng Phase 6 state machine).

### 2.3 GAP — NG count (Q5)

SpecHub Closed card: "Reject `qty_ng` (`%`)". CCL-MES không track NG count
trên WorkOrder hiện. QcInspection có Pass/Fail per inspection nhưng không
cumulative reject count.

**Options**:
- **A — Render "—"** trong PR đầu (defer NG tracking PR sau)
- **B — Aggregate runtime** từ QcInspection.Result=Fail count × sample_size
  (approximate)
- **C — ADD `RejectQty` field** + ProductionLog NG events

Em đề xuất **A — render "—"** PR đầu, defer B/C PR sau (NG tracking semantic
phức tạp, cần product owner chốt).

### 2.4 GAP — Demo WO template (Q1)

SpecHub Demo = template master không có WO instance. CCL-MES chỉ có WO
instances. **Em đề xuất 3 lựa chọn**:

- **A — Drop Demo section entirely** trong PR đầu (chỉ Active + Closed).
  Đơn giản, mất 1 SpecHub section.
- **B — Show 5 most-recent Draft WOs** as "Demo / Templates" (semantic gần
  nhất với SpecHub "click to start").
- **C — ADD `IsTemplate` flag** trên WorkOrder + admin curate template list.
  Migration + UX phức tạp.

Em đề xuất **A — drop Demo section** (PR đầu); operator click "+ New WO"
trong header thay vì pick demo.

### 2.5 GAP — BOM materials count

SpecHub `m.bom_materials.length` (array count). CCL-MES không có BOM gắn
trực tiếp WorkOrder; có `ManufacturingStructures` link qua ProductCode.

**Options**:
- **A — Query phụ** `ManufacturingStructures.Where(s => s.ParentPart ==
  wo.Product.ProductCode).CountAsync()` per card. Cache miss nếu repeat.
- **B — Pre-flatten** vào DTO ProductRevisionListItem-style.

Em đề xuất **B — pre-flatten** vào `WorkOrderCardItem` DTO (1 query for
list + 1 cumulative count subquery).

---

## 3. Migration scope

Em đề xuất **KHÔNG migration** cho PR đầu. Mọi GAP derive runtime hoặc
render "—". Hệ quả:
- 0 schema risk
- 0 backfill cần
- Bảo toàn FK ProductRevision↔WorkOrder (RESTRICT, PR #28) 100%
- IQC=3 + vùng cấm khác intact

Nếu sau này operator request NG tracking / Paused state / IsTemplate → PR
sau decide migration scope.

---

## 4. Coupling map (DO NOT BREAK)

| Field | Relationship | ON DELETE | PR này có đụng? |
|---|---|---|---|
| `WorkOrder.CustomerId` → `Customer` | FK | (default) | KHÔNG, chỉ đọc Customer.Name |
| `WorkOrder.ProductId` → `Product` | FK | (default) | KHÔNG, chỉ đọc Product.ProductCode + Name |
| `WorkOrder.ProductRevisionId` → `ProductRevision` | FK | **RESTRICT (PR #28)** | KHÔNG đụng FK; chỉ đọc ProcessCode để derive `process` label |
| `WorkOrder.MachineCode` (string snapshot) | logical | — | KHÔNG đụng |
| `WorkOrder.Inspections` (1:N QcInspection) | nav | — | KHÔNG đụng |
| `WorkOrder.History` (1:N WoStatusHistory) | nav | — | KHÔNG đụng |
| `ProductionLog` (separate entity, link via WoNo?) | — | — | KHÔNG đọc, KHÔNG ghi |
| `ManufacturingStructures` | logical join via ProductCode | — | Chỉ COUNT subquery (read-only) |

**IQC**: Phase 6 Bước 7 IQC = pre-WO raw-material inspection, link tới
RawMaterial (NOT WorkOrder). KHÔNG đụng IQC entity / IqcInspection /
IqcResultDetail.

**Machine**: WorkOrder dùng MachineCode + MachineName string snapshot —
KHÔNG có FK Machine entity. KHÔNG đụng Machine.

**ProductionLog**: KHÔNG đọc trong PR đầu (defer NG tracking + duration).

---

## 5. Phase 6 functionality preservation (CRITICAL)

CCL-MES `Pages/WorkOrders.razor` Phase 6 đã có:
- 9-cột table với progress dot flow
- Per-role RBAC button gating (Admin/Supervisor advance + flags; QC pass;
  Operator start/pause/resume/finish)
- WorkOrderStateMachine 7-step lifecycle
- SignalR ShopfloorNotifier real-time push
- i18n EN/VN keys `workorders.*`

**Question Q12 — kế thừa Phase 6 vs replace?**

Em đề xuất **2 route cùng tồn tại** (giống Spec PR #29 modal + PR #31d
full-page):
- `/workorders` (existing) — **giữ NGUYÊN** Phase 6 9-col table cho
  planner overview + action gating. Đổi tên menu "Work Orders (table)".
- `/workorders/shop` (mới) — Shop Order kiosk view port SpecHub. Đổi tên
  menu "Shop Order".

Hoặc anh quyết route swap (PR đầu thay replace `/workorders` = Shop Order
+ defer table).

Em đề xuất GIỮ CẢ 2 (Q12) — Shop Order tốt cho shop-floor; table tốt cho
planner review tổng quan. Operator tự chọn.

---

## 6. Functionality PR split

### PR #32a — Shop Order LIST view parity (em đề xuất ship đầu)

- New page `/workorders/shop` Razor SSR
- Sidebar nav entry "Shop order" trong PRODUCT OPERATION group
- Header: scan QR/code input + LOOKUP button + "+ New WO" button
  (defer scan logic nếu phức tạp; LOOKUP redirect tới detail page existing
  hoặc full-page detail PR sau)
- Empty hero (icon + tagline)
- 3 sections card-based:
  - ⚡ Active Work Orders (N) — card grid với 5-row body
  - ✔ Closed Work Orders (N) — card grid với 3-row body
- WorkOrderStatusBadge mapping → 9 status pill (color + icon + label)
- CSS port SpecHub `.mes-wo-card` + `.mes-wo-cards` grid + status pill
- i18n EN/VN keys (~25 mới)
- RBAC `WoRead` (xác nhận Phase 6 policy — Q9)
- KHÔNG đụng existing `/workorders` table

LOC estimate: ~450 LOC Razor + ~150 CSS + ~30 i18n + ~80 status badge
service = **~710 LOC**. Size **M**.

### PR #32b — Scan QR + Lookup + detail flow (defer)

- Scan QR camera integration (web Camera API)
- LOOKUP route resolve → WorkOrder detail (Phase 6 existing OR new full-
  page)
- Shop Order detail drawer port (mes-flow-view state machine viz)

### PR #32c — Shop Order History page + Export

- `renderShopOrderHistory` port (forensic record search/filter)
- Export CSV (reuse PR #31c pattern)

### PR #32d — NG tracking + Demo template

- ADD RejectQty field + ProductionLog NG event aggregation
- Demo template curation UI

---

## 7. Q1..Q14 — chốt semantics

| Q | Default em đề xuất |
|---|---|
| **Q1 — Demo WO section** | **DROP** trong PR đầu (chỉ Active + Closed). Operator click "+ New WO" tạo mới thay vì pick demo template. |
| **Q2 — Status badge mapping** | **DERIVE runtime** từ `WoStatus + CurrentStep + LastQc result`. Map 9 SpecHub status (NEW/PREPRESS/SETTING/IPQC_WAIT/IPQC_APPROVED/QA_PENDING/RUNNING/PAUSED/DONE) → CCL-MES existing enum. Helper `WorkOrderStatusBadge.From(wo)`. |
| **Q2b — Paused state** | **DERIVE runtime** từ ProductionLog last event = PAUSE chưa RESUME. KHÔNG ADD enum + KHÔNG migration. |
| **Q3 — Process label** | **DERIVE runtime** từ `wo.ProductRevision.Print.ProcessCode + Diecut.CutProcessCode` join " + ". KHÔNG migration. |
| **Q4 — BOM materials count** | **Pre-flatten** vào `WorkOrderCardItem` DTO. 1 query list + cumulative `ManufacturingStructures.Count(s => s.ParentPart == ProductCode)` per row. |
| **Q5 — NG / Reject count** | **Render "—"** PR đầu. Defer NG tracking PR sau (semantic phức tạp). |
| **Q6 — Scan QR input** | **PR đầu render input + LOOKUP button** nhưng LOOKUP redirect tới existing `/workorders` table filtered by WoNo. Camera QR scan defer PR sau. |
| **Q7 — Empty hero** | Port SpecHub icon + "Scan Work Order QR code để bắt đầu" + demo WO codes hint từ 3 most-recent Active rows. |
| **Q8 — Sidebar nav** | ADD "Shop order" entry trong PRODUCT OPERATION group (mirror SpecHub). Route `/workorders/shop`. Existing `/workorders` table giữ menu "Work Orders (table)". |
| **Q9 — RBAC** | `WoRead` (xác nhận Phase 6 policy — em chưa verify chính xác matrix; phải đọc Program.cs). Mọi role xem được trừ Viewer (nếu có). |
| **Q10 — Migration** | **KHÔNG migration** PR đầu. Mọi GAP derive runtime hoặc "—". |
| **Q11 — i18n keys** | ~25 mới (`shop_order.*` prefix). Existing `workorders.*` keys giữ cho table page. |
| **Q12 — Existing /workorders table** | **GIỮ NGUYÊN Phase 6** — 2 route song song (`/workorders` table + `/workorders/shop` cards). Operator chọn. Alt: swap (Shop = default). Em đề xuất giữ 2. |
| **Q13 — Card hover/click** | Click card → defer PR sau (PR #32b detail). PR đầu chỉ render hiển thị + status pill visual. |
| **Q14 — SignalR real-time** | GIỮ NGUYÊN ShopfloorNotifier; card grid auto-refresh khi WO state thay đổi. (Phase 6 push pattern — KHÔNG đụng). |

---

## 8. Hard constraints

- ❌ KHÔNG migration PR đầu (Q10)
- ❌ KHÔNG break FK ProductRevision ↔ WorkOrder (RESTRICT, PR #28)
- ❌ KHÔNG đụng Phase 6 state machine + RBAC button gating + SignalR
  ShopfloorNotifier (đã ship + tested)
- ❌ KHÔNG đụng existing `/workorders` Phase 6 table page (Q12 giữ song
  song)
- ❌ KHÔNG đụng IQC / ProductionLog / Machine / Spec / 4 NPI tab khác /
  Ops Control v1.2 / SpecHub READ-ONLY / CMES sibling READ-ONLY (bỏ qua
  per anh chỉ thị) / Old ver
- ❌ Bài học #27: render từ entity grid + try-catch wrap query phụ +
  pre-flatten DTO
- ❌ Bảo toàn baseline + IQC=3 + vùng cấm khác
- ❌ Bài học #33/#17: KHÔNG dot-extension trong route template (Razor
  page `@page "/workorders/shop"` OK; API endpoint nếu có dùng path
  segment)
- ❌ Bài học #14: `[Authorize(Roles=...)]` cho ApiController, không Policy
  challenge
- ❌ i18n EN/VI mọi label
- ❌ Sticky header KHÔNG cần (card grid không có header to fix)

---

## 9. Verify gates (post-implementation)

| # | Check | Method |
|---|---|---|
| V1 | dotnet build clean | 0 W / 0 E |
| V2 | `/workorders/shop` render đúng 3 section (Active / Closed); empty state khi 0 WO | Browser |
| V3 | Active card 5-row body khớp SpecHub field map (Customer/Product/Machine/Process/Progress) | Browser |
| V4 | Closed card 3-row body (Customer/Output/Reject=—) | Browser |
| V5 | Status badge color + icon đúng cho mọi WoStatus + CurrentStep combo | Browser test 9 cases |
| V6 | Process label derive đúng từ ProductRevision.Print.ProcessCode + Diecut.CutProcessCode | Sample silk + flexo + null cases |
| V7 | BOM materials count subquery đúng cho 3 sample WO khác Customer | sqlite verify |
| V8 | RBAC `WoRead` test 5 role: Admin/Supervisor/Engineer/QC/Operator | Browser login switch |
| V9 | Scan QR input + LOOKUP button visible (functional defer PR sau OK) | Browser |
| V10 | Vùng cấm intact (existing /workorders table + state machine + SignalR + FK ProductRevision↔WO + IQC=3) | git diff scope + functional smoke |
| V11 | Restart no-op | Boot 2 lần |
| V12 | i18n EN+VN switch (toolbar header + section labels + status pill labels) | Browser language toggle |

---

## 10. LOC estimate + PR split confirmation

PR #32a Shop Order LIST parity:

| Component | LOC |
|---|---|
| `Pages/WorkOrders/ShopOrder.razor` (mới full-page) | ~350 |
| `WorkOrderCardItem` DTO + `WorkOrderService.ShopOrderListAsync` | ~120 |
| `WorkOrderStatusBadge.cs` static helper (map 9 status) | ~80 |
| CSS `mes-wo-card / mes-wo-cards / mes-pill` port SpecHub | ~150 |
| `Pages/_Host.cshtml` + sidebar nav entry "Shop order" | ~30 |
| i18n EN+VN (~25 keys) | ~50 |
| Empty hero + scan input UI shell | ~80 |

**Total**: ~860 LOC. Size **M**.

(Defer PR #32b/c/d sau)

---

## 11. STOP — chờ duyệt

Em sẽ KHÔNG tạo branch / KHÔNG code cho đến khi anh:

1. **Confirm scope PR #32a** — Shop Order LIST parity 3 section (Active +
   Closed; drop Demo) + scan input shell + status badge. ~860 LOC. PR
   #32b/c/d defer.

2. **Chốt Q1-Q14** (em đề xuất default cho hầu hết — em flag rõ:
   - Q1 Drop Demo section vs Show 5 Draft "Templates"
   - Q12 Giữ 2 route song song vs Swap Shop = default
   - Q6 Scan QR camera integration defer PR sau OK?
   - Q5 NG count render "—" PR đầu OK?
   )

3. **Confirm Q9 RBAC**: Em chưa verify chính xác Phase 6 WoRead policy.
   Em cần đọc `Program.cs` AddPolicy("WoRead", ...) hoặc xem
   existing `[Authorize]` trên WorkOrdersController. Anh confirm matrix
   nào (Admin/Supervisor/Engineer/QC/Operator)?

4. **Confirm Q12**: 2 route song song (`/workorders` table giữ + `/workorders/shop`
   mới) HAY swap (`/workorders/shop` = default + table thành `/workorders/legacy`)?

Sau khi anh chốt, em sẽ:
- Tạo branch `feat/phase8-shop-order-parity`
- Code Shop Order page + status badge helper + sidebar nav
- KHÔNG đụng existing /workorders table + state machine + SignalR
- V1-V12 verify
- Mở PR riêng + STOP chờ duyệt.

---

## 12. Files surveyed (transparency)

SpecHub READ-ONLY sources:
- `spechub-prototype.html`:
  - `renderMesEmptyState()` HTML:18193-18290 (3-section card render)
  - `WO_STATES` HTML:15042-15052 (9 status config)
  - `MES_WO_MASTER` HTML:15081+ (master data shape)
  - Sidebar nav HTML:7263 + `openShopOrderHistory()` HTML:18698+
- `apps/server/src/modules/mes/` (backend MES module — referenced not
  surveyed for PR đầu)

CCL-MES current:
- `src/CCL.MES.Domain/Entities/WorkOrder.cs` (entity schema)
- `src/CCL.MES.Web/Pages/WorkOrders.razor` (Phase 6 table page — giữ NGUYÊN)
- `src/CCL.MES.Application/Services/WorkOrderService.cs` (sẽ extend)

**Bỏ qua** (per anh chỉ thị):
- CMES sibling `/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CMES/`
  — KHÔNG dùng làm nguồn parity
- Ops Control v1.2 — KHÔNG dùng làm nguồn parity
- Old ver — KHÔNG dùng làm nguồn parity
