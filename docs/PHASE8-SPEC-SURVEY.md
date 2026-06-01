# PHASE 8 — SPEC CONTROL ONLINE (SpecHub reference)

> Khảo sát-only. KHÔNG code, KHÔNG branch, KHÔNG migration. Mục tiêu cuối:
> merge full chức năng Spec control của SpecHub (READ-ONLY reference tại
> `/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/SpecHub/`) vào Engineer
> Spec của CCL-MES bằng quy trình incremental N PR/PR-1-branch, theo nguyên
> tắc A→B→C SAFE migration + reuse `rt-*` infrastructure đã có từ Phase 7.
>
> Tham chiếu SpecHub: `spechub.md`, `spechub-prototype.html` (13.5K LOC),
> `docs/02-data-model.md`, `i18n/en.json` + `i18n/vi.json`, `screenshots/07-1c-spec-library.png`.
>
> Tham chiếu CCL-MES baseline: `src/CCL.MES.Domain/Entities/Spec.cs`,
> `src/CCL.MES.Application/Services/SpecService.cs`, `src/CCL.MES.Web/Pages/Npi/EngineerSpec.razor`,
> `src/CCL.MES.Web/Shared/CreateSpecModal.razor`.

---

## 1. Inventory — SpecHub Spec features

Liệt kê toàn bộ chức năng Spec control SpecHub có (vùng "1C Spec library" + module Spec) — chia 6 nhóm:

### 1.1 Spec library catalog (browsing + filter)

| # | Feature | SpecHub source | Mô tả 1 dòng |
|---|---|---|---|
| L1 | Grid view 14 columns | `renderOneCList` @ HTML:12929 | #/Planner/REF NO/Customer/Part No/Part Name/Colors/Cavity/Pitch/Spec/Status/Rev/Rev Date/By |
| L2 | Status badge 3-state | `.onec-row-stamp` @ CSS:5449 | Approved (green) · Draft (amber) · Superseded (gray, line-through) |
| L3 | Planner badge color-coded | `SPEC_CATEGORIES` @ HTML:11312 | 6 planner types: SILK · FLEXO · LETTER · INDIGO · DIECUT · UNKNOWN — màu riêng |
| L4 | Search free-text | `oneCFiltered` filter | Khớp refNo / customer / code / description (client-side) |
| L5 | Stats panel sidebar | `statsHtml` @ HTML:13043 | 5 cards: Library Health · By Category · By Status · Recent Activity · Tips |
| L6 | Empty state CTA | `routine-empty` @ HTML:12947 | "Create new spec" hoặc "Load 6 sample specs" |
| L7 | Double-click → detail | `ondblclick="viewOneCSpec"` @ HTML:13000 | Full-screen detail view |
| L8 | Right-click → context menu | `oncontextmenu="showOneCContextMenu"` @ HTML:14112 | Open / Copy / Get Info / Delete |

### 1.2 Detail view (read + edit)

| # | Feature | SpecHub source |
|---|---|---|
| D1 | Silkscreen full layout | `renderSilkscreenSpecInto` |
| D2 | Flexo full layout | (flexo branch in `viewOneCSpec`) |
| D3 | Product info section (part name, version, size, diameter) | `productInfo` field |
| D4 | Print parameters (cavity, pitch, material) | `printParams` |
| D5 | Print rows table (color/ink code/mesh/squeegee/dry/UV/emulsion/plate) | `printRows[]` |
| D6 | Flexo printing + cutting + ink rows | `flexoData.{printingRows,cuttingRows,inkRows}` |
| D7 | Revisions chain table | `revisions[]` |
| D8 | Signatures (R&D / R&D-confirm / PD / QA) | `signatures[]` |
| D9 | Approval stamp watermark | `approvalStamp = 'APPROVED'/'DRAFT'/'Superseded'` |
| D10 | History log (append-only) | `historyLog[]` per spec (`_oneCAppendLog` HTML:12324) |

### 1.3 Lifecycle (create + revise + supersede)

| # | Feature | SpecHub source |
|---|---|---|
| C1 | Create empty draft (manual) | `createOneCManual` @ HTML:12889 |
| C2 | Import xlsx → parse → preview → save | `parseXlsxToSpec` + `openCreateOneCModal` @ HTML:11865 |
| C3 | Multi-planner xlsx parser (Silkscreen + Flexo + 4 stubs) | `SPEC_CATEGORIES` lookup + `detectSpecCategory` |
| C4 | Duplicate detection on import (by refNo) | `dupIdx` check @ HTML:11914 |
| C5 | Duplicate modes (replace / upgrade / save-as-copy / cancel) | radio modes @ HTML:11942 |
| C6 | Approved-spec freeze (block overwrite, force upgrade) | `if (isApproved) { mode=upgrade }` @ HTML:12011 |
| C7 | Revision letter incrementer (A → B → … → Z → AA) | `nextRev` @ HTML:11343 |
| C8 | Revise flow (in-place rewrite vs create new) | `openReviseDecisionModal` @ HTML:12700 |
| C9 | Reason required for revise + deep diff | `_oneCDeepDiff` @ HTML:12359 + reason textarea |
| C10 | Copy spec → independent draft with lineage pointer | `ctxCopySpec` @ HTML:14144 |
| C11 | Approve (Draft → Approved with stamp) | (per-spec finalize in `_oneCFinalize` @ HTML:12256) |
| C12 | Supersede old rev when upgrade lands | "old becomes Superseded" logic @ HTML:12032 |
| C13 | Soft-delete to Trash | `trashOneCSpec` @ HTML:12536 |
| C14 | 30-day auto-purge from Trash | `_trashedAt` + scheduled cleanup |

