# PHASE 8 SPEC SHOWCARD PLAN — merge full SpecHub spec sheet showcard

> **STOP sau plan.** Anh chỉ thị: gác Shop Order, deep audit SpecHub NPI
> Spec → merge full showcard + template từng dòng máy vào Engineer Spec
> (detail + Create preview + Import preview). Anh cũng chốt **màu showcard
> = tông navy** (template SEAL/Flexo SpecHub, KHÔNG dùng beige/tan SILK).
>
> Plan này: (1) báo root-cause bug hiện tại; (2) deep audit SpecHub
> template per category; (3) audit current CCL-MES; (4) merge plan với
> palette navy + PR split + Q&A.

---

## Bước 1 — ROOT-CAUSE BUG hiện tại (modal "PARAMETERS" 10 dòng "—")

### 1.1 Bug location chính xác

| File | Line | Vấn đề |
|---|---|---|
| `src/CCL.MES.Web/Shared/SpecDetailModal.razor` | 117-152 | UI table render header `param_name / nominal / tol_min / tol_max / uom / is_critical` + foreach `_params` → mỗi row hiển thị `pp.ParamName ?? ""` + `pp.Nominal ?? "—"` + 5 cell khác `?? "—"` |
| `src/CCL.MES.Web/Shared/SpecDetailModal.razor` | 360-388 | `TryParseParams(string json)` đọc `el.TryGetProperty("param_name", ...)`, `"nominal"`, `"tol_min"`, `"tol_max"`, `"uom"`, `"is_critical"` |

### 1.2 JSON shape persisted ≠ shape modal đọc

PR #31a `SpecImportService.SerializeColorsForLegacy` (line ~340) ghi vào
`SpecPrint.ColorSpecJson` với shape silk print row:

```json
[
  { "no":1, "surface":"R", "color":"WN-212", "ink_name":"CCLISOL-1160",
    "ink_code":"HI1160", "maker":"CCL MIX", "retarder":"T980", "visc":16,
    "speed":0, "squeegee":"BS", "dry":"OVEN", "temp":60, "time_min":20,
    "uv":"", "emulsion_um":15, "plate_size":"700×950", "mesh":"L120",
    "angle":22.5, "plate_code":"SP1620-1", "control":44, "remark":"" },
  ... 9 more rows
]
```

