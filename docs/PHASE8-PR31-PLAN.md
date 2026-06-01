# PHASE 8 PR #31 — Create Spec + Planner Category + xlsx Import + Refresh Samples

> Khảo sát-only. KHÔNG code, KHÔNG branch. STOP sau plan, chờ duyệt + chốt Q.
>
> Pivot tiếp theo PR #30 list-parity (merged). Đây là chỗ data SpecHub THẬT
> chảy vào model `ProductRevision` qua xlsx import.
>
> Tham chiếu SpecHub READ-ONLY:
> - `parseXlsxSilkscreen` (HTML:11434-11645) — 600 LOC real parser
> - `parseXlsxFlexo` (HTML:11647-11848) — 200 LOC real parser
> - `parseXlsxToSpec` (HTML:11359-11379) — router + fallback logic
> - `openCreateOneCModal` (HTML:11865-11882) + modal UX (HTML:8263-8319)
> - `loadOneCSamples` (HTML:14245-14291) — refresh samples flow
> - `SPEC_CATEGORIES` (HTML:11312-11319) — 6 categories palette
> - Data files: `/Volumes/.../SpecHub/Data/Specs/` — 9 xlsx samples (~13KB each)

---

## 1. SpecHub Create modal — UX extract

### 1.1 Modal layout (HTML:8263-8319)

```
┌─ Create new NPI Spec — silkscreen NPI ─────────────────────────────────┐
│                                                                          │
│  1. Chọn Planner (Category) *                                            │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │ [SILK]  Silkscreen           [FLEXO]  Gallus / Brotech             │  │
│  │ [LETTER] Letterpress         [INDIGO] HP Indigo                    │  │
│  │ [DIECUT] Die cut / RDC / CNC                                       │  │
│  └────────────────────────────────────────────────────────────────────┘  │
│  ℹ Mỗi planner có template spec sheet riêng. App sẽ chọn parser khi import.│
│                                                                          │
│  2. Import từ file Excel/CSV                                             │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │  📄 Chọn file Excel (.xlsx) hoặc CSV                                │  │
│  │  Drop file vào đây hoặc click để chọn                              │  │
│  └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│  Preview parsed data           (hidden until file parsed)                │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │  REF NO:  CCL-Silk-19235                                            │  │
│  │  Customer: PANASONIC · Japan        Part No: AWW0146C98C0-WC0       │  │
│  │  Print rows: 9 colors detected                                      │  │
│  └────────────────────────────────────────────────────────────────────┘  │
│                                                                          │
│  ℹ Hệ thống tự nhận diện cell cố định: Customer R4C1, Part No R4C6,      │
│    Part Name R4C14, Material R4C24+, Print params R8, Print rows R13+.   │
│    Nếu file lệch layout, dùng "Manual entry".                           │
│                                                                          │
│  [Cancel] [Manual entry (empty form)] [Save imported spec] ←(primary)   │
└──────────────────────────────────────────────────────────────────────────┘
```

### 1.2 Flow operator

1. Mở `+ Create new spec` từ toolbar
2. Chọn planner category (default = SILK)
3. Hoặc:
   - **Đường 1**: chọn file xlsx → app parse → hiện preview với summary fields → click `Save imported spec` → create ProductRevision + sub-spec trong transaction
   - **Đường 2**: click `Manual entry` → tạo empty draft ProductRevision với category đã chọn, redirect sang Edit form (PR sau)
4. Nếu refNo trùng spec đã có: duplicate banner hiện 4 mode (replace/upgrade/copy/cancel) — SpecHub Phase 2 feature, em đề xuất defer PR #31 (chỉ làm "Save as new" - báo lỗi nếu trùng RefNo)

### 1.3 5 planner categories (HTML:11312-11319)

| Code | Label | Display | Màu |
|---|---|---|---|
| `silkscreen` | SILK | Silkscreen | #c8102e red |
| `flexo` | FLEXO | Gallus / Brotech | #0033a0 blue |
| `letterpress` | LETTER | Letterpress | #7c2d12 brown |
| `indigo` | INDIGO | HP Indigo | #00897b teal |
| `diecut` | DIECUT | Die cut / RDC / CNC | #9333ea purple |

