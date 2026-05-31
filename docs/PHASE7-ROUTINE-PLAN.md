# PHASE 7 — HẠNG MỤC 2: Engineer Routine

> Khảo sát + plan đồng bộ tab **Engineer Routine** (Routing Operations) theo
> CMES tham chiếu. **Chưa code** — chờ anh duyệt Q1–Q6 trước khi tạo branch.
>
> Pattern tái dùng 100% từ hạng mục 1 (Structure): A→B→C SAFE migration,
> provider-agnostic 3.2.B strip, `ICsvImportTarget<TEntity>` + `NpiImportService`
> + `NpiImportModal<TEntity>` đã sẵn sàng. Hạng mục 2 chỉ thêm
> `RoutineCsvTarget` + 9 cột entity + 1 migration + UI redesign.

## 1) State hiện tại (CCL-CMES `main` post-PR #22)

### 1.1 Entity `RoutingOperation` (`src/CCL.MES.Domain/Entities/Npi.cs`)

10 field, 4 numeric đều là `double` (non-nullable):

| Field | Type | Note |
|---|---|---|
| PartNo | string | required |
| PartDescription | string? | |
| OpNo | string? | |
| Operation | string? | |
| WorkCenterNo | string? | |
| WorkCenterDescription | string? | |
| MachineSetupTime | **double** | 0 nếu CSV trống → mất phân biệt 0 vs missing |
| LaborSetupTime | **double** | 0 nếu CSV trống |
| MachineRunTime | **double** | 0 nếu CSV trống |
| LaborRunTime | **double** | 0 nếu CSV trống (KHÔNG hiển thị trên UI hiện tại) |

### 1.2 UI `EngineerRoutine.razor`

9 cột, không có freeze header, không có Columns toggle, không có Import button:
`Part No | Part Description | Op No | Operation | Work Center | WC Description | Mach Setup | Labor Setup | Mach Run`

Thiếu so với CMES: **Labor Run + 9 cột mới + freeze + Columns toggle + Import**.

### 1.3 Importer (`tools/import_npi.py:read_routing`)

Đọc 13 cột đầu của IFS CSV `RoutingOperations *.csv` (file có 62 cột total).
9 cột CMES cần đều có sẵn trong IFS export — chỉ chưa map.

### 1.4 Search hiện tại

`NpiService.RoutingAsync` filter 4 field: `PartNo`, `PartDescription`, `Operation`, `WorkCenterNo`.

---

## 2) Gap với CMES tham chiếu

### 2.1 Columns mapping (19 cột CMES UI ↔ IFS CSV cols ↔ CCL-CMES action)

| # | CMES field | CMES type | IFS CSV column | CSV idx | Đã có? | Action |
|---|---|---|---|---|---|---|
| 1 | part_no | string | Part No | 1 | ✓ PartNo | keep |
| 2 | part_desc | string | Part Description | 3 | ✓ PartDescription | keep |
| 3 | op_no | string | Operation No | 2 | ✓ OpNo | keep |
| 4 | operation | string | Operation Description | 4 | ✓ Operation | keep |
| 5 | work_center | string | Work Centre No | 5 | ✓ WorkCenterNo | keep |
| 6 | wc_desc | string | Work Centre Desc | 8 | ✓ WorkCenterDescription | keep |
| 7 | mach_setup | number/null | Mach Setup Time | 9 | ✓ MachineSetupTime `double` | **Q1**: keep `double` hay `double?` |
| 8 | labor_setup | number/null | Labour Setup Time | 10 | ✓ LaborSetupTime `double` | **Q1** |
| 9 | mach_run | number/null | Mach Run Factor | 11 | ✓ MachineRunTime `double` | **Q1** |
| 10 | labor_run | number/null | Labour Run Factor | 12 | ✓ LaborRunTime `double` (ẩn trên UI hiện tại) | **Q1**; show |
| 11 | **unit** | string | Factor Unit | 13 | ✗ | **NEW** `string?` |
| 12 | **crew** | number/null | Crew Size | 20 | ✗ | **NEW** `double?` |
| 13 | **setup_crew** | number/null | Setup Crew Size | 19 | ✗ | **NEW** `double?` |
| 14 | **labor_class** | string | Labour Class | 7 | ✗ | **NEW** `string?` |
| 15 | **alt** | string | Alternative | 21 | ✗ | **NEW** `string?` |
| 16 | **effectivity** | string | Routing Effectivity | 24 | ✗ | **NEW** `string?` |
| 17 | **efficiency** | number/null | Efficiency Factor | 43 | ✗ | **NEW** `double?` |
| 18 | **site** | string | Site | 58 | ✗ | **NEW** `string?` |
| 19 | **routing_type** | string | Routing Type | 60 | ✗ | **NEW** `string?` |