### 1.4 Drawings + attachments (NOT in `1C Spec`, sống ở `artwork` + `artwork_version` table per DDL)

| # | Feature | SpecHub source |
|---|---|---|
| A1 | Artwork master record per "slot" (customer_drawing / npi_print_layout / ipqc_print_reference / fqc_checksheet …) | `artwork` table @ DDL §2.10 |
| A2 | Artwork versioned content (v1, v2, v3…) | `artwork_version` table @ DDL §2.11 |
| A3 | `change_reason` mandatory v2+ | `CHECK (version_no = 1 OR change_reason IS NOT NULL)` |
| A4 | File hash SHA256 + size + preview JPEG | `file_hash`, `preview_key` columns |
| A5 | 3-role approval chain per version (NPI / Production / QC) | `artwork_approval` table @ DDL §2.12 |
| A6 | Lifecycle: approved version frozen, new upload = new version | trigger rules per `docs/02-data-model.md` §Lifecycle |
| A7 | Audit events ARTWORK_UPLOAD / APPROVE / REJECT / SUPERSEDE | enumerated in §Audit emit |

> **Lưu ý**: trong prototype HTML, artwork chỉ là field text trên spec (chưa hiện thực blob storage). Production design ở `02-data-model.md` mới đầy đủ.

### 1.5 QC plan definition (NOT in 1C Spec, lives in `spec_qc_window` + `qc_criterion` per DDL)

| # | Feature | SpecHub source |
|---|---|---|
| Q1 | QC windows per revision per stage (IPQC_PRINT / IPQC_CUT / FQC / OQC) | `spec_qc_window` table |
| Q2 | Process-scope restriction (vd: FLEXO-only IPQC) | `process_code` nullable |
| Q3 | Sample plan + frequency + reject_action | columns |
| Q4 | Criterion list per window (8 avg per window) | `qc_criterion` table |
| Q5 | Criterion fields: name + type + target + min/max + UoM + pass_criteria + reference_image | columns + JSONB |
| Q6 | Approve chain with `spec_approval` Engineer→QA→Release | `spec_approval` table |

### 1.6 Process catalog (lookup table)

| # | Feature | SpecHub source |
|---|---|---|
| P1 | 30 process codes seeded (FLEXO/INDIGO/SILKSCREEN/FLATBED_CUT/RDC/CNC/LAMINATION/FOIL_STAMP/…) | `process_catalog` table + INSERT block |
| P2 | Category: print / cut / finishing | column |
| P3 | Display name VI + EN | `display_name_vi/en` columns |
| P4 | Active/deprecated status + display order | column |

---

## 2. Gap analysis — CCL-MES có gì so với full SpecHub

### 2.1 Entity-level (Domain)

| SpecHub concept | CCL-MES hiện tại | Trạng thái |
|---|---|---|
| `product` (master) | `Product` entity (Phase 6 Bước 5) | ✅ ĐÃ CÓ |
| `product_revision` (per-rev spec container) | KHÔNG có — `Spec` không revision-keyed | ❌ THIẾU |
| `spec_material` / `spec_print` / `spec_diecut` / `spec_finishing` | KHÔNG có — chỉ `SpecParameter` (single flat list) | ❌ THIẾU |
| `artwork` + `artwork_version` + `artwork_approval` | KHÔNG có | ❌ THIẾU |
| `spec_qc_window` + `qc_criterion` | KHÔNG có (có `Iqc` runtime nhưng KHÔNG plan) | ❌ THIẾU |
| `spec_approval` (multi-step chain) | KHÔNG có — `SpecVersion.ApprovedBy/At` single-role | ❌ THIẾU |
| `process_catalog` (lookup) | KHÔNG có | ❌ THIẾU |
| Audit log | `AuditLog` entity + `IAuditWriter` ✅ | ✅ ĐÃ CÓ |
| `Spec` + `SpecVersion` + `SpecParameter` baseline | `Spec.cs` 3-class graph | ✅ ĐÃ CÓ — sẽ deprecate/migrate |

### 2.2 UI-level (EngineerSpec.razor + CreateSpecModal.razor)

