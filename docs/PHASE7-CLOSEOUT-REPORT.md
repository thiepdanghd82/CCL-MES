# PHASE 7 — CLOSE-OUT REPORT

> 5 PR đợt NPI đã merged vào `main` (PR #21–#26). Báo cáo tổng kết trạng
> thái 5 tab + infrastructure dùng chung + data baseline cuối + carry-over
> cho Phase 8+.

---

## 1) PR timeline

| PR | Date | Branch | Mục tiêu | LOC |
|---|---|---|---|---|
| **#21** | 2026-05-31 | `feat/phase7-engineer-structure` | Structure 16 cols + freeze + Columns toggle + lập pattern `rt-*` | ~1,200 |
| **#22** | 2026-05-31 | `feat/phase7-npi-import-csv` | Bổ sung Hạng mục 1 — `NpiImportService` infrastructure + `ICsvImportTarget<T>` generic + 3-step `NpiImportModal` | ~900 |
| **#23** | 2026-05-31 | `feat/phase7-engineer-routine` | Routine 20 cols + reuse infra | ~1,990 |
| **#24** | 2026-05-31 | `feat/phase7-raw-materials` | RawMaterials 28 cols + drop 5 legacy + frozen first col + 21 new fields | ~2,315 |
| **#25** | 2026-05-31 | `feat/phase7-engineer-spec` | Spec rt-* parity + Create/Approve workflow (Option B) | ~900 |
| **#26** | 2026-06-01 | `feat/phase7-work-center` | WorkCenter 6 cols + 3 new fields + WC_CODE_RE strict + 17 KNOWN_AREAS palette | ~1,925 |

**Tổng**: 6 PR (5 hạng mục + 1 infrastructure), ~9,200 LOC.

---

## 2) Trạng thái 5 tab cuối Phase 7

### 2.1 Engineer Structure (`/npi/engineer-structure`)

- **Entity**: `ManufacturingStructure` 16 fields (Migration v5 từ PR #21)
- **Data**: **20,530 rows** derived từ IFS Manufacturing Structures CSV
- **UI**: 16 cols + freeze + Columns toggle + Import CSV (Admin/Engineer)
- **RBAC**: `NpiRead` policy
- **Search**: 6 fields (parent_part, parent_desc, component_part, component_desc, planner, structure_type)

### 2.2 Engineer Routine (`/npi/engineer-routine`)

- **Entity**: `RoutingOperation` 20 fields (Migration `AddRoutingExtendedFields` PR #23)
- **Data**: **38,441 rows** từ IFS Routing Operations CSV
- **UI**: 20 cols + freeze + Columns toggle + Import CSV
- **Schema change**: 4 numeric (MachineSetupTime/LaborSetupTime/MachineRunTime/LaborRunTime) `double` → `double?` + 10 fields mới (Unit/Crew/SetupCrew/LaborClass/Alt/Effectivity/Efficiency/Site/RoutingType/Planner)
- **Search**: 7 fields

### 2.3 Raw Materials (`/npi/raw-materials`)

- **Entity**: `RawMaterial` 28 fields (Migration `AddRawMaterialExtendedFields` PR #24)
- **Data**: **2,127 rows** từ IFS Raw Materials.xlsx (operator pre-convert CSV)
- **UI**: 28 cols (12 visible / 16 hidden default) + freeze + **frozen first column PartNo** + Columns toggle + Import CSV
- **Schema change**: DROP 5 legacy proxy fields (CatalogGroup/CatalogDesc/Grp/Type/TypeDesc) + Price `double`→`double?` + ADD 21 fields mới
- **IQC coupling preserved**: `FK_IqcInspections_RawMaterials_RawMaterialId` + `IqcService.SnapshotAsync` PartNo→SupplierName lookup vẫn hoạt động
- **Search**: 6 fields (+ StatusCode + CountryOfOrigin)

### 2.4 Engineer Spec (`/npi/engineer-spec`)

- **Entity**: `Spec → SpecVersion → SpecParameter` (KHÔNG migration — entity đã đủ từ Phase 6 Bước 1+5)
- **Data**: **1 spec / 1 version / 3 parameters** (DbSeeder fixture)
- **UI**: 7 cols (ALL visible) + freeze + Columns toggle + **+ Create Spec modal** + **✓ Approve per-row** (Phase 6 backend method `SpecService.CreateAsync`/`ApproveAsync` đã có sẵn — Phase 7 hạng mục 4 chỉ wire UI)
- **RBAC**: `NpiSpecRead` (Admin/Supervisor/Engineer — NOT Qc); Mutation gate Admin/Engineer only (Supervisor R per matrix §2.C)
- **Search**: 5 fields (+ ApprovedBy + Status enum string)
- **Audit emit**: `SPEC_CREATE` + `SPEC_APPROVE`
- **Scope decision**: chốt Option B (UI + wire backend). Option C (CMES sibling Spec module — drawings/QC/blob/multi-tab) defer Phase 8+.

### 2.5 Work Center (`/npi/workcenter`)

- **Entity**: `WorkCenter` 6 fields (Migration `AddWorkCenterExtendedFields` PR #26)
- **Data**: **43 rows** derived từ Routing CSV (NOT IFS WC export — không có riêng)
- **UI**: 6 cols (ALL visible) + freeze + Columns toggle + **Area badge 17 KNOWN_AREAS palette** + Active badge + Import CSV
- **Schema change**: ADD 3 fields (IdealSpeedPcsH/ShiftPattern/Active), tất cả nullable; Migration UPDATE Sql() set Active=TRUE cho 43 row hiện tại
- **Validation**: `WC_CODE_RE = ^[A-Z0-9_-]{3,12}$` strict (REJECT + skip count)
- **Search**: 4 fields (+ ShiftPattern)

---

## 3) Infrastructure dùng chung

### 3.1 `NpiImportService` (PR #22)

`Application/Services/NpiImport/`:
- `ICsvImportTarget<TEntity>` — generic interface
- `NpiCsvParser` — RFC-4180 tolerant parser (UTF-8 BOM, CRLF, quoted fields, escaped quotes)
- `NpiImportTypes` — `CsvParseResult<TEntity>`, `CsvImportResult`, `CsvImportException`

`Web/Services/NpiImportService.cs`:
- `ApplyAsync<TEntity>(rows, target, actor, skipped, ct)` — replace-all atomic (BeginTransaction → DELETE → AddRange chunked 500 → SaveChanges + Detach → Commit)
- Auto-backup via `BackupService.CreateSnapshotAsync` TRƯỚC khi mutate (defense)
- SHA256 hash backup file
- RBAC `IsImportRole(role)` → Admin/Engineer
- Audit emit `NPI_IMPORT` per call

`Web/Shared/NpiImportModal.razor`:
- Generic 3-step wizard (Pick CSV → Preview Step 2 → Confirm Step 3)
- `[Parameter] ICsvImportTarget<TEntity> Target` injected
- Inline error display (mirror Phase 7 error key namespace)
- Max file size 100 MB

**5 concrete `ICsvImportTarget` implementations**:
- `StructureCsvTarget` (PR #21)
- `RoutineCsvTarget` (PR #23)
- `RawMaterialCsvTarget` (PR #24)
- `WorkCenterCsvTarget` (PR #26) — duy nhất có strict validation `WC_CODE_RE`

### 3.2 `.rt-*` grid CSS namespace (PR #21)

`wwwroot/css/site.css` (~250 LOC tổng):
- `.rt-page` (page container)
- `.rt-breadcrumb` + `.rt-toolbar` + `.rt-title` + `.rt-search`
- `.rt-table-wrap` + `.rt-table` + `.rt-num` + `.rt-right`
- Freeze sticky thead `max-height: calc(100vh - 240px)` + `position:sticky`
- `.rt-cols-wrap` + `.rt-cols-pop` popover (Columns toggle)
- `.rt-format-hint` (yellow inline alert cho Import context)
- `.rt-sticky-col` (frozen first column — chỉ RawMaterials dùng)

### 3.3 Status / Area badge palettes

- `.rm-status--*` (active/inactive/draft/review/approved/obsolete + IFS numeric 1/2/4) — shared RawMaterials + Spec + WorkCenter
- `.wc-area--*` (17 KNOWN_AREAS palette: cnc/diecut/drying/finishing/flexo/forming/indigo/inspection/lamination/letterpress/manual/prep/punch/qc/rdc/silkscreen/special + fallback `--other`)
- `.spec-*` (form + params table + approve btn)

### 3.4 i18n keys

**Tổng 200+ keys mới** dưới namespace `npi.*`:
- `npi.import.*` (~25 keys — shared, defined ở PR #22)
- `npi.structure.*` (~25 keys)
- `npi.routine.*` (~30 keys)
- `npi.rawmaterials.*` (~40 keys)
- `npi.spec.*` (~35 keys)
- `npi.workcenter.*` (~20 keys)

**EN + VI parity** giữ chặt — 0 keys missing translation.

### 3.5 RBAC matrix §2.C (Phase 6 Bước 4)

| Surface | Admin | Supervisor | Engineer | QC | Operator |
|---|---|---|---|---|---|
| `/npi/engineer-routine` | RW | R | RW | R | – |
| `/npi/engineer-structure` | RW | R | RW | R | – |
| `/npi/engineer-spec` | RW | R | RW | – | – |
| `/npi/raw-materials` | RW | R | RW | R | – |
| `/npi/workcenter` | RW | R | RW | R | – |

**Import button** mọi tab gated `<AuthorizeView Roles="Admin,Engineer">`. Defense-in-depth: `NpiImportService.IsImportRole(role)` server-side validate. **Supervisor R only** trên 5 tab — bám đúng matrix, không tự nới quyền.

### 3.6 AuditAction codes

Đã có sẵn (Phase 6 Bước 5): `SpecCreate`, `SpecApprove`, `IqcCreate`, `IqcApprove`.
Phase 7 thêm: `NpiImport` (PR #22).

---

## 4) Data baseline cuối Phase 7

```
=== Live DB state post-PR #26 ===
WorkCenters             43        + 43/43 Active=TRUE
RoutingOperations       38,441
ManufacturingStructures 20,530
RawMaterials            2,127
Specs                   1
SpecVersions            1
SpecParameters          3
Users                   5
IqcInspections          3
Machines                1         (vùng cấm)
ProductionLogs          0         (vùng cấm)
DowntimeReasons         4         (vùng cấm)
```

**SHA256 backup chain** đầy đủ:
- `data/ccl_mes.backup-phase7-routine-pre.db` (`3b2c435f…83e`)
- `data/ccl_mes.backup-phase7-rawmat-pre.db` (`c08b5602…841`)
- `data/ccl_mes.backup-phase7-spec-pre.db` (`460cb4bc…f0c`)
- `data/ccl_mes.backup-phase7-wc-pre.db` (`e6cefad8…b3d`)

---

## 5) Vùng cấm preserved (verified mỗi PR)

5 PR Phase 7 không đụng:

- ✅ **Ops Control v1.2** (sibling, read-only)
- ✅ **CMES sibling** (read-only reference cho UI/UX patterns)
- ✅ **SpecHub** sibling project
- ✅ **Old ver** folder
- ✅ **Machine entity + ProductionLog + DowntimeReason** (concept khác WorkCenter)
- ✅ **IQC entity coupling** (PartNo + SupplierName + Id preserved qua RawMaterials migration)
- ✅ **RoutingOperation.WorkCenterNo** (string field, không phải FK — WC migration không vỡ Routing)
- ✅ **Product/Customer entity** (Spec.ProductId FK target preserved)
- ✅ **About.razor count** vẫn đếm rows, không phụ thuộc schema cố định

---

## 6) Carry-over cho Phase 8+

### 6.1 Đã commit nhưng chờ Phase 8 setup (khuyến nghị enable)

| ID | Mô tả | Trigger |
|---|---|---|
| CO-1 | **Phase 8 PR #27** — Work Center right-click context menu + Get Info modal (Open/Edit/Copy/Toggle Active) | Đã build trong commit này |

### 6.2 Backlog technical debt (đề xuất Phase 8.x)

| ID | Mô tả | Estimate | Priority |
|---|---|---|---|
| TD-1 | **PagingHelper.PageAsync** đang shared 4 NPI service methods + 1 Spec; cần refactor để generic accept `IIncludableQueryable<TSrc, TJoined>` (Spec dùng nested Include) | M | P3 |
| TD-2 | **IPQC/OQC Create Spec modal** — hiện chỉ có Engineer Spec Create modal; IPQC/OQC plan + capture vẫn dùng API only | L | P2 |
| TD-3 | **Test framework Spec.razor + WC modals** — không có unit test cho 3 modals + helpers (`WorkingHrs`, `DailyCapDisplay`, etc.). Cần Bunit / Razor component testing | L | P3 |
| TD-4 | **Machine ↔ WorkCenter mapping** — quyết định mapping convention (1-1, N-1, code match, etc.) để enable section "Recent production" trong WC Info modal | XS | P3 |
| TD-5 | **NpiCsvParser xlsx support** — operator hiện tại pre-convert xlsx → csv. Thêm xlsx parser via ClosedXML | M | P4 |
| TD-6 | **CMES sibling Spec parity** (Option C deferred từ hạng mục 4) — drawings + QC capture + blob storage + multi-tab editor | XL | P3 |
| TD-7 | **WC InReview workflow** — currently Direct Draft → Approved; thêm InReview transition + reviewer field | M | P4 |

### 6.3 Operational backlog

| ID | Mô tả | Owner |
|---|---|---|
| OPS-1 | Update operator manual với 5 NPI tabs (Import CSV procedure + WC_CODE_RE rule) | Ops |
| OPS-2 | Periodic re-import schedule (Structure + Routine từ IFS daily; RawMaterials weekly; WC on-demand) | Ops |
| OPS-3 | Audit log retention policy cho `NPI_IMPORT` events (replace-all → operator có thể bulk-delete; archive nightly?) | Compliance |

---

## 7) Lessons learned (Phase 7 specific)

1. **Provider-agnostic migration ≡ strip `type:` + Designer/Snapshot `HasColumnType`** — EF Core SQLite provider tự add column type annotations vào migration .cs; script Python 3.2.B remove ngay sau khi `dotnet ef migrations add`. Snapshot kept WITH HasColumnType vì là internal EF state — only per-migration Designer + migration .cs stripped.

2. **EF migrations add cần rebuild trước apply** — PendingModelChangesWarning trigger khi compiled DLL vs entity model ra of sync. Workflow: edit entity → `dotnet build` → `dotnet ef migrations add` → `dotnet build` → `dotnet ef database update`.

3. **Razor name collision với entity** — `WorkCenter.razor` page class implicit name conflicts với `WorkCenter` entity. Fix: `@using WCEntity = CCL.MES.Domain.Entities.WorkCenter` alias.

4. **CMES sibling project ≠ uniform pattern** — 4/5 hạng mục mirror được CMES grid (Structure/Routine/RawMaterials/WorkCenter); hạng mục 4 (Spec) thì CMES module hoàn toàn khác (document mgmt, không phải grid). Plan phải scope theo entity domain CCL-CMES, không bê CMES blanket.

5. **WC source data = derived from Routing** — không có IFS WC export riêng. Import CSV mở khả năng add WC không phụ thuộc Routing (vd cell test). Trade-off: chạy `tools/import_npi.py` sau sẽ overwrite — format hint cảnh báo operator.

6. **RBAC bám matrix, KHÔNG tự nới quyền** — Supervisor R only trên `/npi/engineer-spec`; mutation gate phải Admin+Engineer (NOT Supervisor) đúng §2.C. Easy to drift nếu copy-paste pattern.

7. **3 cột mới với 1 SQL UPDATE = idempotent default** — Q2 chốt Active=TRUE cho 43 WC hiện tại không cần seed script ngoài migration. `migrationBuilder.Sql("UPDATE … WHERE Active IS NULL")` chạy 1 lần, idempotent.

---

## 8) Phase 8 entry points

Phase 7 close-out clean. Phase 8 có thể bắt đầu với (theo thứ tự ưu tiên em đề xuất):

1. **PR #27** — Work Center right-click context menu (em đang build trong commit này — bundle với close-out report).
2. **PHASE7-FIX-1** (nếu phát sinh từ operator test) — bug fixes.
3. **TD-2** (IPQC/OQC Create modal) — closes Phase 6 mutation UI gap.
4. **TD-4** (Machine↔WC mapping) — unlock Info modal "Recent production" section.

---

**Phase 7 NPI: COMPLETED 2026-06-01.**