### 2.2 IFS columns có sẵn nhưng CMES KHÔNG dùng (Q2/Q3 candidates)

| IFS column | CSV idx | Có dùng cho CCL không? |
|---|---|---|
| Planner | 26 | Có ích cho liên kết với Structure tab (Q9 đã chốt Planner cho Structure). **Q2**: include? |
| Cavity | 14 | Operation-level cavity (overlap với BOM Cavity ở Structure?). **Q3**: include? |
| Color Nums | 15 | Operation-level color count. **Q3**: include? |
| Pitch | 16 | Operation-level pitch (overlap với BOM Pitch ở Structure?). **Q3**: include? |
| Std Operation Name/Desc | 27, 28 | Liên kết với routing template — chưa cần ở UI grid. Skip. |
| Routing Revision | 61 | Skip (chưa có version control yêu cầu). |

### 2.3 Search field expansion (Q4)

CMES filter: `part_no, part_desc, operation, work_center, wc_desc, labor_class, op_no`.

CCL-CMES hiện tại: `PartNo, PartDescription, Operation, WorkCenterNo` (thiếu 3 field cuối).

**Q4**: mở rộng filter thêm `WorkCenterDescription + LaborClass + OpNo` cho match CMES? (`OpNo` rất hữu ích khi operator nhớ `Op No` cụ thể.)

---

## 3) Plan code (sau khi anh chốt Q1–Q6)

### 3.1 Migration v6 `AddRoutingExtendedFields`

A→B→C SAFE pattern (giống Structure):
- **A**: backup SHA256 của `cclmes.db` production
- **B**: test isolated trên `MES_CONNSTR=Data Source=/tmp/routing-design.db` — generate migration, apply, verify row count unchanged
- **C**: apply real trên `cclmes.db`, verify row count vẫn `~20,530` (giả sử IFS export Routing có số dòng tương đương)

Migration script:
- ADD COLUMN `Unit` TEXT NULL
- ADD COLUMN `Crew` REAL NULL
- ADD COLUMN `SetupCrew` REAL NULL
- ADD COLUMN `LaborClass` TEXT NULL
- ADD COLUMN `Alt` TEXT NULL
- ADD COLUMN `Effectivity` TEXT NULL
- ADD COLUMN `Efficiency` REAL NULL
- ADD COLUMN `Site` TEXT NULL
- ADD COLUMN `RoutingType` TEXT NULL
- **Q1 dependency**: nếu chốt đổi 4 numeric → `double?`, cần SQLite migration phức tạp hơn (recreate table) vì SQLite KHÔNG hỗ trợ `ALTER COLUMN`. Giải pháp: tạo table mới `RoutingOperations_v2` với schema mới + `INSERT INTO _v2 SELECT * FROM _old` (NULL coerce cho 0?) + DROP old + RENAME. EF Core scaffolds tự động pattern này khi nó detect nullability change.

Provider-agnostic strip:
- Script Python 3.2.B sẵn có sẽ remove `type:` + `.HasColumnType()` để cùng migration chạy được trên SQL Server. Chỉ run sau khi `dotnet ef migrations add`.

### 3.2 Entity update (`Npi.cs`)

```csharp
public class RoutingOperation : BaseEntity
{
    public string PartNo { get; set; } = "";
    public string? PartDescription { get; set; }
    public string? OpNo { get; set; }
    public string? Operation { get; set; }
    public string? WorkCenterNo { get; set; }
    public string? WorkCenterDescription { get; set; }
    // Q1 dependency: keep double hoặc đổi sang double?
    public double? MachineSetupTime { get; set; }
    public double? LaborSetupTime { get; set; }
    public double? MachineRunTime { get; set; }
    public double? LaborRunTime { get; set; }
    // Phase 7 hạng mục 2 — 9 field mới khớp CMES UI tham chiếu.
    public string? Unit { get; set; }
    public double? Crew { get; set; }
    public double? SetupCrew { get; set; }
    public string? LaborClass { get; set; }
    public string? Alt { get; set; }
    public string? Effectivity { get; set; }
    public double? Efficiency { get; set; }
    public string? Site { get; set; }
    public string? RoutingType { get; set; }
}
```

