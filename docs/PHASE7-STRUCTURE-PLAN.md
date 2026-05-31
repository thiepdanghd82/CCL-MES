# Phase 7 — Hạng mục 1: Engineer Structure khảo sát + phương án

> **Mục tiêu**: làm Engineer Structure tab của CCL-CMES bằng đầy đủ tính năng + 16 cột của tab tương đương trong dự án CMES (sibling, READ-ONLY reference). Đây là PR khảo sát; KHÔNG code, KHÔNG commit gì trừ file này (untracked).
>
> Output sau khi anh chốt: 1 PR riêng cho Engineer Structure. Sau khi merge, lặp lại quy trình tương tự cho Engineer Routine → Raw Materials → Spec Master → Machine List.

---

## 1. Nguồn tham khảo (READ-ONLY)

CMES tại `3. PROJECTS/CMES/` (stack: TypeScript + NestJS + React + Postgres/SQLite). Đã đọc các file sau (KHÔNG sửa, KHÔNG chạy, KHÔNG commit):

- `apps/web/src/modules/engineer-structure/EngineerStructure.tsx` — UI (16 cột + Columns toggle + freeze + Import + Clear)
- `apps/server/src/modules/engineer-structure/engineer-structure.service.ts` — CSV header aliases + list query + import logic
- `apps/server/src/modules/engineer-structure/engineer-structure.controller.ts` — REST endpoints + RBAC `RequireTabAccess('spec-master', 'read'|'edit')`
- `db/migrations/sqlite/007_engineer_structure.sql` — 16 cột schema + 4 index
- `apps/web/src/styles/index.css` §1570–1760 — `.rt-page` / `.rt-toolbar` / `.rt-table-wrap` / `.rt-table thead th { position: sticky; top: 0 }` etc.

---

## 2. CCL-CMES hiện tại

- `src/CCL.MES.Web/Pages/Npi/EngineerStructure.razor` (62 LOC) — **10 cột**, không freeze header, không Columns toggle, không Import/Clear.
- `src/CCL.MES.Application/Services/NpiService.cs:63-75` — `StructuresAsync(search, page, pageSize)` paginated qua `PagingHelper.PageAsync`, search trên 4 field (ParentPart / ParentDescription / ComponentPart / ComponentDescription).
- `src/CCL.MES.Domain/Entities/Npi.cs:43-57` — entity `ManufacturingStructure` có **11 field** (10 displayed + ScrapFactor không hiển thị).
- `tools/import_npi.py:173-232` — import từ IFS CSV `ManufacturingStructures 260525-65635.csv` (31 cột), hiện chỉ map 10 + 1 (UOM cột 30).

---

## 3. Bảng đối chiếu 3 chiều (CMES UI ↔ CCL-CMES entity ↔ IFS CSV)

| # | CMES UI label | CMES field key | IFS CSV header (1-based idx) | CCL-CMES entity field | Trạng thái CCL-CMES |
|---|---|---|---|---|---|
| 1 | Parent Part | `parent_part` | "Parent Part No" (1) | `ParentPart` (string) | ✅ Có |
| 2 | Parent Description | `parent_desc` | "Parent Part Description" (2) | `ParentDescription` (string?) | ✅ Có |
| 3 | Component Part | `component_part` | "Component Part" (3) | `ComponentPart` (string) | ✅ Có |
| 4 | Component Description | `component_desc` | "Component Part Description" (4) | `ComponentDescription` (string?) | ✅ Có |
| 5 | Qty/Assembly | `qty_per_assembly` (num, 6 digits) | "Qty Per Assembly" (6) | `QtyAssembly` (double) | ✅ Có (number) |
| 6 | UOM | `uom` | "UOM" (30) | `Uom` (string?) | ✅ Có |
| 7 | Scrap | `scrap` (num, 2 digits) | "Component Scrap" (7) | `ScrapFactor` (double) | ✅ Có (đổi label hiển thị) |
| 8 | Scrap % | `scrap_pct` (pct fmt) | "Scrap Factor (%)" (8) | `ScrapPct` (string? — carries "%") | ⚠ Type khác — entity string, CMES number |
| 9 | Pitch | `pitch` (num, 2 digits) | "Pitch" (9) | `Pitch` (string?) | ⚠ Type khác — entity string, CMES number |
| 10 | Cavity | `cavity` (num, 0 digits) | "Cavity" (10) | `Cavity` (string?) | ⚠ Type khác — entity string, CMES number |
| 11 | Colors | `colors` | "Color Nums" (11) | `Color` (string?) | ✅ Có (rename hiển thị Color → Colors) |
| 12 | Structure Type | `structure_type` | "Structure Type" (22) | ❌ THIẾU | Cần bổ sung entity field |
| 13 | Alt | `alt` | "Alternative No" (16) | ❌ THIẾU | Cần bổ sung |
| 14 | Effectivity | `effectivity` | "Structure Effectivity" (15) | ❌ THIẾU | Cần bổ sung |
| 15 | Date | `effectivity_date` | "Phase In" (12) | ❌ THIẾU | Cần bổ sung |
| 16 | Planner | `planner` | "Planner" (29) | ❌ THIẾU | Cần bổ sung |

