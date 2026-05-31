# PHASE 7 — HẠNG MỤC 4: Engineer Spec

> Khảo sát + plan đồng bộ tab **Engineer Spec** với pattern UI mới. **Chưa code**
> — chờ anh chốt **scope** trước khi tạo branch.
>
> Hạng mục 4 khác nghiêm trọng với hạng mục 1-3 ở 2 điểm:
> 1. **CMES sibling Spec module ≠ grid pattern** — nó là document management
>    system (24 files server + 25 files web; có drawings + blob storage + QC
>    plans + multi-tab editor). Không thể bê toàn bộ trong 1 PR — out of scope.
> 2. **Spec entity của CCL-CMES KHÔNG có IFS source** — không có xlsx/csv để
>    import. Spec là data nội bộ do engineer tạo + approve qua workflow.

---

## 1) State hiện tại (CCL-CMES `main` post-PR #24)

### 1.1 Entity model (`src/CCL.MES.Domain/Entities/Spec.cs`)

3-level hierarchy hoàn chỉnh, dùng cho quality control / inspection spec:

```
Spec                       ← top-level (SpecCode, Title, ProductId FK)
 └── SpecVersion           ← versioned (VersionNo, Status enum, EffectiveDate, ApprovedBy)
       └── SpecParameter   ← N parameters per version (ParamName, Nominal, TolMin/Max, Uom, IsCritical)
```

Status enum: `Draft / InReview / Approved / Obsolete`.

### 1.2 Backend `SpecService` (ĐÃ HOÀN CHỈNH)

| Method | Phase 6 status | UI exposed? |
|---|---|---|
| `SpecsAsync(search, page, pageSize)` | ✓ Bước 1 | ✓ EngineerSpec.razor grid |
| `GetAllAsync()` | ✓ | ✗ (unused) |
| **`CreateAsync(req, user)`** | ✓ Bước 5 | **✗ NO UI** |
| **`ApproveAsync(versionId, user)`** | ✓ Bước 5 | **✗ NO UI** |

Audit emit ready: `AuditAction.SpecCreate` + `SpecApprove` (Phase 6 Bước 5).

### 1.3 UI hiện tại (`Pages/Npi/EngineerSpec.razor`)

7 cột read-only, dùng `wo data` CSS class cũ (KHÔNG phải `rt-*` namespace mới):
`SpecCode | Title | Product | Version | Status | EffectiveDate | Params`

- Có status badge inline (`badge spec-draft/review/approved/obsolete`)
- Không có Columns toggle
- Không có freeze header
- Không có "+ Create" button (backend có nhưng UI không expose)
- Không có "Approve" per-row action (backend có nhưng UI không expose)
- Search 3 field: SpecCode, Title, Product.Name

### 1.4 Data baseline

```
Specs           = 1   (seed: SPEC-BRD-7656-D, PCB ID Label 20x8mm)
SpecVersions    = 1   (v1, Approved)
SpecParameters  = 3   (Width / Height / Process)
```

### 1.5 RBAC khác hạng mục 1-3

Spec dùng policy `NpiSpecRead` (KHÔNG phải `NpiRead`):

| Role | NpiRead (1-3) | NpiSpecRead (4) |
|---|---|---|
| Admin | ✓ | ✓ |
| Supervisor | ✓ | ✓ |
| Engineer | ✓ | ✓ |
| Qc | ✓ | **✗** |
| Operator | ✗ | ✗ |

→ QC role thấy Structure/Routine/RawMaterials nhưng KHÔNG thấy Spec (theo Phase 6 Bước 4 matrix). Plan giữ nguyên invariant này.

---

## 2) Gap analysis

### 2.1 CMES sibling — KHÔNG áp dụng làm reference

CMES Spec module có 24 server file + 25 web file:
- Drawings (`drawing.controller`, `drawing.service`, `revisions.controller`)
- QC plans + capture (`qc.controller`, `qc.service`, `QCCaptureTab`)
- Blob storage + ledger (`blob-ledger.service`)
- Preview worker (background image gen)
- Spec authoring (5-step modal flow: `CreateSpecModal`, `UploadDrawingModal`, `UploadRevisionModal`, `DecideDrawingModal`, `CaptureQCModal`)
- Spec history drawer
- Multi-tab editor (`SpecificationTab`, `DrawingsTab`, `ArtworkTab`, `SetupTab`, `QCPlansTab`, `QCCaptureTab`)
- Import + export controllers

→ **Đây là phạm vi Phase 8 hoặc Phase 9 riêng** — không vừa 1 PR. CCL-CMES domain model `Spec → SpecVersion → SpecParameter` cũng KHÔNG khớp với CMES (CMES dùng JSON payload + revision_id + blob_url, completely different).

