# PHASE 8 PR #28 — SCHEMA-ONLY (Spec → ProductRevision clean rewrite)

> Branch `feat/phase8-spec-schema` base `main`. Migration RỦI RO NHẤT từ
> đầu repo: drop+replace 3 bảng Spec/SpecVersion/SpecParameter, refactor
> FK WorkOrder.SpecVersionId → ProductRevisionId, ADD 11 entity mới. Plan
> này specify từng bước + risk gate + verify checkpoint.

---

## 0. Trạng thái baseline đã verify (pre-PR)

| Item | Trạng thái |
|---|---|
| User.Department field | ❌ KHÔNG có (`src/CCL.MES.Domain/Entities/User.cs` chỉ có Username/PasswordHash/Role/DisplayName/LastLoginAt/IsActive/MustChangePassword). **Cần ADD trong PR #28 này.** |
| IQC↔Spec FK | ✅ KHÔNG có. `IqcInspection` chỉ FK `RawMaterialId` + snapshot `PartNo` + `SupplierName`. Refactor Spec an toàn cho IQC. |
| WorkOrder↔Spec FK | ⚠️ CÓ: `WorkOrder.SpecVersionId` (nullable) + nav `SpecVersion`. 3 callsite: `WorkOrderService.cs:45`, `WorkOrderStateMachine.cs:44`, `DbSeeder.cs:83`. **Phải refactor → `ProductRevisionId`.** |
| Spec/SpecVersion/SpecParameter tables | 1 fixture (SPEC-BRD-7656-D, 1 version, 3 params Width/Height/Process) seeded bởi DbSeeder. |
| Migration history | 14 migrations. Last: `20260531163247_AddWorkCenterExtendedFields` |
| Phase 7 data baseline cần bảo toàn | Structure 20,530 / Routine 38,441 / RawMat 2,127 / WorkCenter 43 / IQC 3 / Machine 1 / Users 5 / Customer 1 / Product 1 / WorkInstruction 1 / WorkOrder 1 / ProductionLog 0 |

---

## 1. Scope chính xác — Cái nào ADD / MODIFY / DROP / KEEP

### 1.1 ADD (mới hoàn toàn)

**Entities mới — 11 class** trong `src/CCL.MES.Domain/Entities/Spec.cs` (rewrite file) + `Drawing.cs` (mới) + `ProcessCatalog.cs` (mới):

| Entity | Mô tả 1 dòng |
|---|---|
| `ProductRevision` | Replace `Spec` parent. FK `ProductId`. RevCode A/B/C. Status enum. EffectiveFrom/To. ParentRevisionId. Soft-delete fields cho PR #30. |
| `SpecMaterial` | 1:1 keyed by ProductRevisionId. Substrate type/brand, thickness, liner, adhesive type/brand, ExtraJson. |
| `SpecPrint` | 1:1. ProcessCode FK → ProcessCatalog. NumColors. ColorSpecJson. Varnish/Lamination. WhiteUnderprint. ExtraJson. |
| `SpecDiecut` | 1:1. CutProcessCode. DieId. DieType. Width/Length/CornerRadius/KissCutDepth. BleedMm. PerforationJson. CNC/Laser/Powerpunch fields. ExtraJson. |
| `SpecFinishing` | 1:1. OutputForm (roll/sheet/fanfold). LabelsPerRoll. CoreDiameter. WindingDirection. FinishingProcessesJson. ExtraJson. |
| `Drawing` | Master record per "slot" — Kind enum (CustomerDrawing/NpiPrintLayout/IpqcPrintRef/FqcChecksheet…). Title. CurrentVersionId nullable. Status. |
| `DrawingVersion` | VersionNo. FileName. StorageKey. FileHash sha256. FileSize. PreviewKey. ChangeReason. Status. SupersededByVersionId. UploadedAt/By. |
| `DrawingApproval` | FK DrawingVersionId + Role (Npi/Production/Qc) + Status (Pending/Approved/Rejected) + ActedBy/At + Comment. |
| `SpecQcWindow` | FK ProductRevisionId + Stage (IpqcPrint/IpqcCut/Fqc/Oqc) + ProcessCode optional + Title/Description/SamplePlan/Frequency + RejectAction + Status. **Bảng tạo sẵn nhưng KHÔNG UI ở PR #28** (defer Phase 9 cho QC plan editor). |
| `QcCriterion` | FK SpecQcWindowId + Seq + Name + Type + MeasureMethod + Target/Min/Max + Unit + PassCriteria + ReferenceImageKey + Required + ExtraJson. **Tạo sẵn, KHÔNG UI.** |
| `ProcessCatalog` | Lookup: Code (PK string) + Category (Print/Cut/Finishing) + DisplayNameVi/En + Description + Status + DisplayOrder. **Seed 17 codes** trong DbSeeder. |