**Tóm tắt**:
- 11/16 cột **đã có** trong entity → hiển thị được ngay
- 5/16 cột **thiếu trong entity** (Structure Type / Alt / Effectivity / Date / Planner) → cần thêm field + migration v5 + re-import
- 3 cột Pitch / Cavity / ScrapPct hiện là `string?` nhưng CMES treat as `number` → cần đổi sang `double?` nếu muốn format đúng (`fmtNum(2)` / `fmtNum(0)` / `fmtPct`)

---

## 4. CMES UI pattern — chi tiết để bê sang Blazor

### 4.1 Layout

```html
<section class="rt-page">
  <div class="rt-breadcrumb">DATABASE / <strong>PRODUCT STRUCTURE</strong> · {N} rows loaded</div>
  <div class="rt-toolbar">
    <h2 class="rt-title">🏠 Manufacturing Structures</h2>
    <span class="rt-count-chip">{filteredOrTotal} rows</span>
    <input class="rt-search" placeholder="Search…" />
    <button class="rt-btn">☰ Columns ({visible}/{total})</button>
    <button class="rt-btn rt-btn-primary">⬆ Import…</button>   <!-- canEdit only -->
    <button class="rt-btn rt-btn-danger">🗑 Clear Data</button>  <!-- canEdit only -->
  </div>
  <div class="rt-table-wrap">
    <table class="rt-table">
      <thead><tr><th>#</th>...16 th sticky...</tr></thead>
      <tbody>{rows.map(r => ...)}</tbody>
    </table>
    <div class="rt-footer-warn">⚠ Showing first 500 of {N} rows. Use Search to narrow down.</div>
  </div>
</section>
```

### 4.2 Freeze header

CSS đơn giản (`apps/web/src/styles/index.css:1733-1744`):

```css
.rt-table-wrap { overflow-x: auto; }  /* table-wrap = scroll container */
.rt-table thead th {
  background: #f7f8fa;
  position: sticky;
  top: 0;
  z-index: 5;
}
```

`.rt-table-wrap` cần **`max-height`** (CMES không set explicit — chắc dùng grid layout của container ngoài). Sang Blazor có thể set `max-height: calc(100vh - 220px)` hoặc tương tự để table fit khung.

### 4.3 Columns toggle (show/hide)

```javascript
// persist localStorage key: 'cmes.engineer-structure.columns-hidden.v1'
const [hidden, setHidden] = useState<Set<string>>(...);   // load from localStorage
useEffect(() => localStorage.setItem(KEY, JSON.stringify([...hidden])), [hidden]);
const visibleColumns = useMemo(() => COLUMNS.filter(c => !hidden.has(c.key)), [hidden]);
```

UI: dropdown 16 checkbox (mỗi cột) + "Show all" link.

### 4.4 Format

```javascript
const fmtNum = (digits) => (v) => v==null||v===''? '—' : Number(v).toFixed(digits);
const fmtPct = (v) => v==null||v===''? '—' : `${Number(v)}%`;
// Mặc định: raw==null? '—' : String(raw)
```

- Qty/Assembly: 6 digits (`fmtNum(6)`)
- Scrap: 2 digits
- Scrap %: append "%"
- Pitch: 2 digits
- Cavity: 0 digits (integer)
- Còn lại: string, "—" nếu null/empty

### 4.5 Search

- Debounced 300ms (`useEffect` setTimeout)
- Backend SQL `LIKE`: 6 cột (parent_part / parent_desc / component_part / component_desc / planner / structure_type)
- CCL-CMES hiện chỉ search 4 field — cần thêm 2 field nữa (planner + structure_type) khi đã bổ sung entity

### 4.6 Paging

CMES: **không paging** truyền thống — `limit=500&offset=0`, kèm footer warn nếu `rows.length < filtered_total`. CCL-CMES hiện dùng `Pager` component với `pageSize=50`. **Phương án thảo luận**: giữ `Pager` (CCL-CMES style) hay đổi sang scroll + footer warn (CMES style)?

### 4.7 Title + count

- Title hardcoded: `🏠 Manufacturing Structures`
- Chip:
  - `{total.toLocaleString()} rows` mặc định
  - `{filtered.toLocaleString()} / {total.toLocaleString()} rows` khi đang search

---

## 5. Phương án triển khai

### 5.A — Option "đầy đủ 16 cột + entity migration" (recommended)

**Scope**:
1. **Entity migration v5**: thêm 5 field mới vào `ManufacturingStructure` + đổi 3 field string→`double?` (Pitch / Cavity / ScrapPct).
2. **Re-import**: cập nhật `tools/import_npi.py:read_structures()` đọc cột 12, 15, 16, 22, 29 (Phase In / Structure Effectivity / Alternative No / Structure Type / Planner) + parse Pitch/Cavity sang double.
3. **`Pages/Npi/EngineerStructure.razor`**: rewrite — 16 cột + Columns toggle (localStorage persist via JSInterop) + freeze header CSS + count chip + breadcrumb.
4. **i18n**: ~30 key mới (16 column labels EN+VI + toolbar buttons + footer warn) thay cho hardcoded English.
5. **NpiService.StructuresAsync**: search thêm Planner + StructureType.
6. **RBAC**: giữ `[Authorize(Policy="NpiRead")]` page-level + `<AuthorizeView Roles="Admin,Supervisor,Engineer">` quanh Import/Clear button (chỉ Engineer trở lên mới edit).
7. **CSS**: thêm 1 block `.npi-grid-rt-*` (~80 LOC) hoặc reuse `.rt-*` namespace tuỳ chọn naming.