### 2.2 Chính xác có 3 lựa chọn scope khả thi cho 1 PR

| Option | Scope | Effort | Phù hợp khi |
|---|---|---|---|
| **A. UI consistency** | Migrate grid từ `wo data` sang `rt-*` namespace + freeze header + Columns toggle. NO new functionality. | S | Anh muốn 4 NPI tabs đồng nhất visual, defer feature dev. |
| **B. UI consistency + Wire backend** | A + thêm "+ Create Spec" button (modal form) + "Approve" per-row action. Backend `CreateAsync` + `ApproveAsync` đã sẵn — chỉ wire UI. | M | Anh muốn engineer tự tạo spec qua UI thay vì DbSeeder/API only. |
| **C. CMES parity** | Bê CMES Spec module toàn bộ (drawings + QC capture + blob + multi-tab). | XL | Defer Phase 8+ (multi-PR). |

**Em đề xuất Option B** vì:
- Chi phí trung bình (~1-2× hạng mục 3)
- "Đóng" 2 backend method đang dormant (Phase 6 Bước 5 đã ship code nhưng UI chưa expose)
- Engineer/Admin có thể tạo spec ngay không cần SQL hoặc API tool
- Vẫn giữ pattern `rt-*` đồng nhất với 3 tab kia

---

## 3) Plan code (giả định Option B — anh có thể chốt khác)

### 3.1 KHÔNG có migration

Entity `Spec/SpecVersion/SpecParameter` đã đầy đủ. KHÔNG ADD/DROP cột nào.
→ Skip Step A/B/C SAFE migration pattern. Chỉ backup live DB (precaution) + xác nhận row counts trước/sau (1/1/3 giữ nguyên).

### 3.2 UI rewrite `EngineerSpec.razor` mirror Raw Materials pattern

Pattern reuse hạng mục 3:
- Migrate `wo data` → `.rt-page` + `.rt-toolbar` + `.rt-table-wrap` + `rt-table`
- Freeze sticky thead (max-height: calc(100vh - 240px))
- **NO frozen first column** — 7 cột không cần widescroll như RawMaterials 28 cột
- Columns toggle popover + localStorage `cclmes.engineer-spec.columns-hidden.v1`
- Status badge (reuse `.rm-status--{draft/review/approved/obsolete}` styling)
- 7 cột mặc định ALL visible (số cột ít, không cần hide)
- **+ Create Spec** button (AuthorizeView Admin/Engineer) → modal form
- **Approve** button per-row (chỉ hiện khi version status = Draft hoặc InReview)
- Pager 50/page giữ nguyên

### 3.3 Create Spec modal `CreateSpecModal.razor` (NEW)

Reuse pattern `NpiImportModal` (scrim + modal-card + 3-step), nhưng KHÔNG dùng generic — đây là form thuần Blazor binding:

```razor
<EditForm Model="_form" OnValidSubmit="OnSubmit">
  - SpecCode (required, unique check)
  - Title (required)
  - Product dropdown (load Product list từ db)
  - Initial parameters table (dynamic add/remove rows):
      ParamName | Nominal | TolMin | TolMax | UoM | IsCritical (checkbox)
  - At least 1 parameter required
</EditForm>
```

Submit → `SpecService.CreateAsync(req, user)` → success → reload grid + close modal.

### 3.4 Approve action

Per-row inline button (small icon `✓`) hiển thị khi `latest.Status == Draft || InReview`:
- Click → confirm dialog
- Call `SpecService.ApproveAsync(version.Id, user)` → reload grid

NO multi-step approval workflow trong PR này (Draft → InReview → Approved). Chỉ direct Approve. Future PR có thể add InReview transition.

### 3.5 Search expand

Currently 3 field (SpecCode, Title, Product.Name). Thêm:
- ApprovedBy (operator search "who approved this spec")
- Status (literal text "approved" / "draft" matching enum name)

→ 3 → 5 field.

### 3.6 i18n keys

Hiện đã có ~10 keys `npi.spec.*` (title/search_placeholder/empty/col.*/status.*).
Thêm:
- `npi.spec.breadcrumb`, `.rows_loaded`, `.rows_count`
- `.btn_columns`, `.btn_show_all`, `.btn_create`, `.btn_approve`
- Create modal: `~12 keys` (title/spec_code_label/title_label/product_label/params_header/param_name/nominal/tol_min/tol_max/uom/is_critical/btn_add_param/btn_remove_param/btn_cancel/btn_save/err_unique)
- Approve confirm: `~3 keys`

Total ~30 keys mới × 2 file resx.

### 3.7 RBAC

