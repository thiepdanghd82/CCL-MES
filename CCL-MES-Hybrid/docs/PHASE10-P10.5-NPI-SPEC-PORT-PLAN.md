# Phase 10 — P10.5 NPI Data + Engineer Spec port — PLAN

> **Status: DRAFT — awaiting Henry approval.** P10.3 W4 (PR #79) đã ship +
> verified trên Mac Catalyst (heartbeat + scan→accept end-to-end). P10.4
> (offline-sync) defer per Q4 lock. P10.5 là PORT FULL của NPI Data + Engineer
> Spec subsystem từ legacy `CCL.MES.Web` (Blazor server-side) sang MAUI Hybrid
> client (online-only), kèm các API mutation endpoints thiếu trong `CCL.MES.Api`.
>
> **Scope tóm tắt**:
> - 5 grid NPI: Engineer Spec list, Structure (BOM), Routine (Routings),
>   Raw Materials, Work Centers (read + filter + pagination + import + context menu).
> - Engineer Spec full subsystem: showcard render + 6-tab detail
>   (Specification / Drawings / QC Plans / QC Capture / Artwork / Setup) +
>   lifecycle modals (Create / Import xlsx / Copy / Edit / Revise / Supersede /
>   Trash / Restore) + drawings upload chain + QC Plan upsert + QC Capture +
>   exports (CSV / XLSX / PDF).
> - **~24 mutation endpoints** thêm vào `CCL.MES.Api` — wraps existing
>   Application services qua project-reference (legacy code 0 diff).
> - File upload từ device (xlsx + drawing files) qua `MediaPicker` + multipart
>   HTTP POST.
> - Verify thật trên Mac Catalyst (Windows defer cho W3 song song; UI Razor RCL
>   hoạt động trên cả hai khi flag ready).
>
> Constraints — đối chiếu W4 đã hardened:
> - All code stays inside `CCL-MES-Hybrid/`. Legacy `src/CCL.MES.{Domain,
>   Application,Infrastructure,Web}` is READ-ONLY baseline (project-reference
>   only).
> - Sibling project (`3. PROJECTS/Ops Control v1.2/`) READ-ONLY reference.
> - Reuse `ICclApiClient` + JWT/RBAC + `IBarcodeScannerService` (W2) +
>   `MacCatalystKeyboardFix` from P10.2/P10.3.
> - Online-only per Q4 lock — mutation paths fail fast with operator-visible
>   error + retry; no half-outbox.
> - Mutation endpoints land ONLY in `CCL.MES.Api`; legacy Web's
>   `Pages/Npi/EngineerSpec.razor` etc. tiếp tục chạy độc lập với in-process
>   service path (no API call from legacy).
> - Verify thật trên Mac Catalyst (hardware test); Windows ports parity-verified
>   on best-effort (đã có shared abstraction layer; Catalyst-specific edge cases
>   không ép buộc port).

---

## 0. Lessons từ P10.2/P10.3/P10.4 W4 (carry forward vào mọi P10.5 PR)

| Lesson (source) | P10.5 application |
| --- | --- |
| **Catalyst Keyboard Fix** (P10.2 — `dotnet/maui#13934`) | Mọi grid + modal mới phải nằm dưới `MainLayout` để inherit `MacCatalystKeyboardFix.razor` Tab/Enter polyfill. Modal-level keyboard nav (Esc-to-close, Enter-to-submit, Tab cycling) phải test trên Catalyst trước flag flip. |
| **Adhoc Keychain MissingEntitlement** (P10.2) | Token store fallback đã có (`MauiSecureTokenStore` → in-memory). Không cần thay đổi cho P10.5; vẫn dùng JWT bearer attached qua `AuthorizationDelegatingHandler`. |
| **IPv4 BaseUrl** (P10.2) | `CclApi:BaseUrl` trong `appsettings.json` đang là `http://127.0.0.1:5100`. Test endpoint mới phải chạy cùng pattern (xlsx upload chunk size + timeout: tăng `ApiClientOptions.Timeout` lên 60s cho route upload). |
| **`#if DEBUG` gating** (P10.3 W2) | Mọi DEBUG verify hook (DEBUG button, JS observer, Console.WriteLine breadcrumb) phải gate `#if DEBUG` + REMOVE trước ship — gắn vào PR cleanup checklist. |
| **No silent fail on permission denied** (P10.2 + P10.3 W2 camera) | File picker (xlsx + drawings) denied/cancelled — surface operator-readable banner + retry CTA. Không "swallow exception". |
| **WO_ADVANCE_DEVICE from/to capture trap** (P10.3 W4) | Audit hooks trong controller wrapping mutator — luôn capture entity state BEFORE service call vì EF tracks same instance. Áp dụng cho mọi mutation endpoint mới (SpecApprove, SpecRevise, etc.). |
| **MAUI hosted-service phải tự khởi động** (P10.3 W4) | App.xaml.cs đã có `Task.Run(...GetServices<IHostedService>())` boot. Nếu P10.5 thêm hosted-service mới (vd background re-fetch của grid), boot qua đường đã có sẵn. |
| **PostConfigure<IServiceProvider> for ApiClientOptions** (P10.3 W4) | DeviceId đã được wired vào client requests. P10.5 reuse y nguyên — không thêm config hook mới. |
| **Bilingual VN trước EN** (P10.2 login) | UI mới ship VN trước; EN fallback ở error code maps. Không block trên i18n resx — strings inline với comment marker `// i18n` cho future port. |
| **Test-first cho regression-fix path** (P10.3 W4) | Mọi gotcha trong hardware verify → unit/integration test trước khi PR merge. |

---

## 1. AUDIT — legacy Web + API hiện có

### 1.1 Legacy UI surface (CCL.MES.Web)

5 NPI grids + 12+ shared modals + Engineer Spec full subsystem. Total ~7,690 LOC
spec-related code; ~1,150 LOC NPI grids; ~1,150 i18n keys.

#### NPI grids (5 surface)

| Surface | Route | File | LOC | Cols | Features |
| --- | --- | --- | --- | --- | --- |
| Engineer Spec list | `/npi/engineer-spec` | `Pages/Npi/EngineerSpec.razor` | 792 | 14 visible (+5 legacy hidden) | Search 3-field + paginate 50/page + status filter chip (Active/Trash/All) + status badge + columns toggle (localStorage) + context menu 7 items |
| Structure (BOM) | `/npi/engineer-structure` | `Pages/Npi/EngineerStructure.razor` | 267 | 16 | Search + paginate + columns toggle + CSV import |
| Routine | `/npi/engineer-routine` | `Pages/Npi/EngineerRoutine.razor` | 263 | 20 | Search + paginate + columns toggle + CSV import |
| Raw Materials | `/npi/raw-materials` | `Pages/Npi/RawMaterials.razor` | 320 | 28 total / 12 default | Search + paginate + columns toggle + sticky first col + status badge + CSV import |
| Work Centers | `/npi/workcenter` | `Pages/Npi/WorkCenter.razor` | 414 | 6 | Search + paginate + columns toggle + 17-area badge + active toggle + context menu (Edit/Copy/Toggle/Open) + CSV import |

#### Engineer Spec subsystem (showcard + 6-tab detail)

| Component | File | LOC | Purpose |
| --- | --- | --- | --- |
| `EngineerSpecDetail.razor` | `Pages/Npi/` | **2,407** | Full detail page with all 6 tabs |
| `SpecShowcard.razor` | `Shared/` | **1,024** | Render dispatch (Silk/Flexo/Generic) — Full/Compact/Preview modes |
| `SpecDetailModal.razor` | `Shared/` | 163 | Compact modal peek (entrypoint từ grid double-click) |
| `CreateSpecModal.razor` | `Shared/` | ~150 | 3-step create: planner pick → xlsx preview → save |
| `SpecEditModal.razor` | `Shared/` | 201 | Draft-only field edit |
| `SpecCopyModal.razor` | `Shared/` | 210 | Copy → new Draft |
| `SpecReviseModal.razor` | `Shared/` | 166 | Mandatory reason field + auto-supersede source |
| `SpecSupersedeConfirmModal.razor` | `Shared/` | 149 | Type-SpecCode confirm gate |
| `SpecTrashConfirmModal.razor` | `Shared/` | 123 | Soft-delete with WO-active blocker |
| `NpiImportModal.razor` | `Shared/` | 282 | Generic 3-step CSV/xlsx import (Pick → Preview → Apply) |
| `WorkCenterContextMenu.razor` + `WorkCenterEditModal.razor` + `WorkCenterInfoModal.razor` | `Shared/` | ~200 | Per-row actions for WC grid |

**6-tab structure** trong `EngineerSpecDetail.razor` (confirmed exact):
1. **Specification** — showcard render (material + print + diecut + finishing sub-specs)
2. **Drawings** — 9 `DrawingKind` slots (CustomerDrawing, NpiPrintLayout, NpiCutLayout, IpqcPrintReference, IpqcCutReference, FqcChecksheet, OqcChecksheet, CustomerApproval, InternalProof); multi-version per slot; 3-role approval chip
3. **QC Plans** — 4 stages (IpqcPrint, IpqcCut, Fqc, Oqc); criterion editor 6-col table per stage; atomic per-stage upsert
4. **QC Capture** — operator-side per-criterion entry (Pass/Fail/NA + measurement + ng_reason + comment)
5. **Artwork** — color spec rendering
6. **Setup** — process parameters

#### Permissions matrix (observed)

| Surface | Policy | Roles |
| --- | --- | --- |
| Engineer Spec read | `NpiSpecRead` | Admin / Supervisor / Engineer |
| Spec mutation (Create/Approve/Edit/Copy/Revise/Supersede/Trash/Restore) | inline `Authorize(Roles=…)` | Admin / Engineer |
| Drawing upload + decide chain | inline | Admin / Engineer (+ department check trong service) |
| QC Plan upsert | inline | Admin / Engineer |
| QC Capture record | inline | Operator + Admin + Engineer + QC |
| Spec Export (CSV/XLSX/PDF) | inline | Admin / Supervisor / Engineer |
| WC update / copy / set-active | inline | Admin / Engineer |
| Refresh-samples (admin batch) | inline | Admin only |
| Trash purge (hard-delete) | inline | Admin only |

#### Toolbar actions / context menu (per grid)

- **Engineer Spec**: Create Spec (Admin/Engineer) | Search 3-field | Status chip | Columns toggle. Per-row: Open / Edit (Draft only) / Copy / Revise (Approved/Released) / Mark Superseded / Trash | Restore (Trash view only) | Approve inline (Draft/InReview).
- **Structure / Routine / Raw Materials**: Search | Columns toggle | CSV import (Admin/Engineer). No per-row actions.
- **Work Centers**: Search | Columns toggle | CSV import. Per-row: Open / Info / Edit / Copy / Toggle Active.

### 1.2 Application services state

| Service | Public READ | Public WRITE | Status |
| --- | --- | --- | --- |
| `NpiService` | WorkCentersAsync, RawMaterialsAsync, RoutingAsync, StructuresAsync, WorkCenterUsageAsync, WorkCenterDetailAsync | UpdateWorkCenterAsync, CopyWorkCenterAsync, SetActiveAsync | WC mutations exist; RM/Routing/Structure READ-only by design (CSV import bulk-replace via separate `*CsvTarget` classes) |
| `SpecService` | SpecsAsync, SpecDetailAsync, SpecContentAsync, SpecAuditTrailAsync, ProductsForDropdownAsync | CreateAsync, ApproveAsync, UpdateAsync, CopyAsync, ReviseAsync, SupersedeAsync, TrashAsync, RestoreAsync | **COMPLETE** — full lifecycle |
| `SpecImportService` | (preview reads xlsx; no DB) | SaveAsync (atomic with audit emit), RefreshSamplesAsync (admin batch) | **COMPLETE** |
| `DrawingsService` | ListByRevisionAsync, GetForDownloadAsync | UploadAsync (blob persist + version row), DecideAsync (3-role chip with department gate) | **COMPLETE** |
| `SpecQcWindowService` | ListByRevisionAsync | UpsertStageAsync (atomic per-stage CRUD: criteria add/update/delete) | **COMPLETE** |
| `SpecQcCaptureService` | ListByRevisionAsync, ListReasonCodesAsync | CreateAsync (per-criterion result), ApproveAsync | **COMPLETE** |
| `SpecTrashPurgeService` | — | PurgeAsync (admin hard-delete + blob cleanup) | **COMPLETE** (hosted/manual job) |
| `IqcService` | ListAsync, GetWithDetailsAsync | CreateAsync, ApproveAsync | COMPLETE — pre-WO raw-material gate |
| `QcService` | ListAsync | CreateAsync, ApproveAsync | COMPLETE — in-WO IPQC/FQC/OQC |
| `WorkOrderService` | GetAllAsync, GetAsync, ShopOrderListAsync, GetDrawerAsync | CreateAsync, AdvanceAsync (P10.3 W4), UpdateFlagsAsync | Advance only exposed; Create/UpdateFlags ports defer cho P10.6+ shop-floor screens |
| `OeeService` | GetMachinesAsync, ComputeAsync | StartAsync, PauseAsync, ResumeAsync, FinishAsync | defer P10.5 — production events là core của offline-sync (P10.4) |

### 1.3 CCL.MES.Api hiện có (post-P10.4 W4)

| Controller | Route base | READ endpoints | Mutation endpoints |
| --- | --- | --- | --- |
| `AuthController` | `/api/v2/auth` | `me` | `login`, `refresh`, `logout` |
| `HealthController` | `/api/v2/health` | base | — |
| `SystemLogController` | `/api/v2/system-log` | filtered query (Admin only) | — |
| `NpiController` | `/api/v2/npi` | `workcenters`, `workcenters/{id}`, `rawmaterials`, `routings`, `structures` | — |
| `SpecsController` | `/api/v2/specs` | `List`, `Detail/{id}`, `Products` (drop-down) | — |
| `IqcController` | `/api/v2/iqc` | `List`, `Get/{id}` | — |
| `QcController` | `/api/v2/qc` | `List` | — |
| `QcSpecController` | `/api/v2/qc-specs` | `Windows/{revId}`, `Captures/{revId}`, `ReasonCodes` | — |
| `DrawingsController` | `/api/v2/drawings` | `ListByRevision/{revId}` | — |
| `WorkOrdersController` | `/api/v2/work-orders` | `List`, `Get/{id}`, `ShopOrders`, `Drawer/{woNo}`, `Summary` | `Advance/{id}` (W4) |
| `OeeController` | `/api/v2/oee` | `machines`, `compute` | — |
| `WiController` | `/api/v2/wi` | (Work Instructions read) | — |
| `DevicesController` | `/api/v2/devices` | `Get/{id}` | `scan-log`, `heartbeat` (W4) |

**API hiện tại 80% read-only**. Engineer Spec mutation path KHÔNG TỒN TẠI — legacy
Web bypass API và gọi `SpecService` qua DI in-process. MAUI client không có
in-process service path → cần expose toàn bộ mutation surface qua HTTP.

### 1.4 MAUI Hybrid client hiện có (post-W4)

| Component | LOC | Status (reuse target trong P10.5) |
| --- | --- | --- |
| `NpiWorkCenters.razor` | 123 | **Pattern proven** — clone cho 4 grid mới (Structure / Routine / RawMaterials / Spec list) |
| `WorkOrders.razor` | 362 | Pattern cho scan-driven detail flow — reuse logic skeleton cho Spec detail navigation |
| `Login.razor` | 184 | Login form (autocomplete=off, tabindex, Enter submit, network-error banner) — pattern reuse cho mutation forms |
| `MacCatalystKeyboardFix.razor` | 84 | Tab/Enter polyfill — mandatory wrap cho mọi mới page |
| `ConnectivityBanner.razor` | 39 | Offline banner — đã có; reuse cho mutation paths |
| `Mode.razor`, `Hardware.razor`, `Lock.razor` | 297 | Existing pages — không thay đổi |
| `ScannerTestPanel.razor` | 151 | Reuse cho scan-driven Spec detail navigation |
| `app.css` | 750 | Add new sections cho 14-col grid + 6-tab modal + showcard layout (~+1000 LOC across 7 PRs) |
| `ICclApiClient` | 220+ | **Extend** với SpecMutations + NpiMutations + DrawingsUpload + QcMutations + Exports |
| MAUI Picker / File APIs | not imported | **THÊM**: `Microsoft.Maui.Storage.FilePicker` + `MediaPicker` cho xlsx + drawing upload + (optional) camera-as-drawing capture |

---

## 2. INVENTORY: API mutation endpoints cần thêm

**Tất cả endpoints land trong `CCL-MES-Hybrid/src/CCL.MES.Api/Controllers/`** —
legacy `src/CCL.MES.Web/Controllers/*Controller.cs` 0 diff. Reuse Application
service qua project-reference (`ProjectReference Include="..\..\..\src\CCL.MES.Application\..."`)
đã có sẵn trong `CCL.MES.Api.csproj` (theo Lesson 0 từ P10.1 setup).

### 2.1 Auth policies (cần thêm vào Program.cs)

| Policy | Roles | Use sites |
| --- | --- | --- |
| `NpiSpecWrite` | Admin, Engineer | SpecsController mutations |
| `NpiWrite` | Admin, Engineer | NpiController WC mutations |
| `QcWrite` | Admin, Engineer, QC | QcController + IqcController + QcSpecController mutations |
| `QcCapture` | Admin, Engineer, QC, Operator | QcSpecController capture-only |
| `WoWrite` | Admin, Supervisor, Engineer | WorkOrdersController Create / UpdateFlags |
| `AdminOnly` | Admin | Trash purge, refresh-samples, system-log filter (đã có) |

### 2.2 Endpoints (24 mutations) — chia theo controller

#### `SpecsController` (8 mutations + 2 export endpoints)

| Verb | Route | Body | Policy | Application call |
| --- | --- | --- | --- | --- |
| POST | `/api/v2/specs` | `CreateSpecRequest` | `NpiSpecWrite` | `SpecService.CreateAsync(r, actor)` |
| POST | `/api/v2/specs/{revId}/approve` | `{}` | `NpiSpecWrite` | `SpecService.ApproveAsync(revId, actor)` |
| POST | `/api/v2/specs/{revId}/copy` | `CopySpecRequest` | `NpiSpecWrite` | `SpecService.CopyAsync(srcRevId, r, actor)` |
| PUT  | `/api/v2/specs/{revId}` | `UpdateSpecRequest` | `NpiSpecWrite` | `SpecService.UpdateAsync(revId, r, actor)` (Draft-only — server enforces 422 on non-Draft) |
| POST | `/api/v2/specs/{revId}/revise` | `ReviseSpecRequest` (`reason ≥5 chars`) | `NpiSpecWrite` | `SpecService.ReviseAsync(srcRevId, r, actor)` |
| POST | `/api/v2/specs/{revId}/supersede` | `SupersedeSpecRequest` (`confirmSpecCode`) | `NpiSpecWrite` | `SpecService.SupersedeAsync(revId, r, actor)` |
| POST | `/api/v2/specs/{revId}/trash` | `{}` | `NpiSpecWrite` | `SpecService.TrashAsync(revId, actor)` (returns 409 nếu WO-active) |
| POST | `/api/v2/specs/{revId}/restore` | `{}` | `NpiSpecWrite` | `SpecService.RestoreAsync(revId, actor)` |
| POST | `/api/v2/specs/import` | multipart `file: xlsx` + `productId` + `specCode` + `title` | `NpiSpecWrite` | `SpecImportService.SaveAsync(stream, args, actor)` |
| POST | `/api/v2/specs/import/preview` | multipart `file: xlsx` | `NpiSpecWrite` | `SpecImportService.PreviewAsync(stream)` (no DB; returns parse preview) |
| GET  | `/api/v2/specs/export/{kind: csv\|xlsx\|pdf}` | query: `?search=&view=` | `NpiSpecRead` | List exporter; binary stream |
| GET  | `/api/v2/specs/{revId}/export/sheet.pdf` | none | `NpiSpecRead` | `SpecSheetExporter` (single-spec render to PDF) |
| POST | `/api/v2/specs/admin/refresh-samples` | `{ force: bool }` | `AdminOnly` | `SpecImportService.RefreshSamplesAsync(force, actor)` |

#### `NpiController` (3 WC mutations)

| Verb | Route | Body | Policy | Application call |
| --- | --- | --- | --- | --- |
| PUT  | `/api/v2/npi/workcenters/{id}` | `UpdateWorkCenterRequest` | `NpiWrite` | `NpiService.UpdateWorkCenterAsync(id, r, actor)` |
| POST | `/api/v2/npi/workcenters/{id}/copy` | `CopyWorkCenterRequest` | `NpiWrite` | `NpiService.CopyWorkCenterAsync(srcId, r, actor)` |
| PATCH | `/api/v2/npi/workcenters/{id}/active` | `{ active: bool }` | `NpiWrite` | `NpiService.SetActiveAsync(id, active, actor)` |

(CSV import cho Structure / Routine / RawMaterials / WC: NOT trong P10.5 scope.
Bulk replace CSV qua legacy Web hoặc 1 endpoint thêm trong P10.5.next nếu Henry
muốn — operator hiện tại upload qua Web là OK.)

#### `DrawingsController` (2 mutations)

| Verb | Route | Body | Policy | Application call |
| --- | --- | --- | --- | --- |
| POST | `/api/v2/drawings/{revId}/upload` | multipart: `file` + `kind: DrawingKind` + `changeReason?` | `NpiSpecWrite` | `DrawingsService.UploadAsync(revId, stream, kind, ...)` |
| POST | `/api/v2/drawings/{revId}/versions/{verId}/decide` | `{ role: Npi\|Production\|Qc, decision: Approved\|Rejected, comment? }` | `NpiSpecWrite` + per-role department check trong service | `DrawingsService.DecideAsync(...)` |
| GET  | `/api/v2/drawings/{revId}/versions/{verId}/file` | inline download (already exists in legacy; mirror trong API) | `NpiSpecRead` | `DrawingsService.GetForDownloadAsync(...)` |

#### `QcSpecController` (2 mutations — QC Plan + QC Capture)

| Verb | Route | Body | Policy | Application call |
| --- | --- | --- | --- | --- |
| PUT  | `/api/v2/qc-specs/{revId}/windows/{stage}` | `UpsertQcStageRequest` (criteria[]: id?, name, passCriteria?, measureMethod?, frequency?) | `NpiSpecWrite` | `SpecQcWindowService.UpsertStageAsync(revId, stage, r, actor)` |
| POST | `/api/v2/qc-specs/{revId}/captures` | `CreateQcCaptureRequest` | `QcCapture` | `SpecQcCaptureService.CreateAsync(revId, r, actor)` |

#### `IqcController` + `QcController` (4 mutations)

| Verb | Route | Body | Policy | Application call |
| --- | --- | --- | --- | --- |
| POST | `/api/v2/iqc` | `CreateIqcRequest` | `QcWrite` | `IqcService.CreateAsync(r, actor, role)` |
| POST | `/api/v2/iqc/{id}/approve` | `{ pass: bool }` | `QcWrite` | `IqcService.ApproveAsync(id, pass, actor, role)` |
| POST | `/api/v2/qc` | `CreateQcRequest` | `QcWrite` | `QcService.CreateAsync(r, actor, role)` |
| POST | `/api/v2/qc/{id}/approve` | `{ pass: bool }` | `QcWrite` | `QcService.ApproveAsync(id, pass, actor, role)` |

**Tổng cộng**: 8 spec mutations + 2 import + 2 exports + 1 admin batch + 3 WC
mutations + 2 drawings + 2 QC spec + 4 QC = **24 endpoints + 2 file streams =
26 endpoints**. Cộng test count target: ~80 new endpoint tests + ~50 UI logic
tests (helpers + viewmodel pure logic).

### 2.3 Audit emit trong controllers

Per **W4 lesson learned**, mọi mutation wrapping legacy service phải:
1. **Capture entity state BEFORE service call** (vì EF tracks same instance).
2. Emit `<ACTION>_DEVICE` paired audit row khi `X-Device-Id` header có
   (đã pattern hoá trong `WO_ADVANCE_DEVICE`).
3. Source audit từ service tự body sang controller chỉ khi pre-existing emit
   không cover device id.

Concrete: `SpecService.CopyAsync` đã emit `SPEC_COPY`. Controller thêm
`SPEC_COPY_DEVICE` khi `X-Device-Id` present — detail JSON: `{source_id,
source_code, new_id, new_code, device_id}`. Pattern lặp lại cho mọi mutation.

---

## 3. UI REUSE STRATEGY — chọn Option B

### 3.1 Two paths considered

**Option A** — refactor Spec UI vào RCL với double-binding abstraction
(server-direct cho Web + API-backed cho MAUI):
- Pros: DRY tuyệt đối. Single source of truth cho 6-tab UI.
- Cons:
  - Spec UI ~7,690 LOC hiện tại tightly coupled vào DI `DbContext` + service
    in-process + `IStringLocalizer<SharedResource>` + Razor circuit cookie
    auth.
  - Refactor cần introduce `ISpecApi` abstraction wrap toàn bộ
    `SpecService`/`DrawingsService`/`SpecQcWindowService`/etc. có 2 impl: direct
    DI gọi service, và HTTP gọi API.
  - Mỗi UI component cần unwrap khỏi DI direct injection → inject abstraction
    interface mới.
  - Refactor 7,690 LOC trong legacy Web là vi phạm constraint "**legacy
    `src/CCL.MES.{Domain,Application,Infrastructure,Web}` is READ-ONLY**".
  - Cookie auth → JWT mapping ở mid-tier sẽ cần một adapter layer.
  - Test suite cũ của Web (Blazor circuit tests) sẽ vỡ.
  - Estimate effort: 5–7 PRs riêng cho refactor + 7 PRs cho port = 12–14 PRs +
    rủi ro break legacy Web ở mỗi step.

**Option B** — build new UI components trong `CCL.MES.Hybrid.Razor` calling
`ICclApiClient`. Một phần component sẽ trùng với Web (vd showcard render):
- Pros:
  - Zero diff cho legacy Web. Constraint honored.
  - Mỗi PR shipping standalone — không depend vào legacy Web refactor.
  - Verify pattern proven (NpiWorkCenters.razor + WorkOrders.razor đã clone
    được — same pattern repeat).
  - File picker / MediaPicker cho upload phải MAUI-specific anyway → không
    share được với server Web.
  - Test framework rõ ràng: integration test endpoints + unit test viewmodel
    + manual hardware verify.
- Cons:
  - **Component duplication**: 6-tab Spec detail + showcard render code lặp
    lại giữa Web + MAUI. Future mỗi feature change ở Spec UI phải apply 2 lần.
  - Khoảng ~3,000–4,000 LOC duplication estimated (showcard + 6-tab markup +
    modal forms).
  - i18n key duplication (Web có `IStringLocalizer`; MAUI tạm inline VN
    strings).

### 3.2 Quyết định: Option B

**Rationale**:
1. Constraint "legacy 0 diff" là CỨNG. Option A đụng vào constraint từ PR thứ
   nhất.
2. Spec UI legacy đang ổn định + đã tested + đã shipped — operator hiện tại
   trên Web. Touching nó để port sang MAUI là risk-multiplier với 0 immediate
   benefit cho Web user.
3. MAUI client phục vụ shop-floor + traveling engineer → UX requirements khác
   với Web admin console (touch-friendly, scan-driven, larger fonts, offline-
   tolerant in future). Component-shared sẽ ép Web phải accommodate mobile UX
   anyway.
4. P10.3 W4 đã verify pattern "MAUI page calls ICclApiClient → API wraps
   legacy service" hoạt động end-to-end. Repeat cho 7 surface mới.
5. Duplication 3–4k LOC chấp nhận được vì:
   - Mỗi tab modal là 100–300 LOC độc lập.
   - Showcard render template logic là pure function — có thể extract sang
     `CCL.MES.Shared` thành C# helper trả về DTO + render Razor cả 2 phía.
6. i18n duplication: P10.6+ có thể consolidate khi MAUI ship resx. P10.5
   accept inline VN strings + EN comment marker.

### 3.3 Showcard render — partial sharing strategy

`SpecShowcard.razor` (1,024 LOC) là phần Spec UI lớn nhất. **Không port full
trong P10.5b** (Spec list read-only). Phân chia:
- **P10.5b**: ship **compact mode** (~200 LOC) — chỉ render top-level identity
  + status. Đủ cho list double-click peek.
- **P10.5c**: thêm **full mode** (~600 LOC) — material + print + diecut +
  finishing sub-spec render. Cần khi mutation modal ship.
- **P10.5b–c shared**: extract `SpecShowcardDataMapper` sang `CCL.MES.Shared`
  (pure C#, takes `SpecContentDto` → returns flat view-model) — Razor 2 phía
  có thể consume cùng VM.

### 3.4 Other MAUI-specific patterns

- **Pagination**: clone `NpiWorkCenters` pattern (manual `_page` + `_pageSize`
  + prev/next). DO NOT introduce virtual scroll — operators on Catalyst with
  trackpad scroll smooth enough.
- **Filter / sort**: query params on API. Sort defer P10.5.next nếu Henry
  muốn (current legacy Web không có sort UI).
- **Column toggle**: persist qua `Preferences` (key pattern
  `cclmes.hybrid.grid-cols.<surface>.v1`), mirror localStorage approach.
- **Status badges**: clone Web CSS class names (`shop-pill-running`,
  `wc-area-*`, `spec-status-*`) → app.css addition.
- **Modal primitive**: build `<Modal>` shared component first (1 PR's worth in
  P10.5a) so 12+ modals reuse same shell with size + severity + responsive
  variants. Lesson 10 of Ops Control CLAUDE.md (sibling read-only) — bài học
  tốt mà không vi phạm constraint.
- **Context menu**: trigger right-click + long-press (Catalyst touchpad +
  tablet). Position: relative to row anchor (avoid clipped at viewport edge).

---

## 4. FILE UPLOAD từ device

### 4.1 Use cases trong P10.5

| Use case | Trigger | Size limit (existing) | MIME |
| --- | --- | --- | --- |
| Spec import xlsx | CreateSpecModal step 2 | 10 MB (legacy SpecImportService) | `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet` |
| Drawing upload | Drawings tab per-slot | **5 MB** (legacy `DrawingsService` enforces) | `application/pdf`, `image/png`, `image/jpeg`, `image/svg+xml`, `image/gif`, `image/webp`, `application/dxf` (AutoCAD DXF), `application/acad` (DWG), `application/postscript` (AI) — 9 types |
| QC Capture optional photo (P10.5f, optional) | Per-criterion entry | 2 MB | `image/jpeg`, `image/png` |

### 4.2 MAUI file picker integration

```csharp
// CCL.MES.Hybrid (host project) — Platforms/MacCatalyst/CatalystFilePicker.cs
public sealed class CatalystFilePicker : IFilePickerService
{
    public async Task<PickedFile?> PickAsync(FilePickFilter filter, CancellationToken ct = default)
    {
        // Reuse MAUI's FilePicker.Default.PickAsync which abstracts UIDocumentPicker
        // on Catalyst + iOS. Catalyst FilePicker uses UIDocumentPickerViewController
        // with allowedTypes mapped from UTIs.
        var pickOpts = new PickOptions
        {
            FileTypes = ToFilePickerFileType(filter),
        };
        var result = await FilePicker.Default.PickAsync(pickOpts);
        if (result is null) return null;
        using var stream = await result.OpenReadAsync();
        // Buffer to memory for size check + multi-pass upload; OK at 10MB limit.
        var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return new PickedFile(result.FileName, result.ContentType, ms.ToArray());
    }
}
```

Interface trong `CCL.MES.Hybrid.Client`:
```csharp
public interface IFilePickerService
{
    Task<PickedFile?> PickAsync(FilePickFilter filter, CancellationToken ct = default);
}
public sealed record PickedFile(string FileName, string ContentType, byte[] Bytes);
public sealed record FilePickFilter(string[] AllowedExtensions, long MaxBytes);
```

### 4.3 Camera-as-drawing (optional, P10.5e)

For drawing slots that accept image kinds (IpqcPrintReference, IpqcCutReference,
CustomerApproval, InternalProof), allow shop-floor operator to **capture photo
directly via Catalyst camera** (reuse W2 AVFoundation permission gate):

```csharp
// MediaPicker.Default.CapturePhotoAsync() — uses UIImagePickerController
// on Catalyst. Reuses existing NSCameraUsageDescription entitlement.
```

UI: drawing slot button group `[Pick file] [Take photo*]` where `*` only renders
when `kind` is image-capable AND `MediaPicker.Default.IsCaptureSupported` true.

### 4.4 Upload qua API

Multi-part form data via `MultipartFormDataContent`:
```csharp
public async Task<DrawingUploadResponse> UploadDrawingAsync(long revId, PickedFile file,
    DrawingKind kind, string? changeReason, CancellationToken ct)
{
    using var form = new MultipartFormDataContent();
    using var bytes = new ByteArrayContent(file.Bytes);
    bytes.Headers.ContentType = MediaTypeHeaderValue.Parse(file.ContentType);
    form.Add(bytes, "file", file.FileName);
    form.Add(new StringContent(kind.ToString()), "kind");
    if (!string.IsNullOrWhiteSpace(changeReason))
        form.Add(new StringContent(changeReason), "changeReason");
    using var resp = await _http.PostAsync(
        $"/{ApiVersion.Prefix}/drawings/{revId}/upload", form, ct);
    return await ReadAsAsync<DrawingUploadResponse>(resp, ct);
}
```

### 4.5 Server-side enforcement (reuse legacy guards)

`DrawingsService.UploadAsync` đã có 6 security guards trong legacy
(file-extension allowlist, MIME sniff, max-size, blob-key safe-name, path
traversal, virus-pattern). Reuse y nguyên qua project-reference call. API
controller chỉ validate:
- `revId` exists + scoped to authenticated user's permission
- `kind` is parseable enum
- Multipart payload < `ApiBehaviorOptions.MaxAllowedContentLength` (set 15 MB
  in Program.cs)
- Returns 413 Payload Too Large nếu vượt; client UI surfaces operator-readable
  banner.

### 4.6 Network resilience

Upload có khả năng failure rate cao hơn read endpoints (mobile network, blob
storage hiccup). Per Q4 lock — **không local outbox**. UI behaviour:
- Show progress (% bytes uploaded) trong modal — cần `ProgressHandler` qua
  `HttpClient.SendAsync` (sample sẵn cho Catalyst).
- On failure: error banner + `[Thử lại]` button keeps file payload in memory
  for re-upload without re-pick.
- Timeout: per-route timeout 90s for upload routes (override default 30s từ
  `ApiClientOptions`).

---

## 5. ONLINE-ONLY (Q4 lock — carry forward)

### 5.1 Confirm

Per Q4 from P10.3 plan + W4 lock: **mutation paths fail fast với operator-visible
error + retry**. P10.5 honors y nguyên. No outbox; no background re-queue.

### 5.2 UI patterns

- **Mutation submit button**: disabled during in-flight; loading spinner.
- **Connectivity check**: `IConnectivityMonitor.IsConnected` gated trước mutation
  call. Banner `<ConnectivityBanner>` đã có ở top of layout.
- **Idempotency**: mutation endpoint phụ thuộc backend (existing services already
  emit unique IDs). Client retry không cần idempotency key cho version đầu —
  nếu duplicate submit phát hiện trong UAT, P10.5 followup PR thêm `Idempotency-
  Key` header.
- **Optimistic update**: NO — mutation thành công phải refetch (server-authoritative
  truth). Server returns updated DTO, client replaces local state.

### 5.3 Read-path caching

- **Per-page memory cache** với `IMemoryCache` trong client (TTL 30s) optional.
  V1 ship without cache — measure UX latency on Catalyst trước rồi add cache
  trong followup nếu cần.
- **Pagination snapshot**: khi user scroll trong WorkOrder drawer mở (giữ snapshot
  để insulate khỏi background re-fetch — lesson 29 trong Ops Control sibling).
  Apply pattern cho Spec list + 6-tab detail page.

---

## 6. CHIA PHASE — 7 sub-PRs P10.5a → P10.5g

Estimate per PR + risk factor. Total effort ~6–8 weeks 1 dev FTE; ~12 weeks
parallel dev. Each PR shippable independent — Henry approval per PR.

### 6.1 Risk classification

- **L (Low)**: read-only port; pattern proven by NpiWorkCenters.razor.
- **M (Medium)**: mutation endpoint introduction; modal form + validation;
  audit hooks.
- **H (High)**: file upload (xlsx + blob); state machine ports; multi-step
  flows; hardware integration risk.

### 6.2 PR sequence

#### **P10.5a — NPI 4 read-only grids + shared modal primitive** (Risk: L)

- Scope:
  - Clone `NpiWorkCenters.razor` pattern → `NpiStructure.razor`,
    `NpiRoutine.razor`, `NpiRawMaterials.razor`, `NpiSpec.razor` (read-only
    list cho Spec, 14-col, không context menu yet).
  - ICclApiClient extend (already have 4 NPI methods — just add Spec list endpoint
    via existing `SpecsController.GetAll`).
  - `<Modal>` shared component primitive (size sm/md/lg/xl + severity + close-on-
    esc) — reuse cho mọi PR sau.
  - Nav menu update: SẢN XUẤT section → NPI sub-group với 4 grid links + Spec
    list link.
  - Status badge CSS additions (`shop-pill-*`, `spec-status-*`, `wc-area-*`).
- Endpoints needed: 0 mutation (read-only). Maybe expose `SpecsController.GetAll`
  thêm filter query support.
- File touches:
  - `CCL.MES.Hybrid.Razor/Pages/NpiStructure.razor` (new ~150 LOC)
  - `…/NpiRoutine.razor` (new ~150)
  - `…/NpiRawMaterials.razor` (new ~200)
  - `…/NpiSpec.razor` (new ~250)
  - `…/Shared/Modal.razor` (new ~150)
  - `…/Shared/NavMenu.razor` (+1 section)
  - `…/wwwroot/css/app.css` (+150)
  - `CCL.MES.Hybrid.Client/ICclApiClient.cs` (+1 method nếu cần filter)
- Tests: 4 unit tests per grid (pagination + filter + error path) = ~16 tests.
- Hardware verify: 4 grids render trên Catalyst with engineer login + pagination
  works.
- Estimate: **3–4 days**.
- Risk: L — pattern proven.
- Open risks: 14-col grid may overflow viewport on narrower Catalyst windows →
  column toggle helper essential. Sticky first col CSS.

#### **P10.5b — Spec list context menu + detail page (read) + compact showcard** (Risk: M)

- Scope:
  - Spec list: per-row context menu trigger (right-click + long-press).
  - Spec detail page (`/spec/{revId}`): mount 6-tab structure (Specification /
    Drawings / QC Plans / QC Capture / Artwork / Setup) but **all tabs read-
    only**. Just render data fetched from API.
  - Compact showcard mode (~200 LOC) cho list double-click peek.
  - Spec detail data fetch: `GET /api/v2/specs/{revId}/detail` (already exists)
    + `GET /api/v2/qc-specs/windows/{revId}` (exists) + `GET /api/v2/qc-specs/
    captures/{revId}` (exists) + `GET /api/v2/drawings/{revId}` (exists).
- Endpoints needed: 0 new (all read endpoints already exist). Maybe add
  `GET /api/v2/specs/{revId}/showcard` returning pre-flattened SpecShowcardVM
  (in `CCL.MES.Shared`).
- File touches:
  - `Pages/SpecDetail.razor` (new ~350 with all 6 tabs basic render)
  - `Shared/SpecContextMenu.razor` (new ~120)
  - `Shared/SpecShowcardCompact.razor` (new ~250)
  - `CCL.MES.Shared/Spec/SpecShowcardVm.cs` (new ~100 — flatten helper)
  - `app.css` (+250)
- Tests: 6 viewmodel unit tests (showcard flatten); 4 integration tests for
  `/showcard` if added.
- Hardware verify: navigate from list → detail; switch tabs; all data renders.
- Estimate: **5–7 days**.
- Risk: M — first deep multi-tab UI; UX surprises possible.
- Open risks: 6-tab navigation pattern on Catalyst (touch-tap vs click). 14-col
  grid + sidebar at same time can be cramped.

#### **P10.5c — Spec mutations + Import xlsx upload** (Risk: H)

- Scope:
  - SpecsController: add 8 mutation endpoints (Create / Approve / Copy / Update /
    Revise / Supersede / Trash / Restore).
  - SpecsController: add 2 import endpoints (`POST /import/preview` + `POST /import`)
    handling multipart xlsx.
  - MAUI host: `CatalystFilePicker` + `IFilePickerService`.
  - UI modals: `<CreateSpecModal>` 3-step (planner pick → xlsx preview → save);
    `<SpecEditModal>` Draft-only field edit; on-row Approve button inline.
  - Per-status display logic (Draft/InReview/Approved/Released/Superseded).
- Endpoints: 8 + 2 = **10 mutations + 1 file upload route**.
- File touches:
  - `CCL.MES.Api/Controllers/SpecsController.cs` (+~350 LOC mutations + upload
    handler)
  - `CCL.MES.Api/Program.cs` (+`NpiSpecWrite` policy)
  - `CCL.MES.Hybrid/Platforms/MacCatalyst/CatalystFilePicker.cs` (new ~80)
  - `CCL.MES.Hybrid.Client/IFilePickerService.cs` (new ~30)
  - `CCL.MES.Hybrid.Client/CclApiClient.cs` (+8 method wrappers + upload)
  - `Razor/Shared/CreateSpecModal.razor` (new ~300)
  - `Razor/Shared/SpecEditModal.razor` (new ~180)
  - `Razor/Pages/NpiSpec.razor` (wire context menu to actions)
- Tests: 8 endpoint tests + 2 upload integration tests + 3 viewmodel tests
  for modal flow.
- Hardware verify: create new spec via xlsx upload on Catalyst; approve; edit
  Draft.
- Estimate: **7–10 days**.
- Risk: H — first file upload + first cross-tab mutation flow.
- Open risks:
  - File picker permission UI на Catalyst (UIDocumentPicker may need adhoc
    entitlement check).
  - xlsx parse may surface preview rows differently in MAUI memory budget.
  - Timeout on slow LAN — upload 10MB at 1Mbps takes 80s vs 30s default. Bump
    to 90s for upload routes.

#### **P10.5d — Spec lifecycle (Copy / Revise / Supersede / Trash / Restore)** (Risk: M)

- Scope:
  - 5 modal forms: Copy / Revise / Supersede / Trash / Restore confirm.
  - Wire each to existing API mutations (shipped P10.5c).
  - Spec list status filter chip (Active / Trash / All).
  - List "Trash" view requires `?view=trash` query support (server already has
    `SpecListView` enum).
- Endpoints: 0 new (5 endpoints shipped P10.5c).
- File touches:
  - `Shared/SpecCopyModal.razor` (new ~200)
  - `Shared/SpecReviseModal.razor` (new ~200) — mandatory reason ≥5 chars
  - `Shared/SpecSupersedeConfirmModal.razor` (new ~150) — type-SpecCode confirm
  - `Shared/SpecTrashConfirmModal.razor` (new ~120) — WO-active block message
  - `Shared/SpecRestoreConfirmModal.razor` (new ~80)
  - `Pages/NpiSpec.razor` (+filter chip + Trash view)
- Tests: 5 modal viewmodel tests + 2 list filter tests.
- Hardware verify: full lifecycle cycle on Catalyst: copy → edit → revise →
  trash → restore.
- Estimate: **4–5 days**.
- Risk: M — mostly UI form work; mutation backends already proven in P10.5c.
- Open risks: WO-active blocker (409 from server) needs operator-clear
  message. Test với WO-attached spec on real DB.

#### **P10.5e — Drawings tab + upload chain + approval chip** (Risk: H)

- Scope:
  - Drawings tab full UI (9 slots per `DrawingKind`).
  - Upload modal per slot with file picker + (optional camera capture for image
    kinds).
  - Multi-version display per slot + version status badge.
  - 3-role approval chip (Npi / Production / Qc) with decide button.
  - Drawing version download viewer (inline PDF + image — Catalyst WebView
    handles natively).
  - DrawingsController: add upload + decide + inline file endpoints.
- Endpoints: 2 mutations + 1 file stream = 3 new.
- File touches:
  - `CCL.MES.Api/Controllers/DrawingsController.cs` (+~250 LOC mutations +
    file stream)
  - `Hybrid.Client/ICclApiClient.cs` (+3 methods)
  - `Razor/Shared/DrawingsTab.razor` (new ~600 — biggest single component)
  - `Razor/Shared/DrawingUploadModal.razor` (new ~250)
  - `Razor/Shared/DrawingApprovalChip.razor` (new ~120)
  - `Razor/Shared/DrawingVersionRow.razor` (new ~150)
  - `Hybrid/Platforms/MacCatalyst/CatalystMediaPicker.cs` (new ~100 — camera
    capture optional)
- Tests: 3 endpoint tests + 4 upload integration tests + 5 viewmodel tests.
- Hardware verify: upload drawing → request approval → decide on Catalyst.
- Estimate: **10–14 days**.
- Risk: H — most complex P10.5 PR; multiple async chain interactions.
- Open risks:
  - 5 MB drawing limit on slow LAN.
  - Camera-as-upload UX requires NSCameraUsageDescription verify (already there
    for W2 scanner; same entitlement).
  - PDF inline preview Catalyst WebView limit — fallback to system Preview
    via `IDeviceSettingsLauncher` style hook nếu fail.
  - 9 `DrawingKind` slots = lots of UI surface; need responsive layout.

#### **P10.5f — QC Plan upsert + QC Capture** (Risk: H)

- Scope:
  - QC Plans tab full UI: 4 stages (IpqcPrint / IpqcCut / Fqc / Oqc) with
    criterion editor 6-col table per stage. Per-stage dirty flag + Save button.
  - QC Capture tab: per-criterion form Pass/Fail/NA + measurement + ng_reason
    dropdown (from existing `ListReasonCodesAsync`) + comment.
  - QcSpecController: add 2 mutations (upsert stage + create capture).
  - IqcController + QcController: add 4 mutations (create + approve cho IQC +
    inline QC).
- Endpoints: 2 + 4 = **6 mutations**.
- File touches:
  - `CCL.MES.Api/Controllers/QcSpecController.cs` (+~200)
  - `CCL.MES.Api/Controllers/IqcController.cs` (+~150)
  - `CCL.MES.Api/Controllers/QcController.cs` (+~150)
  - `Hybrid.Client/ICclApiClient.cs` (+6 methods)
  - `Razor/Shared/QcPlansTab.razor` (new ~500)
  - `Razor/Shared/QcCaptureTab.razor` (new ~450)
  - `Razor/Shared/QcCriterionRow.razor` (new ~120)
  - `Razor/Shared/QcCaptureRow.razor` (new ~150)
- Tests: 6 endpoint tests + 3 viewmodel tests for dirty-flag logic + 2 for
  reason-code conditional rendering.
- Hardware verify: define QC plan → capture results on Catalyst.
- Estimate: **8–10 days**.
- Risk: H — biggest data-entry surface; ng_reason conditional logic; per-stage
  atomic upsert.
- Open risks:
  - Atomic upsert: nếu network drops mid-save, partial criterion may be lost
    server-side. Use single transaction wrapping in `SpecQcWindowService` —
    already there.
  - Operator-friendly UX for Fail-with-reason flow.

#### **P10.5g — Exports + admin refresh-samples** (Risk: M)

- Scope:
  - SpecsController: add 4 export endpoints (CSV list / XLSX list / PDF list /
    single-spec sheet PDF).
  - Admin: refresh-samples endpoint.
  - UI: toolbar export dropdown on Spec list (kind picker + filename auto-gen).
  - File download trên Catalyst: stream to MAUI `Save` dialog or write to
    `FileSystem.Current.AppDataDirectory` + open via system handler.
  - Admin refresh-samples button (Settings → Account Control style page nếu cần
    — defer cho P10.5g.next nếu không phải critical).
- Endpoints: 4 + 1 = 5 new.
- File touches:
  - `CCL.MES.Api/Controllers/SpecsController.cs` (+~150 — export handlers reuse
    legacy exporters via project-ref)
  - `Hybrid.Client/ICclApiClient.cs` (+5)
  - `Razor/Shared/SpecExportToolbar.razor` (new ~150)
  - `Razor/Pages/AdminBatchOps.razor` (new ~100 — refresh-samples + future
    admin jobs)
  - `Hybrid/Platforms/MacCatalyst/CatalystFileSave.cs` (new ~80 — UIDocumentPicker
    save mode)
- Tests: 5 endpoint tests + 1 viewmodel test.
- Hardware verify: export Spec list to PDF on Catalyst; download to ~/Downloads
  visible in Finder.
- Estimate: **4–5 days**.
- Risk: M — depends on legacy export library availability (ClosedXML / QuestPDF)
  in API project. Project reference should pull deps; verify trên Catalyst.
- Open risks:
  - Native libs (font rendering for QuestPDF on Catalyst arm64). Test build
    early.
  - Save-dialog UX trên Catalyst differs from Win — use UIDocumentPickerViewController
    mode picker.

### 6.3 Total estimate

| PR | Risk | Days | Cumulative |
| --- | --- | --- | --- |
| P10.5a | L | 3–4 | 3–4 |
| P10.5b | M | 5–7 | 8–11 |
| P10.5c | H | 7–10 | 15–21 |
| P10.5d | M | 4–5 | 19–26 |
| P10.5e | H | 10–14 | 29–40 |
| P10.5f | H | 8–10 | 37–50 |
| P10.5g | M | 4–5 | 41–55 |

**Total: 41–55 working days (~8–11 weeks)** for 1 dev FTE. With parallelism
(e.g., 5e + 5f independent + 5g independent + 5a/b read foundation done) could
compress to **6–8 weeks**.

### 6.4 PR ordering rationale

- 5a establishes pattern + modal primitive — every later PR builds on top.
- 5b unlocks Spec detail page mount — 5c/5d/5e/5f all attach to this surface.
- 5c is the foundation for write surface — every later PR uses its file picker
  + audit emit pattern.
- 5d ships after 5c because lifecycle modals reuse `SpecsController` mutation
  endpoints.
- 5e and 5f can ship in either order — both depend on 5b (detail page mount)
  + 5c (file picker for 5e). 5e is slightly higher risk so order should be:
  5e first to surface blob upload risks early; 5f follows.
- 5g last because exports surface end-to-end UX and admin batch ops are
  inherently low-traffic.

### 6.5 Rollback strategy

Each PR isolates UI behind:
- `Hardware:ScanEnabled` flag (existing) — gates Spec list + grids nav menu
  visibility.
- Per-PR: introduce `OPS_FEATURE_SPEC_MUTATION` env var defaulting OFF in API.
  Mutation endpoints return 503 nếu flag off. Flip ON after operator UAT per PR.

---

## 7. ARCHITECTURE deviations from baseline

1. **Project reference vs HTTP wrap**: API controllers wrap legacy Application
   services via DI direct (project reference). Pros: zero duplication of logic.
   Cons: API process depends on legacy assembly being deployable as a unit.
   Defensible because we ship both as a single Hybrid solution.
2. **No version negotiation**: API version is `v2` (existing); no v3 yet.
   Breaking changes ship in mutation route bodies (P10.5 first major mutation
   surface). Future Web client doesn't depend on these endpoints.
3. **MAUI host bundle size**: file picker + media picker + PDF render
   capabilities may bump install ~10MB. Defer to ship-time benchmark.
4. **Test target framework**: API tests use `net10.0`; UI logic tests use
   `net10.0` (xUnit only, no Razor render). UI integration tests defer to
   manual hardware verify until E2E test framework lands (planned for P10.6+).

---

## 8. RISK summary + open questions

### 8.1 Highest risks

| Risk | Severity | Mitigation |
| --- | --- | --- |
| File picker entitlement on adhoc Catalyst dev build | H | Fallback path: synthesize file via base64 input field for testing; reuse Keychain entitlement check pattern from P10.2. |
| xlsx parse memory budget on Catalyst | M | Stream parse on server; client only ships bytes; 10MB limit enforced before send. |
| 6-tab navigation UX on touchpad-only Catalyst | M | Tab interaction via click — standard; ESC closes modal pattern from PR shared modal primitive. |
| Drawing 5MB upload on slow LAN | M | Progress bar + retry; bump timeout to 90s; surface percentage trên modal. |
| Per-stage QC plan atomicity | L | Server-side transaction wrapping already in service; just verify trên test. |
| Export PDF render via QuestPDF on Catalyst arm64 | M | Test build early (P10.5a sanity); fallback to server-side render. |
| Duplication maintenance burden (3–4k LOC mirror Web Spec UI) | M | Doc convention: any change to Web Spec UI must mirror in MAUI PR within 7 days. Tracked via CLAUDE.md sprint history (sibling project pattern). |

### 8.2 Open questions (Q1..Q15)

**Q1**. UI Reuse strategy — chấp nhận Option B (build new in MAUI, duplicate
showcard partial)?
**Default**: YES — accept B per §3.2 rationale.

**Q2**. CSV import cho NPI grids (Structure / Routine / RawMaterials / WC) —
ship trong P10.5 hay defer?
**Default**: DEFER. Operator hiện upload qua legacy Web ok; MAUI ship đọc-thôi
NPI cho P10.5; CSV upload từ MAUI là P10.5.next.

**Q3**. Spec showcard full-mode render (1,024 LOC trong legacy) — port toàn bộ
6 sub-spec template trong P10.5b/c hay defer?
**Default**: ship **compact mode** trong P10.5b; full mode trong P10.5c (chỉ
khi mutation cần render diff). Defer Silk/Flexo dedicated templates → generic
fallback OK cho V1.

**Q4**. Online-only confirmed for all P10.5 mutations? (carry P10.3 W4 lock).
**Default**: YES — Q4 lock honored. No outbox.

**Q5**. File picker permission UX on Catalyst — fallback path nếu adhoc dev
build throw MissingEntitlement?
**Default**: Surface operator-readable error + "build prod-signed bundle"
guidance. Same pattern as P10.2 Keychain fallback (in-memory dev fallback NOT
applicable here — operator must use prod build for upload).

**Q6**. Camera-as-drawing (image kinds) — ship trong P10.5e hay defer?
**Default**: DEFER to P10.5e.next. Reuse W2 NSCameraUsageDescription
entitlement; UI affordance ship sau khi field operators yêu cầu.

**Q7**. Spec exports — file save UX on Catalyst (UIDocumentPickerViewController
save mode vs auto-write AppDataDirectory)?
**Default**: UIDocumentPickerViewController save dialog (operator picks
location). Fallback: write to ~/Downloads via FileSystem helpers.

**Q8**. Admin refresh-samples — ship dedicated admin page hay button trong
existing settings menu?
**Default**: Defer dedicated `/admin/batch-ops` page until P10.5g.next.
Ship endpoint chỉ trong P10.5g; admin tự gọi via curl/Postman cho V1.

**Q9**. Hardware verify trên Windows — defer hay parity-test mỗi PR?
**Default**: DEFER Windows verify ép buộc cho P10.6+. Mac Catalyst là test
gate cho mọi P10.5 PR. Windows abstraction maintained but only smoke-test
once at end of P10.5.

**Q10**. PR cleanup gate — `#if DEBUG` verification checklist?
**Default**: YES — mỗi PR có cleanup section bắt buộc trong description: no
DEBUG buttons, no Console.WriteLine breadcrumbs, no JS auto-clicker, no DEBUG
auto-login. Audit qua `grep -r "DEBUG\|w4-observer\|dbg-" CCL-MES-Hybrid/src/`
before push.

**Q11**. Idempotency key cho mutation? (e.g. spec.create duplicate clicks).
**Default**: NOT V1. Surface UAT detection issues then add `Idempotency-Key`
header in followup nếu duplicate submit detected (W4 pattern).

**Q12**. i18n strategy — ship Vietnamese inline hay setup MAUI ResourceLoader?
**Default**: Inline Vietnamese strings với `// i18n` marker comment. Future
P10.6+ ports resx infrastructure khi muốn. Don't block P10.5 trên i18n.

**Q13**. Modal primitive shape — port `<Modal>` từ sibling project (Ops Control
v1.2 Lesson 10) hay build new in MAUI?
**Default**: Build new (clean room). Sibling project is read-only reference;
study pattern, do not copy code.

**Q14**. Hosted-service heartbeat extension cho NPI/Spec last-touch — ship?
**Default**: DEFER P10.5.next. Heartbeat hiện tại đã cover station liveness;
last-touch on specific entity là analytics-grade, not operational.

**Q15**. Test framework cho UI logic — xUnit pure-C# viewmodel tests vs
component render tests (bUnit)?
**Default**: xUnit pure-C# cho viewmodel + helper. bUnit defer cho P10.6+ E2E
test framework. Manual hardware verify Catalyst is gate.

### 8.3 Henry decisions needed

Pls accept defaults hoặc override per Q (vd `Q3 = override: ship full mode in
P10.5b`). Once accepted, P10.5a starts immediately.

---

## 9. SUCCESS criteria

End of P10.5 (all 7 PRs merged):

- ✅ Operator on Mac Catalyst can browse all 5 NPI grids + filter + paginate.
- ✅ Engineer can create Spec via xlsx upload from device → server-side parse
  + persist + audit.
- ✅ Engineer can full lifecycle: Create → Edit (Draft) → Approve → Copy →
  Revise → Mark Superseded → Trash → Restore.
- ✅ Engineer can upload drawings (PDF/PNG/JPG/SVG/DWG/DXF — 9 types) +
  request approval chain (Npi → Production → Qc) → decide.
- ✅ QC engineer can define QC plan per stage (IpqcPrint/IpqcCut/Fqc/Oqc) +
  capture results.
- ✅ Spec list export (CSV / XLSX / PDF) downloadable to Catalyst Finder.
- ✅ Mutation endpoints all behind RBAC policies (`NpiSpecWrite`, `NpiWrite`,
  `QcWrite`, `QcCapture`).
- ✅ Legacy Web `Pages/Npi/EngineerSpec.razor` (792 LOC) etc. **unchanged**.
- ✅ Test suite: ~80 new endpoint integration tests + ~50 viewmodel/helper
  tests. 0 regressions.
- ✅ Hardware verify per PR before flag flip.

End-of-P10.5 ops state:
- Active engineer can full-time work on Spec authoring + lifecycle from
  Catalyst.
- Operator (shop-floor) continues using existing legacy Web for everything
  except scanned WO Accept (P10.3 W4).
- P10.6+ ports operator-side flows (WO create from template, IPQC inline, etc).

---

## 10. NEXT step

**Henry to review + accept/override Q1..Q15.** Once approved, P10.5a starts
immediately (branch `feat/p10.5a-npi-grids`). Per-PR Henry approval before
merge.

**STOP — chờ Henry review plan.**
