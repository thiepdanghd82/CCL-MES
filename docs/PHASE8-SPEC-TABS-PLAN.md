# Phase 8 — Spec Detail Tabs Plan

> Audit + plan for porting the 5 stub tabs in Engineer Spec Detail
> chrome (PR #42) to functional implementations.
>
> **Reference**: CMES sibling project at
> `/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CMES/apps/web/src/modules/spec/`
> — READ-ONLY (pattern study only; no writes back into CMES tree).
>
> **STOP point**: this document. No code until user approves PR split + Q answers.

---

## 0. Current state — chrome shipped, 5 tabs stub

PR #42 (merged in `126d411`) shipped the chrome shell on `/npi/engineer-spec/{id}`:

| # | Tab key | EN label | VI label | Icon | Status |
|---|---------|----------|----------|------|--------|
| 1 | `spec`       | Specification | Thông số       | (none)   | ✅ **Live** — renders `<SpecShowcard Mode="Full" />` |
| 2 | `drawings`   | Drawings      | Bản vẽ         | 📐       | 🟡 Stub "Coming soon" |
| 3 | `qc-plans`   | QC Plans      | Kế hoạch QC    | 📋       | 🟡 Stub "Coming soon" |
| 4 | `qc-capture` | QC Capture    | Ghi nhận QC    | ✅       | 🟡 Stub "Coming soon" |
| 5 | `artwork`    | Artwork       | Artwork        | 🎨       | 🟡 Stub "Coming soon" |
| 6 | `setup`      | Setup         | Cài đặt        | ⚙        | 🟡 Stub "Coming soon" |

Tab keys + labels are LOCKED — they map 1:1 to CMES sibling. This plan turns
tabs 2-6 into functional content.

---

## 1. Bước 1 — DEEP AUDIT CMES per tab

Below: pattern study only. CMES is READ-ONLY (per CLAUDE.md "không đụng CMES").

### 1.1 `drawings` — CMES `DrawingsTab.tsx`

**Layout**: master grouped by `file_kind` (customer_drawing / npi_layout / ipqc_reference / fqc_checksheet). One **section per kind** containing:
- Header: title + "Upload new revision" button
- Body: current `DrawingCard` (or empty state) + collapsible version timeline

**Per-card content**:
- Thumbnail (PDF preview img if `preview_url`, else placeholder with `v#`)
- Title + version number + metadata row (size KB · created_by · created_at)
- **Status pill**: pending / approved / rejected / superseded (color-coded)
- **3-role approval grid**: NPI · Production · QC chips, each ✓ (approved) / ✕ (rejected) / pending (gray)
- Change reason quote
- Actions: "View PDF" link + "Versions (N)" expander

**Mutations**:
- **Upload new revision** → `UploadDrawingModal` (drag-drop file picker + change_reason textarea + XHR progress bar). Supersedes prior pending/approved; restarts all 3 approvals.
- **Approve / Reject per role** → `DecideDrawingModal` (Approve/Reject buttons + optional comment textarea, required for reject).

**Endpoints used**:
- `GET /api/specs/{specId}/drawings` (listDrawings)
- `POST /api/specs/{specId}/drawings` (multipart upload: file_kind, change_reason, file)
- `POST /api/specs/{specId}/drawings/{drawingId}/decide` (decideDrawing)
- `GET /api/specs/{specId}/drawings/{drawingId}/file` (PDF download)

**RBAC** (CMES):
- Upload: requires `canUpload` prop
- Decide: `canUpload === true` AND `drawing.status === 'pending'` AND user role matches via `userCanActAs(userRole, asRole)`
- Mapping: sys/admin can act as any; npi → NPI; production/planner → Production; qc/quality → QC

**Blob dependency**: YES — `preview_url` + PDF download both blob-backed.

### 1.2 `qc-plans` — CMES `QCPlansTab.tsx`

**Layout**: master grouped by **4 fixed QC stages** (`IPQC-Print`, `IPQC-Cut`, `FQC`, `OQC`). One section per stage:
- Header: stage label + description + "Add criterion" button + "Save changes" button
- Body: inline editable table OR empty state

**Per-stage table** (6 columns):
| Column | Width | Input type | Max chars |
|---|---|---|---|
| Criterion (label) | 220px | text | 200 |
| Target | 140px | text | 200 |
| Tolerance | 110px | text | 120 |
| Method | flex | text | 200 |
| Frequency | 110px | text | 120 |
| Delete | 40px | X icon button | — |

**Mutations**:
- Add criterion → local row append (auto-id `c-{ts}-{rand}`)
- Edit cell → in-place text edit, kept in React draft state
- Delete row → local splice
- **Save changes** → atomic per-stage upsert (POST all criteria for that stage)

**Dirty pattern**: `dirtyStage` tracks which stage has unsaved changes. Save button disabled until dirty; shows "Saving…" / "Saved".

**Endpoints used**:
- `GET /api/specs/{specId}/qc-plans` (listQCPlans)
- `POST/PUT /api/specs/{specId}/qc-plans` (upsertQCPlan — atomic per stage)

**RBAC**: `canEdit === true` for any mutation. View-only otherwise.

**Blob dependency**: NO. **Modal-free.** **No status badges.**

### 1.3 `qc-capture` — CMES `QCCaptureTab.tsx`

**Layout**: same 4 stages as QC Plans. Per stage:
- Header: stage name + criterion count + capture count
- Empty state if no criteria
- Data table if criteria exist

**Per-stage table** (5 columns):
- **Criterion**: label + method (small text below)
- **Target**: value + "± tolerance" (small)
- **Latest result**: pill `PASS` (green) / `FAIL` (red) / `N/A` (gray) + measurement (small) + NG reason code (red, if fail)
- **Captured by**: username + timestamp
- **Action**: "Capture" button (conditional by role)

**Mutations**:
- **Capture** → `CaptureQCModal` (3-button result PASS/FAIL/N/A + measurement input 200 chars + NG reason code dropdown — conditional on FAIL + comment textarea 500 chars)
- NG reason code dropdown loads from `GET /api/reason-codes` (silent fallback to text input if API fails)

**Endpoints used**:
- `GET /api/specs/{specId}/qc-plans` (read criteria)
- `GET /api/specs/{specId}/qc-captures` (list captures)
- `POST /api/qc-captures` (createQCCapture)
- `GET /api/reason-codes` (optional)

**RBAC** (CMES): 7 specific roles can capture (sys, admin, qc, quality, production, planner, npi). View-only for others.

**Blob dependency**: NO.

### 1.4 `artwork` — CMES `ArtworkTab.tsx`

**Layout**: control panel + canvas, both client-side only.
- Control panel: 4 toggle buttons (layer visibility)
- Canvas: SVG preview rendered from one of 3 template variants

**4 layer toggles** (with default state):
| Layer | Color | Default |
|---|---|---|
| Print layer | yellow `#FFCD00` | ON |
| Die-cut line | red `#ef4444` | ON |
| Register marks | blue `#00b4d8` | OFF |
| Bleed area | light blue `#60a5fa` | OFF |

**3 template variants**: `brady-safety` / `ccl-cosmetic` / `panel-face` — selected from `payload.artwork.template`.

**Mutations**: NONE. Pure client-side toggle (ephemeral; not persisted).

**Endpoints used**: NONE. All data comes from `payload.artwork`.

**RBAC**: none — all roles can view + toggle.

**Blob dependency**: NO (procedural SVG, not raster).

### 1.5 `setup` — CMES `SetupTab.tsx`

**Layout**: 2 side-by-side cards.
- Card 1: **Press Settings** (Flexo line)
- Card 2: **Tolerances** (Critical)

**Per-card content**: header (label + accent text) + key/value `RowList` of config parameters.

**Mutations**: NONE — pure read-only display.

**Endpoints used**: NONE. All data from `payload.press` + `payload.tolerance`.

**RBAC**: none — all roles can view.

**Blob dependency**: NO.

**Purpose clarification**: NOT a config form. IS static reference documentation for production operators (press settings + tolerance bands defined upstream by NPI).

### 1.6 SpecHub equivalence check

Per the user's instruction (prefer SpecHub when it has matching pattern):

| Tab | SpecHub equivalent? |
|---|---|
| `drawings` | ❌ No drawings upload/approval module in SpecHub prototype. Source = CMES. |
| `qc-plans` | ❌ No QC criteria editor in SpecHub. Source = CMES. |
| `qc-capture` | ❌ No QC capture modal in SpecHub. Source = CMES. |
| `artwork` | ❌ No artwork SVG viewer in SpecHub. Source = CMES. |
| `setup` | ❌ No press-setup card in SpecHub. Source = CMES. |

→ All 5 tabs use CMES as the structural reference. CMES tree remains READ-ONLY.

---

## 2. Bước 2 — Gap analysis: CMES tab ↔ CCL-MES entity

### 2.1 Quick legend

- ✅ **ready** — entity + table + endpoints exist; just needs UI
- 🟡 **partial** — entity + table exist but no endpoints (wire CRUD)
- ❌ **missing** — needs new entity + migration
- 📦 **blob-dependent** — needs `IBlobStore` real impl before functional
- 🔌 **client-only** — no backend at all (CMES routes from `payload`)

### 2.2 Per-tab mapping

#### `drawings`
| CMES needs | CCL-MES entity | Status | Notes |
|---|---|---|---|
| Drawing master per kind | `Drawing` (`Entities/Drawing.cs:11`) | 🟡 partial | Has `Kind` enum (customer_drawing / npi_layout / ipqc_reference / fqc_checksheet) — matches CMES exactly. No endpoints. |
| Version history | `DrawingVersion` (`Drawing.cs:34`) | 📦 blob-dependent | Has `StorageKey` + `FileHash` (sha256) — schema mirrors CMES blob model. **Cannot ship without `IBlobStore` real impl.** |
| 3-role approval chain | `DrawingApproval` (`Drawing.cs:67`) | 🟡 partial | Has `Role` enum (npi/production/qc) + status (pending/approved/rejected). Matches CMES. No decide endpoint. |
| Blob storage | `IBlobStore` interface (`Application/Storage/IBlobStore.cs`) | ❌ stub-only | Interface defined; NO implementation, NOT registered in DI. Will fail at runtime. Originally deferred for PR #31 (FilesystemBlobStore impl). |

**Verdict**: 🛑 **DEFER tab** until `IBlobStore` real impl ships in a dedicated infra PR. Otherwise we ship broken UI.

#### `qc-plans`
| CMES needs | CCL-MES entity | Status | Notes |
|---|---|---|---|
| 4-stage scoped criteria | `SpecQcWindow` (`Entities/Spec.cs:299`) | ❌ missing | Table EXISTS (PR #28). `Stage` enum matches CMES: `IpqcPrint / IpqcCut / Fqc / Oqc`. No service. No endpoints. |
| Per-criterion config | `QcCriterion` (`Spec.cs:321`) | ❌ missing | Table EXISTS. Fields: `CriterionType` (Visual/Dimensional), tolerance, target, reference image key. **Need to add Method + Frequency text columns** (CMES has these; CCL-MES schema missing). |
| Per-stage atomic upsert | (no endpoint) | ❌ missing | Need new `SpecQcWindowService.UpsertStageAsync(revisionId, stage, criteria[])`. |

**Verdict**: ✅ **High-priority candidate.** Tables ready; needs (a) light migration to add `Method` + `Frequency` columns, (b) service + endpoints, (c) UI. No blob, no modal — relatively simple.

#### `qc-capture`
| CMES needs | CCL-MES entity | Status | Notes |
|---|---|---|---|
| Captures linked to criterion | (no CCL-MES entity yet) | ❌ missing | **Need new `SpecQcCapture` entity.** Phase 6's `QcInspection` is per-WorkOrder runtime; CMES `qc-capture` is per-Spec template-following (different semantic). |
| Reason code lookup | (no CCL-MES entity) | ❌ missing | **Need new `ReasonCode` lookup table** (seed common codes like QC-NG-001 etc.) OR allow free-text. |
| QC plan read (for criteria list) | `SpecQcWindow + QcCriterion` | depends on `qc-plans` PR | This tab READS what `qc-plans` writes — must ship qc-plans first. |

**Verdict**: 🟡 **Mid-priority.** Depends on qc-plans landing first. Needs new entity + migration. No blob.

#### `artwork`
| CMES needs | CCL-MES entity | Status | Notes |
|---|---|---|---|
| Layer toggle state | (none — ephemeral) | 🔌 client-only | Pure client-side; CMES doesn't persist. |
| Template selection | `payload.artwork.template` | ❌ missing | CMES reads `'brady-safety' / 'ccl-cosmetic' / 'panel-face'` from `payload.artwork`. CCL-MES has no `artwork` field in any entity. |
| SVG rendering | (procedural) | 🔌 client-only | 3 hand-coded SVG templates in CMES — port verbatim. |

**Verdict**: ✅ **Low-effort low-fidelity.** Can ship as **client-only stub** that auto-detects template from `Planner` (SILK → panel-face fallback; FLEXO → brady-safety fallback; UNKNOWN → empty placeholder + "Awaiting real artwork sample" note). NO migration. NO real artwork until samples land.

#### `setup`
| CMES needs | CCL-MES entity | Status | Notes |
|---|---|---|---|
| Press settings (Flexo line) | Subset of `SpecPrint` + `SpecMaterial` | ✅ ready | All press-relevant fields already in `SpecPrint` (`ProcessCode`, `Cavity`, `PitchMm`, `NumColors`, `ProductSizeWmm/Hmm`) + `SpecMaterial` (`SubstrateType`, `SubstrateBrand`, `Thickness`, `AdhesiveType`). |
| Tolerance bands (critical) | (none) | ❌ missing | CMES reads from `payload.tolerance` array. CCL-MES has no tolerance schema (relates to QC criterion target/tolerance instead). |

**Verdict**: ✅ **Low-effort.** Cards 1 (Press Settings) ships immediately from existing `SpecDetailDto`. Card 2 (Tolerances) renders empty placeholder with "Defined in QC Plans tab → see Tolerance column" pointer.

### 2.3 Coupling + FK risks (do NOT break)

- `Drawing.ProductRevisionId` FK → `ProductRevisions` ✓ already set
- `DrawingVersion.DrawingId` FK + `Drawing.CurrentVersionId` self-back-FK — must maintain pointer integrity on upload (transactional)
- `DrawingApproval.DrawingVersionId` FK — restart on new version upload (CMES pattern: supersede prior pending/approved)
- `SpecQcWindow.ProductRevisionId` FK ✓ already set
- `QcCriterion.SpecQcWindowId` FK ✓ already set
- Phase 6 `QcInspection.WorkOrderId` — **DO NOT confuse with new `SpecQcCapture`**. Different semantics (per-WO runtime vs per-Spec template). Phase 6 WO state machine + SignalR untouchable per vùng cấm.
- `IqcInspection` — independent surface, not relevant here.

---

## 3. Bước 3 — Priority + PR split

### 3.1 Effort + dependency matrix

| Tab | Effort | Migration? | Blob? | Sample needed? | Depends on |
|---|---|---|---|---|---|
| **Setup** | **S** | No | No | No | (none — uses `SpecDetailDto`) |
| **Artwork** | **S** | No | No | Optional (3 SVG templates port verbatim) | (none) |
| **QC Plans** | **M** | YES (light: add Method + Frequency to QcCriterion) | No | No | (none) |
| **QC Capture** | **L** | YES (new SpecQcCapture + ReasonCode tables) | No | No | QC Plans (must ship first) |
| **Drawings** | **XL** | No (tables exist) | YES (need real `IBlobStore` impl) | Need sample PDFs | **`IBlobStore` infra PR** |

### 3.2 Recommended ordering — 5 PRs

> Pattern from prior phase: 1 tab ≈ 1 PR. Each PR ships its own i18n + audit
> emit + RBAC. STOP between each PR for hardware verify.

#### **PR-D-1 — Setup tab (S)**
- Read `SpecDetailDto` (no new query).
- Render 2 cards: Press Settings + Tolerances placeholder.
- ~150 LOC Razor + ~25 LOC CSS + i18n keys.
- No migration. No mutation. RBAC: route-level `NpiSpecRead` (already in place).
- Verify: open silk + flexo spec → confirm Press Settings shows correct values for both planners.

#### **PR-D-2 — Artwork tab (S)**
- Port 3 SVG templates verbatim from CMES (READ-ONLY copy of markup structure).
- 4 layer toggles (client-side state).
- Auto-select template from `Detail.Planner` (silk → panel-face; flexo → brady-safety; else → empty with hint).
- ~250 LOC Razor + ~50 LOC CSS + 8 i18n keys.
- No migration. No mutation. RBAC: open to all `NpiSpecRead`.
- Verify: open spec → all 4 toggles work; template renders.

#### **PR-D-3 — QC Plans tab (M)**
- **Migration**: add `Method` (string 200) + `Frequency` (string 120) columns to `QcCriterion`. Provider-agnostic. A→B→C SAFE (backup + SHA256 + /tmp test before LIVE).
- New `SpecQcWindowService` with `ListByRevisionAsync` + `UpsertStageAsync` (atomic per-stage).
- 4-stage editable table UI (inline edit, no modal). Per-stage dirty flag + Save button.
- Audit emit `SPEC_QC_PLAN_UPSERT` per stage save.
- ~400 LOC Razor + ~80 LOC CSS + service + endpoint + ~25 i18n keys.
- RBAC: View = `NpiSpecRead`. Edit = `Admin,Engineer` via `<AuthorizeView>` + server-side role check inside `UpsertStageAsync`.
- Verify: open spec → add 3 criteria to IPQC-Print → save → reload → criteria persist.

#### **PR-D-4 — QC Capture tab (L)**
- **Migration**: new `SpecQcCapture` entity (`Id`, `SpecQcWindowId` FK, `QcCriterionId` FK, `Result` enum Pass/Fail/Na, `Measurement` text, `ReasonCode` text/FK, `Comment`, `CapturedBy`, `CapturedAt`). Plus optional `ReasonCode` lookup seed.
- New `SpecQcCaptureService` + endpoints.
- 4-stage table UI mirror QC Plans + `CaptureQCModal`.
- Reason code dropdown (graceful fallback to text input if seed empty).
- Audit emit `SPEC_QC_CAPTURE` per capture.
- ~500 LOC Razor + ~100 LOC CSS + new entity + service + endpoints + ~30 i18n keys.
- RBAC: View = `NpiSpecRead`. Capture = Admin/Engineer (CCL-MES roles narrower than CMES 7-role list; consistent with rest of app).
- Verify: ship QC Plan first → add criteria → switch to QC Capture → record PASS/FAIL/N/A → confirm latest result shows in table.

#### **PR-D-5 — Drawings tab (XL) — DEFER until blob infra PR**
- **Pre-requisite PR**: `IBlobStore` real impl. Recommended: `FilesystemBlobStore` writing to `<DATA_DIR>/blobs/drawings/<revisionId>/<drawingId>/v<n>_<sha8>.<ext>` (Ops Control v1.2 layout per existing convention). DI register in `AddInfrastructure()`.
- Then port `DrawingsTab` UI: 4 sections per kind, card with thumbnail + status pill + 3-role approval chips + upload modal + decide modal.
- New endpoints: `POST /api/specs/{id}/drawings` (multipart upload), `POST /api/specs/{id}/drawings/{drawingId}/decide`, `GET /api/specs/{id}/drawings/{drawingId}/file`.
- Audit emit per upload + per decide.
- Estimated ~1200 LOC total (UI + 2 modals + blob impl + endpoints + service).
- RBAC: Upload + Decide gated by role matching (sys/admin = any; engineer = npi; production = production; qc = qc).
- Defer until: (1) `IBlobStore` impl shipped, (2) real CCL Vietnam sample PDFs available, (3) approval-chain RBAC matrix confirmed.

### 3.3 Sprint shape

**Sprint 1** (low-effort tabs first, builds momentum + zero-risk):
- PR-D-1 Setup + PR-D-2 Artwork → ship in same sprint as 2 PRs.

**Sprint 2** (QC pair):
- PR-D-3 QC Plans → STOP hardware verify
- PR-D-4 QC Capture → STOP hardware verify

**Sprint 3** (separate infra + Drawings) — schedule after Sprint 2:
- PR-D-5a `IBlobStore` FilesystemBlobStore impl + DI wiring
- PR-D-5b Drawings UI on top of blob infra
- Or roll into a single bigger PR if blob impl is small

---

## 4. Bước 4 — Q1..Q12 (defaults bold)

> Anh chỉ cần đánh dấu các Q anh muốn đổi; còn lại lấy default.

**Q1 — Order of PRs.** Default: **PR-D-1 Setup → PR-D-2 Artwork → PR-D-3 QC Plans → PR-D-4 QC Capture → PR-D-5 Drawings** (lowest-effort first, defer Drawings until blob infra).

**Q2 — Setup tab data source.** Default: **read from existing `SpecDetailDto.Material + Print` (no new query, no new entity)**. Card 2 (Tolerances) renders empty placeholder pointing to QC Plans tab for tolerance bands.

**Q3 — Artwork tab template selection.** Default: **auto-select from `Detail.Planner`** — SILK → `panel-face` template; FLEXO → `brady-safety` template; INDIGO/LETTER/DIECUT/UNKNOWN → empty placeholder with chip "Awaiting real artwork sample". No persisted state.

**Q4 — QC Plans 4 stages match CCL-MES enum?** Default: **YES** (`IpqcPrint / IpqcCut / Fqc / Oqc` in `SpecQcWindow.Stage` matches CMES 1:1).

**Q5 — QC Plans save model: atomic per-stage vs per-row CRUD?** Default: **atomic per-stage upsert** (mirror CMES — simpler UX, single dirty flag per stage, single API call).

**Q6 — QC Plans schema delta.** Default: **add 2 columns to `QcCriterion`**: `Method` (string 200) + `Frequency` (string 120). Migration is light + additive + provider-agnostic. A→B→C SAFE.

**Q7 — QC Capture entity.** Default: **new `SpecQcCapture` entity** (separate from Phase 6's `QcInspection` — different semantic: per-Spec template vs per-WO runtime). Phase 6 surface stays vùng cấm.

**Q8 — NG reason code source.** Default: **new lightweight `ReasonCode` lookup table** with seed data (QC-NG-001..QC-NG-010 common defects). Graceful fallback to text input if seed empty. Future PR can wire reason code admin UI.

**Q9 — Drawings tab defer?** Default: **YES, defer until `IBlobStore` real impl ships**. Stub stays in PR #42 chrome with "Coming soon" card until blob infra PR lands. No half-wired upload UI.

**Q10 — Blob storage impl choice.** Default: **`FilesystemBlobStore` writing to `<DATA_DIR>/blobs/drawings/<revisionId>/<drawingId>/v<n>_<sha8>.<ext>`** (Ops Control v1.2 layout). Later upgrade path to MinioBlobStore for cloud + SqliteBlobStore for portable single-file deploy.

**Q11 — RBAC enforcement per tab.**
- Default view-tier: route-level `NpiSpecRead` (Admin, Supervisor, Engineer) — already in place via `@attribute [Authorize(Policy = "NpiSpecRead")]`.
- Default mutation-tier:
  - **Setup / Artwork**: no mutation; view only.
  - **QC Plans Add/Edit/Save**: `<AuthorizeView Roles="Admin,Engineer">` client + role check inside `UpsertStageAsync` server.
  - **QC Capture**: `<AuthorizeView Roles="Admin,Engineer">` client + role check inside `CreateCaptureAsync` server. (CCL-MES role-set narrower than CMES 7-role list; consistent with existing chrome Promote button.)
  - **Drawings Upload + Decide** (when shipped): role-matching per `decideDrawing` pattern.

**Q12 — Audit emit per mutation.** Default: **YES, every mutation emits `AuditAction.<Kind>`**:
- `SPEC_QC_PLAN_UPSERT` (per stage save, detail = `{ stage, criteria_count }`)
- `SPEC_QC_CAPTURE` (per capture, detail = `{ stage, criterion_id, result, has_reason_code }`)
- (Drawings PR adds `DRAWING_UPLOAD` + `DRAWING_DECIDE` later.)

---

## 5. Ràng buộc cứng — checklist trước khi code mỗi PR

- ☐ CMES sibling tree READ-ONLY (chỉ học pattern; KHÔNG ghi).
- ☐ SpecHub READ-ONLY (không có equivalent cho 5 tab này — CMES là nguồn).
- ☐ Migration (PR-D-3 + PR-D-4) → A→B→C SAFE: isolated `/tmp` test + backup + SHA256 verify + provider-agnostic (SQLite + SqlServer guards) + KHÔNG raw SQL trừ khi guard.
- ☐ Reuse `<SpecShowcard>` + chrome shell + tab framework có sẵn (KHÔNG tạo render path mới).
- ☐ Navy theme tokens nguyên: `--navy-primary`, `--navy-dark`, `--navy-border`, `--navy-light`, `--navy-accent`, `--alt-row-bg`.
- ☐ Bảo toàn baseline + IQC 3 + FK `ProductRevision ↔ WorkOrder` không phá.
- ☐ Render từ entity + try-catch (bài học hotfix #27): mỗi handler async wrap try-catch, error vào `_actionError`, KHÔNG freeze Blazor circuit.
- ☐ i18n EN/VI mỗi PR ship cùng commit (không drift label).
- ☐ RBAC: route `NpiSpecRead` cho view; `<AuthorizeView Roles="Admin,Engineer">` + server role check cho mutation.
- ☐ KHÔNG đụng: Ops Control v1.2 / Old ver / Machine / ProductionLog / 4 NPI tab khác / IQC Phase 6 / Phase 6 WO state machine + SignalR / Shop Order.

---

## 6. STOP — chờ duyệt

Anh review:
1. **5 tab + tên** (PR #42 stub, locked) — OK chưa?
2. **CMES audit per tab** (§1) — đủ chi tiết để port chưa?
3. **Gap entity per tab** (§2.2) — map đúng entity hiện có chưa?
4. **Ưu tiên + chia 5 PR** (§3.2) — order Setup → Artwork → QC Plans → QC Capture → Drawings, defer Drawings cho blob infra — OK chưa?
5. **Q1..Q12 defaults** (§4) — đánh dấu Q nào muốn đổi.

Sau khi duyệt, anh ra lệnh "code PR-D-1 Setup" → em tạo branch `feat/phase8-spec-tab-setup` + ship.