**Pros**:
- Khớp UI/UX với CMES 100%
- Dữ liệu hữu ích thật (Structure Type / Effectivity / Planner) — operator dùng được thay vì 10 cột rút gọn
- Migration A→B→C SAFE (CLAUDE.md §4.4) — backup tường minh trước

**Cons**:
- Cần migration v5 + re-import 20 530 rows (~30s)
- Risk migrate fail làm corrupt data → mitigation = backup SHA256 + Phase A→B→C
- Pitch/Cavity hiện đã có data string, đổi sang double có thể mất rows non-numeric (rare nhưng cần verify)

**LOC ước lượng**:
- Entity v5 + migration: ~80 LOC
- Razor + i18n: ~150 LOC (page) + 30 key × 2 locale = ~60 LOC resx
- NpiService search expand: ~3 LOC
- CSS sticky + toolbar: ~80 LOC
- import_npi.py: ~30 LOC
- **Total ~400 LOC + 2 test (snapshot row count + smoke admin → 200)**

**Rủi ro**:
- Pitch/Cavity string→double migration: nếu CSV có giá trị non-numeric, `num()` Python coerce về 0.0 → có thể mất ý nghĩa. **Mitigation**: dump 5 rows mẫu để xác nhận format raw từ IFS.
- Re-import overwrite operator-edited data (nếu sau Bước 7 có thêm data tay). **Mitigation**: backup SHA256 trước + verify post-import row count = 20 530 không đổi.

---

### 5.B — Option "chỉ UI/UX, KHÔNG migrate entity" (lite)

**Scope**:
1. Razor UI rewrite với **11 cột hiện có** (10 + thêm ScrapFactor) + freeze header + Columns toggle + count chip + breadcrumb.
2. 5 cột Structure Type / Alt / Effectivity / Date / Planner **bỏ qua** hoặc render placeholder "(not imported)".
3. Pitch/Cavity giữ string, không reformat.
4. Không đụng entity / migration / import.

**Pros**:
- Risk thấp nhất, không đụng DB schema
- LOC ~200, deliver nhanh

**Cons**:
- KHÔNG khớp với CMES — operator vẫn thiếu Planner / Structure Type / Effectivity
- Phải làm Option A một ngày khác nếu muốn full parity

---

### 5.C — Option "shared grid component" (DRY long-term)

Tạo `Shared/NpiDataGrid.razor` generic component (giống `Shared/QcInspectionGrid.razor` Phase 6 Bước 3) nhận:
- `T` row type
- `IEnumerable<ColumnDef<T>>` columns
- `Func<T, string>` render value
- `Title` / `BreadcrumbPath` / `Toolbar` slot
- Built-in freeze header + Columns toggle + count chip + search debounce

Sau đó EngineerStructure (Bước 7-1), EngineerRoutine (7-2), RawMaterials (7-3), Spec (7-4), WorkCenter (7-5) đều dùng chung.

**Pros**:
- 1 nơi sửa, 5 nơi hưởng — chuẩn DRY
- Phù hợp với pattern Phase 6 (QcInspectionGrid)

**Cons**:
- Phải design generic API (sortable column, freeze, persist key) — over-engineer nếu chỉ 1 tab
- Risk: làm xong hạng mục 1 tốt nhưng generic API không khớp với 4 hạng mục sau (mỗi tab có quirk riêng — RawMaterials có price, Routing có time numerics, Spec có versioning, WorkCenter có area)

**Phương án ghép**: ship Option A cho hạng mục 1 (EngineerStructure standalone). Sau khi làm hạng mục 2 (EngineerRoutine), nếu thấy pattern giống nhau → refactor extract Shared component. KHÔNG over-engineer ngay từ đầu (YAGNI).

---

## 6. Câu hỏi anh cần chốt

| # | Câu hỏi | Default em đề xuất |
|---|---|---|
| Q1 | Đủ 16 cột (Option A) hay subset 11 (Option B)? | **A** — đầy đủ, có giá trị thực với operator |
| Q2 | Columns toggle có persist sang load lại không? | **Có** — localStorage key `cclmes.engineer-structure.columns-hidden.v1`. Cần JSInterop tối thiểu (Blazor Server không có direct localStorage) — có thể dùng package `Blazored.LocalStorage` hoặc gọi `IJSRuntime` thẳng. |
| Q3 | Import CSV + Clear Data có làm trong PR Engineer Structure hay tách riêng? | **Tách riêng** — Import CSV hiện đang qua `tools/import_npi.py` (Python script). Đưa lên UI là feature mới đáng kể (file upload + multipart + audit + RBAC edit gate). Đề xuất PR riêng sau khi 5 grid view UI xong. |
| Q4 | Paging: giữ `Pager` cũ pageSize=50, hay đổi sang scroll + 500 rows + footer warn (CMES style)? | **Giữ Pager** ban đầu — đỡ scope. Chuyển scroll sau nếu user feedback. |
| Q5 | Freeze header — `max-height` cố định hay grid layout responsive? | **`max-height: calc(100vh - 240px)`** — đơn giản, fit khung. Tinh chỉnh sau khi screenshot. |
| Q6 | Shared NpiDataGrid component (Option C) — làm ngay hay defer? | **Defer** sang sau hạng mục 2. YAGNI cho hạng mục 1. |
| Q7 | Re-import 20 530 rows post-migration: chấp nhận downtime ~30s hay zero-downtime migration (giữ old data, fill new field lazy)? | **Re-import** — script đã idempotent (DELETE + INSERT), backup SHA256 trước, restart sau. Server CCL-CMES chưa production live → downtime OK. |
| Q8 | Rename entity field `Color` → `Colors` và `ScrapFactor` → `Scrap` cho match CMES? | **Không** — giữ tên hiện tại trong entity (backward compat), chỉ đổi LABEL hiển thị + i18n key. Tránh churn unnecessary. |
| Q9 | Pitch/Cavity string → `double?` — nếu CSV có "N/A" hoặc "AS NEEDED" sẽ mất → có chấp nhận? | **Yes** — verify trước bằng `grep -i "[a-z]" colcol` trên CSV, nếu 100% numeric thì migrate clean. Nếu có alpha values, giữ string + format `fmtNum` chỉ apply khi parse được. |