| SpecHub feature | CCL-MES hiện tại |
|---|---|
| Grid 14 cols | 7 cols (rt-* pattern) ✅ partial |
| Status badge | rm-status--{draft/review/approved/obsolete} ✅ |
| Planner badge | KHÔNG có (không có planner field) ❌ |
| Search free-text | search 5 fields ✅ |
| Stats panel sidebar | KHÔNG có ❌ |
| Double-click → detail | KHÔNG có ❌ |
| Right-click → context menu | KHÔNG có (mới làm cho WC ở PR #27 — pattern reusable) ❌ |
| Detail view full | KHÔNG có ❌ |
| Lifecycle: Create + Approve | ✅ minimal (CreateSpecModal + Approve button) |
| Lifecycle: Revise (rewrite vs new) | KHÔNG có ❌ |
| Lifecycle: Copy | KHÔNG có ❌ |
| Lifecycle: Trash + Restore + 30-day purge | KHÔNG có ❌ |
| Lifecycle: Supersede on rev bump | KHÔNG có ❌ |
| Import xlsx | KHÔNG có ❌ |
| Duplicate detection on import | N/A |
| Multi-planner | N/A |
| Drawings/artwork upload + version chain | KHÔNG có ❌ |
| Drawing approval chain (NPI/Prod/QC) | KHÔNG có ❌ |
| QC plan editor (windows + criteria) | KHÔNG có ❌ |
| History log per spec (audit append-only) | Audit emit chỉ SpecCreate + SpecApprove ✅ partial |
| Print to PDF | KHÔNG có ❌ |

**Tổng kết gap**: ~25/30 features chưa có. Phase 7 chỉ wire baseline (UI + 2 audit events). Phase 8 là quả "ăn" lớn nhất từ trước tới giờ.

### 2.3 Service-level (SpecService.cs hiện có)

3 method: `SpecsAsync(search, page, size)`, `ProductsForDropdownAsync()`, `CreateAsync(req, user)`, `ApproveAsync(versionId, user)`. **Không có** Revise / Copy / Trash / Restore / Supersede / detail load / artwork CRUD / QC plan CRUD.

---

## 3. Schema delta — entity cần ADD/UPDATE

> **Phương pháp**: dịch từ SpecHub PostgreSQL DDL → .NET Entity Framework Core
> SQLite. Không đụng entity tồn tại trừ khi bắt buộc (FK target). Field
> dùng `string?` cho VARCHAR nullable, `DateTime?` cho TIMESTAMPTZ nullable,
> `long` cho BIGSERIAL primary key, store JSONB như `string?` (JSON text)
> để tránh provider-specific affinity.

### 3.1 ProductRevision (NEW)

```csharp
public class ProductRevision : BaseEntity
{
    public long ProductId { get; set; }
    public Product? Product { get; set; }
    public string RevisionCode { get; set; } = "A";          // A, B, C, AA…
    public ProductRevisionStatus Status { get; set; }        // Draft/InReview/Approved/Released/Superseded
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public long? ParentRevisionId { get; set; }
    public string? ChangeSummary { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ReleasedBy { get; set; }
    public DateTime? ReleasedAt { get; set; }
}
```

Unique: `(ProductId, RevisionCode)`. Partial index: enforce ≤1 `Released` active per Product.

### 3.2 Drawing / DrawingVersion / DrawingApproval (NEW — refactor "artwork" wording cho dễ hiểu shop-floor)

```csharp
public class Drawing : BaseEntity
{
    public long ProductRevisionId { get; set; }
    public DrawingKind Kind { get; set; }                    // CustomerDrawing / NpiPrintLayout / NpiCutLayout / IpqcPrintReference / IpqcCutReference / FqcChecksheet / OqcChecksheet / CustomerApproval / InternalProof
    public string Title { get; set; } = "";
    public long? CurrentVersionId { get; set; }
    public DrawingStatus Status { get; set; }                // Draft/PendingApproval/Approved/Superseded/Withdrawn
}

public class DrawingVersion : BaseEntity
{
    public long DrawingId { get; set; }
    public int VersionNo { get; set; }
    public string FileName { get; set; } = "";
    public string StorageKey { get; set; } = "";             // filesystem path OR SQLite BLOB ref (Q3)
    public string FileHash { get; set; } = "";               // SHA256
    public long FileSize { get; set; }
    public string? PreviewKey { get; set; }                  // JPEG preview blob
    public string? ChangeReason { get; set; }                // BẮT BUỘC v2+ (validate ở service layer)
    public DrawingVersionStatus Status { get; set; }
    public long? SupersededByVersionId { get; set; }
    public DateTime UploadedAt { get; set; }
    public string? UploadedBy { get; set; }
}

public class DrawingApproval : BaseEntity
{
    public long DrawingVersionId { get; set; }
    public DrawingApprovalRole Role { get; set; }            // Npi / Production / Qc
    public DrawingApprovalStatus Status { get; set; }        // Pending/Approved/Rejected
    public string? ActedBy { get; set; }
    public DateTime? ActedAt { get; set; }
    public string? Comment { get; set; }
}
```

### 3.3 SpecRevision refactor (REPLACE current Spec → ProductRevision-keyed)

Lựa chọn A — **migrate Spec → ProductRevision** (clean, breaks Phase 7 baseline data):
- `Spec` becomes `SpecMaterial` + `SpecPrint` + `SpecDiecut` + `SpecFinishing` (4 sibling 1:1 tables keyed bởi `ProductRevisionId`)
- `SpecVersion` becomes `ProductRevision`
- `SpecParameter` becomes individual columns trong 4 sibling tables + `extra` JSONB

Lựa chọn B — **keep Spec + add ProductRevision parallel** (compatible, dual-source):
- Giữ `Spec` cho legacy data + EngineerSpec grid v1
- Thêm `ProductRevision` cho new flow
- Service layer route theo flag `is_revision_keyed`

Em đề xuất **Option A clean rewrite** vì:
1. Phase 7 Spec data chỉ 1 fixture row (baseline `SpecCode=PCB-001`), migrate dễ.
2. Dual-source là tech debt vĩnh viễn.
3. Schema delta đã lớn, thêm dual-source = double maintenance burden.

Migration `20260602xxxxx_RefactorSpecToProductRevision`:
- Backup `Spec` table → `Spec_Legacy` (DDL rename, không drop, lưu data forensic)
- ADD `ProductRevision` + 4 sibling specs + `Drawing*` + `SpecQcWindow` + `QcCriterion` + `ProcessCatalog`
- Seed `ProductRevision` rev "A" cho baseline fixture từ `Spec_Legacy` (1 row only)

### 3.4 SpecQcWindow + QcCriterion (NEW)

```csharp
public class SpecQcWindow : BaseEntity
{
    public long ProductRevisionId { get; set; }
    public QcStage Stage { get; set; }                       // IpqcPrint / IpqcCut / Fqc / Oqc
    public string? ProcessCode { get; set; }                 // null = applies to all processes
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? SamplePlan { get; set; }
    public string? Frequency { get; set; }
    public QcRejectAction RejectAction { get; set; }         // Rework/Scrap/Escalate/RecordOnly
    public SpecQcWindowStatus Status { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public class QcCriterion : BaseEntity
{
    public long SpecQcWindowId { get; set; }
    public short Seq { get; set; }
    public string Name { get; set; } = "";
    public QcCriterionType CriterionType { get; set; }       // Visual/Dimensional/Colorimetric/Functional/Count
    public string? MeasureMethod { get; set; }
    public double? TargetValue { get; set; }
    public double? ToleranceMin { get; set; }
    public double? ToleranceMax { get; set; }
    public string? Unit { get; set; }
    public string? PassCriteria { get; set; }
    public string? ReferenceImageKey { get; set; }
    public bool Required { get; set; } = true;
    public string? ExtraJson { get; set; }
}
```

### 3.5 ProcessCatalog (NEW lookup, seedable)

```csharp
public class ProcessCatalog : BaseEntity
{
    public string Code { get; set; } = "";                   // PRIMARY KEY (string)
    public ProcessCategory Category { get; set; }            // Print / Cut / Finishing
    public string DisplayNameVi { get; set; } = "";
    public string DisplayNameEn { get; set; } = "";
    public string? Description { get; set; }
    public ProcessCatalogStatus Status { get; set; }
    public short DisplayOrder { get; set; } = 100;
}
```

Seed 17+ codes từ SpecHub DDL (FLEXO/LETTERPRESS/INDIGO/INDIGO_PRIMER/SILKSCREEN/DIGITAL_UV/FLATBED_CUT/ROTARY_CUT/RDC/POWERPUNCH/CNC/LASER_CUT/KISS_CUT/VARNISH/LAMINATION/FOIL_STAMP/EMBOSS) — idempotent SQL seed trong DbSeeder.

### 3.6 Đụng vùng cấm?

| Vùng | Ảnh hưởng | Quyết định |
|---|---|---|
| `Iqc` entity (IQC=3 baseline) | ❌ KHÔNG đụng — `SpecQcWindow` là PLAN definition; `IqcInspection` là runtime capture (đã có ở Phase 6). Phase 8 sẽ thêm optional FK `IqcInspection.SpecQcWindowId` về sau, NOT trong sprint đầu. | Bảo toàn |
| `Machine` / `ProductionLog` / `DowntimeReason` | KHÔNG đụng | Bảo toàn |
| `Product` | Chỉ ADD reverse navigation `List<ProductRevision> Revisions` | Backward compat |
| `RawMaterial` / `RoutingOperation` / `ManufacturingStructure` / `WorkCenter` | KHÔNG đụng | Bảo toàn |
| Audit log | ADD ~12 action codes (`SPEC_REVISE` / `SPEC_COPY` / `SPEC_TRASH` / `SPEC_RESTORE` / `SPEC_PURGE` / `DRAWING_UPLOAD` / `DRAWING_APPROVE` / `DRAWING_REJECT` / `DRAWING_SUPERSEDE` / `QC_WINDOW_CREATE` / `QC_WINDOW_APPROVE` / `PROCESS_CATALOG_UPSERT`) | Additive |

---

## 4. Blob storage — filesystem vs SQLite BLOB

SpecHub DDL dùng MinIO S3-compatible (object storage). CCL-MES KHÔNG có MinIO sẵn → 2 lựa chọn:

### Option F — Filesystem on server (giống Ops Control v1.2 production)

- Path: `<DATA_DIR>/blobs/drawings/<revisionId>/<drawingId>/v<n>_<sha8>.<ext>`
- Preview: cùng folder với `_preview.jpg` suffix
- Pro: dễ backup (rsync), dễ inspect manually, dễ migrate sang MinIO sau, file system caching, download via static-file middleware nhanh
- Con: 2-source-of-truth (DB row + filesystem) → integrity check phức tạp; phải có path sanitization để tránh traversal; backup script phải bao gồm `blobs/`
- Operational: Phase 8 PR đầu cần `DATA_DIR/blobs/` mkdir-on-startup + `IBlobStore` abstraction trỏ filesystem (PHASE9 có thể swap MinIO sau)

### Option B — SQLite BLOB

- Schema: `Drawing.Content BLOB` + `Drawing.PreviewContent BLOB`
- Pro: 1-source-of-truth, integrity = DB integrity, single backup file, đơn giản code
- Con: SQLite DB phình to nhanh (PDF 4MB avg × hundreds = GB-scale DB), backup chậm dần, VACUUM cần định kỳ, page-level locking ảnh hưởng concurrency, migration mất giờ khi prod data lớn

### Đề xuất default

**Option F (filesystem) với IBlobStore abstraction**:

```csharp
public interface IBlobStore
{
    Task<string> PutAsync(Stream content, string suggestedKey, string contentType);
    Task<Stream> GetAsync(string key);
    Task<bool> ExistsAsync(string key);
    Task DeleteAsync(string key);
}
public class FilesystemBlobStore : IBlobStore { /* implement với DATA_DIR/blobs/ */ }
public class SqliteBlobStore : IBlobStore { /* fallback nếu sysadmin opt-in cho single-file deploy */ }
```

Lý do:
1. CCL-MES có thể chạy on-server (Yen Phong Bac Ninh, có disk) — không bị cloud-egress constraint
2. Backup tool team CMES đã có rsync workflow
3. Migrate sang MinIO/S3 sau chỉ cần implement `MinioBlobStore : IBlobStore` — KHÔNG đụng business code
4. SQLite BLOB là escape hatch cho dev/demo (single-file portable)

Default: filesystem; env var `OPS_BLOB_BACKEND=filesystem|sqlite` overridable.

---

## 5. Roadmap — N PR incremental (đề xuất 6 PR)

> Mỗi PR = 1 branch độc lập, build được, test pass, có thể merge mà không cần
> sibling PR. Mỗi PR scope nhỏ ~800-2,000 LOC. Sequence để minimize rework.

### PR #28 — `feat/phase8-spec-revision-schema` (S — ~1,200 LOC)

Schema-only PR (zero UI changes).

- ADD `ProductRevision` + 4 sibling specs (`SpecMaterial` + `SpecPrint` + `SpecDiecut` + `SpecFinishing`) + `ProcessCatalog` entities + DbContext DbSets
- Migration `20260602xxxxxx_AddProductRevisionSchema` (A→B→C SAFE on /tmp test DB first)
- Seed `ProcessCatalog` 17 codes via DbSeeder idempotent block
- Migrate baseline `Spec` (1 fixture row) → `ProductRevision` + `SpecPrint` (silkscreen-style flat)
- Rename old `Spec` → `Spec_Legacy` cho forensic trail, KHÔNG drop
- Update `SpecService.SpecsAsync` → return ProductRevision-shaped DTO, EngineerSpec.razor unchanged display surface (vẫn 7 cols, chỉ data nguồn đổi)
- 1 sample integration test verify row counts pre/post migration identical

**Acceptance gate**: dotnet build pass, EngineerSpec grid renders identically as before, audit log không có error.

### PR #29 — `feat/phase8-spec-context-menu-detail` (M — ~1,800 LOC)

Right-click context menu (reuse pattern PR #27 WC) + detail view (read-only).

- `SpecContextMenu.razor` (reuse `WorkCenterContextMenu.razor` pattern) với 5 items: Open / Copy / Revise / Move to Trash / Get Info
- `SpecDetailModal.razor` — full-screen detail view (4 sections: Identity/Material/Print/Audit Trail)
- `SpecInfoModal.razor` — Get Info quick popup (mirror WorkCenterInfoModal)
- Wire trên `EngineerSpec.razor` row `@oncontextmenu="OnRowContextMenu"`
- Audit emit `SPEC_OPEN` event (forensic trail: ai mở spec nào)
- Defensive: try-catch wrap mọi async handler (Lesson PR #27 hotfix)

### PR #30 — `feat/phase8-spec-revise-copy-trash` (M — ~1,800 LOC)

Lifecycle ops: revise + copy + trash + restore (KHÔNG có drawing upload).

- `SpecService` ADD methods: `ReviseAsync(productRevisionId, reason, user)`, `CopyAsync(srcRevisionId, user)`, `TrashAsync(revisionId, user)`, `RestoreAsync(revisionId, user)`, `PurgeOldTrashAsync(maxAge)`
- `SpecReviseModal.razor` — 2-choice: Rewrite-in-place (force `Status = Draft`, bump rev letter A→B) vs Create-new-spec (branch independent record)
- Reason textarea required
- Deep diff display (text-only — list field path + from→to) for traceability
- Soft-delete: ADD `IsTrashed` + `TrashedAt` + `TrashedBy` columns to `ProductRevision`; query `Where(!x.IsTrashed)` by default
- Background purge: scheduled service runs daily, hard-delete trashed rows older than 30 days (configurable env)
- Audit emit `SPEC_REVISE` + `SPEC_COPY` + `SPEC_TRASH` + `SPEC_RESTORE` + `SPEC_PURGE`
- Update `SpecContextMenu` enable/disable based on `Status` (Approved Specs can't be Copy → must Revise)

### PR #31 — `feat/phase8-spec-drawings-mvp` (L — ~2,500 LOC)

Drawing master + version chain + filesystem blob store.

- ADD `Drawing` + `DrawingVersion` + `DrawingApproval` entities
- ADD `IBlobStore` abstraction + `FilesystemBlobStore` implementation (env-configurable path; `DATA_DIR/blobs/` default)
- `DrawingService` CRUD: `UploadAsync(productRevisionId, kind, title, stream, user)` (auto v+1), `ListAsync(productRevisionId)`, `GetVersionAsync(versionId, user)` (returns stream + audit emit `DRAWING_VIEW`), `SupersedeAsync` (when new ver approved)
- `SpecDrawingsTab.razor` rendered trong `SpecDetailModal` — list drawings + upload + view + download
- Upload UI: input[type=file] + filename validation + 50MB size cap + SHA256 compute client-side
- Preview generation: defer Phase 9 (no JPEG/PDF rasterization MVP — use file icon)
- Audit emit `DRAWING_UPLOAD`
- KHÔNG có approval chain trong MVP (defer PR #32)

### PR #32 — `feat/phase8-spec-drawing-approval` (M — ~1,800 LOC)

3-role approval chain cho DrawingVersion.

- `DrawingService` ADD `ApproveAsync(versionId, role, comment, user)` / `RejectAsync(...)`
- `DrawingApprovalChain.razor` component: 3-tile horizontal display (NPI / Production / QC) với button "Approve" + "Reject" gate theo role hiện tại
- Lifecycle: khi đủ 3 role approved → auto supersede prev version + flip current version status
- Reject any role → version status Rejected, current_version unchanged
- Trigger `DrawingVersion` lifecycle ở service layer (NOT SQL trigger — SQLite limited)
- Audit emit `DRAWING_APPROVE` + `DRAWING_REJECT` + `DRAWING_SUPERSEDE`

### PR #33 — `feat/phase8-spec-qc-plan` (M — ~2,000 LOC) — OPTIONAL, defer nếu out of scope

QC plan editor (windows + criteria).

- ADD `SpecQcWindow` + `QcCriterion` entities
- `SpecQcPlanTab.razor` rendered trong `SpecDetailModal` (5th tab) — list windows + create/edit/delete
- `QcWindowEditModal.razor` form: Stage + ProcessCode dropdown + Title + SamplePlan + Frequency + RejectAction
- `QcCriterionEditor.razor` row editor: criterion list table với add/remove/inline-edit
- KHÔNG hooks tới runtime IQC capture flow (defer Phase 9 — Phase 8 chỉ plan definition)
- Audit emit `QC_WINDOW_CREATE` + `QC_WINDOW_APPROVE`

**Lý do defer**: PR #28-32 đã đủ cover 80% feature value. QC plan là phần khó nhất + ít operator demand từ shop floor (vì IQC runtime đã chạy được từ Phase 6).

---

## 6. RBAC matrix

### 6.1 Phase 6 Bước 4 §2.C reference

| Tab/Action | Admin | Supervisor | Engineer | Qc | User |
|---|---|---|---|---|---|
| `NpiSpecRead` (browse list, view detail) | ✅ | ✅ (R) | ✅ | ❌ | ❌ |
| Spec Create + Approve (current) | ✅ | ❌ (R only) | ✅ | ❌ | ❌ |

### 6.2 Phase 8 expansion (đề xuất, giữ pattern §2.C)

| Action | Admin | Supervisor | Engineer | Qc | User | Rationale |
|---|---|---|---|---|---|---|
| Spec → Open (detail view) | ✅ | ✅ R | ✅ | ❌ | ❌ | Browse vẫn NpiSpecRead |
| Spec → Get Info (quick popup) | ✅ | ✅ R | ✅ | ❌ | ❌ | Same as Open |
| Spec → Copy | ✅ | ❌ | ✅ | ❌ | ❌ | Mutation — Admin/Engineer only |
| Spec → Revise | ✅ | ❌ | ✅ | ❌ | ❌ | Mutation |
| Spec → Move to Trash | ✅ | ❌ | ✅ | ❌ | ❌ | Mutation |
| Spec → Restore from Trash | ✅ | ❌ | ✅ | ❌ | ❌ | Mutation |
| Spec → Purge (hard delete) | ✅ | ❌ | ❌ | ❌ | ❌ | Admin only (irreversible) |
| Spec → Approve current version | ✅ | ❌ | ✅ | ❌ | ❌ | Existing Phase 7 — unchanged |
| Drawing → View (download blob) | ✅ | ✅ R | ✅ | ✅ Pq | ❌ | **Qc gains read access** (cần xem drawing để inspect) |
| Drawing → Upload new version | ✅ | ❌ | ✅ | ❌ | ❌ | Mutation |
| Drawing → Approve (3-role chain) | ✅ | ❌ | ✅(Npi) ✅(Prod-if-engineer-dept-prod) | ✅(Qc) | ❌ | Role-aware: chỉ user thuộc role tương ứng được Approve slot đó |
| Drawing → Reject | ✅ | ❌ | ✅ same as Approve | ✅ | ❌ | |
| QC Window → Create / Edit / Approve | ✅ | ❌ | ✅ | ✅ | ❌ | **Qc gains write access** cho QC plan (đây là vùng QC trách nhiệm) |
| ProcessCatalog → Upsert | ✅ | ❌ | ❌ | ❌ | ❌ | Admin-only lookup table |

**Khác biệt vs SpecHub**: SpecHub có roles `admin/npi/production/qc/operator`. CCL-MES có `Admin/Supervisor/Engineer/Qc/User`. Mapping:
- SpecHub `npi` ≈ CCL-MES `Engineer`
- SpecHub `production` → CCL-MES có thể là `Supervisor` hoặc `Engineer` (cần Q)
- SpecHub `qc` ≈ CCL-MES `Qc`

**KHÔNG tự nới quyền**: Drawing 3-role chain (NPI/Prod/QC) cần resolve mapping với operator. Em đề xuất Q5 dưới.

### 6.3 Server enforcement (defense-in-depth, MES-3-FIX-8 pattern)

- Page-level: `[Authorize(Policy = "NpiSpecRead")]` trên EngineerSpec.razor (hiện đã có)
- Page-level: `[Authorize(Policy = "NpiSpecDrawingRead")]` policy MỚI cho drawing download endpoint (Admin + Supervisor + Engineer + Qc)
- Action-level: `AuthorizeView Roles="Admin,Engineer"` trên context menu items mutation
- Service-level: `SpecService.ReviseAsync` + `TrashAsync` + `CopyAsync` validate role qua `IUserContext` (chưa có — sẽ phải add ở PR #28 hoặc PR #30)

---

## 7. i18n keys mapping

### 7.1 Currently in `SharedResource.resx`

39 keys hiện có dưới prefix `npi.spec.*` (Phase 7 hạng mục 4). Đủ cho grid + Create modal.

### 7.2 New keys cần thêm (~120 keys, EN + VN parallel)

| Group | Sample keys | Count |
|---|---|---|
| Context menu | `npi.spec.ctx.open` / `ctx.copy` / `ctx.revise` / `ctx.trash` / `ctx.info` | 5 |
| Detail view sections | `npi.spec.detail.section.identity` / `material` / `print` / `diecut` / `finishing` / `qc_plan` / `drawings` / `audit_trail` | 8 |
| Material fields | `npi.spec.material.substrate_type` / `thickness_um` / `liner_type` / `adhesive_type` … | ~12 |
| Print fields | `npi.spec.print.process_code` / `num_colors` / `color_spec` / `varnish` / `lamination` … | ~10 |
| Diecut fields | `npi.spec.diecut.cut_process_code` / `width_mm` / `length_mm` / `corner_radius_mm` … | ~12 |
| Finishing fields | `npi.spec.finishing.output_form` / `labels_per_roll` / `core_diameter_mm` … | ~8 |
| Revise modal | `npi.spec.revise.title` / `choice_rewrite` / `choice_create` / `reason_required` / `diff_header` … | ~10 |
| Trash modal | `npi.spec.trash.confirm` / `trash.purge_in_days` / `trash.restore_btn` / `trash.purged_at` … | ~6 |
| Drawing kinds | `npi.spec.drawing.kind.customer_drawing` / `npi_print_layout` / `ipqc_print_ref` / `fqc_checksheet` … | 9 |
| Drawing upload | `npi.spec.drawing.upload.btn` / `upload.size_too_big` / `upload.hash_mismatch` / `upload.change_reason_required` | ~6 |
| Drawing approval | `npi.spec.drawing.approval.npi` / `approval.production` / `approval.qc` / `approval.btn_approve` / `approval.btn_reject` | ~6 |
| QC plan | `npi.spec.qc.window.title` / `qc.criterion.add` / `qc.stage.ipqc_print` / `qc.stage.fqc` / `qc.reject_action.scrap` … | ~20 |
| Process catalog | `npi.spec.process.category.print` / `category.cut` / `category.finishing` | 3 |
| ProductRevision status | `npi.spec.rev.status.draft` / `in_review` / `approved` / `released` / `superseded` | 5 |

VI translation đã được tham chiếu sẵn từ SpecHub `02-data-model.md` (vd `Cắt rotary` / `In Indigo có primer` / `Phủ varnish`). EN sẽ giữ technical terminology nhất quán với `display_name_en` trong process_catalog seed.

---

## 8. Q1..Qn — câu hỏi anh chốt trước khi tạo branch

### Q1 — Scope PR đầu tiên?

- **Default em đề xuất**: PR #28 (schema-only) — chỉ thêm `ProductRevision` + 4 sibling specs + `ProcessCatalog`, migrate baseline `Spec` legacy → ProductRevision, KHÔNG đụng UI. Sau khi schema landed mới ship UI dần (PR #29-32).
- Alternative A: PR #28 + #29 gộp (schema + context menu + detail) — nhanh thấy UI hơn nhưng PR ~3K LOC.
- Alternative B: PR đầu = "Drawings MVP" (skip schema refactor) — bám sát artwork upload là feature operator hỏi nhiều nhất.

### Q2 — Refactor `Spec` → `ProductRevision` clean rewrite hay keep parallel?

- **Default em đề xuất**: Option A — clean rewrite (rename `Spec` → `Spec_Legacy`, migrate 1 fixture, đi tiếp). Tech debt thấp dài hạn.
- Alternative: Option B — dual-source (giữ `Spec` cho legacy, thêm `ProductRevision` cho new flow, service route theo flag). Backward compat tốt hơn nhưng tech debt vĩnh viễn.

### Q3 — Blob storage backend?

- **Default em đề xuất**: filesystem at `DATA_DIR/blobs/` với `IBlobStore` abstraction (mirror Ops Control v1.2 production pattern). Env override `OPS_BLOB_BACKEND=sqlite` cho dev/portable demo.
- Alternative: SQLite BLOB native (single-file deploy đơn giản hơn cho operator nhỏ, nhưng phình DB).
- Alternative: defer blob storage, Phase 8 ship Spec lifecycle ONLY, Drawing đẩy sang Phase 9. Em không khuyến nghị vì SpecHub `1C Spec library` không có gì khác cốt lõi hơn artwork — bỏ artwork = không thực sự "merge SpecHub vào CMES".

### Q4 — QC plan editor (PR #33) trong scope Phase 8 hay defer?

- **Default em đề xuất**: defer Phase 9. Lý do: SpecHub `1C Spec library` prototype KHÔNG có QC plan UI (chỉ trong DDL design). Operator hiện tại đã có IQC runtime từ Phase 6 (`IqcInspection` + `QcInspectionGrid`). Phase 8 focus Spec definition + Drawings; QC plan sẽ là Phase 9 deliverable cùng với hooks runtime IQC → window/criterion lookup.
- Alternative: include — sẽ +2,000 LOC, 1 PR thêm, slip Phase 8 timeline.

### Q5 — Drawing 3-role approval mapping (NPI / Production / QC)?

- **Default em đề xuất**:
  - **NPI slot** → role `Engineer` với department `npi` (giả sử User entity có `Department` field — cần verify ở Phase 6 Bước 2)
  - **Production slot** → role `Supervisor` HOẶC `Engineer` với department `production`
  - **QC slot** → role `Qc` (any department)
- Alternative simpler: ai có role Engineer/Supervisor đều được Approve bất kỳ slot — đơn giản nhưng mất tính chain-of-custody. Em KHÔNG khuyến nghị.
- Cần anh confirm: User entity có Department field chưa? Nếu chưa, Phase 8 phải add — em chưa kiểm tra entity Users.

### Q6 — Multi-planner xlsx import (PR future, NOT in scope đầu)?

- SpecHub có Silkscreen + Flexo parser hoàn chỉnh; 4 planner khác (Letterpress / Indigo / Diecut / Unknown) là stub.
- **Default em đề xuất**: defer toàn bộ xlsx import sang Phase 9. Phase 8 ship Spec lifecycle (create empty, revise, copy) + Drawing upload (PDF/image). xlsx parser là SheetJS heavy + planner-specific cell map — em estimate +3,000 LOC riêng feature này.
- Alternative: include 1 Silkscreen parser ở PR #30 (reuse SpecHub mapping logic ported sang C# với ClosedXML/NPOI).

### Q7 — Stats panel sidebar (Library Health / By Category / By Status / Recent Activity / Tips)?

- **Default em đề xuất**: defer Phase 9 polish. Phase 8 focus core lifecycle; stats panel là nice-to-have visual.
- Alternative: include ở PR #28 hoặc PR #29 (~300 LOC, server-side compute, không phức tạp). Visually impressive cho stakeholder demo.

### Q8 — Trash auto-purge schedule?

- **Default em đề xuất**: scheduled HostedService runs at startup + every 24h, hard-delete `ProductRevision.IsTrashed == true && TrashedAt < UtcNow.AddDays(-30)`. Configurable env `OPS_SPEC_TRASH_RETENTION_DAYS=30`. Mirror Sprint 13 soft-delete + Trash UI pattern từ Ops Control.
- Alternative: manual-only purge (Admin action button) — đơn giản, KHÔNG có HostedService risk, nhưng Trash sẽ tích lũy nếu admin quên.

### Q9 — Audit trail UI per spec (history log)?

- SpecHub có `historyLog[]` array attached to mỗi spec (in-memory, localStorage persist) hiển thị tab Audit Trail trong detail view.
- **Default em đề xuất**: query `AuditLog` table với filter `targetType='Spec' OR targetType='ProductRevision' OR targetType='Drawing'` AND `targetId = revisionId` (hoặc related id). Hiển thị trong tab Audit Trail của `SpecDetailModal`. KHÔNG cần field mới trong entity (audit infra Phase 5 đã đủ).
- Alternative: per-spec inline `HistoryLogJson` text column — simpler nhưng duplicate AuditLog data.

### Q10 — Process catalog admin UI?

- 17 process codes seeded ban đầu. SpecHub cho phép admin extend via Library UI (per `docs/02-data-model.md` §process_catalog).
- **Default em đề xuất**: defer Phase 9. Phase 8 chỉ seed 17 codes via DbSeeder + readonly lookup. Admin UI cho extend = nhu cầu chưa cấp bách.
- Alternative: include ở PR #28 (~200 LOC).

---

## 9. Hard constraints summary

✅ A→B→C SAFE mọi migration (isolated /tmp DB → backup SHA256 → live apply → row count verify).
✅ Provider-agnostic: strip `type:` / `oldType:` from `.cs` migration files, strip `HasColumnType` from `Designer.cs` ONLY (Snapshot giữ).
✅ Bảo toàn IQC=3 baseline + IQC FK + Phase 7 NPI 5-tab data (20,530 Structure + 38,441 Routine + 2,127 RawMaterial + 1 Spec fixture + 43 WC).
✅ Reuse `rt-*` infra + `NpiImportService` + `ICsvImportTarget<T>` (nếu PR future cần import) + audit emit pattern + DI `IAuditWriter`.
✅ EN + VI i18n parity.
✅ KHÔNG đụng `Ops Control v1.2` / `CMES sibling` / `Old ver` (DO NOT USE) / `SpecHub` (READ-ONLY) / Machine / ProductionLog / 4 NPI tab khác.
✅ Right-click menu pattern reuse từ PR #27 WC (`WorkCenterContextMenu.razor` → `SpecContextMenu.razor`), accept entity directly từ grid, try-catch wrap async handlers, error banner UI.

---

## 10. Estimated total scope

| PR | Effort | LOC est. | Risk |
|---|---|---|---|
| #28 schema refactor | S (~4h) | 1,200 | Med (migration baseline 1-row easy, but breaking change in service shape) |
| #29 context menu + detail | M (~8h) | 1,800 | Low (reuse PR #27 pattern) |
| #30 revise + copy + trash | M (~10h) | 1,800 | Med (deep diff + soft-delete + scheduled purge) |
| #31 drawings MVP | L (~14h) | 2,500 | High (blob store abstraction + upload pipeline + SHA256 verify) |
| #32 drawing approval chain | M (~10h) | 1,800 | Med (3-role state machine) |
| #33 QC plan (OPTIONAL, defer Q4) | M (~12h) | 2,000 | Med-High |
| **Subtotal core** (#28-32) | | **~9,100** | |
| **With Q4 included** | | **~11,100** | |

So sánh: Phase 7 đã ship 6 PR / ~9,200 LOC trong 1 ngày — Phase 8 ~9,100 LOC core scope.

---

## 11. Recommended decisions summary (default em đề xuất)

| Q | Default |
|---|---|
| Q1 PR đầu | PR #28 schema-only |
| Q2 Spec → ProductRevision | Option A clean rewrite |
| Q3 Blob storage | Filesystem `DATA_DIR/blobs/` với `IBlobStore` abstraction |
| Q4 QC plan editor | DEFER Phase 9 |
| Q5 Drawing 3-role mapping | NPI=Engineer(npi dept) / Production=Engineer(prod dept) / QC=Qc role |
| Q6 xlsx import | DEFER Phase 9 |
| Q7 Stats panel | DEFER Phase 9 polish |
| Q8 Trash purge | HostedService 24h cycle, 30-day retention env-overridable |
| Q9 Audit trail UI | Query AuditLog table (no new field) |
| Q10 Process catalog admin UI | DEFER Phase 9 |

Final scope: **5 PR (#28-32), ~9,100 LOC**.

---

## 12. STOP — chờ anh duyệt

Em sẽ KHÔNG tạo branch / KHÔNG migration / KHÔNG code cho đến khi anh:
1. Duyệt scope tổng 5 PR (hoặc redirect sang scope khác)
2. Chốt Q1-Q10 (hoặc đề xuất default em nêu)
3. Chốt PR đầu tiên (default = PR #28 schema-only)

Sau khi chốt, em sẽ tạo branch `feat/phase8-spec-revision-schema` cho PR #28, plan riêng `docs/PHASE8-PR28-PLAN.md`, A→B→C SAFE migration, commit + PR.