**Quan trọng**: SpecHub mapping với `ProcessCatalog.Code` (PR #28 seed 17 codes):
- SILK ↔ SILKSCREEN
- FLEXO ↔ FLEXO
- LETTER ↔ LETTERPRESS
- INDIGO ↔ INDIGO / INDIGO_PRIMER
- DIECUT ↔ FLATBED_CUT / ROTARY_CUT / RDC / POWERPUNCH / CNC / LASER_CUT / KISS_CUT (operator chọn cụ thể trong Manual entry)

---

## 2. 5 parsers — TRẠNG THÁI THỰC TẾ SPECHUB

**Critical finding**: SpecHub chỉ có **2 parser thực sự** (`parseXlsxSilkscreen` + `parseXlsxFlexo`). 3 category còn lại **fallback** về silkscreen parser:

```js
// parseXlsxToSpec (HTML:11374-11379)
if (category === 'flexo')      return parseXlsxFlexo(aoa);
if (category === 'silkscreen') return parseXlsxSilkscreen(aoa);
// Default fallback — try silkscreen layout
console.warn(`No dedicated parser for category="${category}", trying silkscreen layout`);
return parseXlsxSilkscreen(aoa);
```

**Hệ quả cho PR #31**: chỉ port 2 parser SpecHub đã làm; 3 category còn lại (LETTER/INDIGO/DIECUT) → user chọn được trong modal NHƯNG file import sẽ dùng silkscreen layout (có warning). Khi user có file LETTER/INDIGO/DIECUT layout riêng → defer PR #32+ port parser khác.

### 2.1 Silkscreen parser — cell layout mapping (HTML:11434-11645)

| Field | Source (dynamic header scan) | Fallback fixed cell |
|---|---|---|
| **HEADER (R1-R5 scan)** | | |
| refNo | label "Số tham chiếu / Ref No" → next cell | `^CCL-(Silk\|Seal\|Flexo\|Indigo\|Letter)-` regex |
| inspectionLevel | regex `^Spec [A-Z]?\d+\w*$` | fallback `A` |
| approvalStamp | regex `^(approved\|draft\|in[\s_-]?review)$` | fallback `Approved` |
| compliance | regex `\brohs\b\|\bcompliance\b` | fallback `RoHS Compliance` |
| **PRODUCT INFO (find header "Khách hàng/Customer" → data row +1)** | | |
| customer | piMap.customer col | R4 C1 |
| partNo | piMap.partNo col | R4 C6 |
| partName | piMap.partName col | R4 C14 |
| materialType | piMap.materialType col | R4 C24 |
| materialSize | piMap.materialSize col | R4 C33 |
| laminationTape | piMap.laminationTape col | R4 C43 |
| laminationSize | piMap.laminationSize col | R4 C49 |
| laminationCavity | piMap.laminationCavity col | R4 C53 |
| **PRINT PARAMS (find header "Số lượng lần in/Printing cavity")** | | |
| printingCavity | ppMap.cavity col | R8 C1 |
| lengthPitch | ppMap.pitch col | R8 C6 |
| productSizeW | ppMap.sizeW col | R8 C14 |
| productSizeH | ppMap.sizeW col + 6 | R8 C20 |
| diameter | ppMap.diameter col | R8 C24 |
| **PRINT ROWS (find "Stt/No." header → data từ +3 rows, until empty/Ghi chú)** | | |
| Per row 20 cols: no/surface/color/inkName/inkCode/maker/retarder/visc/speed/squeegee/dry/temp/time/uv/emulsion/size/mesh/angle/plateCode/control/remark | prMap dynamic | R13+ với fallback cols 1-54 |
| **REMARKS / REVISIONS / SIGNATURES** | scan rows 5-60 | label-anchored |

### 2.2 Flexo parser — cell layout mapping (HTML:11647-11848)

| Field | Cell (FIXED, không scan dynamic) |
|---|---|
| **HEADER** dynamic giống silkscreen | R1-R5 scan |
| **PRODUCT INFO** | |
| customer | R4 C1 |
| partNo | R4 C4 |
| partName | R4 C10 |
| version | R4 C21 |
| productSizeW | R4 C25 |
| productSizeH | R4 C29 |
| diameter | R4 C32 |
| **PRINTING ROWS** (R6 header, R7+ data until "THÔNG TIN CẮT/Cutting") | |
| Per row 12 cols: process/material/thickness/size/cylinders/pitch/speed/tensionHead/tensionEnd/tensionRoll/plateCavity/tension | R7+ cols 1, 3, 6, 9, 13, 15, 17, 20, 23, 26, 29, 32 |
| **CUTTING ROWS** (sau "THÔNG TIN CẮT" +2) | |
| Per row 14 cols: process/lamination/size/cutterLot/cutterName/pcsPerSheet/cuttingCavity/pitch/packing/paperSpeed/cuttingSpeed/cuttingPressure/headTension/rollTension | cols 1, 3, 4, 6, 9, 13, 14, 16, 17, 19, 21, 24, 29, 33 |
| **INK ROWS** (sau "THÔNG TIN MỰC/Ink Information" +2) | |
| Per row 10 cols: no/color/inkCode/inkDesc/brand/anilox/plateCode/pressure/uvPower/irPower | cols 1, 2, 4, 7, 15, 17, 21, 24, 27, 31 |

### 2.3 LETTER / INDIGO / DIECUT layouts

**SpecHub KHÔNG có dedicated parser** — fallback `parseXlsxSilkscreen` với warning. **Em đề xuất**:

- **PR #31**: cho phép user chọn 5 categories trong modal NHƯNG runtime parser chỉ có SILK + FLEXO. Operator chọn LETTER/INDIGO/DIECUT → preview hiện warning "Using silkscreen layout as fallback — may misparse"
- **PR #32+**: port parser riêng nếu CCL Vietnam có file mẫu layout LETTER/INDIGO/DIECUT (cần file mẫu thực)

---

## 3. xlsx library decision — ClosedXML (CHỌN)

### 3.1 Yêu cầu

- Đọc `.xlsx` thuần (không cần Excel cài server)
- Không cần ghi xlsx ở PR #31 (chỉ read; Export ghi ở PR #32)
- Cross-platform (server Mac dev + Windows/Linux prod tiềm năng)
- Pure .NET, không native deps

### 3.2 So sánh

| Library | License | Size | Pros | Cons |
|---|---|---|---|---|
| **ClosedXML** | MIT | ~3 MB + 4 dep | Pure .NET, API thân thiện, mature (10K+ stars), read+write, no Excel installer needed | Phụ thuộc DocumentFormat.OpenXml |
| EPPlus | LGPL/Polyform v5+ | ~5 MB | Performance | License v5+ commercial restrictive — RISK |
| NPOI | Apache 2.0 | ~3 MB | Mature, Apache | API rườm rà, less idiomatic .NET |
| DocumentFormat.OpenXml (raw) | MIT | ~5 MB | Microsoft official | Low-level, lots of boilerplate |

**Em đề xuất ClosedXML 0.104.x (pin latest stable)** — best balance license + API + community + read-perf.

### 3.3 Dependencies graph (sau khi add)

```
CCL.MES.Infrastructure (ADD)
├── ClosedXML 0.104.x  (MIT)
│   ├── DocumentFormat.OpenXml 3.x  (MIT)
│   ├── ExcelNumberFormat 1.x  (MIT)
│   └── SixLabors.Fonts 2.x  (Apache 2.0)
```

Tổng package size ~12 MB (gzipped ~4 MB). Production server không cần Excel cài. Cross-platform 100% (verify: ClosedXML hoạt động trên macOS/Linux net8+).

### 3.4 Deployment impact

- Embedded DMG kiosk (giả sử có deploy Mac): +12 MB → không đáng kể
- Windows IIS prod: chuẩn .NET dep
- KHÔNG đụng SQLite/EF Core stack
- License OK cho commercial use (MIT trên đầu)

### 3.5 Alternative nếu anh KHÔNG muốn dep mới

- Operator tự convert xlsx → CSV trước import (lose cell positional → silkscreen layout không parse được do header rows merged); KHÔNG khuyến nghị
- DocumentFormat.OpenXml raw: vẫn dep mới + viết verbose 3-4x code
- Manual entry only (KHÔNG xlsx import) — quay về Phase 7 form basic; PR #31 mất giá trị

**Em chọn ClosedXML default**. Pin version + ghi vào LESSONS_LEARNED.md + HOW-TO-UPGRADE doc.

---

## 4. Schema gap — SpecPrintColor child entity?

### 4.1 SpecHub data shape (silkscreen, 9-color sample)

```json
{
  "printRows": [
    {"no":1, "surface":"R", "color":"WN-212", "inkName":"CCLISOL-1160", "inkCode":"HI1160",
     "maker":"CCL MIX", "retarder":"T980", "visc":16, "speed":0, "squeegee":"BS",
     "dry":"OVEN", "temp":60, "time":20, "uv":"", "emulsion":15, "size":"700×950",
     "mesh":"L120", "angle":22.5, "plateCode":"SP1620-1", "control":44, "remark":""},
    ...8 more rows
  ]
}
```

20 fields × 9-10 rows = ~180 values per spec. Plus flexo có 12 + 14 + 10 = 36 fields × 3 row types.

### 4.2 Option A — Fold all vào SpecPrint.ColorSpecJson (KHÔNG migration)

**Pro**: zero schema change, port nhanh, current model PR #28 đã support
**Con**:
- KHÔNG query được color/ink/plate cụ thể qua EF (phải parse JSON ở client/service)
- KHÔNG index được trên fields như `plateCode` cho tra cứu
- Forensic preservation OK; analytics limited

### 4.3 Option B — Tách `SpecPrintColor` child entity (MIGRATION)

```csharp
public class SpecPrintColor : BaseEntity
{
    public long SpecPrintId { get; set; }
    public SpecPrint? SpecPrint { get; set; }

    public int Seq { get; set; }                    // 1, 2, 3... (Print rows order)
    public string? Surface { get; set; }            // R / S
    public string? Color { get; set; }              // "WN-212", "PANTONE 186 C", "DENSE BLACK"
    public string? InkName { get; set; }            // "CCLISOL-1160"
    public string? InkCode { get; set; }            // "HI1160"
    public string? Maker { get; set; }              // "CCL MIX", "SEIKO"
    public string? Retarder { get; set; }
    public double? Viscosity { get; set; }
    public double? Speed { get; set; }
    public string? Squeegee { get; set; }
    public string? Dry { get; set; }                // "OVEN", "ND"
    public double? Temperature { get; set; }
    public int? Time { get; set; }                  // dry minutes
    public string? Uv { get; set; }
    public double? EmulsionThickness { get; set; }
    public string? PlateSize { get; set; }
    public string? Mesh { get; set; }
    public double? Angle { get; set; }
    public string? PlateCode { get; set; }
    public int? Control { get; set; }
    public string? Remark { get; set; }
    public string? ExtraJson { get; set; }          // future-proof per-process variance
}
```

Plus `SpecPrint.Colors` reverse nav `List<SpecPrintColor>`.

**Pro**:
- Query/filter/index được trên `PlateCode`, `Color`, `InkCode` (operator tra cứu mạnh)
- Type-safe column ops (sum/avg viscosity, count mesh patterns)
- Detail sheet PR #33 render trực tiếp từ entity (KHÔNG parse JSON)
- Mirror SpecHub data model semantic 1:1

**Con**:
- Migration A→B→C SAFE (ADD 1 bảng + FK SpecPrint, no data loss)
- ~30 fields nullable — bảng wide

### 4.4 Em đề xuất Option B — `SpecPrintColor` child entity

Lý do:
- PR #33 detail sheet sẽ render Print Process 10-color table; query qua entity sạch hơn parse JSON
- Future PR Export CSV/Excel (PR #32) cần query rows — entity native
- Q4 revisit (Part No vs Title) cần khả năng query semantic field cụ thể
- Migration thêm 1 bảng nullable — A→B→C SAFE, low-risk như #30

### 4.5 Bảng Flexo có 3 row-types (printing + cutting + ink)

3 child entity riêng? Hoặc 1 union entity với `RowKind` enum?

**Em đề xuất**: 3 child entities riêng (consistent với spec_diecut/spec_finishing pattern PR #28):
- `SpecPrintColor` — silkscreen + indigo print rows (color/ink/plate per color)
- `SpecFlexoPrintRow` — flexo printing rows (cylinder/pitch/tension)
- `SpecFlexoCuttingRow` — flexo cutting rows (cutter/lamination/cavity)
- (`SpecFlexoInkRow` có thể merge với SpecPrintColor nếu đủ overlap — verify mappings)

**Trade-off**: 3 bảng vs 1 generic — em chọn 3 bảng để giữ type safety + semantic.

**Migration scope PR #31**: ADD 3 bảng (SpecPrintColor + SpecFlexoCuttingRow + tùy chọn SpecFlexoInkRow) → A→B→C SAFE.

**Alt em cân nhắc**: KHÔNG add migration ở PR #31 — fold vào SpecPrint.ExtraJson; PR #33 migrate khi build detail sheet. Lý do: PR #31 đã nặng (xlsx parser); migration đẩy PR #33 đỡ rủi ro overload. Em đề xuất Q3 hỏi anh.

---

## 5. Import semantic

### 5.1 Pattern 3-step preview (NpiImportModal pattern Phase 7 hạng mục 1)

1. **Step 1 — Upload**: chọn xlsx
2. **Step 2 — Parse + Preview**: server parse, trả `ImportPreviewDto` (summary fields + warnings + dup detection)
3. **Step 3 — Confirm save**: operator click `Save` → server transaction tạo ProductRevision + 4 sibling + child rows (color/cutting/ink)

### 5.2 Always create new (default)

Default: 1 file = 1 spec mới (Draft, RevCode='A', ParentRevisionId=null). KHÔNG upsert.

**Nếu RefNo trùng spec đã có** (sau khi DB có data):
- Em đề xuất Q4: chỉ allow `Save as new` (auto-suffix RefNo với `-1`, `-2` etc.) + warning banner. Defer Replace/Upgrade flow sang PR lifecycle (đã plan).
- Alt: reject với error "RefNo `XX` đã tồn tại — đổi RefNo trong file hoặc dùng Manual entry"

### 5.3 Validation + skip report

Per row validation:
- Required: customer + partNo (gate Save button disabled nếu thiếu)
- Optional fields: missing → NULL, vẫn lưu
- Wrong layout: skip row + report "Row N skipped: <reason>"
- Preview hiển thị `N rows parsed, M skipped` chip

### 5.4 Audit emit

`SPEC_CREATE` với detail JSON enrich:
```json
{
  "spec_code": "<spec_code>",
  "ref_no": "<refNo>",
  "title": "<partName>",
  "product_id": <id>,
  "process_code": "SILKSCREEN",
  "source": "xlsx_import",
  "filename": "<original>.xlsx",
  "rows_parsed": 9,
  "rows_skipped": 0,
  "warnings": []
}
```

---

## 6. Manual entry flow

User click `Manual entry (empty form)` → modal close → redirect operator sang form Edit (PR lifecycle scope) HOẶC tạo Draft empty + reload list (Em đề xuất default).

**Default PR #31**: tạo Draft empty với:
- `ProductId = 0` (operator chọn sau trong Edit form)
- `SpecCode = "DRAFT-<timestamp>"` (placeholder)
- `Title = ""` (operator điền sau)
- `RefNo = NULL` / `InspectionLevel = NULL`
- `ProcessCode = silkscreen→SILKSCREEN | flexo→FLEXO | ...` (mapping từ planner category đã chọn)
- `RevisionCode = "A"`, `Status = Draft`

Sau khi tạo → grid refresh → operator double-click row → SpecDetailModal (chỉ xem, EDIT chưa có ở PR #31). Edit thực sự đẩy sang PR lifecycle (Edit modal scope).

**Trade-off**: tạo Draft rỗng không có form là semi-incomplete. Em cân nhắc 2 alt:
- Alt A: defer "Manual entry" hoàn toàn → PR lifecycle ship cùng Edit modal
- Alt B: PR #31 ship minimal Manual entry form (SpecCode + Product dropdown + Title fields) → reuse Phase 7 CreateSpecModal đã có

**Em đề xuất Alt B** — reuse Phase 7 form đơn giản; operator có entry point ngay; richer form (per-category 4-sub-tab) đẩy sau.

---

## 7. Sample loader — "Refresh samples"

### 7.1 Source files

SpecHub `loadOneCSamples` (HTML:14249-14256) dùng 6 files:

```js
const files = [
  ['AWW0146C98C0-WC0.xlsx',   'silkscreen'],   // Panasonic Panel Face B
  ['AWW0146C6FC0-0C5.xlsx',   'silkscreen'],   // Panasonic variant
  ['3205884802.xlsx',         'silkscreen'],   // anonymous Silk
  ['Silk_1000527330.xlsx',    'silkscreen'],   // Silk type 4
  ['G-EHB-HC-DISNEY.xlsx',    'flexo'],        // DELTA Flexo Disney
  ['080-0005-1618-ZE-NP.xlsx','flexo']         // ZE Flexo
];
```

`/Volumes/.../SpecHub/Data/Specs/` chứa **9 files** (4 silk + 2 flexo + 3 unused: `3P631278-1`, `GH68-55731L`, `Silk_3205877502`).

### 7.2 Bundle decision

| Option | Pros | Cons |
|---|---|---|
| **A. Bundle 6 files vào repo CCL-MES** (`CCL-MES/src/CCL.MES.Web/wwwroot/Data/Specs/`) | Reproducible, idempotent, no internet | +80 KB repo size, customer data baked in |
| B. Copy ad-hoc khi cần | Repo cleaner | Operator phải tự copy → fail user demo |
| C. Symlink sang SpecHub Data/ | No duplicate | Path-dependent, KHÔNG cross-platform |

**Em đề xuất A — bundle 6 files** (~80 KB tổng). Operator click `Refresh samples` → server đọc từ `wwwroot/Data/Specs/` → parse 6 files → upsert by RefNo (idempotent: skip nếu RefNo trùng + force=true để re-parse, default skip).

**License consideration**: 6 files customer-spec (PANASONIC / DELTA / DISNEY). Operator CCL Vietnam có quyền. Em đề xuất sanitize trước bundle: replace customer names → "DEMO_CUSTOMER_1/2", part numbers → "DEMO-PARTNO-AWW0146" để KHÔNG share customer data thật trong repo. Hỏi Q.

### 7.3 Idempotent semantic

- Default: skip RefNo đã có (don't double-create)
- `?force=1`: re-parse + UPDATE rev existing in-place (operator overrides for re-test)
- Audit emit `SPEC_REFRESH_SAMPLES` per cycle với count added/skipped/updated

---

## 8. Q4 revisit — Part No vs Part Name vs Title

SpecHub:
- `spec.code` = "AWW0146C98C0-WC0" (Part No = customer SKU)
- `spec.description` = "Panel Face B — automotive interior trim · Silkscreen 9-color" (Part Name = full descriptor)

CCL-MES post-PR #30:
- `Product.ProductCode` = render cột `Part No` (existing model field — đúng semantic)
- `Product.Name` = render cột `Part Name` (existing — đúng descriptor)
- `ProductRevision.Title` = render fallback cho `Part Name` nếu Product.Name rỗng

**Khi import SpecHub xlsx**:
- `partNo` từ xlsx (R4C6 silk, R4C4 flexo) → tạo/lookup `Product.ProductCode`
- `partName` từ xlsx (R4C14 silk, R4C10 flexo) → set `Product.Name` AND `ProductRevision.Title`
- `description` rich (SpecHub) chứa color count + process → store as Title (PR #31 enrich)

**Quyết định Q4 revisit**: KHÔNG cần tách field mới. Map:
- `xlsx.partNo` → `Product.ProductCode` (existing)
- `xlsx.partName` → `Product.Name` + `ProductRevision.Title` (parity ổn vì SpecHub Part Name ngắn — em verified spec sample shape)
- Mỗi xlsx import → tự động tạo/lookup Product nếu chưa có (Q hỏi)

---

## 9. RBAC

| Op | RBAC |
|---|---|
| Open Create modal | Admin, Engineer (mutation) |
| Upload xlsx + parse preview | Admin, Engineer |
| Save imported spec (create rev + sub-spec) | Admin, Engineer + transaction |
| Refresh samples | Admin only (re-parse 6 files bundled, audit emit) |
| Manual entry | Admin, Engineer |

NpiSpecRead role (Supervisor) chỉ xem grid, KHÔNG mở Create modal.

---

## 10. Đề xuất PR split — chia 31a/31b?

Estimate scope:
- Modal + planner picker + file upload UI: ~400 LOC
- SpecHub silk parser port → C#: ~700 LOC (silk có 20 col print rows + dynamic header scan + 12 helpers)
- SpecHub flexo parser port: ~400 LOC (3 row types fixed cell)
- ImportPreviewDto + service ParseAsync + SaveAsync transaction: ~300 LOC
- ClosedXML wiring + Workbook helpers: ~200 LOC
- Sample loader endpoint + bundle 6 files + idempotent gate: ~150 LOC
- Migration (Option B): SpecPrintColor + SpecFlexoCuttingRow + SpecFlexoInkRow entities + DbContext + 3 nullable tables: ~250 LOC
- i18n EN/VI ~40 keys: ~80 LOC
- CSS (modal overrides + planner badges đã có): ~30 LOC
- Tests (parser unit on bundled samples): ~300 LOC

**Total**: ~2,800 LOC + migration. Size **L** (so với PR #28 1,200 LOC + migration; PR #29 1,000 LOC; PR #30 750 LOC + migration).

### 10.1 Em đề xuất chia 2 PR

**PR #31a — Modal + Silkscreen parser + Sample loader + Manual entry**:
- Modal UX (planner picker + upload + preview + Save/Manual entry)
- ClosedXML dep add
- Silkscreen parser only
- Sample loader bundle 4 silk files (defer 2 flexo cho 31b)
- Manual entry minimal form (reuse Phase 7 CreateSpecModal)
- Migration: ADD `SpecPrintColor` entity + table (silkscreen child rows)
- ~1,800 LOC + migration
- Size M

**PR #31b — Flexo parser + 2 flexo entities + 2 flexo samples**:
- Flexo parser port
- Migration: ADD `SpecFlexoCuttingRow` + (`SpecFlexoInkRow` hoặc reuse SpecPrintColor — quyết tại PR #31b sau khi check data)
- Bundle 2 flexo samples
- ~1,000 LOC + migration
- Size S-M

**Trade-off**:
- Pros: PR #31a ship được giá trị (silk import works); operator có thể bắt đầu nhập specs ngay; PR #31b chậm hơn nhưng có nền sạch
- Cons: 2 PR + 2 migration = double ceremony

**Alt — gộp 1 PR #31** (~2,800 LOC):
- Ship trọn xlsx import 2 category
- Risk: PR đại lượng (gấp đôi #28); review tốn công + rollback khó nếu broken
- Migration vẫn chỉ 1 lần

**Em khuyến nghị chia 2 (31a + 31b)** — pattern Phase 7 NPI ban đầu cũng chia (Structure xong → Routine → RawMat → Spec → WC). Mỗi PR nhỏ dễ review + rollback.

---

## 11. Q1..Qn — chốt semantics

| Q | Default em đề xuất |
|---|---|
| **Q1 — Chia PR** | Chia 2: **PR #31a** (modal + silk parser + samples + migration SpecPrintColor) + **PR #31b** (flexo parser + 2-3 flexo entities + flexo samples) |
| **Q2 — xlsx library** | ClosedXML 0.104.x (MIT), pin version, ghi LESSONS_LEARNED + HOW-TO doc. Alt-reject NPOI vì API rườm. |
| **Q3 — Migration timing** | PR #31a: ADD `SpecPrintColor` table. PR #31b: ADD `SpecFlexoCuttingRow` + `SpecFlexoInkRow` (hoặc reuse). Both A→B→C SAFE. **Alt: defer migration sang PR #33 detail sheet, PR #31 fold vào ExtraJson** — em KHÔNG khuyến nghị vì delay query/search benefit + Export PR #32 phải parse JSON. |
| **Q4 — Import semantic** | Always create new (Draft, RevCode=A). Duplicate RefNo → reject với error message. Replace/Upgrade flow đẩy PR lifecycle. |
| **Q5 — Manual entry** | Reuse Phase 7 `CreateSpecModal` đơn giản (SpecCode + Product dropdown + Title) + auto-set ProcessCode theo planner category đã chọn. |
| **Q6 — Sample bundle** | Bundle 6 files (4 silk + 2 flexo) vào `CCL-MES/src/CCL.MES.Web/wwwroot/Data/Specs/`. **Sanitize**: replace customer names (`PANASONIC` → `DEMO_CUSTOMER_1`, etc.) trước commit để KHÔNG leak data. |
| **Q7 — Refresh samples idempotent** | Default skip RefNo trùng; `?force=1` re-parse + UPDATE in-place. Admin role only. Audit `SPEC_REFRESH_SAMPLES`. |
| **Q8 — Auto-create Product khi import** | Yes — nếu `xlsx.partNo` không match Product.ProductCode existing → create Product mới với `ProductCode=partNo` + `Name=partName` + `Customer=null` (operator gán sau). |
| **Q9 — Validation gate** | Required: customer (xlsx) + partNo (xlsx) phải có giá trị → Save enabled. Wrong layout: skip row + warning chip. Operator có thể Save dù có warnings. |
| **Q10 — Q4 revisit (Part No/Name/Title)** | KHÔNG tách field mới. Map `partNo`→`Product.ProductCode`, `partName`→`Product.Name` + `ProductRevision.Title` (mirror). |
| **Q11 — LETTER/INDIGO/DIECUT parsers** | PR #31a/b: KHÔNG port. Operator chọn được trong modal nhưng runtime fall-back silkscreen parser (giống SpecHub) với warning. Port riêng khi có file mẫu thực tế → PR #32+. |
| **Q12 — Preview shape** | DTO `SpecImportPreviewDto`: summary (refNo, customer, partNo, partName, productSize, numColors) + warnings[] + skippedRows[]. Modal hiển thị summary + warnings chip. |
| **Q13 — Transaction scope** | Save → `db.Database.BeginTransactionAsync()` quanh: create Product (nếu mới) → ProductRevision → SpecMaterial/Print/Diecut/Finishing → SpecPrintColor rows → audit emit → commit. Rollback nếu bất kỳ step fail. |
| **Q14 — File size limit** | 5 MB max per xlsx (sample SpecHub ~13 KB, 5 MB ~ 300×scale). Limit qua middleware. Reject với error nếu vượt. |
| **Q15 — Modal lifecycle** | Modal nhận `category` param + reset state khi close. Auto-detect category từ xlsx header nhưng cho operator override. |
| **Q16 — Audit detail JSON** | Per save: `{spec_code, ref_no, title, product_id, process_code, source: "xlsx_import", filename, rows_parsed, rows_skipped, warnings}`. Sanitize, ≤4KB. |

---

## 12. Hard constraints

- ❌ Bài học #27: KHÔNG re-fetch by id cho preview — pass parsed DTO trực tiếp từ server response → modal binding immediate.
- ❌ Migration A→B→C SAFE: backup + SHA256 + `/tmp/spec-import-design.db` test; provider-agnostic (ADD COLUMN nullable + ADD TABLE chuẩn cả SQLite + SQL Server). KHÔNG raw SQL guard cần.
- ❌ ClosedXML dep MUST pin version trong .csproj; ghi vào HOW-TO-UPGRADE doc + LESSONS_LEARNED.
- ❌ Preview phải hiển thị TRƯỚC khi ghi DB (operator confirm). Reject silent-write.
- ❌ Sanitize sample files trước bundle (KHÔNG leak customer data thật).
- ❌ try-catch wrap MỌI async handler + error banner inline (KHÔNG freeze circuit).
- ❌ i18n EN/VI cho mọi nhãn/warning/error.
- ❌ RBAC: mutation Admin/Engineer; Refresh samples Admin only.
- ❌ SpecHub READ-ONLY tuyệt đối; KHÔNG đụng Ops Control v1.2 / CMES sibling / Old ver / Machine / ProductionLog / 4 NPI tab khác / IQC.

---

## 13. Verify gates (post-implementation)

| # | Check | Method |
|---|---|---|
| V1 | dotnet build clean | 0 errors/warnings |
| V2 | Migration A→B→C SAFE: backup SHA256 + /tmp isolated + row counts unchanged + new tables empty | A→B→C |
| V3 | ClosedXML dep added + version pinned | grep -nE "ClosedXML" *.csproj |
| V4 | Silk parser parse 4 bundled samples → 4 ProductRevision + N SpecPrintColor rows | sqlite query post-Refresh samples |
| V5 | Flexo parser (#31b) parse 2 bundled samples → 2 ProductRevision + N flexo rows | sqlite |
| V6 | Manual entry → tạo Draft với category đúng + ProcessCode đúng | Manual UI + sqlite |
| V7 | Duplicate RefNo reject với error message | Manual UI: import 2 file cùng RefNo |
| V8 | Validation: skip wrong layout rows + report skip | Manual UI: import file thiếu R4 layout |
| V9 | Transaction rollback: nếu SaveChanges fail → no partial data | Inject fault: invalid Product FK |
| V10 | Refresh samples idempotent: re-run skip dup | 2 cycles + audit query |
| V11 | Audit emit `SPEC_CREATE` + `SPEC_REFRESH_SAMPLES` với detail JSON đúng | sqlite audit_log |
| V12 | Vùng cấm intact | git diff scope |
| V13 | Restart no-op | Boot 2 lần, counts + samples unchanged |

---

## 14. Out of scope (defer)

- LETTER / INDIGO / DIECUT dedicated parsers (PR #32+ khi có file mẫu thật)
- Export CSV / Excel / Print PDF → PR #32
- Detail sheet full layout (10-color table render + Approval Signatures 4-role) → PR #33
- Lifecycle ops (Revise / Copy / Edit / Trash / Restore / Supersede / Purge) → PR sau (đã plan)
- Stats panel sidebar → Phase 9
- Drawing upload + 3-role approval → PR sau (Phase 8 đã có schema Drawing tables từ #28)

---

## 15. STOP — chờ duyệt

Em sẽ KHÔNG tạo branch / KHÔNG code cho đến khi anh:
1. Duyệt scope tổng + chốt PR split (Q1 — 2 PR #31a/b vs gộp 1 PR #31)
2. Chốt Q2-Q16 (hoặc accept default)
3. Confirm ClosedXML dep + license OK
4. Confirm migration scope (Q3 — PR #31a tạo SpecPrintColor; PR #31b tạo 2 flexo entities)
5. Confirm sample bundle + sanitize policy

Sau khi chốt, em sẽ tạo branch `feat/phase8-spec-create-silk` (nếu chia 2) hoặc `feat/phase8-spec-create-xlsx-import` (nếu gộp), code theo plan, A→B→C SAFE migration, verify đầy đủ V1-V13, commit + PR + STOP.