### 3.3 Importer update (`tools/import_npi.py`)

Mở rộng `read_routing` lên 19 column map (thêm 9 indices: 13/20/19/7/21/24/43/58/60).
`insert_routing` INSERT statement thêm 9 cột tương ứng.
SQL upgraded từ 11 column tuple → 20 column tuple.

### 3.4 UI redesign (`Pages/Npi/EngineerRoutine.razor`)

100% pattern từ `EngineerStructure.razor`:
- 19 ColumnDef array (mirror CMES `COLUMNS` literal)
- Freeze header via `.rt-table-wrap` + `sticky thead`
- Columns toggle popover + localStorage `cclmes.engineer-routine.columns-hidden.v1`
- Import button (AuthorizeView Admin/Engineer) → `<NpiImportModal TEntity="RoutingOperation">`
- Search-as-you-type + Enter trigger
- Pager 50/page (giữ Pager cũ giống Structure)
- Format helpers: `FmtNum(v, digits)` cho numeric, `FmtStr` cho string

### 3.5 `RoutineCsvTarget` (concrete `ICsvImportTarget<RoutingOperation>`)

```csharp
public sealed class RoutineCsvTarget : ICsvImportTarget<RoutingOperation>
{
    public string TableName => "RoutingOperations";
    public string EntityKey => "routine";
    public int MinColumnCount => 13;  // mach_setup minimum
    public IReadOnlyList<string> RequiredFields { get; } = new[] { "part_no", "op_no" };
    public IReadOnlyDictionary<string, string[]> HeaderAliases { get; } = new Dictionary<string, string[]>
    {
        ["part_no"]     = new[] { "part no", "part_no", "partno" },
        ["part_desc"]   = new[] { "part description", "part_description", "part desc" },
        ["op_no"]       = new[] { "operation no", "op no", "op_no", "opno" },
        ["operation"]   = new[] { "operation description", "operation", "op_desc" },
        ["work_center"] = new[] { "work centre no", "work center no", "work_centre_no", "work_center" },
        ["wc_desc"]     = new[] { "work centre desc", "work center desc", "wc_desc" },
        ["mach_setup"]  = new[] { "mach setup time", "machine setup time", "mach_setup" },
        ["labor_setup"] = new[] { "labour setup time", "labor setup time", "labor_setup" },
        ["mach_run"]    = new[] { "mach run factor", "machine run factor", "mach_run" },
        ["labor_run"]   = new[] { "labour run factor", "labor run factor", "labor_run" },
        ["unit"]        = new[] { "factor unit", "unit" },
        ["crew"]        = new[] { "crew size", "crew" },
        ["setup_crew"]  = new[] { "setup crew size", "setup_crew" },
        ["labor_class"] = new[] { "labour class", "labor class", "labor_class" },
        ["alt"]         = new[] { "alternative", "alt" },
        ["effectivity"] = new[] { "routing effectivity", "effectivity" },
        ["efficiency"]  = new[] { "efficiency factor", "efficiency" },
        ["site"]        = new[] { "site" },
        ["routing_type"]= new[] { "routing type", "routing_type" },
    };
    public RoutingOperation? MapRow(string[] row, IReadOnlyDictionary<string, int> indexMap) { ... }
}
```

Engine `NpiImportService.ApplyAsync<RoutingOperation>` đã sẵn sàng — không sửa.
Modal `NpiImportModal<TEntity>` đã generic — không sửa.

### 3.6 i18n keys (EN + VI parity)

Thêm `~17 keys` cho `npi.routine.*` mirror `npi.structure.*`:
- `npi.routine.title`, `npi.routine.breadcrumb`, `npi.routine.rows_loaded`, `npi.routine.rows_count`
- `npi.routine.search_placeholder`, `npi.routine.empty`, `npi.routine.empty_filter`
- `npi.routine.btn_columns`, `npi.routine.btn_show_all`
- 19 keys `npi.routine.col.<key>` cho column labels (part_no/part_desc/...routing_type)