---

## 7. Rủi ro + Mitigation

| Rủi ro | Mức | Mitigation |
|---|---|---|
| Migration v5 fail mid-way → DB corrupt | High | Phase A→B→C SAFE: backup ccl_mes.db.bak.phase7-pre-structure-{ts} + SHA256 → test trên `/tmp/structure-design.db` → rollback nếu sai. |
| Re-import lost data (20 530 → < 20 530 rows) | Med | Pre-import row count snapshot + post-import verify. `DELETE + INSERT` trong cùng transaction → all-or-nothing. |
| Pitch/Cavity parse fail (alpha values trong CSV) | Low | Verify trước bằng `grep -E "[^0-9.]" Cavity` trên CSV; nếu có, fallback giữ string. |
| Columns toggle JSInterop fail (Blazor Server SSR) | Low | Fallback: nếu localStorage không khả dụng, dùng in-memory state (mất khi reload, không crash). |
| Freeze header `max-height` không fit screen nhỏ | Low | Test 3 size: 1024 / 1440 / 4K. Dùng `calc(100vh - Xpx)` linh hoạt. |
| Cascade với 4 hạng mục sau (Routine / Materials / Spec / WorkCenter) bị repeat code | Med | Defer shared component sang sau hạng mục 2; refactor extract sau khi pattern đã ổn. |
| `Page<th sticky>` không hoạt động trong table-scroll wrapper hiện tại | Low | Đã verify ở CMES — pattern chuẩn. Nếu cần, đổi sang display: grid table thay tablulator. |

---

## 8. Vùng cấm + branch base