**Enums mới** (cùng file Spec.cs):
- `ProductRevisionStatus` { Draft, InReview, Approved, Released, Superseded }
- `DrawingKind` { CustomerDrawing, NpiPrintLayout, NpiCutLayout, IpqcPrintReference, IpqcCutReference, FqcChecksheet, OqcChecksheet, CustomerApproval, InternalProof }
- `DrawingStatus` { Draft, PendingApproval, Approved, Superseded, Withdrawn }
- `DrawingVersionStatus` { Draft, PendingApproval, Approved, Rejected, Superseded }
- `DrawingApprovalRole` { Npi, Production, Qc }
- `DrawingApprovalStatus` { Pending, Approved, Rejected }
- `QcStage` { IpqcPrint, IpqcCut, Fqc, Oqc }
- `QcRejectAction` { Rework, Scrap, Escalate, RecordOnly }
- `SpecQcWindowStatus` { Draft, Approved, Superseded }
- `QcCriterionType` { Visual, Dimensional, Colorimetric, Functional, Count }
- `ProcessCategory` { Print, Cut, Finishing }
- `ProcessCatalogStatus` { Active, Deprecated }

**Infrastructure**:
- `IBlobStore` abstraction (stub, KHÔNG implementation; thực ở PR #31) tại `src/CCL.MES.Application/Storage/IBlobStore.cs`
- DbSets mới trong `IMesDbContext` + `MesDbContext`: ProductRevisions, SpecMaterials, SpecPrints, SpecDiecuts, SpecFinishings, Drawings, DrawingVersions, DrawingApprovals, SpecQcWindows, QcCriteria, ProcessCatalogs

### 1.2 MODIFY

| File | Thay đổi |
|---|---|
| `User.cs` | ADD `string? Department` (nullable string, default null). Comment: "Phase 8 PR #28 — required cho Drawing 3-role approval mapping (Q5)". |
| `WorkOrder.cs` | REPLACE `public long? SpecVersionId` → `public long? ProductRevisionId`. REPLACE nav `public SpecVersion? SpecVersion` → `public ProductRevision? ProductRevision`. |
| `WorkOrderStateMachine.cs:44` | Guard `wo.SpecVersionId is not null` → `wo.ProductRevisionId is not null`. |
| `WorkOrderService.cs:45` | `SpecVersionId = r.SpecVersionId` → `ProductRevisionId = r.ProductRevisionId`. |
| `Dtos.cs:12` (CreateWorkOrderRequest) | `long? SpecVersionId` → `long? ProductRevisionId`. |
| `DbSeeder.cs:46-87` | Rewrite Spec seed block: 1 ProductRevision (Code='A', Status=Approved) + 1 SpecPrint với ProcessCode='SILKSCREEN' + ColorSpecJson chứa params Width/Height/Process. WO points to new ProductRevision.Id. |
| `IMesDbContext.cs` | DROP `Specs/SpecVersions/SpecParameters` DbSets. ADD 11 DbSets mới. |
| `MesDbContext.cs` | Same as IMesDbContext + `OnModelCreating`: cấu hình unique index (ProductRevisions.ProductId+RevisionCode), ProcessCatalog PK = Code (string). |
| `SpecService.cs` | Rewrite toàn bộ: `SpecsAsync` đọc ProductRevision; `CreateAsync` ADD ProductRevision + SpecMaterial/Print empty defaults; `ApproveAsync` cập nhật ProductRevision.Status=Approved. RBAC NpiSpecRead unchanged. Audit `SpecCreate`/`SpecApprove` codes unchanged. |
| `Dtos.cs` | ADD `CreateProductRevisionRequest` DTO + `ProductRevisionListItem` for grid binding. Keep `CreateSpecRequest` legacy nhưng sẽ rename internally. |
| `EngineerSpec.razor` | Adjust cột để đọc ProductRevision shape: spec_code → revision_code, title → product_code+description. Giữ Approve button — gọi method mới `ApproveRevisionAsync`. Grid render OK. |
| `CreateSpecModal.razor` | Adjust form: SpecCode + Title → RevisionCode + ProductId + minimal SpecPrint fields (NumColors). Defer richer form sang PR #29-30. **Compromise**: PR #28 chỉ wire create với ProductRevision + 1 SpecPrint empty, full editor form đẩy PR #29. |

### 1.3 DROP (sau khi migrate data)

- Bảng `Specs`, `SpecVersions`, `SpecParameters`
- FK `FK_SpecParameters_SpecVersions_SpecVersionId`
- FK `FK_WorkOrders_SpecVersions_SpecVersionId`
- Column `WorkOrders.SpecVersionId`
- Entity classes `Spec`, `SpecVersion`, `SpecParameter`, enum `SpecStatus` (tất cả trong `Domain/Entities/Spec.cs` — file được rewrite)

### 1.4 KEEP (vùng cấm — KHÔNG đụng)

- `Machine`, `ProductionLog`, `DowntimeReason` entities
- `IqcInspection`, `IqcResultDetail` (IQC = 3 baseline)
- `QcInspection`, `QcResultDetail` (FQC/OQC runtime)
- 4 NPI tabs khác: `ManufacturingStructure`, `RoutingOperation`, `RawMaterial`, `WorkCenter`
- `Customer`, `Product` (chỉ ADD reverse nav `List<ProductRevision> Revisions` lên Product, KHÔNG đụng existing fields)
- `AuditLog` + `IAuditWriter` + Phase 7 `NpiImportService` infrastructure
- Other Ops Control v1.2 / CMES sibling / Old ver / SpecHub

---

## 2. A→B→C SAFE migration plan

### 2.1 Bước A — Backup + SHA256 (live DB)

```bash
cd "/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/CCL-MES"
LIVE_DB="src/CCL.MES.Web/App_Data/mes.db"
BACKUP_DIR="/tmp/phase8-pr28-backup"
mkdir -p "$BACKUP_DIR"
TS=$(date -u +%Y%m%dT%H%M%SZ)
cp "$LIVE_DB" "$BACKUP_DIR/mes.db.$TS.bak"
shasum -a 256 "$BACKUP_DIR/mes.db.$TS.bak" | tee "$BACKUP_DIR/mes.db.$TS.sha256"
```

### 2.2 Bước B — Design-time isolated test trên `/tmp/spec-design.db`

```bash
# Tạo bản copy chỉ dùng để generate migration + verify
cp "$BACKUP_DIR/mes.db.$TS.bak" "/tmp/spec-design.db"

# Verify row count baseline TRƯỚC khi áp migration
sqlite3 "/tmp/spec-design.db" <<'SQL'
SELECT 'ManufacturingStructures', COUNT(*) FROM ManufacturingStructures
UNION ALL SELECT 'RoutingOperations', COUNT(*) FROM RoutingOperations
UNION ALL SELECT 'RawMaterials', COUNT(*) FROM RawMaterials
UNION ALL SELECT 'WorkCenters', COUNT(*) FROM WorkCenters
UNION ALL SELECT 'IqcInspections', COUNT(*) FROM IqcInspections
UNION ALL SELECT 'Machines', COUNT(*) FROM Machines
UNION ALL SELECT 'Users', COUNT(*) FROM Users
UNION ALL SELECT 'Customers', COUNT(*) FROM Customers
UNION ALL SELECT 'Products', COUNT(*) FROM Products
UNION ALL SELECT 'WorkInstructions', COUNT(*) FROM WorkInstructions
UNION ALL SELECT 'WorkOrders', COUNT(*) FROM WorkOrders
UNION ALL SELECT 'Specs', COUNT(*) FROM Specs
UNION ALL SELECT 'SpecVersions', COUNT(*) FROM SpecVersions
UNION ALL SELECT 'SpecParameters', COUNT(*) FROM SpecParameters;
SQL

# Generate migration (design-time)
MES_CONNSTR="Data Source=/tmp/spec-design.db" \
  dotnet ef migrations add AddProductRevisionSchema \
  --project src/CCL.MES.Infrastructure \
  --startup-project src/CCL.MES.Web

# Provider-agnostic clean: strip `type:` / `oldType:` từ .cs file (KHÔNG đụng Designer.cs)
# (sẽ làm bằng tool sau khi check diff)

# Apply on /tmp test DB
MES_CONNSTR="Data Source=/tmp/spec-design.db" \
  dotnet ef database update --project src/CCL.MES.Infrastructure --startup-project src/CCL.MES.Web

# Verify on /tmp test DB
sqlite3 "/tmp/spec-design.db" <<'SQL'
-- New tables tồn tại
SELECT name FROM sqlite_master WHERE type='table' AND name IN (
  'ProductRevisions','SpecMaterials','SpecPrints','SpecDiecuts','SpecFinishings',
  'Drawings','DrawingVersions','DrawingApprovals','SpecQcWindows','QcCriteria','ProcessCatalogs'
);
-- Old tables đã drop
SELECT name FROM sqlite_master WHERE type='table' AND name IN ('Specs','SpecVersions','SpecParameters');
-- Data migrated: 1 ProductRevision
SELECT COUNT(*) AS pr_count, COUNT(*) FILTER (WHERE RevisionCode='A') AS rev_a FROM ProductRevisions;
-- WO FK đã thay
PRAGMA table_info('WorkOrders');
SELECT WoNo, ProductRevisionId FROM WorkOrders;
-- Row counts khác KHÔNG đổi
SELECT 'ManufacturingStructures', COUNT(*) FROM ManufacturingStructures
UNION ALL SELECT 'RoutingOperations', COUNT(*) FROM RoutingOperations
UNION ALL SELECT 'RawMaterials', COUNT(*) FROM RawMaterials
UNION ALL SELECT 'WorkCenters', COUNT(*) FROM WorkCenters
UNION ALL SELECT 'IqcInspections', COUNT(*) FROM IqcInspections;
-- ProcessCatalog seeded 17 codes
SELECT Code, Category, DisplayNameEn FROM ProcessCatalogs ORDER BY Category, DisplayOrder;
-- User.Department column added
PRAGMA table_info('Users');
SQL
```

**GATE B1**: Không lỗi compile EF migration · Không có CASCADE accidental · Row counts unchanged trừ Specs(0)/SpecVersions(0)/SpecParameters(0) + ProductRevisions(1) + ProcessCatalogs(17).

**GATE B2**: `SELECT WoNo, ProductRevisionId FROM WorkOrders` returns `WO-26-3683, <new-rev-id>` (KHÔNG NULL, đã migrate đúng FK).

### 2.3 Bước C — Áp migration vào LIVE DB

Sau khi B1+B2 PASS:

```bash
# Stop running server (nếu có)
pkill -f "CCL.MES.Web" || true

# Final backup live
cp "$LIVE_DB" "$BACKUP_DIR/mes.db.pre-apply.$TS.bak"
shasum -a 256 "$BACKUP_DIR/mes.db.pre-apply.$TS.bak"

# Apply live
MES_CONNSTR="Data Source=$LIVE_DB" \
  dotnet ef database update --project src/CCL.MES.Infrastructure --startup-project src/CCL.MES.Web

# Verify live cùng query như B
sqlite3 "$LIVE_DB" "SELECT WoNo, ProductRevisionId FROM WorkOrders;"
sqlite3 "$LIVE_DB" "SELECT COUNT(*) FROM ProductRevisions; SELECT COUNT(*) FROM ProcessCatalogs;"
sqlite3 "$LIVE_DB" "SELECT 'ManufacturingStructures', COUNT(*) FROM ManufacturingStructures UNION ALL SELECT 'RoutingOperations', COUNT(*) FROM RoutingOperations UNION ALL SELECT 'RawMaterials', COUNT(*) FROM RawMaterials UNION ALL SELECT 'WorkCenters', COUNT(*) FROM WorkCenters;"
```

**GATE C**: Live DB row counts == B1 verify counts. WO baseline still has valid ProductRevisionId.

### 2.4 Bước D — Restart no-op verify

```bash
# Restart embedded server
cd src/CCL.MES.Web
dotnet run --launch-profile https &
SERVER_PID=$!
sleep 5
# /npi/engineer-spec grid render OK?
curl -sk -H "Cookie: <session>" https://localhost:5001/npi/engineer-spec | grep -i "engineer.*spec\|product.*revision" | head -3
kill $SERVER_PID
```

**GATE D**: Server boot không lỗi. EngineerSpec grid render 1 row (SPEC-BRD-7656-D migrated as Rev A). Pager hiển thị "1 rows".

### 2.5 Rollback playbook (nếu bất kỳ gate fail)

```bash
# Restore live DB từ backup
cp "$BACKUP_DIR/mes.db.pre-apply.$TS.bak" "$LIVE_DB"
shasum -a 256 "$LIVE_DB"  # phải match backup SHA
# Drop migration file
git restore --staged --worktree src/CCL.MES.Infrastructure/Migrations/<new-migration>*.cs
# Revert entity + service edits
git checkout -- src/
# Restart server
```

---

## 3. Data migration spec (legacy → new shape)

### 3.1 Source data (baseline DbSeeder)

```
Spec(id=1, SpecCode='SPEC-BRD-7656-D', Title='PCB ID Label 20x8mm', ProductId=1)
SpecVersion(id=1, SpecId=1, VersionNo=1, Status=Approved, EffectiveDate=<seedTime>,
            ApprovedBy='qa.lead', ApprovedAt=<seedTime>)
SpecParameter(id=1, SpecVersionId=1, ParamName='Width', Nominal='20', TolMin='19.9', TolMax='20.1', Uom='mm', IsCritical=true)
SpecParameter(id=2, SpecVersionId=1, ParamName='Height', Nominal='8', TolMin='7.9', TolMax='8.1', Uom='mm', IsCritical=true)
SpecParameter(id=3, SpecVersionId=1, ParamName='Process', Nominal='Silkscreen + Diecut', IsCritical=false)
WorkOrder(id=1, WoNo='WO-26-3683', SpecVersionId=1, ...)
```

### 3.2 Target mapping (in migration Up() data block)

```sql
-- Step 1: Migrate Spec/SpecVersion → ProductRevision
INSERT INTO ProductRevisions (Id, ProductId, RevisionCode, Status, EffectiveFrom, ApprovedBy, ApprovedAt, ChangeSummary, CreatedAt, IsTrashed)
  SELECT
    sv.Id,                                                          -- preserve PK so WO FK trivial remap
    s.ProductId,
    'A',                                                            -- 1st rev = letter A
    sv.Status,                                                      -- TEXT 'Approved' (HasConversion<string>)
    sv.EffectiveDate,
    sv.ApprovedBy,
    sv.ApprovedAt,
    'Migrated from Spec/SpecVersion (PR #28 schema refactor)',
    s.CreatedAt,
    0
  FROM SpecVersions sv
  INNER JOIN Specs s ON sv.SpecId = s.Id;

-- Step 2: Migrate SpecParameters → SpecPrint.ColorSpecJson (as JSON array)
INSERT INTO SpecPrints (ProductRevisionId, ProcessCode, NumColors, ColorSpecJson, CreatedAt)
  SELECT
    sv.Id,
    'SILKSCREEN',                                                   -- baseline spec process
    0,                                                              -- num_colors unknown from params; PR #29 form will enrich
    '[' || GROUP_CONCAT(
      json_object(
        'param_name',  sp.ParamName,
        'nominal',     sp.Nominal,
        'tol_min',     sp.TolMin,
        'tol_max',     sp.TolMax,
        'uom',         sp.Uom,
        'is_critical', sp.IsCritical
      )
    ) || ']',
    datetime('now')
  FROM SpecVersions sv
  LEFT JOIN SpecParameters sp ON sp.SpecVersionId = sv.Id
  GROUP BY sv.Id;

-- Step 3: Migrate WorkOrders.SpecVersionId → ProductRevisionId
ALTER TABLE WorkOrders ADD COLUMN ProductRevisionId INTEGER NULL REFERENCES ProductRevisions(Id);
UPDATE WorkOrders SET ProductRevisionId = SpecVersionId;
-- (Drop SpecVersionId column happens later via EF DropColumn)
```

**Quan trọng**: preserve `ProductRevision.Id == SpecVersion.Id` để WO FK remap trivial (1 UPDATE thay vì JOIN). Sau migration, `ProductRevision.Id=1` ↔ `WorkOrder.ProductRevisionId=1`.

### 3.3 EF migration code shape

Migration sẽ có pattern:
```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    // 1) CREATE new tables (CreateTable cho 11 entity mới)
    migrationBuilder.CreateTable(name: "ProductRevisions", ...);
    migrationBuilder.CreateTable(name: "SpecMaterials", ...);
    // ...

    // 2) ADD Users.Department column
    migrationBuilder.AddColumn<string>(name: "Department", table: "Users", nullable: true);

    // 3) ADD WorkOrders.ProductRevisionId column (nullable)
    migrationBuilder.AddColumn<long>(name: "ProductRevisionId", table: "WorkOrders", nullable: true);

    // 4) SEED ProcessCatalogs (17 codes)
    migrationBuilder.Sql("INSERT INTO ProcessCatalogs (Code, Category, DisplayNameVi, DisplayNameEn, Status, DisplayOrder, CreatedAt) VALUES ...");

    // 5) DATA MIGRATION (3 Sql blocks per §3.2)
    migrationBuilder.Sql("INSERT INTO ProductRevisions (...) SELECT ... FROM SpecVersions sv INNER JOIN Specs s ...");
    migrationBuilder.Sql("INSERT INTO SpecPrints (...) SELECT ... FROM SpecVersions sv LEFT JOIN SpecParameters sp ...");
    migrationBuilder.Sql("UPDATE WorkOrders SET ProductRevisionId = SpecVersionId WHERE SpecVersionId IS NOT NULL;");

    // 6) ADD FK WorkOrders.ProductRevisionId → ProductRevisions
    migrationBuilder.CreateIndex(name: "IX_WorkOrders_ProductRevisionId", table: "WorkOrders", column: "ProductRevisionId");
    migrationBuilder.AddForeignKey(name: "FK_WorkOrders_ProductRevisions_ProductRevisionId", table: "WorkOrders", column: "ProductRevisionId", principalTable: "ProductRevisions", principalColumn: "Id");

    // 7) DROP FK WorkOrders→SpecVersions, DROP column WorkOrders.SpecVersionId
    migrationBuilder.DropForeignKey(name: "FK_WorkOrders_SpecVersions_SpecVersionId", table: "WorkOrders");
    migrationBuilder.DropIndex(name: "IX_WorkOrders_SpecVersionId", table: "WorkOrders");
    migrationBuilder.DropColumn(name: "SpecVersionId", table: "WorkOrders");

    // 8) DROP legacy Spec tables (FK order: SpecParameters → SpecVersions → Specs)
    migrationBuilder.DropTable(name: "SpecParameters");
    migrationBuilder.DropTable(name: "SpecVersions");
    migrationBuilder.DropTable(name: "Specs");
}
```

Down() = reverse cẩn thận: recreate Specs/SpecVersions/SpecParameters, restore WO.SpecVersionId, drop new tables.

---

## 4. Provider-agnostic clean

Sau khi EF generate, em sẽ:
1. Mở file migration `.cs` (NOT Designer.cs)
2. Search-replace bỏ mọi `type:` và `oldType:` argument trong `migrationBuilder.AddColumn` / `AlterColumn` calls
3. KHÔNG đụng Designer.cs hoặc MesDbContextModelSnapshot.cs
4. KHÔNG đụng các .cs file của migration cũ
5. Verify build pass sau strip

Pattern strip tham chiếu Phase 7 PR #26 (commit `93e0871`).

---

## 5. RBAC + i18n

### 5.1 RBAC unchanged

- Page-level: `[Authorize(Policy = "NpiSpecRead")]` trên EngineerSpec.razor — KHÔNG đổi
- Action-level: `<AuthorizeView Roles="Admin,Engineer">` cho Create + Approve — KHÔNG đổi
- Audit codes `SpecCreate` + `SpecApprove` — KHÔNG đổi (PR #30 sẽ ADD `SpecRevise`/`SpecCopy`/`SpecTrash`/`SpecRestore`/`SpecPurge` etc.)

### 5.2 i18n keys mới (PR #28 chỉ thêm strict minimum)

Trong PR #28 chỉ thêm những i18n key cần thiết để EngineerSpec grid render mới (đọc ProductRevision shape):

| Key | EN | VI |
|---|---|---|
| `npi.spec.col.revision_code` | Rev | Rev |
| `npi.spec.col.product_code` | Product | Sản phẩm |
| `npi.spec.col.effective_from` | Effective From | Hiệu lực từ |
| `npi.spec.col.process` | Process | Công đoạn |
| `npi.spec.status.released` | Released | Phát hành |

Các key cho Material/Print/Diecut/Finishing/Drawing/QC labels sẽ đẩy PR #29-32 khi UI thực sự dùng (avoid pre-add bulk keys không dùng).

---

## 6. Verify checklist (post-implementation)

| # | Check | Pass criteria |
|---|---|---|
| V1 | `dotnet build` | 0 errors, 0 warnings related to Spec |
| V2 | EngineerSpec grid render | 1 row, displaying SPEC-BRD-7656-D (or its ProductRevision shape) |
| V3 | Create Spec modal opens + submits | New ProductRevision row inserted |
| V4 | Approve button works | Status flips Draft → Approved |
| V5 | Row count baseline | Structure 20,530 / Routine 38,441 / RawMat 2,127 / WC 43 / IQC 3 / Machine 1 / Users 5 / WO 1 / ProductionLog 0 |
| V6 | ProductRevisions table | 1 row (migrated) |
| V7 | ProcessCatalogs table | 17 rows (seeded) |
| V8 | Specs/SpecVersions/SpecParameters tables | NOT EXIST (dropped) |
| V9 | WorkOrders.SpecVersionId column | NOT EXIST (dropped) |
| V10 | WorkOrders.ProductRevisionId | Demo WO has valid non-null FK |
| V11 | Users.Department column | EXISTS, all rows NULL |
| V12 | IQC FK `FK_IqcInspections_RawMaterials_RawMaterialId` | PRESERVED |
| V13 | Restart no-op | Server boots, no migration triggered again |
| V14 | `npm test`-equivalent | (CMES doesn't have test suite per Phase 7 state — skip) |

---

## 7. STOP checkpoints

Em sẽ STOP nếu:
1. **Gate B1 fail** (compile migration error / accidental CASCADE) → revert, file ticket
2. **Gate B2 fail** (WO ProductRevisionId NULL) → investigate, không apply live
3. **Gate C fail** (row count differ) → restore backup, file ticket
4. **Gate D fail** (server boot error / grid crash) → restore backup, hotfix or revert PR
5. **Any vùng cấm bị đụng** (Machine/ProductionLog/DowntimeReason/IQC FK/Phase 7 tabs touched) → revert immediately

---

## 8. Final scope deliverables

- 11 entity mới + 3 entity modify (User + WorkOrder + Dtos) + 3 entity drop (Spec + SpecVersion + SpecParameter)
- 1 migration file `20260601xxxxxx_AddProductRevisionSchema.cs` + Designer
- 1 IBlobStore abstraction stub
- DbSeeder rewrite Spec block
- SpecService rewrite + EngineerSpec.razor adjust + CreateSpecModal.razor adjust
- 5 i18n keys EN+VI
- Plan này (`docs/PHASE8-PR28-PLAN.md`)
- PR #28 description với migration verification log