`npi.import.*` keys đã share — không cần thêm.

---

## 4) Scope contract (vùng cấm)

Hạng mục 2 **KHÔNG** đụng:
- Ops Control v1.2 (sibling project — read-only reference, không có entanglement)
- CMES sibling (read-only reference cho UI/UX)
- SpecHub sibling
- "Old ver" folder
- Bất kỳ tab nào của CCL-CMES không phải Engineer Routine
- Library/PermissionGroups (RBAC matrix đã có Admin/Engineer cho NpiImport — reuse)
- Audit infrastructure (AuditAction.NpiImport, IAuditWriter — reuse)

Chỉ touch:
- `src/CCL.MES.Domain/Entities/Npi.cs` (RoutingOperation entity)
- `src/CCL.MES.Infrastructure/Migrations/<timestamp>_AddRoutingExtendedFields.{cs,Designer.cs}` (new)
- `src/CCL.MES.Infrastructure/MesDbContextModelSnapshot.cs` (regen)
- `src/CCL.MES.Application/Services/NpiImport/RoutineCsvTarget.cs` (new)
- `src/CCL.MES.Web/Pages/Npi/EngineerRoutine.razor` (full rewrite)
- `src/CCL.MES.Web/Resources/SharedResource.{resx,vi.resx}` (~17 keys × 2)
- `tools/import_npi.py` (read_routing + insert_routing expand)
- `docs/PHASE7-ROUTINE-PLAN.md` (this file, untracked → commit when shipping)

---

## 5) Q-questions cần anh chốt

| Q# | Câu hỏi | Default em đề xuất | Lý do |
|---|---|---|---|
| **Q1** | 4 numeric hiện tại (`MachineSetupTime/LaborSetupTime/MachineRunTime/LaborRunTime`) — giữ `double` hay đổi `double?`? | **đổi `double?`** | (a) khớp CMES tham chiếu (b) khôi phục semantic 0 vs missing (c) consistency với 9 field mới |
| **Q2** | Include `Planner` (CSV col 26) — không có ở CMES UI nhưng hữu ích để cross-link với Structure tab Planner? | **YES, include** | Đã chốt Planner cho Structure ở hạng mục 1 — parity giúp report sau |
| **Q3** | Include `Cavity` / `Color Nums` / `Pitch` (CSV cols 14-16) — operation-level overlap với Structure BOM-level? | **NO, skip** | Tránh confusion với Structure entity (cùng tên field, khác semantic). Có thể add sau khi NPI Engineer cần. |
| **Q4** | Search filter — mở rộng từ 4 → 7 field thêm `WorkCenterDescription + LaborClass + OpNo`? | **YES** | Khớp CMES; `OpNo` rất hữu ích cho operator. |
| **Q5** | Data re-import sau migration — chạy lại `python tools/import_npi.py` để hydrate 9 field mới từ IFS CSV gốc? | **YES, re-import** | Migration chỉ ADD COLUMN NULL; 9 field mới rỗng cho hàng cũ. Re-import cần để demo Import CSV button cũng hoạt động. |
| **Q6** | PR strategy: (a) 1 PR gộp grid + import OR (b) tách thành 2 PR như hạng mục 1 (#21 grid → #22 import)? | **(a) gộp 1 PR** | Hạng mục 1 đã tách vì lý do "ship grid trước để operator dùng được sớm". Hạng mục 2 đã có Import infrastructure → không có lý do tách → ship 1 PR gọn hơn. |

---

## 6) Sau khi anh chốt — em sẽ:

1. Tạo branch `feat/phase7-engineer-routine` base `main`
2. Apply migration A→B→C SAFE pattern
3. Code entity + RoutineCsvTarget + UI + importer + i18n
4. `dotnet build` clean
5. Smoke test: load page → 19 cols + freeze + Columns toggle + Import CSV button (Admin/Engineer) → import sample CSV → verify row count + data sample
6. Mở PR (a) hoặc (b) theo Q6, **STOP chờ anh review + merge**
7. Sau merge → lặp tương tự cho hạng mục 3 Raw Materials

---

**STOP — chờ anh duyệt Q1–Q6 + xác nhận hard constraints.**
