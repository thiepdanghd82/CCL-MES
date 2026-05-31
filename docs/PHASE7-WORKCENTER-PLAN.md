# PHASE 7 — HẠNG MỤC 5: Work Center (Machine List)

> Khảo sát + plan đồng bộ tab **Work Center** theo CMES tham chiếu.
> **Chưa code** — chờ anh chốt scope + Q-questions trước khi tạo branch.
>
> Hạng mục 5 = **tab cuối** Phase 7 NPI. Sau khi merge → close-out report
> tổng kết 5 PR (Structure / Routine / RawMaterials / Spec / WorkCenter).

---

## 1) State hiện tại (CCL-CMES `main` post-PR #25)

### 1.1 Entity `WorkCenter` (`src/CCL.MES.Domain/Entities/Npi.cs:4-9`)

3 field, minimal:

| Field | Type | Note |
|---|---|---|
| Code | string | required, indexed |
| Description | string | required |
| Area | string? | nullable, computed by `infer_area()` heuristic ở importer |

### 1.2 Data source ĐẶC BIỆT — DERIVED từ Routing

`tools/import_npi.py:read_routing` build dict `wc_dict[wcno] = wcdesc` từ distinct values trong `Routing Operations.csv` cột Work Centre No (5) + Work Centre Desc (8). `insert_workcenters` ghi 43 row distinct WC vào table.

→ **KHÔNG có IFS WorkCenter export riêng**. Source of truth = Routing CSV. Nếu operator muốn thêm WC độc lập → phải sửa import logic hoặc add UI Create.

### 1.3 UI hiện tại (`Pages/Npi/WorkCenter.razor`)

4 cột read-only, dùng `wo data` CSS cũ:
`# | WC Code | Description | Area`

- KHÔNG freeze header
- KHÔNG Columns toggle
- KHÔNG Import button
- Area hiển thị badge `step` (Bootstrap-style)
- Search 3 field: Code, Description, Area

### 1.4 Data baseline

```
WorkCenters = 43  (derived từ Routing Ops 38,441)
```

### 1.5 Coupling (CRITICAL — không touch)

- **NOT FK target** — RoutingOperation có `WorkCenterNo` là **string field**, không phải FK. Migration WC entity an toàn không vỡ data.
- **Machine entity (Machine.cs)** = ENTITY KHÁC, không phải WorkCenter:
  - Machine: per-equipment tracking (CurrentState enum, IdealCycleTimeSec, ProductionLog FK)
  - WorkCenter: manufacturing cell catalog
  - 2 concept khác hẳn — hạng mục 5 đụng WorkCenter, **KHÔNG đụng Machine**
- About.razor count, NpiController API, NpiService.WorkCentersAsync — tất cả chỉ đọc, không phụ thuộc schema cố định

### 1.6 RBAC

`NpiRead` policy (Admin/Supervisor/Engineer/QC — KHÔNG Operator) — giống Structure/Routine/RawMaterials.

---

## 2) Gap với CMES tham chiếu

### 2.1 CMES WorkCenter — 6 field (minimal)

```typescript
interface WorkCenter {
  wc_code: string;
  desc: string;
  area: string;
  ideal_speed_pcs_h?: number;        // NEW vs CCL-CMES
  shift_pattern?: 'A' | 'B' | 'C' | 'A+B' | 'A+B+C';  // NEW
  active?: boolean;                  // NEW
}
```

**3 field mới**: `ideal_speed_pcs_h`, `shift_pattern`, `active`.

### 2.2 CMES validation + KNOWN_AREAS

- **WC code regex**: `/^[A-Z0-9_-]{3,12}$/` (3-12 ký tự alnum + `-` + `_`, uppercase)
- **KNOWN_AREAS** (17 hardcoded): CNC, Diecut, Drying, Finishing, Flexo, Forming, Indigo, Inspection, Lamination, Letterpress, Manual, Prep, Punch, QC, RDC, Silkscreen, Special
- → Cho phép operator filter chips theo area + dropdown validation khi Edit

### 2.3 CMES Import (XLSX + CSV) — header aliases nhiều ngôn ngữ

Hỗ trợ alias `mã wc / mã máy / khu vực / tên máy`. KHÔNG strict require code-9 alphabet — chỉ require sau khi parse.

---

## 3) 2 lựa chọn scope khả thi cho 1 PR

| Option | Scope | Effort | Phù hợp khi |
|---|---|---|---|
| **A. UI parity only** | Migrate `wo data` → `.rt-*` namespace + freeze + Columns toggle + status badge cho `active`. **NO new entity fields, NO migration, NO Import CSV**. | S | Anh muốn đồng nhất 5 NPI tabs visual; defer feature dev. WC chỉ 3 cột — Columns toggle hơi overkill. |
| **B. UI parity + 3 fields mới + Import CSV** (em đề xuất) | A + migration ADD `IdealSpeedPcsH/ShiftPattern/Active` + WorkCenterCsvTarget + Import button + UI 6 cột | M | Đóng full parity với CMES + cho operator tự maintain master catalog độc lập với Routing. |