PR #29 modal đọc keys hoàn toàn khác: `param_name / nominal / tol_min /
tol_max / uom / is_critical` (legacy SpecParameter shape Phase 6 trước
khi #28 rewrite). → `TryGetProperty` miss mọi key → `ParamRow` với mọi
field = null → render 10 row "—" + cell "—" cho mọi cột.

### 1.3 Phân loại bug

**Bug binding 100%, KHÔNG phải bug schema.** Data đầy đủ (10 SpecPrintColor
rows + ColorSpecJson silk shape) — chỉ là modal đọc sai shape.

### 1.4 Đề xuất fix (option C trong plan — single source of truth)

**Drop `TryParseParams`** hoàn toàn. Modal binding switch sang **SpecDetailDto**
(đã có từ PR #31d, render bằng full-page route) — single source of truth +
showcard layout consistency cho cả modal + full-page + PDF.

Modal hiện chỉ là "Get Info" peek nên có thể either:
- **Option C1** — Modal show same showcard rendering as full-page (re-use
  Razor partial component). Modal grow lớn (scroll trong modal).
- **Option C2** — Modal show **summary** subset từ SpecDetailDto: Identity
  Section + first 5 print colors + audit stamps. Click "Open full sheet" →
  navigate `/npi/engineer-spec/{id}`. Compact peek view.

Em đề xuất **Option C2** (compact peek) — bài học UX modal vs full-page từ
PR #31d (modal cho quick check, full-page cho deep dive).

**Bug fix** đẩy vào PR-A (xem PR split mục 9 dưới).

---

## Bước 2 — DEEP AUDIT SpecHub NPI Spec showcard

### 2.1 Dispatch logic (HTML:10499-10505)

```js
function applySpecTemplate(p) {
  const useSpecial = p.specType === 'silkscreen' || p.specType === 'flexo';
  // Special = showcard frame; else generic-spec (fallback list view)
  if (p.specType === 'silkscreen') renderSilkscreenSpec(p);
  else if (p.specType === 'flexo') renderFlexoSpec(p);
  // letter/indigo/diecut → fall through → no dedicated showcard
}
```

**Confirmed**: SpecHub có **chỉ 2 dedicated showcard template** (silk +
flexo). LETTER/INDIGO/DIECUT → `generic-spec` view (simpler table list,
KHÔNG có spec-frame showcard). Per HTML:10500-10501 fallback explicit.

### 2.2 Template per category — bảng tóm tắt

| Category | Template | Section + bảng/biểu | Color accent (SpecHub) | Status CCL-MES PR #31d |
|---|---|---|---|---|
| **SILK** (silkscreen) | `renderSilkscreenSpec` HTML:10269-10497 | 8 section: Doc header / Compliance strip / Product Info 8-col / Print Parameters (cavity/pitch/size/diameter + Squeegee/Dry codes legend) / Print Process 10-color table **21 cols** (No/Surf/Color+swatch/InkName/InkCode/Maker/Retarder/Visc/Speed/Squeegee/Dry/Temp/Time/UV/Emul/Size/Mesh/Angle/PlateCode/Ctrl/Remark) / Remarks 1-col / Revision History 4-col / Approval Signatures 4-role | Red `#c8102e` accent + **cream/tan bg** (`#ecddc9`/`#f3ede0`/`#5a4419`) | ✅ Ported web full-page + PDF (PR #31d) — **navy theme đè per anh chốt** |
| **FLEXO** (SEAL) | `renderFlexoSpec` HTML:10510-10770 | 8 section: Doc header / Compliance / Product Info 6-col (+ Version) / Printing Information 12-col / Cutting Information 14-col / Ink Information 10-col / Remarks 2-col / Revision History / Approval Signatures 4-role | **Navy `#0033a0`** accent + light navy bg (`#dde6f3`/`#e8eef7`/`#1e3a73`) | ✅ Ported web full-page + PDF (PR #31d) — **giữ navy nguyên** |
| **LETTER** (Letterpress) | _Fallback `generic-spec`_ | KHÔNG showcard riêng — fallback simple list view (key/value pairs) | Brown `#7c2d12` chip nav | ⚠ Render generic in CCL-MES (chưa có showcard) |
| **INDIGO** (HP Indigo) | _Fallback `generic-spec`_ | KHÔNG showcard riêng — fallback simple list | Teal `#00897b` chip nav | ⚠ Render generic in CCL-MES (chưa có showcard) |
| **DIECUT** (RDC/CNC/Powerpunch/Laser/Kiss) | _Fallback `generic-spec`_ | KHÔNG showcard riêng — fallback simple list | Purple `#9333ea` chip nav | ⚠ Render generic in CCL-MES (chưa có showcard) |

### 2.3 Functions tab NPI Spec SpecHub vs CCL-MES

| Function | SpecHub | CCL-MES status |
|---|---|---|
| Create new spec | `openCreateOneCModal` planner picker + xlsx upload + preview | ✅ PR #31a (CreateSpecModal v2) — Create modal có planner picker + upload preview |
| Import parse silk + flexo | `parseXlsxSilkscreen` + `parseXlsxFlexo` | ✅ PR #31a/b (SilkscreenXlsxParser + FlexoXlsxParser via factory) |
| Showcard preview (post-parse) | `renderSilkscreenSpec` / `renderFlexoSpec` | ⚠ PR #31a preview = **text-only** (refNo/customer/partNo summary) — KHÔNG showcard |
| Showcard detail | render Razor full-page | ✅ PR #31d full-page `/npi/engineer-spec/{id}` |
| Compliance strip 3 chip | "HSF strict control · Spec A126 · RoHS Compliance" | ✅ PR #31d (ComplianceChips derive) |
| Squeegee/Drying code legend | 9 chip hard-coded (YS/BS/YMS/BMS/YR/BR/ND/DR/OVEN/UV) | ⚠ KHÔNG có ở CCL-MES PR #31d (silk only feature) |
| Export CSV/Excel/PDF list | (SpecHub no list view export) | ✅ PR #31c (Export 3 formats) |
| Print spec sheet PDF | (SpecHub có PDF export pattern khác) | ✅ PR #31d (PDF spec sheet) |
| Revision/Upgrade flow | dup banner Replace/Upgrade/Copy/Cancel | ❌ Defer PR lifecycle (paused PR #30 plan) |
| Trash + Restore | (SpecHub có soft-delete) | ✅ Entity fields có (`IsTrashed/TrashedAt/TrashedBy`) — UI defer PR lifecycle |
| Approval workflow (R&D/PD/QA ký) | (SpecHub có signature workflow) | ⚠ Option A render-only PR #31d — workflow defer PR approval-chain |
| Color swatch PANTONE | `PANTONE_SWATCHES` 9-color hard-coded | ✅ PR #31d (SpecDetailColors.SwatchHex) |
| Drawing upload + 3-role approval | (SpecHub có UI) | ❌ Defer PR drawing |

### 2.4 Sample bundle for testing render

`/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/SpecHub/Data/Specs/`:
- ✅ Silk: AWW0146C98C0-WC0 (Panasonic 9-color), AWW0146C6FC0-0C5 (Panasonic 10-color), 3205884802 (DELTA 7-color), Silk_1000527330 (Johnson 6-color) — 4 sample đã port + sanitize CCL-MES PR #31a (`DEMO_SILK_1-4.xlsx`)
- ✅ Flexo: G-EHB-HC-DISNEY (CCL VINA), 080-0005-1618-ZE-NP (FIT) — 2 sample đã port + sanitize PR #31b (`DEMO_FLEXO_1-2.xlsx`)
- ⚠ Unused: 3P631278-1 (chưa xác định category), GH68-55731L (likely flexo), Silk_3205877502 (silk format)

**PR #31d showcard hiện đang test trên 6 sample này** — không cần thêm samples mới. Nếu thêm category → sanitize + bundle như pattern PR #31a/b.

---

## Bước 3 — AUDIT hiện trạng CCL-MES showcard

### 3.1 SpecDetailModal.razor (PR #29 "Get Info" peek)

**Có** (sections):
- Identity table 6-row (SpecCode/Title/Product/Rev/Status/EffectiveFrom)
- Spec content sub-sections (Material/Print/Diecut/Finishing) — list table 5-7 row each
- Drawings placeholder (defer)
- Audit trail timeline

**Render dạng**: 100% list dọc key/value (vertical table). **KHÔNG showcard**
(no spec-frame, no compliance strip, no 10-color print process table, no
4-role approval).

**Bug** (Bước 1): "PARAMETERS" table 10 row "—" do binding `TryParseParams`
sai shape.

### 3.2 EngineerSpecDetail.razor (PR #31d full-page)

**Có** (full showcard, 9 section): Doc header / Compliance strip /
Product Info (silk 8-col / flexo 6-col) / Print Params (silk) / Print
Process 10-color table (silk) HOẶC Flexo 3 sub-tables / Remarks (1 / 2-col) /
Revision History / Approval Signatures 4-role / Change Log.

**Render dạng**: spec-frame showcard giống SpecHub silk + flexo. Đã port
PANTONE swatch + StatusDisplay 5→3.

**Khoảng cách so SpecHub**:
- ❌ Theme color = **silk cream/tan** (`#ecddc9`/`#5a4419`) ở `.spec-block-title`
  bg + border `#d0c8b2` còn sót → **cần thay navy đồng nhất** (Bước 4 §4.1)
- ❌ Flexo cut section bg `#ffeacc` (tan SpecHub) + ink section bg
  `#d8f0d6` (green) → cần unified navy variant (override SpecHub mặc định)
- ❌ Silk "Squeegee codes / Drying codes" legend KHÔNG có ở PR #31d Print
  Params section
- ❌ Modal vs full-page chia 2 render path (DRY violation) — cần unified
  partial component

### 3.3 CreateSpecModal.razor (PR #31a Create + Import preview)

**Có** (preview pane sau parse xlsx):
- DTO summary 8-row dl/dt grid: RefNo / Customer / PartNo / PartName /
  Inspection / Size / Cavity / Pitch / Material / NumColors count
- Warnings collapsible
- Dup banner nếu RefNo trùng

**Khoảng cách so SpecHub**:
- ❌ Preview = **text summary 8-row**, KHÔNG render showcard ngay khi
  parse. Operator mới thấy showcard sau khi Save → reload list → click row →
  full-page detail. **Phải scroll qua nhiều click trước khi xem layout
  thực**.

**SpecHub pattern**: `pendingOneCSpec` parsed → `showOneCPreview` →
**showcard render LIVE trong modal** trước Save. Operator xác nhận layout
trực quan ngay.

### 3.4 SpecPdfDocumentBuilder.BuildDetailSheet (PR #31d PDF)

✅ Reusable layer thiết kế cho PR #33 detail sheet. PR #31d đã extend với
9 Append* helpers. Cùng layout với web full-page (BuildEmpty + 9 section).

**Khoảng cách**:
- ✅ PDF + web cùng dùng SpecDetailDto + SpecListColumns.StatusDisplay
- ❌ KHÔNG share showcard render code Razor (Razor vs MigraDoc API khác
  hẳn — không thể "1 nguồn layout" thuần như Razor partial)
- ✅ Nhưng SHARE: SpecDetailDto + StyleConstants + Status mapping +
  ComplianceChips derive → semantic single source of truth

---

## Bước 4 — PLAN MERGE: showcard navy theme cho 3 surface

### 4.1 Color palette — SpecHub navy hex → CCL-MES token

Trích đúng từ SpecHub `.spec-frame.flexo` CSS HTML:5880-5899 + bonus
override cho silk/cut/ink section + CCL-MES existing IQC/Login navy:

| Token name | SpecHub hex (flexo) | CCL-MES existing | **PR-A đề xuất** | Lý do |
|---|---|---|---|---|
| **navy-primary** (header band, accent strong) | `#0033a0` | `#1f3864` (Login + IQC h1) | **`#1f3864`** | Anh chốt dùng CCL-MES sẵn cho đồng bộ app |
| **navy-gradient-deep** (companion gradient) | — | `#0f2042` (Login) | **`#0f2042`** | Reuse Login gradient |
| **navy-dark** (heading text trên light bg) | `#1e3a73` | — | **`#1e3a73`** | Direct SpecHub (xanh đậm vừa) |
| **navy-light** (section title bar bg) | `#dde6f3` | — | **`#dde6f3`** | Direct SpecHub (gần mốc `#DCE6F1` anh đưa) |
| **navy-tint** (cert/info bg + alt row) | `#e8eef7` | — | **`#e8eef7`** | Direct SpecHub (≈ user mốc `#F5F8FC` cho alt row) |
| **navy-border** | `#b6c4dd` | — | **`#b6c4dd`** | Direct SpecHub |
| **navy-accent-text** (Part No / plate code / cavity) | `#0033a0` | — | **`#1F5FAE`** (user mốc) | Link xanh dịu hơn `#0033a0`, khớp user mốc |
| **gray-col-header** (Process/Material… col header) | (SpecHub `#dde6f3`) | — | **`#F0F2F5`** (user mốc) | Neutral xám for col header — anh muốn phân biệt section bar vs col header |
| **gray-col-header-text** | `#1e3a73` | — | **`#6b7280`** (tailwind gray-500) | User mốc "chữ xám đậm" |
| **alt-row-bg** | `#fafafa` (gray) | — | **`#F5F8FC`** (user mốc) | Very light navy tint cho alt row, KHÔNG dùng pure gray |
| **stamp-approved** (badge) | `#10b981` (emerald) | `#00a651` (spec-stamp-approved hiện) | **`#2E9B57`** (user mốc) | User mốc chính xác hơn (xanh lá đậm balanced) |
| **stamp-draft** | `#d97706` (amber) | `#d97706` (hiện) | **`#d97706`** giữ | OK |
| **stamp-superseded** | `#6b7280` (gray) | `#6b7280` (hiện) | **`#6b7280`** giữ | OK |
| **customer-red** (Customer column highlight) | `#c8102e` (silk-red) | — | **`#C00000`** (user mốc) | Đỏ chuẩn doanh nghiệp |
| **changelog-orange** (audit icon — added/modified) | — | — | **`#E8A33D`** (user mốc) | Audit log entry icon orange |
| **changelog-green** (audit icon — done/approved event) | `#10b981` | — | **`#2E9B57`** | Match stamp-approved |
| **ref-no-text** (REF NO box) | `#0033a0` (flexo) | — | **`#1f3864`** | Match navy-primary |

#### Override cho silk cream/tan (HIỆN có ở CCL-MES `site.css` PR #31d)

Cần thay 4 SpecHub default silk colors bằng navy palette:

| Selector | SpecHub silk (cũ) | PR-A override |
|---|---|---|
| `.spec-doc-header` bg (silk default — SpecHub có solid white, NO bg silk variant) | (no bg) | (no bg) — giữ |
| `.spec-doc-header` borderbottom + `.center-block` color | red `#c8102e` | navy `#1f3864` (header band band navy) |
| `.spec-block-title` bg | `#ecddc9` cream | **`#dde6f3` (navy-light)** |
| `.spec-block-title` color | `#5a4419` brown | **`#1e3a73` (navy-dark)** |
| `.spec-block-title` border-color | `#d4cdb8` cream-border | **`#b6c4dd` (navy-border)** |
| `.spec-info-table th` bg | `#f3ede0` light-cream | **`#dde6f3` (navy-light)** |
| `.spec-info-table th` color | `#5a4419` brown | **`#1e3a73` (navy-dark)** |
| `.spec-info-table th` border | `#d0c8b2` | **`#b6c4dd` (navy-border)** |
| `.spec-info-table .highlight` (Customer cell) | `#c8102e` silk-red | **`#C00000` (customer-red)** |
| `.spec-print-table thead th` bg | (silk: gray `#f3f4f6`) | **`#F0F2F5` (gray-col-header)** ← user mốc |
| `.spec-print-table thead th` color | `#1f2937` | **`#6b7280` (gray-col-header-text)** |
| `.spec-print-table tbody tr:nth-child(even)` bg (alt row) | `#fafafa` | **`#F5F8FC` (alt-row-bg)** |

#### Override flexo subsections (tan + green còn sót)

| Selector | SpecHub (cũ) | PR-A override |
|---|---|---|
| `.flexo-section-cut .spec-block-title` bg | `#ffeacc` (TAN) | **`#dde6f3` (navy-light)** |
| `.flexo-section-cut .spec-block-title` color | `#7b4400` (brown) | **`#1e3a73` (navy-dark)** |
| `.flexo-section-ink .spec-block-title` bg | `#d8f0d6` (green) | **`#dde6f3` (navy-light)** |
| `.flexo-section-ink .spec-block-title` color | `#2c5d2a` (dark green) | **`#1e3a73` (navy-dark)** |
| `.flexo-section-print .spec-block-title` bg | `#cee0fa` (light blue OK) | **`#dde6f3` (navy-light)** (đồng nhất) |

Anh chọn:
- (a) Đồng nhất tất cả 3 subsection cùng navy-light (em đề xuất default,
  phù hợp "thống nhất navy" anh chỉ thị);
- (b) Giữ 3 màu phân biệt nhưng dùng navy variants (light navy / mid navy /
  navy với opacity khác);

Default em chọn (a). Anh override nếu muốn (b).

### 4.2 Per-category template strategy

| Category | Approach PR-A | PR sau |
|---|---|---|
| **SILK** | Re-skin existing PR #31d showcard sang navy palette (override CSS); fix bug binding Bước 1 | — |
| **FLEXO** | Reuse navy palette (đã navy-base sẵn từ PR #31d, chỉ override cut/ink sub bg) | — |
| **LETTER** | KHÔNG showcard riêng (SpecHub fallback) → render silk showcard fallback **với warning chip** "Letterpress layout — using silkscreen template; verify field mapping". Future: PR-D port dedicated khi có sample mẫu thực | PR-D dedicated khi có data |
| **INDIGO** | Tương tự LETTER — silk fallback + warning | PR-D dedicated khi có data |
| **DIECUT** | Tương tự — silk fallback + warning. Defer chừng nào có sample mẫu RDC/CNC/Laser thực | PR-D dedicated khi có data |

**Lý do**: SpecHub bản thân chỉ có 2 dedicated template (silk + flexo) +
parser cũng chỉ 2. Build 3 template còn lại từ data không có là **bịa
parity** (đúng nguyên tắc anh chỉ thị Phase 8 Shop Order). Future PR cần
sample thực từ CCL Vietnam.

### 4.3 Unified showcard render — chia 3 surface

#### Surface 1: `/npi/engineer-spec/{id}` full-page (PR #31d existing)

- Apply navy palette override (CSS-only change)
- Bug fix Bước 1 (drop TryParseParams — đã có SpecDetailDto)
- Add silk Squeegee/Drying codes legend (gap mục 2.3)

#### Surface 2: `SpecDetailModal` (PR #29 "Get Info" peek)

**Option C2 — compact peek**: Modal render Identity section (3 row table) +
first 5 PrintColors (silk) / first 3 FlexoCuttingRows (flexo) + audit
stamps. Click "Open full spec sheet →" button → `Nav.NavigateTo("/npi/engineer-spec/{id}")`.

**Bug fix**: Modal nhận SpecDetailDto via `SpecService.SpecDetailAsync(id)`
thay vì SpecContentAsync + TryParseParams.

#### Surface 3: CreateSpecModal preview (PR #31a parse → preview)

**Pattern SpecHub `showOneCPreview`**:
- Sau khi `SpecImportService.PreviewAsync` trả `SpecImportPreviewDto` →
  modal render **mini-showcard live** thay vì DL/DT summary 8-row.
- Reuse component partial `<SpecShowcard>` (xem 4.4 dưới) với mode="preview"
  binding `previewDto.Parsed` (ParsedSpecDto từ PR #31a).
- Operator scroll trong modal xem layout đầy đủ (silk colors / flexo
  rows) trước khi click Save.

#### Surface 4: PDF (PR #31d existing — gián tiếp)

Re-skin StyleConstants navy palette trong SpecPdfDocumentBuilder. PDF
KHÔNG share Razor markup nhưng share **semantic** (DTO + ComplianceChips
+ StatusDisplay + StyleConstants).

### 4.4 Unified `<SpecShowcard>` Razor component

Mới — `Shared/SpecShowcard.razor` reusable across 3 surface (modal +
full-page + Create preview):

```razor
@code {
    [Parameter] public SpecDetailDto? Detail { get; set; }       // full-page mode
    [Parameter] public ParsedSpecDto? Parsed { get; set; }       // preview mode
    [Parameter] public bool Compact { get; set; }                // modal peek
    [Parameter] public bool ShowApprovalSection { get; set; } = true;
    [Parameter] public bool ShowChangeLog { get; set; } = true;
}
```

3 section group chính (Identity / Product Info / Print Process) render
chung 100%; 6 section còn lại (Compliance / Params / Remarks / Lineage /
Approval / Audit) gate qua `Compact` + `Show*Section` flag.

**Output**: 1 layout reusable; 0 duplicate Razor.

### 4.5 Bonus — Silk Squeegee/Drying codes legend

SpecHub silk Print Params block có 2 legend chip cards (HTML:10370-10374):
- Squeegee codes: YS/BS/YMS/BMS/YR/BR
- Drying codes: ND/DR/OVEN/UV

PR-A add Razor partial `<SilkCodeLegend>` render 2 chip group. CCL-MES
hard-code 6+4 codes (mirror SpecHub HTML:11617-11629). Future PR có thể
move sang ProcessCatalog table.

---

## 5. Migration scope

**KHÔNG migration cho PR-A.**

Bug fix Bước 1 = binding fix, không đụng schema. Color palette = CSS-only.
Squeegee legend = hard-coded chip (no DB). Showcard partial component =
pure Razor.

Future PR (drawing approval / signature workflow / Demo template) sẽ
decide schema riêng.

---

## 6. Coupling — DO NOT BREAK

- ❌ KHÔNG đụng FK ProductRevision↔WorkOrder (RESTRICT, PR #28)
- ❌ KHÔNG đụng IQC / ProductionLog / Machine / Phase 6 WO state machine
- ❌ KHÔNG đụng SpecHub READ-ONLY / CMES / Ops Control v1.2 / Old ver
- ❌ KHÔNG đụng 4 NPI tab khác (Structure / Routine / RawMat / WC)
- ❌ Giữ NGUYÊN SpecListColumns + SpecPdfDocumentBuilder.BuildListView
  (PR #31c list export) — chỉ override theme constants

---

## 7. Hard constraints

- ❌ Bug fix Bước 1 = binding only, KHÔNG schema change
- ❌ Reuse SpecDetailDto + SpecListColumns + StyleConstants (PR #31c/d)
- ❌ Per category dispatch theo `IsSilkscreen` / `IsFlexo` flag
  (SpecDetailDto đã có); LETTER/INDIGO/DIECUT fallback silk template +
  warning chip
- ❌ Navy palette = chuẩn anh chốt; CSS-only override SpecHub default
- ❌ Bài học #27: render từ entity grid + try-catch wrap query phụ
- ❌ Bảo toàn baseline + IQC=3 + vùng cấm khác
- ❌ Sanitize mọi sample nếu commit (DISNEY/PANASONIC → demo) — đã làm ở
  PR #31a/b, không cần thêm samples mới
- ❌ i18n EN/VN cho mọi label section header
- ❌ RBAC NpiSpecRead xem / Admin+Engineer mutation
- ❌ Bài học #14/#33: API route path segment, `[Authorize(Roles=...)]`

---

## 8. Verify gates (post-implementation per PR)

| # | Check | Method |
|---|---|---|
| V1 | dotnet build clean | 0 W / 0 E |
| V2 | Bug fix: modal mở DEMO_SILK_1 (rev 2) → 9 print colors render đúng (KHÔNG "—") | Browser manual |
| V3 | Full-page navy palette áp dụng đầy đủ — KHÔNG còn cream/tan visible | Browser inspect |
| V4 | Flexo cut + ink section bg navy (KHÔNG tan/green) | Browser open rev 6 |
| V5 | Silk Squeegee/Drying codes legend render đúng (BS/YMS/OVEN/UV…) | Browser open rev 2 |
| V6 | Create modal preview: upload `DEMO_SILK_1.xlsx` → showcard render LIVE (KHÔNG text summary) | Browser manual upload |
| V7 | Compact modal peek: "Get Info" rev 2 → showcard mini + "Open full sheet" button → navigate full-page | Browser test |
| V8 | Per-category dispatch: open rev 6 (flexo) renders 3 sub-tables; rev 2 (silk) renders 10-color table | Browser |
| V9 | LETTER/INDIGO/DIECUT fallback: simulate (manually update SpecPrint.ProcessCode="LETTERPRESS") → silk template + warning chip "Using silk template fallback" | sqlite UPDATE + browser |
| V10 | PDF spec sheet navy palette áp + 6 sample render đúng | harness run |
| V11 | RBAC: QC role open `/npi/engineer-spec/2` → 403 | curl + browser |
| V12 | Vùng cấm intact (PR #28 FK + IQC=3 + Phase 6 WO not disturbed) | git diff scope |
| V13 | Restart no-op | Boot 2 lần |

---

## 9. PR split + LOC estimate

### PR-A — Bug fix binding + SILK showcard navy theme + Squeegee legend (em đề xuất ship đầu)

- **Bug fix** Bước 1: Drop `TryParseParams`; modal switch sang SpecDetailDto
  binding (read SpecPrintColor entity rows trực tiếp). Compact modal peek
  + "Open full sheet" button.
- **CSS navy palette** override silk showcard cream/tan → navy variants
  (xem mục 4.1 bảng override 12 selector).
- **Silk Squeegee/Drying codes legend** chip card 2-block render.
- **Bonus**: navy palette apply tới PDF StyleConstants (PR #31d
  SpecPdfDocumentBuilder navy).

LOC: ~80 (modal fix) + ~50 (legend partial) + ~120 (CSS override) +
~30 (PDF StyleConstants update) = **~280 LOC**. Size **S-M**.

### PR-B — FLEXO showcard navy unified + 3 fallback warning

- **Override flexo cut/ink section bg** (tan/green → navy-light)
- **LETTER/INDIGO/DIECUT category fallback warning chip** trên detail page
  toolbar "Using silkscreen template fallback — verify field mapping"
- **Unified `<SpecShowcard>` Razor component** chia 3 mode (full / compact /
  preview)

LOC: ~120 (flexo CSS override + warning chip) + ~250 (SpecShowcard
component extract) + ~80 (refactor modal + full-page + preview call sites) =
**~450 LOC**. Size **M**.

### PR-C — CreateSpecModal LIVE showcard preview

- Replace text-summary preview section bằng `<SpecShowcard Parsed=...
  Compact=true>` partial → operator scroll xem layout thực
- Optional: thêm "Switch to text view" toggle nếu UX cần fallback
- Add per-field warning highlight (red border quanh field NULL required)

LOC: ~80 (preview call site) + ~50 (toggle + warning) = **~130 LOC**.
Size **S**.

### PR-D (defer) — Dedicated LETTER/INDIGO/DIECUT template

Chỉ ship khi có sample xlsx thực CCL Vietnam cho 3 category còn lại. Cần
parser (giống PR #31b flexo pattern) + showcard template + sample
sanitize. Ước lượng ~3000+ LOC per category. **DEFER hoàn toàn**.

### Total PR-A + PR-B + PR-C = ~860 LOC. Size **M**.

**Em đề xuất ship 3 PR tuần tự**:
1. PR-A ship đầu (bug fix critical + silk navy)
2. PR-B sau PR-A merged (flexo unified + component extract)
3. PR-C sau PR-B merged (LIVE Create preview)

Hoặc nếu anh muốn nhanh hơn → **gộp PR-A + PR-B** thành 1 PR ~730 LOC
(component extract làm chung với CSS override). PR-C ship riêng.

---

## 10. Q1..Q12 — chốt semantics

| Q | Default em đề xuất |
|---|---|
| **Q1 — PR split** | **3 PR tuần tự** (A bug+silk navy → B flexo+component → C LIVE preview). Alt gộp A+B nếu anh muốn ship nhanh hơn. |
| **Q2 — Bug fix approach** | **Option C2 compact modal peek** + "Open full sheet" button (xem 1.4). Drop TryParseParams hoàn toàn; modal switch SpecDetailDto. |
| **Q3 — Navy palette source** | **CCL-MES existing `#1f3864`** (anh chốt) cho navy-primary; SpecHub `#dde6f3`/`#e8eef7`/`#1e3a73`/`#b6c4dd` cho secondary tints (anh đưa mốc match). |
| **Q4 — Flexo 3 sub-section bg** | **Đồng nhất navy-light** (a) — anh chỉ thị "thống nhất navy". Alt (b) giữ 3 variants navy distinct. |
| **Q5 — LETTER/INDIGO/DIECUT** | **Silk fallback + warning chip** PR-B. Dedicated template defer PR-D khi có sample thực. |
| **Q6 — Squeegee/Drying codes legend** | **Hard-code 6+4 chip** mirror SpecHub HTML:11617-11629. Move sang ProcessCatalog table future PR. |
| **Q7 — Modal compact peek** | **Show Identity + first 5 PrintColors / 3 FlexoCutting + audit stamps**. Button "Open full sheet →". |
| **Q8 — Create preview** | **Replace text summary bằng `<SpecShowcard Compact=true>`** PR-C. Operator scroll xem showcard live. |
| **Q9 — Per-category dispatch** | Reuse `SpecDetailDto.IsSilkscreen` + `IsFlexo`. LETTER/INDIGO/DIECUT → render silk template + warning chip. |
| **Q10 — PDF palette** | **Apply navy StyleConstants** PR-A. SpecPdfDocumentBuilder StyleConstants override: PrimaryColorHex `#1f3864`, MutedColorHex `#1e3a73`, HeaderBgHex `#dde6f3`, BorderColorHex `#b6c4dd`. |
| **Q11 — Backward compat** | Existing legacy quotes có `ColorSpecJson` shape silk row vẫn render đúng (binding mới đọc trực tiếp SpecPrintColor entity rows + fallback `ColorSpecJson` JSON parse nếu rows rỗng — defensive). |
| **Q12 — Migration** | **KHÔNG migration** PR-A/B/C. Future PR drawing/approval/template decide riêng. |

---

## 11. STOP — chờ duyệt

Em sẽ KHÔNG tạo branch / KHÔNG code cho đến khi anh:

1. **Confirm PR split** Q1 — 3 PR tuần tự A/B/C hay gộp A+B?
2. **Confirm color palette** §4.1 bảng "SpecHub hex → CCL-MES token" — OK
   chưa? Đặc biệt:
   - navy-primary = `#1f3864` (CCL-MES Login existing, KHÔNG dùng SpecHub `#0033a0`)
   - flexo cut/ink subsection: đồng nhất navy-light (Q4 a) vs giữ 3 variants (Q4 b)
   - column header bg `#F0F2F5` user mốc vs SpecHub `#dde6f3` — em đề xuất user mốc
3. **Confirm Q2 modal compact peek** — Option C2 mini-showcard + button
   navigate full-page OK? Hay anh muốn modal show full showcard?
4. **Confirm Q5 LETTER/INDIGO/DIECUT fallback** — silk template + warning OK?
   Hay defer hoàn toàn (KHÔNG render gì cho 3 category, error message)?
5. **Confirm Q11 backward-compat** — ColorSpecJson silk-shape vẫn render fallback?

Sau khi anh chốt, em:
- Tạo branch `feat/phase8-spec-showcard` (PR-A)
- Bug fix binding + CSS navy override silk + Squeegee legend + PDF
  StyleConstants
- V1-V13 verify (subset áp dụng cho PR-A)
- Mở PR-A riêng + STOP chờ duyệt
- Sau khi PR-A merged → PR-B → PR-C tuần tự

---

## 12. Files surveyed (transparency)

**Bug audit**:
- `src/CCL.MES.Web/Shared/SpecDetailModal.razor` (lines 117-152 UI + 360-388 binding)
- `src/CCL.MES.Application/SpecImport/SpecImportService.cs` (SerializeColorsForLegacy — JSON shape source)

**SpecHub READ-ONLY templates**:
- `spechub-prototype.html`:
  - `applySpecTemplate` HTML:10499-10505 (dispatch logic)
  - `renderSilkscreenSpec` HTML:10269-10497 (silk 8-section + 21-col 10-color table)
  - `renderFlexoSpec` HTML:10510-10770 (flexo 8-section + 3 sub-tables 12+14+10 cols)
  - CSS silk cream/tan HTML:5700-5800 (#ecddc9 / #f3ede0 / #5a4419 / #d0c8b2 / #d4cdb8 — TO BE REPLACED)
  - CSS flexo navy HTML:5880-5899 (#0033a0 / #1e3a73 / #dde6f3 / #e8eef7 / #b6c4dd — KEEP)
  - Flexo subsections HTML:5901-5903 (#cee0fa light blue / #ffeacc TAN / #d8f0d6 green)
  - PANTONE_SWATCHES HTML:10257-10261 (9 colors)
  - Squeegee/Dry codes HTML:11617-11629 (6 squeegee + 4 dry)

**CCL-MES current state**:
- `src/CCL.MES.Domain/Entities/Spec.cs` (SpecPrint + SpecPrintColor + Flexo entities)
- `src/CCL.MES.Application/SpecDetail/SpecDetailDto.cs` (PR #31d full graph DTO)
- `src/CCL.MES.Application/SpecDetail/SpecDetailColors.cs` (PANTONE port)
- `src/CCL.MES.Web/Pages/Npi/EngineerSpecDetail.razor` (PR #31d full-page showcard)
- `src/CCL.MES.Web/Shared/CreateSpecModal.razor` (PR #31a Create + text preview)
- `src/CCL.MES.Web/Shared/SpecDetailModal.razor` (PR #29 Get Info modal — bug)
- `src/CCL.MES.Web/wwwroot/css/site.css` (PR #31d showcard CSS — silk cream/tan to override)
- `src/CCL.MES.Infrastructure/SpecExport/SpecPdfDocumentBuilder.cs` (PR #31c/d PDF builder + StyleConstants)
- `src/CCL.MES.Web/wwwroot/css/site.css` line 5 + `login.css` (`#1f3864` IQC/Login navy existing)