- **Vùng cấm** (CLAUDE.md §1): KHÔNG đụng `Ops Control v1.2/`, `CMES/` (sibling), `Old ver ( DO NOT USE)/`, `SpecHub/`. Tất cả thay đổi nằm trong `CCL-CMES/CCL-MES/`.
- **Branch base**: `main` HEAD `0bf43c1` (sau PR #20 close Phase 6).
- **Branch tên đề xuất**: `feat/phase7-engineer-structure`
- **PR title đề xuất**: `feat(npi): Phase 7 hạng mục 1 — Engineer Structure full 16 cột + freeze + Columns toggle`

---

## 9. DoD (Definition of Done) cho PR hạng mục 1

1. ✅ `dotnet build CCL.MES.sln` clean (0 warning / 0 error)
2. ✅ Migration v5 apply lên `/tmp/structure-design.db` isolated → verify `.schema ManufacturingStructures` có 5 column mới
3. ✅ Backup live DB SHA256 trước migrate
4. ✅ Re-import IFS CSV via Python script → post-import row count = 20 530 (unchanged) + 5 column mới đều có data
5. ✅ Restart server lần 2 → "No migrations were applied" (idempotent)
6. ✅ Smoke admin login → GET `/npi/engineer-structure` → 200 + 16 columns hiển thị + freeze header active + search filter hoạt động + Columns toggle persist sau F5
7. ✅ Smoke operator login → GET `/npi/engineer-structure` → 200 (operator có NpiRead policy — confirmed Phase 6 Bước 4)
8. ✅ Engineer login → tương tự, không có Import/Clear button (Bước 7 hạng mục 1 chưa làm)
9. ✅ Row counts NPI khác (WorkCenters=43 / RawMaterials=2 127 / RoutingOperations=38 441) không đổi
10. ✅ EN+VI i18n parity (no missing key warnings)

---

## 10. Sau khi anh chốt phương án

1. Em tạo branch `feat/phase7-engineer-structure`
2. Phase A: backup + SHA256 + isolated migration test
3. Phase B: implement entity + migration + import script + Razor UI + i18n + CSS
4. Phase C: apply migration thật + verify smoke
5. Open PR + smoke matrix + screenshot (admin view với 16 cột active)
6. Anh review + merge → repeat cho hạng mục 2 (Engineer Routine)

---

## 11. Files em đã đọc (audit trail)

CMES (READ-ONLY, KHÔNG sửa):
- `3. PROJECTS/CMES/apps/web/src/modules/engineer-structure/EngineerStructure.tsx` (344 LOC)
- `3. PROJECTS/CMES/apps/server/src/modules/engineer-structure/engineer-structure.service.ts` (339 LOC)
- `3. PROJECTS/CMES/apps/server/src/modules/engineer-structure/engineer-structure.controller.ts` (157 LOC)
- `3. PROJECTS/CMES/db/migrations/sqlite/007_engineer_structure.sql` (42 LOC)
- `3. PROJECTS/CMES/apps/web/src/styles/index.css:1570-1760` (200 LOC vùng rt-*)

CCL-CMES (sẽ thay đổi):
- `src/CCL.MES.Web/Pages/Npi/EngineerStructure.razor` (62 LOC)
- `src/CCL.MES.Application/Services/NpiService.cs:63-75` (StructuresAsync)
- `src/CCL.MES.Domain/Entities/Npi.cs:43-57` (ManufacturingStructure)
- `tools/import_npi.py:173-232` (read_structures)
- IFS source: `3. PROJECTS/CCL-CMES/Data/ManufacturingStructures 260525-65635.csv` (31 cột header verified)

---

*Phase 7 — Hạng mục 1 khảo sát ngày 2026-05-31. Untracked, KHÔNG commit. STOP chờ anh chốt Q1-Q9.*

---

# Bổ sung — Import CSV trên UI (2026-05-31, sau khi anh duyệt Option A)

> **Đổi scope**: Q3 ban đầu chốt "Import/Clear UI tách PR riêng". Anh đổi ý: gộp Import CSV vào PR Engineer Structure luôn vì đây là cách nạp data chính thay cho script Python tay. Em khảo sát + báo cáo trước khi code.

## 12. ⚠ Phát hiện về CMES "auto-merge" — CMES KHÔNG auto-merge thật

Em đọc lại kỹ `apps/server/src/modules/engineer-structure/engineer-structure.service.ts:243-337` (`importCsv` method) + `db/migrations/sqlite/007_engineer_structure.sql` + postgres equivalent. **Sự thật về CMES**:

### 12.1 CMES schema KHÔNG có UNIQUE constraint

```sql
CREATE TABLE IF NOT EXISTS engineer_structure (
  id                  TEXT PRIMARY KEY DEFAULT (gen_random_uuid()),  -- chỉ id là UNIQUE
  parent_part         TEXT NOT NULL,
  component_part      TEXT NOT NULL,
  ...
);
CREATE INDEX idx_eng_structure_parent ON engineer_structure (parent_part);    -- chỉ INDEX, KHÔNG UNIQUE
CREATE INDEX idx_eng_structure_component ON engineer_structure (component_part);
```

→ Không có `UNIQUE(parent_part, component_part)` hay tương tự.

### 12.2 CMES importCsv KHÔNG có ON CONFLICT, KHÔNG DELETE trước

```typescript
await this.repo.query(
  `INSERT INTO engineer_structure (...) VALUES (...)`,  // chỉ INSERT, không có ON CONFLICT
  values as never,
);
```

Không có `DELETE` trước, không có `ON CONFLICT DO UPDATE`, không có upsert. **Mỗi lần import = APPEND**. Re-import cùng file → **DUPLICATE rows**.

### 12.3 CMES có `DELETE` endpoint riêng "Clear Data"

```typescript
@Delete()
async clear() { await this.repo.query(`DELETE FROM engineer_structure`); }
```

→ Operator phải bấm **Clear Data** trước rồi Import lại nếu muốn replace toàn bộ. Đây là pattern hai bước, NHIỀU CÁCH bị sai nếu operator quên Clear.

### 12.4 Kết luận về "auto-merge" của CMES

CMES **KHÔNG** auto-merge. UI có 2 nút riêng (Import + Clear Data). Operator phải tự điều phối:
- Nếu DB rỗng → Import → có data
- Nếu DB đã có data + muốn refresh → Clear Data trước → Import → có data mới
- Nếu Import mà quên Clear → duplicate rows (silent bug, không cảnh báo)

→ **Mental model anh có về CMES "tự động merge" không khớp với code thật**. Trước khi quyết phương án em phải báo điều này. CCL-CMES có thể làm tốt hơn CMES bằng cách thiết kế semantic merge rõ ràng ngay từ đầu.

## 13. So sánh các phương án merge cho CCL-CMES

### 13.A — Replace-all (idempotent, đề xuất ⭐)

Pattern CCL-CMES Python script hiện đang dùng: `DELETE FROM ManufacturingStructures` + `INSERT VALUES (...)` trong **1 transaction**. Atomic, idempotent.

```csharp
using var tx = await _db.Database.BeginTransactionAsync();
try {
  await _db.Database.ExecuteSqlRawAsync("DELETE FROM ManufacturingStructures");
  _db.ManufacturingStructures.AddRange(parsedRows);
  await _db.SaveChangesAsync();
  await tx.CommitAsync();
} catch {
  await tx.RollbackAsync();
  throw;
}
```

**Pros**:
- **Idempotent**: re-import cùng file → same end state (không nhân đôi)
- Pattern này CCL-CMES Python script đã chạy chính xác, ops familiar
- IFS export CSV là FULL DUMP (20,530 rows), không partial — replace-all hợp lý
- Không cần thêm UNIQUE constraint → không cần migration v6
- Code đơn giản (~20 LOC import logic)
- Atomic — fail giữa chừng rollback toàn bộ, DB không corrupt

**Cons**:
- Nếu operator import file PARTIAL (vd CSV chỉ 100 rows test) → mất 20,430 rows còn lại
- → Mitigation: **preview trước confirm** (xem trước số rows + 5 mẫu) + **auto-backup pre-import** để rollback bằng tay nếu sai
- Không thể "patch một phần" (Engineer thêm 5 row mới → phải import full CSV)

### 13.B — Upsert-by-key (preserve manual edits, phức tạp hơn)

Thêm UNIQUE constraint `(ParentPart, ComponentPart, Alt)` qua migration v6. Import dùng `INSERT ... ON CONFLICT (ParentPart, ComponentPart, Alt) DO UPDATE`.

**Pros**:
- Preserve rows không có trong CSV (vd Engineer thêm 1 row tay không trong IFS dump)
- Re-import idempotent (cùng key → update, không trùng)

**Cons**:
- Phải chốt key — `(ParentPart, ComponentPart)` không đủ vì IFS có Alternative No (Alt = "*", "1", "2"…) → cần `(ParentPart, ComponentPart, Alt)`. Risk: nếu CSV nguồn có NULL Alt → SQLite NULL không match nhau trong UNIQUE (mỗi NULL distinct), upsert fail
- Cần migration v6 + verify lại CSV nguồn xem `(ParentPart, ComponentPart, Alt)` có duplicate không (em verify trước khi code)
- LOC ~80 (helper to_upsert + key handling), test complexity cao hơn
- EF Core 10 SQLite chưa support native UPSERT through DbContext API → phải `ExecuteSqlRaw` hand-craft

### 13.C — Append (giống CMES code thật, NOT RECOMMENDED)

Chỉ `INSERT`, không dedup. Operator phải bấm Clear Data trước rồi Import.

**Pros**:
- Đơn giản nhất (~10 LOC)

**Cons**:
- Không idempotent — re-import → duplicate. **Đã thấy bug CMES; không nên lặp lại**
- Operator phải nhớ bấm Clear trước → human error
- Không có safety net

### 13.D — Append + auto-clear (replace-all wrapper, tương đương 13.A)

UI có 1 nút "Import" duy nhất, server tự DELETE trước rồi INSERT (operator không cần bấm Clear). = Replace-all (13.A) với UX 1 click.

→ **Đây chính là phương án 13.A** với UI 1 nút (không có nút "Clear Data" rời).

---

**Em đề xuất 13.D** (1 nút Import + replace-all + preview + auto-backup). Đây là biến thể tốt hơn CMES. Lý do:

| Tiêu chí | CMES (Append + Clear riêng) | CCL-CMES đề xuất (13.D Replace-all + preview + auto-backup) |
|---|---|---|
| Số nút operator phải nhớ | 2 (Import + Clear) | 1 (Import) |
| Re-import idempotent | ❌ duplicate | ✅ atomic |
| Preview trước commit | ❌ | ✅ "Sẽ thay {old N} → {new M} rows, có chắc?" |
| Safety net | ❌ | ✅ auto-backup SHA256 + audit trail trước khi DELETE |
| Mất data partial CSV | – (CMES Append không mất) | ⚠ có, nhưng preview + backup mitigate |

---

## 14. Phương án 13.D chi tiết

### 14.1 Luồng UI (3 step wizard)

```
Step 1 — Pick file
  [InputFile accept=".csv"]  → user chọn file local
  
Step 2 — Preview (server parse + validate)
  - Header detected: 31 cột, mapped 16/16 ✓
  - Rows valid: 20,530
  - Rows skipped: 0 (missing Parent or Component)
  - 5 sample rows shown
  - Current DB: 20,530 rows → sau import sẽ là 20,530 rows
  - [Confirm Import] [Cancel]
  
Step 3 — Result
  - ✓ Backup taken: ccl_mes.db.bak.pre-structure-import-{ts}
  - ✓ DELETE 20,530 + INSERT 20,530 in 1 transaction (2.3s)
  - ✓ Audit emitted: NPI_IMPORT (table=ManufacturingStructures, rows=20530)
  - [Close]
```

### 14.2 Server-side parse + insert (Service code mới)

`Application/Services/NpiImportService.cs` (mới):
- `ParseStructureCsvAsync(Stream stream) → CsvParseResult` (preview)
- `ApplyStructureImportAsync(IReadOnlyList<ManufacturingStructure> parsedRows, ClaimsPrincipal actor) → ImportResult` (commit)

Tái dùng mapping logic từ `tools/import_npi.py:read_structures` (5 cột mới + num_or_none cho double?).

### 14.3 Auto-backup pre-import

```csharp
// Inside ApplyStructureImportAsync, before DELETE:
var backup = await _backup.CreateSnapshotAsync(actor);
if (backup.Outcome != BackupOutcome.Success)
    throw new InvalidOperationException("Backup failed — abort import");
// → backup file: ccl_mes.db.bak.snapshot-{ts}
// Audit Detail JSON sẽ ghi backup filename + sha256
```

### 14.4 RBAC

- Page `[Authorize(Policy="NpiRead")]` giữ nguyên (xem grid)
- Button **Import** chỉ render bằng `<AuthorizeView Roles="Admin,Engineer">` — match matrix Phase 6 Bước 4 §2.C (write master data = Admin/Engineer)
  - Supervisor: read-only NPI (không edit master)
  - QC: read-only NPI (không edit master)
  - Operator: không access NPI nói chung
- Server-side check trong `NpiImportService.ApplyStructureImportAsync` validate role lần nữa (defense-in-depth) — refuse nếu actor không phải Admin/Engineer
- New AuditAction code: `NpiImport = "NPI_IMPORT"` (generic — detail JSON chứa table + rows + backup file + sha256)

### 14.5 Validate dòng

Reuse pattern Python script:
- `len(row) < 11` → skip "short_row"
- Cả `parent_part` lẫn `component_part` đều rỗng → skip "no_parent_or_component"
- Pitch/Cavity/ScrapPct parse fail → store NULL (không skip)
- Counters: parsed / inserted / skipped + skip_reasons dict

**Fail policy**: skip dòng lỗi, KHÔNG fail batch (operator vẫn import được file có vài dòng lỗi nhỏ). Nếu skip ratio > 50% → flag warning bắt buộc operator confirm lại.

### 14.6 Transaction

Toàn bộ DELETE + INSERT trong 1 `BeginTransactionAsync()` → rollback nếu exception. Auto-backup được tạo TRƯỚC khi mở transaction (snapshot file độc lập, vẫn còn nếu rollback).

### 14.7 File size limit

CMES dùng env `OPS_MAX_UPLOAD_MB=200`. CCL-CMES đề xuất hard cap 100 MB (IFS dump ~5 MB cho 20k rows → 100 MB đủ rộng cho gấp 20 lần). Blazor `InputFile` parameter `MaxFileSize` enforce + server-side double-check.

### 14.8 Header CSV — flexibility

CMES có `HEADER_ALIASES` dict (case-insensitive, priority order). CCL-CMES reuse pattern này:
- Map theo header CSV (không hardcode index như Python script hiện tại) → flexibility nếu IFS export đổi thứ tự cột
- Aliases lấy từ CMES (đã verify): `parent_part` ← `"parent part no"|"parent part"`; `parent_desc` ← `"parent part description"|"parent description"`; etc.
- Strip UTF-8 BOM
- Separator = `,` (CSV comma); tab/semicolon defer (IFS export luôn CSV comma)

### 14.9 i18n

~15 key mới `npi.structure.import.*`:
- `btn_import`, `modal.title`, `modal.pick_file`, `modal.parsing`, `modal.preview_header`, `modal.preview_rows`, `modal.preview_warning` (nếu skip > 50%), `modal.confirm_replace` (X cũ → Y mới), `modal.btn_confirm`, `modal.btn_cancel`, `modal.success`, `modal.error`, `result.imported`, `result.skipped`, `result.elapsed`

EN+VI parity.

---

## 15. Tổng quát hóa cho 5 tab NPI (Structure / Routine / RawMaterials / Spec / Machine List)

Pattern em đề xuất:

```
Application/Services/NpiImport/
  ├── ICsvImportTarget<T>.cs       — interface chung: ParseRow / Apply
  ├── StructureCsvTarget.cs        — implement mapping cho Structure
  ├── RoutineCsvTarget.cs          — hạng mục 2
  ├── RawMaterialsCsvTarget.cs     — hạng mục 3
  ├── SpecCsvTarget.cs             — hạng mục 4
  └── WorkCenterCsvTarget.cs       — hạng mục 5
  
Application/Services/NpiImportService.cs   — Generic engine:
  - ParseCsvAsync<T>(Stream, ICsvImportTarget<T>) → CsvParseResult<T>
  - ApplyImportAsync<T>(IReadOnlyList<T>, ICsvImportTarget<T>, actor) → ImportResult
    1. CreateSnapshotAsync (auto-backup)
    2. BeginTransaction
    3. DELETE FROM <table>
    4. INSERT rows
    5. CommitAsync
    6. Audit emit NPI_IMPORT { table, rows, backup_file, backup_sha256 }
    
Web/Shared/NpiImportModal.razor   — Generic 3-step wizard component:
  - InputFile + preview + confirm + result
  - Bind T type-generic; nhận ICsvImportTarget<T>
  - Reusable cho 5 tab
```

Hạng mục 1 (Structure) làm REFERENCE implementation. Hạng mục 2+ refactor extract khi pattern đã ổn (giống định hướng NpiDataGrid Q6 chốt).

→ **Bước 1 KHÔNG ép tổng quát hoá ngay**. Em làm `StructureCsvTarget` + `NpiImportService` (generic shape sẵn) + `NpiImportModal` (generic shape sẵn). Hạng mục 2 chỉ cần viết `RoutineCsvTarget` + dùng lại Service + Modal.

---

## 16. Câu hỏi mới Q10-Q15

| # | Câu hỏi | Default em đề xuất |
|---|---|---|
| Q10 | Merge semantic: 13.A Replace-all (idempotent) hay 13.B Upsert-by-key (preserve manual edits)? | **13.A Replace-all** ⭐ vì idempotent + IFS export là full dump + Python script đã pattern này + có safety net (preview + auto-backup). 13.B phức tạp + Alt NULL handling tricky. |
| Q11 | Có preview trước confirm không? | **Có** — 3-step wizard. Preview = header detected + rows valid + sample 5 rows + "{old N} → {new M}" diff. Operator confirm trước khi DELETE. |
| Q12 | Auto-backup pre-import có bắt buộc không? | **Có** — gọi `BackupService.CreateSnapshotAsync` trước transaction. Backup fail → abort import. Audit detail ghi backup filename + sha256. |
| Q13 | Giới hạn file size? | **100 MB** (IFS dump thực ~5 MB, dư rộng). Cả Blazor `InputFile MaxFileSize` + server check. |
| Q14 | RBAC role nào được Import? | **Admin + Engineer** (match Phase 6 Bước 4 §2.C — write master data). Supervisor/QC read-only NPI; Operator không access NPI. |
| Q15 | "Clear Data" UI tách rời như CMES, hay chỉ có "Import" (Replace-all)? | **Chỉ "Import"** — 13.D pattern. Replace-all đã có behavior tương đương Clear+Import nhưng atomic + ít human error. Không cần nút Clear riêng. |
| Q16 | Generic NpiImportModal + ICsvImportTarget pattern làm ngay từ hạng mục 1 hay defer? | **Làm shape sẵn** ở hạng mục 1 (Service + Modal là generic, chỉ StructureCsvTarget concrete). Hạng mục 2 chỉ cần add RoutineCsvTarget — không refactor. |
| Q17 | Header mapping reuse `HEADER_ALIASES` từ CMES hay viết riêng theo IFS CCL? | **Reuse CMES aliases** (đã verify khớp với CSV thật CCL IFS export). Add aliases mới nếu thấy gap qua test. |

---

## 17. Rủi ro + Mitigation mới

| Rủi ro | Mức | Mitigation |
|---|---|---|
| Operator import sai file → mất 20,530 rows | **High** | Preview confirm (Step 2) + auto-backup SHA256 trước DELETE. Restore từ backup nếu sai. |
| File CSV quá lớn → OOM | Med | 100 MB cap + InputFile streaming (không buffer toàn bộ vào memory một lúc). Blazor `OpenReadStream(maxAllowedSize: 100_000_000)`. |
| CSV header thay đổi (IFS update) → mapping lỗi | Med | `HEADER_ALIASES` dict + log mapped_columns trong audit detail để debug. |
| Transaction rollback fail giữa chừng → DB partial | Low | `BeginTransactionAsync` SQLite atomic. Auto-backup là safety net cuối cùng. |
| Concurrent import (2 operator cùng nhấn) → race | Low | Page-level lock không cần (Blazor Server circuit per-user). Server-side: dùng `DbInitializer` lock pattern hoặc `lock` keyword đơn giản (rare scenario). |
| Backup file disk full → cycle backup folder | Low | `BackupService.CreateSnapshotAsync` đã có log size. Future: cleanup old backups > N. Out of scope. |
| Audit detail JSON quá dài (5 sample rows) | Low | Truncate detail nếu > 10 KB; chỉ ghi metadata (rows, filename, sha256, mapped_columns count). Không ghi raw CSV content. |

---

## 18. LOC ước lượng update (Option A + Import CSV gộp)

| Module | LOC |
|---|---|
| Entity v5 + migration (đã chốt Option A) | 80 |
| Razor grid 16 cột + freeze + Columns toggle (đã chốt) | 150 |
| CSS + JS interop (đã chốt) | 80 |
| i18n grid + import keys × 2 locale | 100 |
| import_npi.py update (đã chốt) | 30 |
| **NpiImportService + ICsvImportTarget + StructureCsvTarget** | 200 |
| **NpiImportModal (3-step wizard, generic)** | 250 |
| **NpiImport route handler trong page + AuditAction NPI_IMPORT** | 50 |
| Test (parse mapping + replace-all idempotent + auto-backup chain) | 60 |
| **TOTAL** | **~1000 LOC** |

Vẫn 1 PR nhưng từ ~400 LOC → ~1000 LOC. Vẫn nằm trong giới hạn cho 1 PR.

---

## 19. DoD update cho PR hạng mục 1 (gộp Import)

Bổ sung cho list §9:

11. ✅ admin login → page có nút "Import" (Admin role pass `<AuthorizeView Roles="Admin,Engineer">`)
12. ✅ engineer login → page có nút "Import"
13. ✅ supervisor/qc/operator login → KHÔNG có nút Import (UI hide) + server-side refuse nếu force POST
14. ✅ Import test file: pick CSV → preview hiển thị 31 cột mapped 16/16 + 5 sample rows + "20,530 → 20,530" → confirm → success
15. ✅ Auto-backup file tạo TRƯỚC khi DELETE (verify by ls timestamps in `<DATA_DIR>/Backup/SQLite/`)
16. ✅ Re-import cùng file → DB row count vẫn 20,530 (idempotent — KHÔNG duplicate)
17. ✅ Audit log có entry `NPI_IMPORT` với detail JSON `{table: "ManufacturingStructures", rows: 20530, backup_file: "ccl_mes.db.bak.snapshot-...", elapsed_ms: ...}`
18. ✅ Restore từ backup pre-import → DB rollback về 20,530 rows trước import (verify SHA256 match)

---

*Bổ sung Import CSV — 2026-05-31. Untracked, KHÔNG commit. STOP chờ anh chốt Q10-Q17.*
