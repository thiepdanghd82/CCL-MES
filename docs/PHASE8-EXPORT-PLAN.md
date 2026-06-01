# PHASE 8 EXPORT PLAN — CSV / Excel / PDF cho NPI Spec Library

> Plan ngắn — nguồn data đã có (PR #31a+b). 3 nút "Export CSV / Export Excel /
> Print PDF" trong EngineerSpec toolbar (đã ẨN từ PR #30; PR này flip lại).
> STOP nếu PDF cần dep mới — chờ anh chốt trước khi code.

---

## 1. Reference SpecHub (READ-ONLY)

| Button | Mô tả SpecHub |
|---|---|
| Export CSV | `spechub-prototype.html` — list-view CSV với 14 cột grid (Planner/RefNo/Customer/PartNo/PartName/Colors/Cavity/Pitch/Spec/Status/Rev/RevDate/By) |
| Export Excel | Tương tự CSV nhưng xlsx với column types + auto-filter |
| Print PDF | SpecHub có cả 2 mode: list export + single-spec detail sheet (4 sub-section). Em đề xuất mặc định LIST cho PR này, single-spec đẩy PR #33 detail sheet. |

---

## 2. Scope EXPORT

### 2.1 Export CSV — list view (default: full filter/search applied)

- Endpoint: `GET /npi/engineer-spec/export.csv?search=<x>` (Blazor SSR file download)
- Data: chạy lại `SpecSvc.SpecsAsync(search, page=1, pageSize=int.MaxValue)` để lấy TẤT CẢ rows match filter (không paginate)
- Format: RFC 4180-compliant; BOM UTF-8 cho Excel Vietnam locale (bài học Ops Control v1.2 Sprint S-D15-COSTING-CUTOVER PR #90)
- Columns mặc định = 13 cột visible (skip cột # = row index)
- Filename: `NpiSpecLibrary_<yyyyMMdd-HHmmss>.csv`
- KHÔNG cần dep mới — `System.Text.Encoding` + `StringBuilder` đủ; helper `CsvEscape` rule:
  - Wrap `"` if value contains `,` / `"` / `\n` / `\r`
  - Double embedded `"`
- Empty result → empty CSV với chỉ header row (KHÔNG error)

### 2.2 Export Excel — list view

- Endpoint: `GET /npi/engineer-spec/export.xlsx?search=<x>`
- Reuse **ClosedXML 0.104.2** đã pin PR #31a — KHÔNG dep mới
- Features:
  - Header row bold + freeze-pane row 1
  - Auto-filter on column range
  - Per-column type formatting (text / int / decimal-1 / yyyy-MM-dd)
  - Column width auto-fit
  - Sheet name = "NPI Spec Library"
  - Filename `NpiSpecLibrary_<yyyyMMdd-HHmmss>.xlsx`
- Single sheet, 14 cols + N data rows (= filtered rows count, no paginate)

### 2.3 Print PDF — DECISION NEEDED

**Hai option, mỗi option có trade-off khác nhau. STOP — chờ anh chọn:**

#### Option A — Browser-side print (NO server dep)

- Endpoint: `GET /npi/engineer-spec/print` — Razor page render print-friendly HTML
- CSS `@media print` reformat list view (A4 landscape, compact rows, no toolbar/sidebar)
- Operator clicks "Print PDF" → opens new window with print page → auto-trigger `window.print()` → browser shows native Print dialog → operator "Save as PDF"
- **Pros**: 0 new dep, leverage browser PDF engine (chrome/safari/edge), per-browser font + locale autocorrect, no server CPU
- **Cons**: Workflow 2-step (button → Save dialog), không 1-click download; printable area depends on browser (different margins per OS); embedded watermark/header repeat khó hơn

#### Option B — Server-side PDF gen (NEW dep needed)

**Em đề xuất QuestPDF 2024.x** (license đánh giá kỹ trước commit):

| Lib | License | Size | Lý do em đề xuất / loại |
|---|---|---|---|
| **QuestPDF** | Community License (free for revenue ≤ $1M USD/yr); Professional commercial above | ~5 MB | Modern fluent API; pure .NET; popular cho new projects; CCL Vietnam revenue chắc dưới threshold nhưng cần xác nhận business |
| **PdfSharp + MigraDoc** | MIT | ~3 MB | Safe license, mature; API verbose hơn QuestPDF ~2-3× LOC nhưng KHÔNG có revenue gate |
| iText 7+ | AGPL or commercial $$$ | ~10 MB | License loại — AGPL không phù hợp commercial |
| DinkToPdf (wkhtmltopdf) | native binaries | ~30 MB | Native dep cross-platform footgun — pattern bị burn Phase 6 (bài học Ops Control Lesson 28). Loại. |

**Em sẽ KHÔNG tự ý add dep**. Anh chốt 1 trong 3:
- **A** — Browser-side print, KHÔNG cần dep mới (em đề xuất nếu UX 2-step chấp nhận được)
- **B-Quest** — QuestPDF Community License (free dưới revenue ngưỡng, cần CCL business xác nhận)
- **B-PdfSharp** — PdfSharp + MigraDoc MIT (safer license, viết verbose hơn)

### 2.4 PDF content layout (nếu Option B)

Default: **list view 14 cols** (cùng dữ liệu CSV/Excel) — operator chia rõ trang ngang A4, header row repeat trên mỗi trang, page number footer. Đề xuất single-spec detail sheet đẩy PR #33 (detail sheet vẫn chưa build).

---

## 3. RBAC

- **Read = Export** (em đề xuất default): policy `NpiSpecRead` đã có (Admin/Engineer/Supervisor). Operator nào xem được list thì export được — read-only operation.
- Alt: Admin/Engineer only. Phổ biến hơn nhưng quá hẹp cho data analytical scope.
- **Em chọn `NpiSpecRead`** — pattern Ops Control v1.2 csvExport (PR #90) cho phép read-user export.

---

## 4. UI toolbar — flip 3 buttons từ ẩn

PR #30 đã có 3 nút ẩn (comment trong code). Flip lại với pattern SpecHub `rt-btn-toolbar`:

```html
<button class="rt-btn" @onclick="ExportCsvAsync" disabled="@_busyExport">📄 Export CSV</button>
<button class="rt-btn" @onclick="ExportXlsxAsync" disabled="@_busyExport">📊 Export Excel</button>
<button class="rt-btn" @onclick="PrintPdfAsync" disabled="@_busyExport">🖨 Print PDF</button>
```

Wire `_busyExport` flag với try-catch + error banner inline. Confirm dialog optional (em đề xuất không — file download là idempotent, không destructive).

---

## 5. i18n EN/VN

~12 keys mới:

| Key | EN | VN |
|---|---|---|
| `npi.spec.btn_export_csv` | Export CSV | Xuất CSV |
| `npi.spec.btn_export_xlsx` | Export Excel | Xuất Excel |
| `npi.spec.btn_print_pdf` | Print PDF | In PDF |
| `npi.spec.export_count` | Exporting {0} rows… | Đang xuất {0} dòng… |
| `npi.spec.export_ok` | Exported {0} rows to {1} | Đã xuất {0} dòng → {1} |
| `npi.spec.export_err` | Export failed | Xuất thất bại |
| `npi.spec.export_empty` | No data to export (filter returns 0 rows) | Không có dữ liệu (filter trả 0 dòng) |

---

## 6. Hard constraints

- ❌ KHÔNG migration (chỉ read-only export)
- ❌ KHÔNG đụng IQC / Machine / ProductionLog / RawMaterial / Routing / Manufacturing / WC / WI
- ❌ KHÔNG đụng Ops Control v1.2 / SpecHub READ-ONLY / CMES "Old ver"
- ❌ Bảo toàn baseline data 100% (chỉ read; no write)
- ❌ Try-catch wrap mọi async handler + error banner inline (bài học #27)
- ❌ Reuse ClosedXML 0.104.2 cho Excel — KHÔNG add new xlsx lib
- ❌ PDF dep: STOP chờ anh chốt Option A vs B-Quest vs B-PdfSharp TRƯỚC khi code
- ❌ Filename collision-safe (timestamp suffix)
- ❌ CSV BOM UTF-8 cho Excel Vietnam locale (bài học OPS PR #90)

---

## 7. Verify gates (post-code)

| # | Check | Method |
|---|---|---|
| V1 | dotnet build clean (0 warnings/errors) | |
| V2 | Empty filter → CSV/xlsx có header + 0 data rows | Manual UI |
| V3 | Filter "DEMO_CUSTOMER" → CSV/xlsx chỉ 6 rows match | Manual UI |
| V4 | No filter → CSV/xlsx có tất cả 7 rows (Brady + 6 demo) | Manual UI |
| V5 | UTF-8 BOM verified bằng `head -c 3 file.csv \| xxd` (= `efbbbf`) | CLI |
| V6 | Excel cell types: NumColors integer, PitchMm decimal, LastUpdatedAt date | Open in Excel |
| V7 | RBAC: anonymous → 401/302; Engineer → OK; Supervisor (NpiSpecRead) → OK | Manual curl |
| V8 | Vùng cấm intact | git diff scope |
| V9 | Restart no-op (file download không persist DB state) | Boot 2 lần |
| V10 (PDF Option B) | PDF page break + header repeat đúng cho >50 rows | Manual UI |

---

## 8. PR plan

- Branch: `feat/phase8-spec-export` (base main, post-PR #31a/b)
- LOC estimate:
  - CSV endpoint + helper: ~80 LOC
  - Excel endpoint + ClosedXML builder: ~150 LOC
  - PDF endpoint (Option A — Razor print page + @media print CSS): ~120 LOC
  - PDF endpoint (Option B — QuestPDF/PdfSharp): ~200 LOC
  - EngineerSpec toolbar wire + handlers: ~80 LOC
  - i18n (12 keys EN+VN): ~30 LOC
  - **Total**: ~460 LOC (Option A) hoặc ~540 LOC (Option B)
- Size **M** (so với PR #31a 5000 LOC do parser + samples; export nhẹ hơn)

---

## 9. STOP — chờ anh chốt

Em sẽ KHÔNG tạo branch / KHÔNG code cho đến khi anh:

1. **Chốt PDF Option**: A (browser-side, no dep) vs B-Quest (QuestPDF Community License) vs B-PdfSharp (MIT, more verbose). Riêng B-Quest cần xác nhận CCL Vietnam revenue dưới $1M USD/yr threshold.
2. **Confirm RBAC = NpiSpecRead** (Admin/Engineer/Supervisor) — hay scope hẹp Admin/Engineer?
3. **Confirm PDF content default = list view 14 cols** (single-spec detail sheet defer PR #33)
4. **Confirm UTF-8 BOM cho CSV** (Excel Vietnam locale fix bài học OPS PR #90)

Sau khi anh chốt, em sẽ tạo branch + code theo plan + V1-V10 verify + PR + STOP chờ duyệt.