- Page Authorize policy `NpiSpecRead` giữ nguyên.
- "+ Create" + "Approve" buttons gate AuthorizeView Roles="Admin,Engineer" (KHÔNG Supervisor — supervisor chỉ approve hậu cần, không tạo spec).
- Defense-in-depth: `SpecService.CreateAsync/ApproveAsync` có sẵn audit emit; thêm role validate ở server-side trước SaveChangesAsync (nếu cần).

---

## 4) Scope contract (vùng cấm)

Hạng mục 4 **KHÔNG** đụng:
- Ops Control v1.2 (sibling — read-only)
- CMES sibling (read-only reference; CMES Spec module out of scope)
- SpecHub sibling
- "Old ver" folder
- Các tab khác CCL-CMES (Structure/Routine/RawMaterials/WorkCenter/IQC/Settings...)
- Spec entity, SpecVersion, SpecParameter — **NOT MODIFY** (Option B chỉ wire UI, không sửa schema)
- `SpecService` Create/Approve method signatures — không sửa
- AuditAction.SpecCreate/SpecApprove — không sửa
- Product entity (Spec.ProductId FK reference) — không sửa
- IQC entity (không liên quan tới Spec hierarchy directly)
- DbSeeder spec fixture — không sửa

Chỉ touch:
- `src/CCL.MES.Web/Pages/Npi/EngineerSpec.razor` (rewrite to `rt-*` pattern)
- `src/CCL.MES.Web/Shared/CreateSpecModal.razor` (new modal — Option B only)
- `src/CCL.MES.Application/Services/SpecService.cs` (nếu cần expose Product list cho dropdown — small helper method, không sửa Create/Approve)
- `src/CCL.MES.Web/Resources/SharedResource.{resx,vi.resx}` (~30 keys × 2)
- `src/CCL.MES.Web/wwwroot/css/site.css` (status badge bổ sung nếu cần, dùng lại `.rm-status--*`)
- `docs/PHASE7-SPEC-PLAN.md` (this file)

---

## 5) Q-questions cần anh chốt

| Q# | Câu hỏi | Default em đề xuất |
|---|---|---|
| **Q1** | Scope chính: A (UI only) / B (UI + wire Create+Approve backend) / C (CMES parity — defer)? | **Option B** (đóng 2 backend method dormant + UI consistency cùng PR) |
| **Q2** | Nếu Option B — Create modal có cần "Product" dropdown bắt buộc, hay cho phép null ProductId? | **Required** (entity `ProductId` là long, không nullable; spec phải gắn product cụ thể cho IQC pipeline) |
| **Q3** | Approve action — direct Draft → Approved hay phải qua InReview? | **Direct Draft → Approved** (giữ scope nhẹ; Draft→InReview→Approved có thể add PR sau khi business cần) |
| **Q4** | 7 cột mặc định ALL visible, hay hide cột rare (vd Params count, Effective date)? | **ALL visible** (số cột ít, operator cần hết) |
| **Q5** | Search expand 3 → 5 field thêm ApprovedBy + Status? | **YES** |
| **Q6** | Status badge: reuse `.rm-status--{draft/review/approved/obsolete}` từ Raw Materials hay tạo namespace mới `.spec-badge`? | **Reuse `.rm-status--*`** (DRY; thêm CSS class draft/review nếu chưa có) |
| **Q7** | "Approve" button tự động set `EffectiveDate = UtcNow` nếu null (như backend đã có), hay yêu cầu operator nhập ngày trước? | **Auto UtcNow** (backend đã handle; operator có thể edit sau khi cần) |
| **Q8** | i18n keys mới — bilingual EN+VI parity (như 3 PR trước)? | **YES** |
| **Q9** | PR strategy — gộp 1 PR cả grid rewrite + Create modal + Approve? | **YES, 1 PR** (đồng pattern hạng mục 1-3) |

---

## 6) Sau khi anh chốt — em sẽ:

1. Tạo branch `feat/phase7-engineer-spec` base `main`
2. Backup live DB (precaution; không có migration nên không cần A→B→C SAFE)
3. Rewrite UI EngineerSpec.razor mirror RawMaterials pattern
4. (Option B) Build CreateSpecModal + wire Approve action
5. Add i18n + CSS extensions
6. `dotnet build` clean
7. Smoke test: open Spec tab → grid render đẹp + Columns toggle → create spec test fixture → approve → verify audit log `SPEC_CREATE` + `SPEC_APPROVE` emit
8. Verify Spec row counts: 1 → 2 (sau create), 1 → 1 approve (status flip)
9. Mở PR, **STOP chờ anh review + merge**
10. Sau merge → lặp tương tự cho **hạng mục 5 Machine List** (WorkCenter)

---

**STOP — chờ anh duyệt Q1–Q9 (đặc biệt Q1 chốt scope A/B/C) + xác nhận hard constraints.**