**Em đề xuất Option B** vì:
- CMES chỉ thêm 3 field nhỏ (1 numeric + 1 enum + 1 bool)
- Reuse 100% infrastructure NpiImportService/Modal/Csv (chỉ thêm WorkCenterCsvTarget concrete)
- WC hiện tại derived từ Routing — operator KHÔNG thể add WC mới (vd cell mới chưa có routing operation) — Import CSV mở khả năng này
- Khớp pattern hạng mục 1-3 (grid + freeze + Columns + Import)
- Hoàn tất "đồng bộ NPI tabs theo CMES" → close Phase 7 sạch

---

## 4) Plan code (giả định Option B)

### 4.1 Migration `AddWorkCenterExtendedFields` (A→B→C SAFE)

A→B→C SAFE pattern (mirror hạng mục 1-3):
- **A**: backup live DB + SHA256
- **B**: test `/tmp/wc-design.db` migration apply, verify WorkCenters=43 unchanged, các bảng khác nguyên vẹn
- **C**: apply real, verify Routing 38,441 / Structure 20,530 / RawMaterials 2,127 / WC 43 / Users 5 / IQC 3 / Specs 1 (post PR#25 baseline)

Migration ops (provider-agnostic, không type:):
- ADD COLUMN `IdealSpeedPcsH` REAL NULL
- ADD COLUMN `ShiftPattern` TEXT NULL
- ADD COLUMN `Active` INTEGER NULL (SQLite stores bool as int)

Phase 7 hm5 dropdown enum suggestion: `ShiftPattern` stored TEXT plain ("A", "B", "C", "A+B", "A+B+C"); KHÔNG dùng C# enum + HasConversion vì giá trị "A+B" không hợp pattern enum identifier (cần underscore). UI sẽ dùng `InputSelect` với 5 hardcoded options + 1 "(none)".

### 4.2 Entity update (`Npi.cs`)

```csharp
public class WorkCenter : BaseEntity
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Area { get; set; }
    // Phase 7 hạng mục 5 — 3 field mới khớp CMES UI tham chiếu.
    public double? IdealSpeedPcsH { get; set; }
    public string? ShiftPattern { get; set; }  // "A" / "B" / "C" / "A+B" / "A+B+C"
    public bool? Active { get; set; }
}
```

### 4.3 UI rewrite `WorkCenter.razor` mirror RawMaterials pattern

- `.rt-page` + `.rt-toolbar` + `.rt-table-wrap` + freeze sticky thead
- 6 ColumnDef (Code/Description/Area/IdealSpeed/Shift/Active)
- ALL visible default (số cột vừa phải)
- Columns toggle popover + localStorage `cclmes.workcenter.columns-hidden.v1`
- Area badge với 17 KNOWN_AREAS palette (CSS `.wc-area--cnc/--diecut/...`)
- Active badge (reuse `.rm-status--active/--inactive`)
- Import button gated AuthorizeView Admin/Engineer

### 4.4 `WorkCenterCsvTarget` (concrete `ICsvImportTarget<WorkCenter>`)

Header aliases mirror CMES `wcImport.ts`:
```
code:    [wc code, wc_code, code, work center code, machine code, mã wc, mã máy]
desc:    [desc, description, work center, mô tả, machine name, name, tên máy]
area:    [area, section, nhóm, khu vực, department]
ideal_speed: [ideal speed, ideal_speed_pcs_h, speed, ideal pcs/h]
shift:   [shift, shift pattern, shift_pattern, ca]
active:  [active, enabled, 1/0]
```
RequiredFields = `["code"]`, MinColumnCount = 2.

Validation regex `^[A-Z0-9_-]{3,12}$` cho code (mirror CMES `WC_CODE_RE`); reject rows không khớp.

Replace-all semantic giống hạng mục 1-3 (DELETE + INSERT atomic + auto-backup). **Trade-off lưu ý**: nếu operator chạy `tools/import_npi.py` sau khi UI import → WC bị overwrite với derived-from-routing. Plan ghi rõ trong help hint trên toolbar.

### 4.5 NpiService.WorkCentersAsync search expand

Current 3 field (Code/Description/Area). Thêm:
- `ShiftPattern` (operator filter theo ca)

→ 3 → 4 field.

### 4.6 Importer `tools/import_npi.py`

`read_routing` không cần đổi (vẫn derive WC dict từ Routing CSV).
`insert_workcenters` UPDATE: thêm 3 cột mới = NULL khi derive (vì Routing CSV không có speed/shift/active info).
→ Operator có thể chỉnh sau qua UI Import CSV với CSV custom.

### 4.7 i18n keys (EN + VI parity)

Thêm `~20 keys` cho `npi.workcenter.*`:
- breadcrumb, rows_loaded, rows_count, btn_columns, btn_show_all, empty, empty_filter
- 6 col labels (code/description/area/ideal_speed/shift_pattern/active)
- Active labels (active/inactive)
- Format hint cho Import (xlsx/csv accepted, replace-all semantic)

`npi.import.*` keys đã share — không cần thêm.

---

## 5) Scope contract (vùng cấm)

Hạng mục 5 **KHÔNG** đụng:
- Ops Control v1.2 / CMES / SpecHub / "Old ver"
- Tab khác CCL-CMES (Structure/Routine/RawMaterials/Spec/IQC/Settings)
- **Machine entity** (Machine.cs) — concept KHÁC WorkCenter
- **DowntimeReason** (Machine.cs) — không liên quan
- **ProductionLog FK** — không trỏ tới WorkCenter
- RoutingOperation.WorkCenterNo (string field, không phải FK) — entity Routing không sửa
- Library / RBAC matrix / Audit infra
- About.razor count (vẫn đếm `Db.WorkCenters.CountAsync()` — work fine với schema mới)

Chỉ touch:
- `src/CCL.MES.Domain/Entities/Npi.cs` (WorkCenter entity + 3 field)
- `src/CCL.MES.Infrastructure/Migrations/<ts>_AddWorkCenterExtendedFields.{cs,Designer.cs}`
- `src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs`
- `src/CCL.MES.Application/Services/NpiImport/WorkCenterCsvTarget.cs` (NEW)
- `src/CCL.MES.Application/Services/NpiService.cs` (search expand)
- `src/CCL.MES.Web/Pages/Npi/WorkCenter.razor` (full rewrite)
- `src/CCL.MES.Web/Resources/SharedResource.{resx,vi.resx}` (~20 keys × 2)
- `src/CCL.MES.Web/wwwroot/css/site.css` (.wc-area + .rm-status--active/--inactive nếu chưa có)
- `tools/import_npi.py` (insert_workcenters tuple expand)
- `docs/PHASE7-WORKCENTER-PLAN.md` (this file)

---

## 6) Q-questions cần anh chốt

| Q# | Câu hỏi | Default em đề xuất |
|---|---|---|
| **Q1** | Scope: A (UI only) hay B (UI + 3 fields + Import CSV)? | **Option B** (CMES parity gọn 3 fields nhỏ + reuse infra) |
| **Q2** | `Active` mặc định cho rows hiện tại (sau migration ADD COLUMN NULL) = NULL hay TRUE? | **TRUE** (43 WC hiện tại đều đang active per derived-from-Routing — null sẽ confuse operator) |
| **Q3** | Code validation regex `^[A-Z0-9_-]{3,12}$` cho Import CSV — reject hay warn? | **REJECT + report skip count** (mirror CMES strict behavior; operator dễ phát hiện CSV xấu) |
| **Q4** | Re-import xlsx/csv semantic — Replace-all (hạng mục 1-3) hay Upsert-by-Code? | **Replace-all** (đồng pattern; lưu ý trade-off: chạy `tools/import_npi.py` sau sẽ overwrite WC bằng derived-from-Routing — em sẽ ghi rõ trong format hint) |
| **Q5** | Shift Pattern enum — text plain "A"/"B"/"C"/"A+B"/"A+B+C" hay UI bắt buộc 5 options? | **Text plain + UI dropdown 5 options** (entity flexibility; operator pick via UI/CSV) |
| **Q6** | Area badge — chỉ infer_area hiện tại (~10 area) hay add full 17 KNOWN_AREAS? | **Reuse 17 KNOWN_AREAS** (parity CMES; operator dễ filter sau này nếu có Area filter chips Phase 8) |
| **Q7** | Import button accept format — CSV only (hạng mục 3 pattern) hay CSV + XLSX? | **CSV only** (đồng pattern hạng mục 3; KHÔNG thêm xlsx dep; WC list nhỏ — operator dễ Save As .csv) |
| **Q8** | Search expand +ShiftPattern? | **YES** (operator filter "A+B+C 24/7 cell" hữu ích) |
| **Q9** | i18n EN+VI parity + ~20 keys mới? | **YES** |
| **Q10** | PR strategy gộp 1 PR? | **YES** |

---

## 7) Sau khi anh chốt — em sẽ:

1. Tạo branch `feat/phase7-workcenter` base `main`
2. A→B→C SAFE migration (backup SHA256 → /tmp/wc-design.db test → live apply → verify 43 + 6 NPI tables + 5 Users + 3 IQC + 1 Spec nguyên vẹn)
3. Code entity + WorkCenterCsvTarget + UI + importer + i18n + Area badge CSS
4. `dotnet build` clean
5. Smoke test trên /tmp:
   - Re-import (derive-from-Routing) → 43 unchanged
   - Import custom CSV với 2 rows mới + 1 row update → verify replace-all
   - Validation: thử CSV với code "x" → reject, code "MACHINE_42" → accept
6. Verify Machine + ProductionLog tables không thay đổi (vùng cấm)
7. Mở PR, **STOP chờ anh review + merge**
8. Sau merge → viết close-out report `docs/PHASE7-CLOSEOUT-REPORT.md` tổng kết 5 PR

---

**STOP — chờ anh duyệt Q1–Q10 + xác nhận hard constraints (Machine entity / ProductionLog / 4 NPI tabs + Spec + IQC không đụng) trước khi em tạo branch `feat/phase7-workcenter`.**
