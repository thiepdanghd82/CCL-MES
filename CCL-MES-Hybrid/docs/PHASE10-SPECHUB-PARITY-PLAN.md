# Phase 10 — Full SpecHub Parity in MAUI Hybrid Client — PLAN

> **Status: DRAFT — awaiting Henry approval.** This plan SUPERSEDES the
> narrower P10.5 NPI+Spec port plan (`PHASE10-P10.5-NPI-SPEC-PORT-PLAN.md`)
> by widening scope to every SpecHub tab + function. P10.5a already
> shipped (PR #80 merged 2026-06-03) and remains the foundation; the
> phase plan in §6 re-numbers later PRs and ADDS new phases for SpecHub
> features the original P10.5 plan didn't cover (MES 5-phase scan,
> Machine Dashboard, Shop Order History, QMS Inspection Queue, Speed
> Performance card, Audit Log Viewer admin tab, Settings sub-tabs).
>
> **Goal**: MAUI client (CCL-MES-Hybrid) has every tab + function
> SpecHub offers. **Sàn tối thiểu = parity SpecHub**. Improvements (làm
> tốt hơn SpecHub) are allowed but **must be approved per item** —
> không gold-plate.
>
> **Sources audited** (all READ-ONLY — KHÔNG sửa SpecHub):
> - SpecHub: `/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/SpecHub/`
>   — `spechub-prototype.html` (20,678 LOC), `apps/` (NestJS skeleton +
>   16 TS files), `db/migrations/002_mes_scan_product.sql` (~210 LOC),
>   `i18n/{vi,en}.json` (89 keys), `screenshots/`, `spechub.md`,
>   `CHANGELOG.md`, `LESSONS_LEARNED.md` (581 LOC, 14 sections).
> - Web CCL-MES legacy: `/src/CCL.MES.Web/` (~6,600 LOC Razor,
>   1,045 i18n keys).
> - MAUI client current: `/CCL-MES-Hybrid/src/` (post-P10.5a).
>
> **Constraints honored** (carry every P10.2/P10.3/W4 lesson + P10.5a
> ship constraints):
> - All code under `CCL-MES-Hybrid/`. Legacy `src/CCL.MES.*` READ-ONLY
>   (project-reference only — never edit).
> - SpecHub READ-ONLY — chỉ học pattern. Sibling projects (CMES /
>   Ops Control v1.2 / Old ver) READ-ONLY.
> - Online-only per Q4 lock; offline deferred to P10.4.
> - Reuse `ICclApiClient` + JWT/RBAC + `<Modal>` + grid pattern +
>   `SpecShowcardVm` + scanner + `MacCatalystKeyboardFix` +
>   Connectivity banner + PBKDF2 device passcode.
> - Verify thật trên Mac Catalyst per PR. Windows defer to P10.6+.
> - Test framework: xUnit pure-C# viewmodel + endpoint integration.
>   bUnit defer.
> - i18n inline VN với `// i18n` marker — resx infrastructure defer
>   to P10.6+.
> - `#if DEBUG` cleanup gate **bắt buộc** per PR (grep audit clean).
> - Q11 client-side `_submitting` guard MANDATORY mỗi mutation form
>   (idempotency outbox key defer P10.4).

---

## 0. P10.2 / P10.3 / W4 / P10.5a lessons carried into mọi phase

Below already woven into the per-phase notes in §6 — listed here as the
single source of truth so reviewer can verify each carry-forward lands.

| Lesson | Source | Applied where in this plan |
| --- | --- | --- |
| Catalyst Tab/Enter keyboard fix (`#13934`) | P10.2 | Mọi page mới mount under `MainLayout` để inherit `MacCatalystKeyboardFix` |
| Keychain `MissingEntitlement` adhoc fallback | P10.2 | `MauiSecureTokenStore` + future `CatalystFilePicker` permission probe |
| IPv4 BaseUrl + ATS exception | P10.2 | `appsettings.json` unchanged; new upload routes inherit |
| `#if DEBUG` cleanup gate | P10.3 W2 | Every PR's cleanup section + grep audit |
| No silent fail on permission denied | P10.3 W2 + W4 | Camera + file picker + Save dialog phải show operator banner |
| `WO_ADVANCE_DEVICE` capture BEFORE service call | P10.3 W4 | Mọi controller wrapping legacy service phải dùng pattern |
| Hosted-service self-start trong App.xaml.cs | P10.3 W4 | Boot path đã có — reuse cho any new background service |
| `PostConfigure<IServiceProvider>` cho ApiClientOptions DeviceId | P10.3 W4 | Reuse y nguyên |
| Bilingual VN-first | P10.2 + P10.5a | Inline VN labels + i18n marker comments |
| Test-first cho regression-fix path | P10.3 W4 | xUnit cover any gotcha trước PR merge |
| Online-only Q4 lock | P10.3 plan | Mutation fail fast + retry; no outbox |
| `_submitting` guard mọi mutation form | Q11 P10.5 + W4 | MANDATORY checklist item per mutation PR |
| PBKDF2 device passcode | P10.3 W4 | Reused as-is — no replacement |
| Lesson #27 try/catch + inline error banner | P10.5a | Pattern locked — every section section tab catches per-API call |
| Modal primitive clean-room (Q13) | P10.5a | Build modal as composition root — never copy Ops Control |
| GridColumnsMenu + GridPagination + IGridPreferenceStore | P10.5a | Every grid + matrix surface reuses these helpers |
| Render từ entity/DTO + per-section try-catch | P10.5a | 6-tab spec detail + per-tab QMS + per-card dashboard all isolate failure |

---

## 1. AUDIT TOÀN DIỆN SpecHub — mọi tab + chức năng

SpecHub prototype tổng cộng **10 top-level surfaces** + **6 sub-tab Engineer
Spec** + **6 sub-tab Settings**. Bảng dưới liệt kê đầy đủ.

### 1.1 Top-level navigation

| # | Section | Surface | Route trigger | Roles | Status SpecHub |
| --- | --- | --- | --- | --- | --- |
| 1 | Workspace | **Home** | `openHomeView()` | All | v0.4+ prod-ready |
| 2 | Product Operation | **Shop Order** (MES 5-phase) | `openProductOpView()` | op, prod, npi, admin | v0.4+ prod-ready |
| 3 | Product Operation | **Machine Dashboard** | `openMachineDashboard()` | All | v0.4+ prod-ready |
| 4 | Product Operation | **Shop Order History** | `openShopOrderHistory()` | All | v0.5.0 new |
| 5 | QMS | **Inspection Queue** (IPQC/FQC/OQC) | `openQualityInspection()` | qc, npi, admin | v0.5.0 new |
| 6 | QMS | **QC History** | `openQualityHistory()` | qc, npi, admin | v0.5.0 new |
| 7 | Database | **Routine** | `openDatabaseTab('routine')` | All | v0.3+ |
| 8 | Database | **Product Structure** | `openDatabaseTab('product-structure')` | All | v0.3+ |
| 9 | Database | **NPI Spec** (6 sub-tabs) | `openDatabaseTab('1c-spec')` | All view; mutations gated | v0.4+ |
| 10 | Database | **Machine List** | `openDatabaseTab('machine-list')` | All | v0.3+ |
| 11 | System | **Settings** (6 sub-tabs) | `openDatabaseTab('settings')` | All view; admin sub-tabs gated | v0.4+ |
| (sidebar widget) | Workspace | **Recent Scans** | dynamic | All | v0.4+ |

### 1.2 Per-surface chức năng

#### Surface 1 — **Home**
| Function | Detail |
| --- | --- |
| Welcome + greeting | Vietnamese-only seed; user-name binding |
| Recent specs (5) | Sort by last accessed; click → open Spec Detail |
| Quick action tiles | Create 1C spec; Import CSV; Load samples; Account Control (admin) |
| KPI summary | Counts: total specs, WO active, machines up; live tick |
| Clock | 1Hz tick, shows current shift |

#### Surface 2 — **Shop Order (MES 5-phase)**
| Phase | Function |
| --- | --- |
| PREPRESS | 3 check sections (Materials N rows / Plate 1 row / Cutter 1 row), each row OK/NG + reason picker + photo, gate to SETTING |
| SETTING | Timer-driven; Start/Done buttons; capture `duration_sec` |
| IPQC | 5 criteria (ΔE / Registration / Content / Barcode / Other), judgment Accept lô / Stop lô / Special Accept → QA approval modal |
| READY-TO-RUN | Confirm gate before Run; summary card |
| RUNNING | Qty counter (good + reject), live Design / Actual / Efficiency % bar (v0.5.2), 1Hz tick |
| PAUSED | Pause modal: pick `downtime_reason_code` + free text + timestamp |
| FQC | 5 criteria, Accept/Reject |
| OQC | 4 criteria (Dimension / Weight / Packaging / Label), Accept/Reject |
| DONE summary | OEE % + yield % + reject Pareto |
| Audit | Append-only log: SCAN_WO / SETTING_START / IPQC_OK / RUN_START / PAUSE / FQC_OK / etc. (25+ event types) |
| State machine | In-memory `MES_WO_STATE` store + role gates + audit emit |
| NG modal | Picker (context-aware codes) + free text + photo upload |
| Auth modals | IPQC / FQC / OQC signoff (signature + user stamp) |
| Right sidebar | WO No. / Qty / Due Date / Batch + live QC checklist |
| Speed card (v0.5.2) | Design speed lookup (`_mesGetDesignSpeed`) + actual calc + efficiency %, 0–120% bar |

#### Surface 3 — **Machine Dashboard**
| Function | Detail |
| --- | --- |
| Grid view | 10-machine cards × status pill (Running/Idle/Alarm) + KPI badges |
| Status coloring | Green/Yellow/Red tone via `_mdPickStatus()` |
| Downtime Pareto | Aggregated pause reason tally (top 5 per area) |
| Alerts | Threshold-based: Low Material / Tooling Due / Overdue Maintenance |
| Activity log | Recent 20 events (scan/start/pause/resume/stop) |
| Detail drawer | Per-machine: full state + live metrics + historical trend |
| Filtering | By area / machine code / status / search |
| Auto-refresh | 10s tick, gated by `OPS_DISABLE_AUTOREFRESH` kill-switch |
| Collapsible areas | Folded-areas state Set, persisted |
| Seed alerts | 4 categories pre-seeded for demo |

#### Surface 4 — **Shop Order History** (v0.5.0)
| Function | Detail |
| --- | --- |
| KPI tiles | 5: Total WOs / Output pcs / Yield % / OEE % / Reject % |
| Filter bar | Search + period chips (All/Today/7d/30d) + Status/Customer/Machine dropdowns |
| Main table (10 col) | WO code / Product / Machine / Qty plan/done / Yield / OEE / Reject % / Duration / Start / End |
| Progress bar | Animated qty_done / qty_plan per row |
| Side stats | Top 5 Customers / Top 5 Machines / Downtime Pareto (top 10) |
| Detail drawer (19 col) | Final Summary / Performance / Personnel / Downtime / Audit Trail |
| Export CSV | 19-col, filter-state-aware |
| Demo data | 22 synthetic records on first load |

#### Surface 5 — **Inspection Queue (QMS)**
| Function | Detail |
| --- | --- |
| Queue build | Auto from `MES_WO_STATE` — RUNNING/PAUSED → IPQC due; DONE → FQC + OQC due |
| IPQC stage | 5 criteria; Accept lô / Reject lô / Special Accept gates; sign-out |
| FQC stage | 5 criteria; Accept/Reject; sign-out |
| OQC stage | 4 criteria; Accept/Reject; sign-out |
| Signout | Role-gated lock (qc/npi/admin) |
| Audit | Per criterion + final judgment |

#### Surface 6 — **QC History**
| Function | Detail |
| --- | --- |
| Forensic table | All historical QC inspections sortable |
| Filter | Stage / customer / date range / result |
| Drill-down | Open original IPQC/FQC/OQC record |
| Export | CSV (deferred per CHANGELOG) |

#### Surface 7 — **Routine (Process catalog)**
| Function | Detail |
| --- | --- |
| Process catalog table | Code / Name / Speed target pcs/h / Setup time / Material category |
| Inline edit | Seed values (Flexo 8000 / Silkscreen 4000 / Indigo 3000 pph) |
| Sync to MES | Powers `_mesGetDesignSpeed()` in Speed card |
| CRUD | Create / Edit / Delete via inline rows |
| Search / filter | By code or name |

#### Surface 8 — **Product Structure (BOM)**
| Function | Detail |
| --- | --- |
| Tabular BOM | Material code / Spec / Lot spec / Qty / Unit |
| Edit | Add row / delete row / inline cell edit |
| Sync to MES | Prepress materials check source-of-truth |
| Search | By parent / component |

#### Surface 9 — **NPI Spec Library** (6 sub-tabs)
| Sub-tab | Function |
| --- | --- |
| **Specification** | Showcard render: identity / sub-spec blocks per template (silk / flexo / generic fallback); editable rows in Draft (contenteditable); add/delete row; row CRUD |
| **Drawings** | 9 `DrawingKind` slots; per-slot version control; per-version Approval chain (3-role chip); auto-supersede on change; PDF preview + image preview |
| **QC Plans** | Per stage criterion editor (6-col table); add criterion; delete criterion; per-stage atomic upsert |
| **QC Capture** | Per-criterion result Pass/Fail/NA + measurement value + ng_reason picker (conditional) + comment + optional photo |
| **Artwork** | SVG canvas + 4-layer toggle (Print / Die / Register / Bleed); 3 template variants (brady-safety / ccl-cosmetic / panel-face) |
| **Setup** | Press settings (Flexo-specific params); tolerances (critical ranges) |
| Lifecycle (sidebar) | Create / Import CSV / Load Samples / Duplicate / Supersede / Delete |
| Export | CSV / XLSX / PDF list export + single-spec PDF sheet |
| Diff toggle | Before/after view on Revise |
| Audit timeline | Append-only `_oneCAppendLog` with user stamp |
| Status pills | Draft (gray) / Approved (green) / Superseded (gray strikethrough); 3-state (vs CCL.MES.Web 5-state — see §3 delta) |
| 14-col list view | Planner / REF NO / Customer / Part No / Part Name / Colors / Cavity / Pitch / Spec / Status / Rev / Rev Date / By |
| Planner chip filter | Click planner color chip → filter rows |
| Search | 3-field (customer / part / name) |
| Sort | REF NO primary + Rev Date desc secondary |

#### Surface 10 — **Machine List**
| Function | Detail |
| --- | --- |
| 150-machine table | Code / area / process type / status / last active |
| Search / filter | By area / code / process type |
| Detail drawer | Per-machine KPIs: uptime % / avg cycle time / recent 10 downtime |
| CRUD | Edit row inline; deletion gated |

#### Surface 11 — **Settings** (6 sub-tabs)
| Sub-tab | Function |
| --- | --- |
| **My Profile** | Display name / email / department / role (read-only — operator self-service) |
| **My Password** | Current + new × 2 password fields; policy check |
| **Appearance** | Theme selector (light / dark / auto) + Language picker (vi / en); persist |
| **Account Control** (admin) | User CRUD: create / search / edit role / disable / delete |
| **Connection Mode** | Info panel: embedded mode @ localhost:8765 / localStorage backend / migration guide to NestJS |
| **Audit Log** (admin, v0.5.0) | Forensic search by action / WO / daterange; 5 KPI stats; color-coded action pills; CSV export (6-col); max 500 events |
| **About** | Build version / SheetJS version / spec library size / user count / storage used |

#### Recent Scans (sidebar widget)
| Function | Detail |
| --- | --- |
| Recent 5 scans list | Click → re-open scanned WO or spec |
| LocalStorage persistence | `RECENT_SCANS` key |

### 1.3 SpecHub architecture stack snapshot

| Layer | Tech |
| --- | --- |
| Frontend | Vanilla HTML5 + CSS3 + ES6, no framework, ~20,678 LOC prototype + SheetJS 0.18.5 (CDN), IBM Plex fonts |
| Client state | localStorage (`MES_WO_STATE` / `ROUTINE_STATE` / `SPEC_LIBRARY` / `SETTINGS`) + JS globals |
| Auth | Demo login + role-gated UI (5 prefilled accounts) |
| Real-time | 1Hz polling + localStorage poll; auto-refresh kill-switch |
| Backend (planned) | NestJS skeleton (16 TS files, 13 endpoints) |
| Database | PostgreSQL via docker-compose; 7 MES tables + state-machine func + 37 seeds |
| File storage | localStorage (prototype) → MinIO (planned) |
| CI | GitHub Actions + Playwright (46 specs, v0.5.1) |
| Deployment | Embedded HTTP @ localhost:8765 (prototype phase) |

### 1.4 Lessons from `LESSONS_LEARNED.md` (14 sections — locked design rules)

| # | Pattern | MAUI port action |
| --- | --- | --- |
| 1 | Full-iframe layout fluid max-width | Apply container queries trong Razor pages |
| 2 | Workspace grid `:has()` for collapsible rightpanel | Reuse CSS pattern (Safari/Chromium supports `:has()`) |
| 3 | Container queries > media queries | Default approach cho responsive |
| 4 | Fluid typography `clamp()` | Apply to KPI numbers + dashboard tiles |
| 5 | Right-column showcards flex-fill | Spec detail + dashboard layout |
| 6 | Table-row consistency check sections | Materials/Plate/Cutter same pattern |
| 7 | Unicode emoji rendering trap | Avoid emoji JS literals — use SVG circles |
| 8 | View-switching hide ALL siblings | `MainLayout` already does via Blazor routing |
| 9 | Sticky header + stepper independent scroll | Apply to MES phase shell |
| 10 | Append-only audit pattern (REVOKE UPDATE/DELETE) | Server-side enforce; controller never PATCH |
| 11 | 3-layer state machine defense | DB function + Application service + UI disabled buttons |
| 12 | localStorage per-entity shape | MAUI uses Preferences (MauiGridPreferenceStore pattern proven) |
| 13 | Role gate at 3 sites (HTML attr + element.style + function) | Blazor uses `AuthorizeView` + role-gated render + server-side policy |
| 14 | Acceptance checklist for new tabs | Reuse as MAUI PR review checklist |

---

## 2. GAP ANALYSIS 3-cột — SpecHub vs Web CCL-MES vs MAUI client

Legend:
- ✅ Has it (production-ready)
- ⚠ Partial / stub / different shape
- ❌ Missing entirely

For each SpecHub function, column **Web** = CCL.MES.Web legacy state;
column **MAUI** = post-P10.5a state + scope sách `PHASE10-P10.5-NPI-SPEC-
PORT-PLAN.md` PRs (5b–5g). Column **GAP** = thực tế thiếu nếu MAUI ship
hết 5a-5g.

### 2.1 Home / Dashboard / Recent

| Function | SpecHub | Web | MAUI now | MAUI planned 5a-5g | GAP |
| --- | --- | --- | --- | --- | --- |
| Welcome greeting | ✅ | ✅ Dashboard | ✅ `/` Home | (no expansion) | ⚠ minimal — needs KPI tiles |
| Recent specs list (5) | ✅ | ⚠ Quote History pattern (not specs) | ❌ | ❌ | **Full gap** |
| Quick action tiles | ✅ | ✅ Dashboard chips | ⚠ NPI tile only | ❌ | ⚠ |
| KPI summary tile (total specs / WO active / machines up) | ✅ | ✅ Dashboard | ❌ | ❌ | **Full gap** |
| 1Hz live clock + shift | ✅ | ⚠ static | ❌ | ❌ | **Full gap** |

### 2.2 Shop Order (MES 5-phase scan)

| Function | SpecHub | Web | MAUI now | Planned 5a-5g | GAP |
| --- | --- | --- | --- | --- | --- |
| Scan WO code → drawer | ✅ | ✅ WorkOrders card | ✅ `/workorders` W4 | (no new) | ✅ parity |
| **PREPRESS** 3 check sections (Materials/Plate/Cutter) | ✅ | ⚠ via WorkOrders flags only | ❌ | ❌ | **Full gap** — needs new page + 3 check sections |
| **SETTING** timer + Start/Done | ✅ | ⚠ via state machine advance | ❌ | ❌ | **Full gap** — needs timer UI + duration capture |
| **IPQC** 5 criteria + judgment (Accept/Stop/Special Accept) | ✅ | ⚠ `/qcqa/ipqc` stub (10 LOC) | ❌ | ❌ | **Full gap** — neither side ships |
| **READY** confirm gate | ✅ | ⚠ via advance button | ❌ | ❌ | **Full gap** |
| **RUNNING** qty counter + good/reject | ✅ | ⚠ ProducedQty update | ❌ | ❌ | **Full gap** — operator UI missing |
| **RUNNING** Speed card (v0.5.2: Design/Actual/Efficiency %) | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| **PAUSED** modal + downtime reason picker | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| **FQC** 5 criteria + signoff | ✅ | ⚠ generic QC create | ❌ | ❌ | **Full gap** |
| **OQC** 4 criteria + signoff | ✅ | ⚠ `/qcqa/oqc` stub (10 LOC) | ❌ | ❌ | **Full gap** |
| **DONE** summary (OEE % / yield % / reject Pareto) | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| Audit log append-only (25+ events) | ✅ | ⚠ generic `audit_log` table | ⚠ DEVICE_SCAN + WO_ADVANCE_DEVICE | ❌ | ⚠ — extend event taxonomy |
| State machine (DB + app + UI guard) | ✅ | ✅ `WorkOrderStateMachine` (8-step but different) | ⚠ Advance only | ❌ | ⚠ — MES 10-state model not mapped |
| NG modal (context picker + free text + photo) | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| Auth modals (IPQC/FQC/OQC signoff signature) | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| Right sidebar WO context (No / Qty / Due / QC checklist) | ✅ | ⚠ WO drawer pattern | ❌ | ❌ | ⚠ |

### 2.3 Machine Dashboard

| Function | SpecHub | Web | MAUI now | Planned | GAP |
| --- | --- | --- | --- | --- | --- |
| 10-machine grid view (status pill + KPI badges) | ✅ | ⚠ Dashboard OEE table (no grid view) | ❌ | ❌ | **Full gap** |
| Downtime Pareto top 5/area | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| Threshold alerts (low material / tooling due / maint due) | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| Activity log (recent 20) | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| Per-machine detail drawer | ✅ | ⚠ WC info modal (different shape) | ❌ | ❌ | **Full gap** |
| Auto-refresh + kill-switch | ✅ | ⚠ SignalR hub (different) | ❌ | ❌ | **Full gap** |
| Filter by area/code/status + search | ✅ | ❌ | ❌ | ❌ | **Full gap** |

### 2.4 Shop Order History (v0.5.0)

| Function | SpecHub | Web | MAUI now | Planned | GAP |
| --- | --- | --- | --- | --- | --- |
| 5 KPI tiles (Total WO / Output / Yield / OEE / Reject) | ✅ | ✅ Dashboard equiv | ❌ | ❌ | **Full gap** |
| Period chips (All/Today/7d/30d) | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| 10-col forensic table | ✅ | ⚠ partial in `/workorders` | ❌ | ❌ | **Full gap** |
| Progress bar qty_done/plan | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| Side stats panel (Top 5 customers / machines / Pareto) | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| 19-col detail drawer | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| Filter-aware CSV export | ✅ | ⚠ WO list export (different cols) | ❌ | ❌ | ⚠ |

### 2.5 QMS — Inspection Queue + QC History

| Function | SpecHub | Web | MAUI now | Planned | GAP |
| --- | --- | --- | --- | --- | --- |
| Auto-build queue (RUNNING→IPQC, DONE→FQC+OQC) | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| IPQC 5-criterion entry | ✅ | ⚠ stub | ❌ | 5f covers QcSpec | ⚠ — MES context different from spec QC |
| FQC 5-criterion entry | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| OQC 4-criterion entry | ✅ | ⚠ stub | ❌ | 5f QcCapture | ⚠ — MES context different |
| Signout role-gate lock | ✅ | ⚠ generic QC approve | ❌ | ❌ | ⚠ |
| QC History forensic table | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| QC export CSV | ✅ deferred | ❌ | ❌ | ❌ | **Full gap** |

### 2.6 Routine / Product Structure / Machine List (Database)

| Function | SpecHub | Web | MAUI now | Planned | GAP |
| --- | --- | --- | --- | --- | --- |
| **Routine** read | ✅ | ✅ | ✅ 5a `/npi/routine` | (no expand) | ✅ parity |
| **Routine** inline edit | ✅ | ⚠ via CSV bulk only | ❌ | ❌ | **Full gap** for inline; bulk via web ok |
| **Routine** sync to MES (`_mesGetDesignSpeed`) | ✅ | ❌ | ❌ | ❌ | **Full gap** |
| **Structure** read | ✅ | ✅ | ✅ 5a `/npi/structure` | (no expand) | ✅ parity |
| **Structure** inline edit | ✅ | ⚠ via CSV | ❌ | ❌ | **Full gap** for inline |
| **Machine List** read 150 machines | ✅ | ✅ via WorkCenter (different shape) | ✅ 5a `/npi/workcenters` | (no expand) | ⚠ — 150-grid layout vs 43-row pattern |
| **Machine List** detail drawer | ✅ | ⚠ WC info modal | ❌ | ❌ | ⚠ |
| **Raw Materials** read | ❌ SpecHub doesn't surface | ✅ | ✅ 5a `/npi/rawmaterials` | (no expand) | (n/a — web extra) |

### 2.7 NPI Spec Library (6 sub-tabs)

| Function | SpecHub | Web | MAUI now | Planned 5b-5g | GAP |
| --- | --- | --- | --- | --- | --- |
| 14-col list view | ✅ | ✅ EngineerSpec | ❌ | ✅ 5b | (none after 5b) |
| Planner chip filter | ✅ | ⚠ status filter only | ❌ | ⚠ 5b context menu (no chip) | **Gap** — chip filter not in plan |
| Search 3-field | ✅ | ✅ | ❌ | ✅ 5b | (none after 5b) |
| Status pill 3-state (SpecHub) vs 5-state (Web) | ⚠ 3 | ⚠ 5 | ❌ | TBD 5b | ⚠ — need decision |
| Sub-tab **Specification** (showcard) | ✅ | ✅ 1,024 LOC SpecShowcard | ❌ | ✅ 5b compact + 5c full | (none) |
| Editable rows in Draft (contenteditable) | ✅ | ⚠ via SpecEditModal | ❌ | ⚠ 5c via modal | **Gap** — inline contenteditable not planned |
| Add/delete row (Draft) | ✅ | ⚠ via modal | ❌ | ⚠ 5c | **Gap** |
| Sub-tab **Drawings** | ✅ 9 kinds | ✅ | ❌ | ✅ 5e | (none after 5e) |
| 3-role approval chip | ✅ | ✅ | ❌ | ✅ 5e | (none) |
| Auto-supersede on change | ✅ | ✅ | ❌ | ✅ 5e | (none) |
| PDF preview inline | ✅ | ✅ | ❌ | ✅ 5e Catalyst WebView | (none) |
| Sub-tab **QC Plans** | ✅ stub | ✅ | ❌ | ✅ 5f | (none after 5f) |
| Sub-tab **QC Capture** | ✅ stub | ✅ | ❌ | ✅ 5f | (none after 5f) |
| Sub-tab **Artwork** SVG 4-layer | ✅ | ⚠ stub | ❌ | ⚠ 5b read only | **Gap** — 4-layer toggle + 3-template not planned in MAUI |
| Sub-tab **Setup** press settings | ✅ | ⚠ stub | ❌ | ⚠ 5b read | ⚠ |
| Lifecycle Create / Import xlsx | ✅ | ✅ Modal | ❌ | ✅ 5c | (none) |
| Lifecycle Copy / Edit / Revise / Supersede / Trash / Restore | ✅ | ✅ | ❌ | ✅ 5d | (none) |
| Export CSV/XLSX/PDF list | ✅ | ✅ | ❌ | ✅ 5g | (none) |
| Export single-spec PDF sheet | ✅ | ✅ | ❌ | ✅ 5g | (none) |
| Diff toggle (Revise before/after) | ✅ | ⚠ | ❌ | ❌ | **Gap** — diff view not planned |
| Audit timeline append-only | ✅ | ✅ | ❌ | ⚠ via 5b SpecAuditEntry list | ⚠ partial |

### 2.8 Settings (6 sub-tabs)

| Function | SpecHub | Web | MAUI now | Planned | GAP |
| --- | --- | --- | --- | --- | --- |
| **My Profile** read | ✅ | ✅ Settings/Profile | ❌ | ❌ | **Full gap** |
| **My Profile** edit DisplayName | ✅ | ✅ | ❌ | ❌ | **Full gap** |
| **My Password** change | ✅ | ✅ Settings/Password | ❌ | ❌ | **Full gap** |
| **Appearance** language picker | ✅ | ✅ LangFlagPicker | ❌ | ❌ | **Full gap** |
| **Appearance** theme switcher | ✅ | ⚠ stub | ❌ | ❌ | **Full gap** |
| **Account Control** user CRUD (admin) | ✅ | ✅ Settings/Account | ❌ | ❌ | **Full gap** |
| **Connection Mode** info | ✅ | ⚠ via About | ❌ (P10.3 W1 unrelated) | ❌ | ⚠ partial via P10.3 |
| **Audit Log Viewer** (admin, v0.5.0) | ✅ | ✅ Settings/AuditLogs | ❌ | ❌ | **Full gap** |
| Audit Log CSV/XLSX export | ✅ CSV only | ✅ both | ❌ | ❌ | **Full gap** |
| **About** info table | ✅ | ✅ Settings/About | ❌ | ❌ | **Full gap** |
| **Backup/Restore** (admin) | ⚠ partial | ✅ Settings/Data | ❌ | ❌ | **Full gap** |
| **Recent Scans** sidebar widget | ✅ | ❌ | ❌ | ❌ | **Full gap** |

### 2.9 Cross-cutting

| Function | SpecHub | Web | MAUI now | Planned | GAP |
| --- | --- | --- | --- | --- | --- |
| Bilingual VN+EN i18n | ⚠ seed 89 | ✅ 1,045 keys | ⚠ inline VN | (resx defer P10.6) | ⚠ |
| Hub real-time updates | ⚠ poll-based | ✅ SignalR | ❌ | ❌ | **Full gap** — SignalR client integration |
| `HubSessionBanner` 4-state | ❌ | ✅ Phase 9 | ❌ | ❌ | ⚠ — needed when SignalR lands |
| Camera capture (drawing/NG photo) | ✅ via file picker | ❌ | ❌ | ⚠ 5e optional camera-as-drawing | ⚠ |
| Settings deep-link (camera permission) | ✅ Catalyst | ❌ | ✅ P10.3 W2 | (no expand) | ✅ parity |

### 2.10 Tổng kết gap — entities hoàn toàn thiếu trong CẢ web LẪN MAUI

These are SpecHub features NEITHER project has built yet — true greenfield
gaps:

1. **MES 5-phase shop floor flow** (PREPRESS → SETTING → IPQC → READY →
   RUNNING → FQC → OQC → DONE) — neither Web nor MAUI ships this. Web has
   a simpler 8-step state machine; SpecHub has the full operator-driven
   phase UI.
2. **Machine Dashboard** with downtime Pareto + threshold alerts +
   activity log.
3. **Shop Order History v0.5.0** — forensic closed-WO module.
4. **Speed Performance card v0.5.2** (Design/Actual/Efficiency %).
5. **QMS Inspection Queue** auto-built from `MES_WO_STATE` (different
   from `/qcqa/iqc` raw-material acceptance).
6. **QC History** forensic table.
7. **NG modal** picker + free text + photo upload.
8. **IPQC/FQC/OQC signoff modals** with signature + user stamp.
9. **Editable rows in Draft spec** (contenteditable) + inline add/delete
   row.
10. **Recent Scans sidebar widget**.
11. **Planner chip filter** in Spec list.
12. **Diff toggle** for Revise before/after.
13. **Artwork SVG 4-layer toggle** + 3 template variants (brady-safety /
    ccl-cosmetic / panel-face).

Items 1-9 require **server-side service additions** (legacy
`CCL.MES.Application` does not have `MesScanProductService` or
`MachineDashboardService`). Items 10-13 are pure UI features that can
land in MAUI without server changes (or with read-only server additions).

---

## 3. CẢI TIẾN (improvements) — optional, ngoài parity, chờ Henry duyệt từng cái

Mỗi mục dưới có flag **"improvement"**. Không implement nếu Henry không
duyệt. Default = DEFER.

### IMP-1. Adopt CCL.MES.Web's 5-state spec status (over SpecHub's 3-state)
**Rationale**: Web đã ship Draft/InReview/Approved/Released/Superseded;
operators đã quen với InReview gate. SpecHub-3 (Draft/Approved/Superseded)
mất InReview làm sales review pre-approve trap.
**Trade-off**: Spec list status filter chip thêm 2 options, badge palette
mở rộng 5 màu. Migration data: Web đã có 5-state schema, no DB change.
**Cost**: +0 LOC (5-state already exists in Web; MAUI just renders 5 not 3).
**Recommend**: ACCEPT.

### IMP-2. Theme switcher trên Appearance (light/dark/auto)
**Rationale**: Catalyst supports `UIUserInterfaceStyle` change; operators
on night shift comfort.
**Cost**: 1 day. CSS custom-properties + body `data-theme` attr + MAUI
Preferences persist.
**Recommend**: DEFER to P10.6+ — không trong parity floor.

### IMP-3. SpecHub speed card (v0.5.2) chỉ shown trong MES flow → expose riêng trong WC drawer
**Rationale**: WC drawer on `/npi/workcenters` đang là info only. Adding
real-time speed comparison là direct value.
**Cost**: 1 day + WC drawer extension.
**Recommend**: DEFER to MES phase (parity gap 2.3).

### IMP-4. Real SignalR client trong MAUI (vs SpecHub polling pattern)
**Rationale**: Catalyst supports SignalR (`Microsoft.AspNetCore.SignalR.
Client`). Real-time updates eliminate 1Hz polling load + reduce latency.
**Cost**: 2-3 days. Hub connection state machine + `HubSessionBanner` port
+ ConnectivityMonitor integration.
**Recommend**: Bundle with the new MES phases (P10.6) so real-time is
available when operator UX matters most.

### IMP-5. Hardware barcode scanner triggered scan (vs camera-only)
**Rationale**: Shop floor có handheld USB scanner. P10.3 W2 stub
`IBarcodeScannerService` đã cover camera. Add USB-HID listener via
existing Catalyst Hardware abstractions.
**Cost**: 1-2 days. Catalyst impl for USB-HID via `IOKit`.
**Recommend**: ACCEPT — minimal cost + actual factory scenario.

### IMP-6. Offline outbox for MES qty increments + signoffs
**Rationale**: Factory wifi spotty. Operator typing qty mid-run shouldn't
fail on LAN hiccup.
**Cost**: 5-7 days. Plus idempotency key per W4 Q11.
**Recommend**: DEFER to P10.4 (already plan'd for offline phase).

### IMP-7. AI-assisted NG reason picker
**Rationale**: 26-code NG dropdown overwhelms; suggest top-3 based on
historical pattern per machine.
**Cost**: 3-4 days + analytics backend.
**Recommend**: DEFER — beyond parity scope.

### IMP-8. Drawing OCR — extract text from uploaded PDF to populate spec fields
**Rationale**: Engineer wastes 10 min re-typing colors / dimensions from
customer drawing.
**Cost**: 5+ days + Tesseract integration.
**Recommend**: DEFER.

### IMP-9. Improved Spec list — virtual scroll for 1000+ specs
**Rationale**: SpecHub renders all rows; long lists scroll slow.
**Cost**: 1 day. Use container queries + intersection observer.
**Recommend**: DEFER until > 500 specs measured.

### IMP-10. Mobile/tablet responsive (Catalyst window resize + iPad)
**Rationale**: SpecHub LESSONS_LEARNED.md §3 mandates container queries.
**Cost**: 2-3 days across all surfaces. Reuse SpecHub patterns.
**Recommend**: ACCEPT — small extra cost when building each page.

### IMP-11. Audit Log Viewer with XLSX export (vs SpecHub CSV-only)
**Rationale**: Web đã có XLSX. SpecHub-CSV is regression.
**Cost**: +0 LOC over CSV impl — ClosedXML already pulled in by 5g.
**Recommend**: ACCEPT.

### IMP-12. PBKDF2 device passcode strength feedback (zxcvbn-style)
**Rationale**: P10.3 W4 ship PBKDF2 silently. Operator picks "1234" — no
warning.
**Cost**: 1 day. zxcvbn-cs is free-license MIT.
**Recommend**: DEFER — defensive layer not in SpecHub baseline.

---

## 4. API mutation endpoint inventory (gộp với P10.5 §2)

Bảng này mở rộng `PHASE10-P10.5-NPI-SPEC-PORT-PLAN.md` §2 với endpoints
mới cho MES + Machine Dashboard + Shop Order History + QMS + Settings.
Cờ **NEW SERVICE** = legacy Application service chưa có method tương
ứng → cần service layer mới.

### 4.1 Endpoints ĐÃ có trong P10.5 plan (giữ nguyên)

24 mutations + 2 file streams cho Spec/NPI/Drawings/QC/IQC/WO — xem
`PHASE10-P10.5-NPI-SPEC-PORT-PLAN.md` §2.

### 4.2 Endpoints MỚI cho parity SpecHub features

| Surface | Endpoint | Verb | Policy | Application | Status |
| --- | --- | --- | --- | --- | --- |
| MES Prepress | `/api/v2/mes/wo/{id}/prepress/check` | POST | `OperatorWrite` (new) | `MesScanProductService.PrepressCheckAsync` | **NEW SERVICE** |
| MES Prepress | `/api/v2/mes/wo/{id}/prepress/status` | GET | `OperatorRead` | (read aggregate) | **NEW SERVICE** |
| MES Setting | `/api/v2/mes/wo/{id}/setting/start` | POST | `OperatorWrite` | `MesScanProductService.SettingStartAsync` | **NEW SERVICE** |
| MES Setting | `/api/v2/mes/wo/{id}/setting/done` | POST | `OperatorWrite` | `MesScanProductService.SettingDoneAsync` | **NEW SERVICE** |
| MES IPQC | `/api/v2/mes/wo/{id}/ipqc` | POST | `QcWrite` | `MesScanProductService.IpqcSubmitAsync` | **NEW SERVICE** |
| MES QA approve | `/api/v2/mes/wo/{id}/qa-approve` | POST | `QcWrite` | `MesScanProductService.QaApproveAsync` | **NEW SERVICE** |
| MES Run | `/api/v2/mes/wo/{id}/run/start` | POST | `OperatorWrite` | `MesScanProductService.RunStartAsync` | **NEW SERVICE** |
| MES Run | `/api/v2/mes/wo/{id}/run/qty` | POST | `OperatorWrite` | `MesScanProductService.AddQtyAsync` | **NEW SERVICE** |
| MES Run | `/api/v2/mes/wo/{id}/run/pause` | POST | `OperatorWrite` | `MesScanProductService.PauseAsync` | **NEW SERVICE** |
| MES Run | `/api/v2/mes/wo/{id}/run/resume` | POST | `OperatorWrite` | `MesScanProductService.ResumeAsync` | **NEW SERVICE** |
| MES Run | `/api/v2/mes/wo/{id}/run/finish` | POST | `OperatorWrite` | `MesScanProductService.FinishAsync` | **NEW SERVICE** |
| MES FQC | `/api/v2/mes/wo/{id}/fqc` | POST | `QcWrite` | `MesScanProductService.FqcSubmitAsync` | **NEW SERVICE** |
| MES OQC | `/api/v2/mes/wo/{id}/oqc` | POST | `QcWrite` | `MesScanProductService.OqcSubmitAsync` | **NEW SERVICE** |
| MES Audit | `/api/v2/mes/wo/{id}/audit` | GET | `OperatorRead` | (queries audit_log filtered) | reuse existing |
| Reasons master | `/api/v2/master/ng-reasons` | GET | Any auth | `MasterDataService.ListNgReasonsAsync` | **NEW SERVICE** |
| Reasons master | `/api/v2/master/downtime-reasons` | GET | Any auth | `MasterDataService.ListDowntimeReasonsAsync` | **NEW SERVICE** |
| Machine Dashboard | `/api/v2/dashboard/machines` | GET | Any auth | `MachineDashboardService.SnapshotAsync` | **NEW SERVICE** |
| Machine Dashboard | `/api/v2/dashboard/machines/{wc}` | GET | Any auth | `MachineDashboardService.MachineDetailAsync` | **NEW SERVICE** |
| Machine Dashboard | `/api/v2/dashboard/downtime-pareto` | GET | Any auth | `MachineDashboardService.DowntimeParetoAsync` | **NEW SERVICE** |
| Machine Dashboard | `/api/v2/dashboard/alerts` | GET | Any auth | `MachineDashboardService.AlertsAsync` | **NEW SERVICE** |
| Shop Order History | `/api/v2/wo-history` | GET (paged) | Any auth | `ShopOrderHistoryService.ListAsync` | **NEW SERVICE** |
| Shop Order History | `/api/v2/wo-history/{id}` | GET | Any auth | `ShopOrderHistoryService.DetailAsync` | **NEW SERVICE** |
| Shop Order History | `/api/v2/wo-history/kpis` | GET | Any auth | `ShopOrderHistoryService.KpisAsync` | **NEW SERVICE** |
| Shop Order History | `/api/v2/wo-history/export.csv` | GET | Any auth | `ShopOrderHistoryService.ExportAsync` | **NEW SERVICE** |
| QMS queue | `/api/v2/qms/queue` | GET | `QcWrite` | `QmsQueueService.QueueAsync` | **NEW SERVICE** |
| QMS history | `/api/v2/qms/history` | GET | `QcWrite` | `QmsQueueService.HistoryAsync` | **NEW SERVICE** |
| Settings: profile | `/api/v2/settings/me` | GET / PATCH | Any auth | `UserProfileService` (exists) | reuse |
| Settings: password | `/api/v2/settings/password` | POST | Any auth | `UserAdminService` (exists) | reuse |
| Settings: appearance | `/api/v2/settings/appearance` | GET / PATCH | Any auth | client-side mostly | reuse |
| Settings: users (admin) | `/api/v2/admin/users` etc. | various | `AdminOnly` | `UserAdminService` (exists) | reuse |
| Settings: audit log | `/api/v2/admin/audit-logs` | GET (filter) | `AdminOnly` | `AuditLogService` (exists) | reuse |
| Settings: audit export | `/api/v2/admin/audit-logs/export.{csv,xlsx}` | GET | `AdminOnly` | `AuditLogService.ExportAsync` (exists) | reuse |
| Settings: backup | `/api/v2/admin/backup/snapshot` | POST | `AdminOnly` | `BackupService.CreateSnapshotAsync` (exists) | reuse |
| Settings: backup list | `/api/v2/admin/backup` | GET | `AdminOnly` | `BackupService` (exists) | reuse |
| Recent Scans | `/api/v2/me/recent-scans` | GET | Any auth | client-only (Preferences) | no API needed |
| Spec planner filter | (no new endpoint) | — | — | list endpoint already supports `?planner=` (extension on existing) | reuse with query param add |
| Drawing OCR | (defer IMP-8) | — | — | — | (improvement, deferred) |

### 4.3 Tổng cộng

- 24 + 2 từ P10.5 plan (Spec/NPI/Drawings/QC/IQC/WO)
- +30 từ SpecHub parity (MES + Dashboard + History + QMS + Settings + masters)
- +0 file streams new (settings export reuses pattern)

**Total: ~54 mutation + read endpoints + 2 file streams + 1 query-param
extension = ~56 endpoints**.

### 4.4 NEW SERVICES required trong CCL.MES.Application

Constraint: legacy 0 diff. Bypass: **add new services under
`CCL.MES.Application/Mes/` subfolder** — `MesScanProductService`,
`MachineDashboardService`, `ShopOrderHistoryService`, `QmsQueueService`,
`MasterDataService`. These don't modify existing services; they're new
files in the existing assembly. Project-reference + DI registration in
API project.

| New Service | Methods (sketch) | Domain entities touched |
| --- | --- | --- |
| `MesScanProductService` | PrepressCheck/Status, SettingStart/Done, IpqcSubmit, QaApprove, RunStart/Qty/Pause/Resume/Finish, FqcSubmit, OqcSubmit | `WorkOrder` (extend Mes columns), `WoMaterials`, `WoPlateCheck`, `WoCutterCheck`, `WoSettingLog`, `IpqcChecks`, `RunSessions`, `RunEvents`, `MesAuditLog` |
| `MachineDashboardService` | SnapshotAsync, MachineDetailAsync, DowntimeParetoAsync, AlertsAsync | `WorkCenter` (read), `RunEvents` (read), `WoMaterials` (read for alerts) |
| `ShopOrderHistoryService` | ListAsync, DetailAsync, KpisAsync, ExportAsync | `WorkOrder` (filter Done state) |
| `QmsQueueService` | QueueAsync, HistoryAsync | `WorkOrder` + `IpqcChecks` + `RunSessions` |
| `MasterDataService` | ListNgReasonsAsync, ListDowntimeReasonsAsync | `NgReasons`, `DowntimeReasons` (need new tables — DB migration required) |

**DB migration needed** (separate, additive, KHÔNG sửa legacy tables):
- `ng_reasons` table + seed 26 codes
- `downtime_reasons` table + seed 10 codes
- `wo_materials`, `wo_plate_check`, `wo_cutter_check`, `wo_setting_log` tables
- `ipqc_checks`, `run_sessions`, `run_events`, `mes_audit_log` tables
- `work_orders` extend with `mes_status` enum + `mes_started_at` /
  `mes_finished_at` (additive columns; ALTER ADD COLUMN keeps backward compat)

Migration files live in `CCL.MES.Infrastructure/Migrations/` (legacy
project). **Constraint check**: does adding new EF migrations count as
"legacy diff"? Pragmatic answer: **YES it's a diff**, but it's
ADDITIVE (no schema breaks, no column drops). Two options:

- **Option A**: Treat additive migrations as acceptable diff. Lower
  risk, cleaner architecture.
- **Option B**: Stand up a **second `CCL.MES.Hybrid.Migrations`
  project** in `CCL-MES-Hybrid/` for MES tables. Legacy schema
  untouched; new tables live separately. Higher cost but pure 0-diff.

**Recommend**: Option A — additive migrations to legacy project.
SpecHub DDL is `002_mes_scan_product.sql` shape and CCL.MES.Application
service layer is the natural home. **Henry decides** in Q1.

---

## 5. CHIA PHASE — extended P10.5 + new MES/Dashboard/Settings phases

Renumbered + extended. P10.5a already shipped. P10.5b-g per original
plan with adjustments noted. P10.6+ adds full SpecHub parity surfaces.

Total estimate: **~22-30 weeks 1 FTE** for full parity (excluding
improvements). With 1.5 FTE parallel: **~14-18 weeks**.

### 5.1 P10.5 — Spec library + NPI grids (already planned)

| PR | Status | Days | Notes |
| --- | --- | --- | --- |
| **P10.5a** | ✅ Shipped (PR #80) | (done) | 4 NPI read grids + Modal + grid helpers |
| **P10.5b** | Planned, **adjusted** | 5-7 | Spec list + 6-tab read + compact showcard + **ADD** planner chip filter (IMP from §2.7) |
| **P10.5c** | Planned | 7-10 | Spec mutations + xlsx import + **ADD** inline contenteditable rows (§2.7 gap 9) |
| **P10.5d** | Planned | 4-5 | Spec lifecycle modals + **ADD** diff toggle (§2.10 item 12) |
| **P10.5e** | Planned | 10-14 | Drawings + upload + 3-role chain + **ADD** Artwork SVG 4-layer (§2.10 item 13) |
| **P10.5f** | Planned | 8-10 | QC Plans + QC Capture + per-criterion entry |
| **P10.5g** | Planned | 4-5 | Spec exports + admin refresh-samples |

P10.5 total: 38-51 days.

### 5.2 P10.6 — Settings sub-tabs + Recent Scans + theme

| PR | Risk | Days | Scope |
| --- | --- | --- | --- |
| **P10.6a** | L | 3-4 | Settings shell + My Profile + My Password read + change |
| **P10.6b** | L | 2-3 | Appearance (Language picker reuse existing — theme deferred if IMP-2 not accepted) |
| **P10.6c** | M | 4-5 | Account Control admin: user CRUD list + Create user modal + Reset password |
| **P10.6d** | L | 2 | About page + Connection Mode info |
| **P10.6e** | M | 4-5 | Audit Log Viewer + CSV/XLSX export (IMP-11 ACCEPT) |
| **P10.6f** | L | 2 | Recent Scans sidebar widget (Preferences-based, client-only) |
| **P10.6g** | M | 3-4 | Theme switcher (IMP-2 conditional) |
| **P10.6h** | L | 1-2 | Backup/Restore admin (read snapshot list + trigger snapshot) |

P10.6 total: 21-30 days.

### 5.3 P10.7 — MES 5-phase shop floor flow

| PR | Risk | Days | Scope |
| --- | --- | --- | --- |
| **P10.7a** | **H** | 10-14 | DB migration (additive) + master data tables + `MasterDataService` + `MesScanProductService` skeleton + audit emit pattern + state machine guards |
| **P10.7b** | M | 5-7 | PREPRESS UI: 3 check sections + NG modal + photo upload (reuse `CatalystMediaPicker` from 5e) |
| **P10.7c** | M | 5-7 | SETTING UI: timer + Start/Done |
| **P10.7d** | **H** | 8-10 | IPQC UI: 5-criterion entry + judgment (Accept/Stop/Special Accept) + QA approval modal + signoff |
| **P10.7e** | M | 3-4 | READY-TO-RUN confirm gate |
| **P10.7f** | **H** | 10-14 | RUNNING UI: qty counter + good/reject + 1Hz tick + Speed card (v0.5.2 parity) |
| **P10.7g** | M | 5-7 | PAUSED modal + downtime reason picker + resume |
| **P10.7h** | M | 5-7 | FQC + OQC UI + signoff modals (similar pattern to IPQC) |
| **P10.7i** | M | 3-4 | DONE summary: OEE + yield + reject Pareto |

P10.7 total: 54-74 days. (Largest phase by volume.)

### 5.4 P10.8 — Machine Dashboard + Shop Order History

| PR | Risk | Days | Scope |
| --- | --- | --- | --- |
| **P10.8a** | M | 5-7 | `MachineDashboardService` + 4 read endpoints |
| **P10.8b** | M | 5-7 | Machine Dashboard UI: 10-machine grid + status pills + KPI badges + detail drawer |
| **P10.8c** | M | 3-4 | Downtime Pareto chart + alerts panel + activity log |
| **P10.8d** | L | 2-3 | Auto-refresh + kill-switch + filter/search |
| **P10.8e** | M | 5-7 | `ShopOrderHistoryService` + endpoints |
| **P10.8f** | M | 5-7 | Shop Order History UI: 5 KPI tiles + period chips + 10-col table + progress bar |
| **P10.8g** | L | 3-4 | Side stats panel + 19-col detail drawer + CSV export |

P10.8 total: 28-39 days.

### 5.5 P10.9 — QMS Inspection Queue + QC History

| PR | Risk | Days | Scope |
| --- | --- | --- | --- |
| **P10.9a** | M | 4-5 | `QmsQueueService` + endpoints |
| **P10.9b** | M | 5-7 | Queue UI auto-build + stage tabs (IPQC/FQC/OQC) |
| **P10.9c** | M | 3-4 | QC History forensic table + filter + export |

P10.9 total: 12-16 days.

### 5.6 P10.10 — Home / Recent / KPI summary + real-time SignalR (IMP-4)

| PR | Risk | Days | Scope |
| --- | --- | --- | --- |
| **P10.10a** | M | 4-5 | Home page redesign: greeting + 5 KPI tiles + Recent Specs (5) + quick actions + 1Hz clock |
| **P10.10b** | M | 5-7 | SignalR client integration + `HubSessionBanner` 4-state port (IMP-4 ACCEPT) |
| **P10.10c** | L | 2-3 | Real-time hooks: Machine Dashboard refresh on hub event |

P10.10 total: 11-15 days.

### 5.7 P10.11 — Hardware extensions (IMP-5 USB-HID scanner + camera capture polish)

| PR | Risk | Days | Scope |
| --- | --- | --- | --- |
| **P10.11a** | M | 3-4 | USB-HID scanner Catalyst impl (IMP-5 ACCEPT) |
| **P10.11b** | L | 2-3 | Mobile/tablet responsive polish across all surfaces (IMP-10 ACCEPT) |

P10.11 total: 5-7 days.

### 5.8 P10.12 — Improvements approved (IMP-2 theme, IMP-3 WC speed card, etc.)

Henry-gated per IMP. Scope depends on which IMP items accepted.
Estimate range: 0-15 days.

### 5.9 Cumulative roadmap

| Phase | Days | Cumulative | Notes |
| --- | --- | --- | --- |
| P10.5a | ✅ shipped | — | Done 2026-06-03 |
| P10.5b-g | 38-51 | 38-51 | Original P10.5 plan |
| P10.6a-h | 21-30 | 59-81 | Settings |
| P10.7a-i | 54-74 | 113-155 | MES 5-phase (largest) |
| P10.8a-g | 28-39 | 141-194 | Dashboard + History |
| P10.9a-c | 12-16 | 153-210 | QMS |
| P10.10a-c | 11-15 | 164-225 | Home + SignalR |
| P10.11a-b | 5-7 | 169-232 | Hardware extensions |
| P10.12 (improvements) | 0-15 | 169-247 | Henry-gated IMPs |

**Full parity (no improvements)**: ~169-225 working days = **34-45 weeks
1 FTE** ≈ **9-12 months** solo.

**Parity + accepted improvements (5 ACCEPT items: IMP-1, IMP-4, IMP-5,
IMP-10, IMP-11)**: ~190-240 days = **38-48 weeks**.

**Compressible via parallelism**: P10.5 + P10.6 + P10.7a (DB+service)
can split across 2 devs. P10.8 + P10.9 parallel. Roughly **20-30 weeks
with 2 FTE**.

### 5.10 Ordering rationale

1. **Read-first per phase**: every phase has read PR before mutation PR
   (P10.5a→b→c, P10.7a→b→c, P10.8a→b, etc.). Reduces blast radius and
   lets Henry verify data flow before write paths land.
2. **Master data + services + DB before MES UI**: P10.7a is the
   foundation enabling 7b-i.
3. **Dashboard before SignalR**: P10.8 establishes poll-based dashboard
   first; P10.10b adds real-time as additive enhancement.
4. **Settings parallel-safe**: P10.6 can ship between any of the bigger
   phases since it doesn't share files with MES surfaces.
5. **Improvements last**: don't gold-plate; ship parity first.

---

## 6. RISK summary + open questions

### 6.1 Highest risks

| Risk | Severity | Mitigation |
| --- | --- | --- |
| P10.7 MES schema migration on shared DB | **H** | Use additive migrations only; never drop column or change FK; coordinate with Web team — option A vs B in §4.4 |
| SignalR Catalyst stability | M | Reuse `Microsoft.AspNetCore.SignalR.Client` Cocoa support; test trên adhoc dev build trước GA |
| Photo upload size + Catalyst camera entitlement | M | Reuse `NSCameraUsageDescription` from W2; max 2MB image; multipart with progress |
| Per-shift state machine corruption (operator B picks up A's WO mid-phase) | M | Lock at server (transaction + WHERE clause guard on state) + UI shows "this WO is in phase X by user Y" — block forward |
| Audit log table growth | L | Partitioning + retention policy via `SpecTrashPurgeService` pattern |
| MAUI client bundle size > 100 MB after P10.7 | L | Strip unused MAUI controls + lazy-load image picker |
| Drawing PDF render on Catalyst WebView | M | Fallback to system Preview via `IDeviceSettingsLauncher` style hook |
| i18n drift between MAUI inline VN + Web resx | M | Q12 lock keeps MAUI inline VN until P10.6+ ports resx; doc-string compare on key matches |
| Concurrent operator WO state writes | **H** | Optimistic lock via `WorkOrder.RowVersion` + 409 retry on client |
| Permission ladder for MES (operator vs supervisor vs npi vs qc vs admin) | M | Map SpecHub 5 roles to existing 5 UserRole strings; ladder enforced at controller via `[Authorize(Policy=...)]` |

### 6.2 Open questions (Q1..Q20)

**Q1** (BLOCKING) — DB migration policy: Option A (additive in legacy
project) or Option B (separate `CCL.MES.Hybrid.Migrations` project)?
**Default**: A (pragmatic + cleaner service registration).

**Q2** (BLOCKING) — Adopt CCL.MES.Web's 5-state spec status over
SpecHub's 3-state? (IMP-1)
**Default**: ACCEPT (already exists in Web; MAUI just renders 5).

**Q3** — Real SignalR vs polling? (IMP-4)
**Default**: ACCEPT — bundle với P10.10 so MES phases benefit.

**Q4** — USB-HID hardware scanner? (IMP-5)
**Default**: ACCEPT — small cost, real factory scenario.

**Q5** — Mobile/tablet responsive polish? (IMP-10)
**Default**: ACCEPT — minimal extra cost when building each page.

**Q6** — Audit Log XLSX export? (IMP-11)
**Default**: ACCEPT — ClosedXML already pulled in by 5g.

**Q7** — Theme switcher? (IMP-2)
**Default**: DEFER to P10.12 — not parity floor.

**Q8** — Specific MES service location: `CCL.MES.Application/Mes/` vs
new project `CCL.MES.Application.Mes`?
**Default**: subfolder in existing assembly (lighter ceremony).

**Q9** — `mes_status` enum on legacy `WorkOrders` table: extend column
or shadow table?
**Default**: extend column (SpecHub DDL pattern; backward-compat ALTER ADD).

**Q10** — Per-shift / per-operator WO lock semantics?
**Default**: server-side optimistic lock on `WorkOrder.RowVersion`; UI
shows "WO đang được thực hiện bởi <user>" + block.

**Q11** — Idempotency key cho MES qty increments / signoffs?
**Default**: P10.4 offline queue handle. P10.7 ship without
(`_submitting` guard prevents double-tap; LAN hiccup operator retry OK
since GOOD/REJECT are operator-authoritative).

**Q12** — i18n: still inline VN per P10.5 plan, or accelerate resx port
khi P10.6 lands Settings?
**Default**: ACCELERATE — `Resources/SharedResource.resx` from Web is
portable; P10.6a includes resx setup as foundation for Settings strings.

**Q13** — Audit log retention: SpecHub `mes_audit_log` infinite vs Web
audit_log with 90-day purge?
**Default**: 90-day purge + monthly archive snapshot.

**Q14** — Demo / seed users in MAUI vs API?
**Default**: API seeds demo users (already done from Web Phase 5); MAUI
no demo button.

**Q15** — Recent Scans persistence: Preferences (local per device) vs
server (cross-device)?
**Default**: Preferences (per-device; SpecHub local pattern).

**Q16** — Speed card design speed source: routing operations table or
`product.design_speed`?
**Default**: Routing first, product fallback. Match SpecHub
`_mesGetDesignSpeed()`.

**Q17** — Machine Dashboard layout: 10-machine grid hard-coded vs
all-machines responsive grid?
**Default**: responsive grid `auto-fit minmax(280px, 1fr)` — accommodate
factory adding machines.

**Q18** — `#if DEBUG` cleanup gate: keep checklist per PR? Audit grep
mandatory?
**Default**: YES — `grep -r "DEBUG\|p10-[0-9]+\|dbg-" CCL-MES-Hybrid/src/`
pre-merge for every PR.

**Q19** — Hardware verify gate: every PR must verify Catalyst? Or
sample every N PRs?
**Default**: every PR per current P10.3/W4/P10.5a discipline.

**Q20** — Plan re-numbering: does Henry want each new phase (P10.6+)
fully spec'd before P10.5b starts, or can we ship P10.5b-g first and
spec P10.6+ as P10.5 completes?
**Default**: SHIP P10.5b-g first (already planned + Henry approved
Q1-Q15). Spec P10.6 detailed plan after P10.5e (drawings) lands so we
understand file picker + camera patterns.

### 6.3 Henry decisions needed (BLOCKING vs DEFAULT)

Pls accept defaults hoặc override per Q. **Q1 + Q2 are BLOCKING** — phải
chốt trước khi P10.5b starts. Q3-Q7 + Q12 affect P10.6+ scope. Q8-Q17
affect P10.7+ implementation détail.

---

## 7. SUCCESS criteria — full SpecHub parity

End of P10.11 (all phases shipped):

- ✅ Mac Catalyst operator scans WO → 5-phase MES flow end-to-end
  (Prepress → Setting → IPQC → Ready → Running with Speed card → FQC →
  OQC → Done)
- ✅ Machine Dashboard live grid of all factory machines + Pareto +
  alerts + auto-refresh
- ✅ Shop Order History forensic table + 5 KPIs + 19-col drawer +
  filter-aware CSV export
- ✅ QMS Inspection Queue + QC History
- ✅ NPI Spec Library full read + lifecycle + 6-tab detail (Spec /
  Drawings / QC Plans / QC Capture / Artwork / Setup) + drawings upload
  + 3-role approval + xlsx import + CSV/XLSX/PDF exports
- ✅ Settings 6 sub-tabs + Account Control admin + Audit Log Viewer with
  XLSX export
- ✅ Recent Scans sidebar
- ✅ SignalR real-time for dashboards (IMP-4 ACCEPT)
- ✅ USB-HID scanner (IMP-5 ACCEPT)
- ✅ Mobile/tablet responsive (IMP-10 ACCEPT)
- ✅ 5-state spec status (IMP-1 ACCEPT)
- ✅ Audit Log XLSX export (IMP-11 ACCEPT)
- ✅ Legacy `CCL.MES.{Domain,Application*,Infrastructure,Web}` zero
  edits (project-reference + additive migrations only per Q1=A).
- ✅ Test suite: ~250 new endpoint integration tests + ~150
  viewmodel/helper tests. 0 regressions.
- ✅ Hardware verify per PR before merge.
- ✅ All sibling projects (SpecHub / CMES / Ops Control v1.2 / Old ver)
  zero edits.

End-of-P10.11 operational state:
- MAUI client = single Catalyst app replacing CCL.MES.Web entirely for
  shop-floor operator + traveling engineer use cases.
- Web continues for desktop admin / archive access until ops decides
  retirement.

---

## 8. NEXT step

**Henry to review + decide**:
1. **BLOCKING**: Q1 (DB migration policy) + Q2 (5-state spec status)
2. Improvements: which of IMP-1..11 accept (defaults are 5 ACCEPT / 5 DEFER / 1 conditional)
3. Plan re-numbering: ship P10.5b-g first or pre-spec P10.6+?

Once Henry approves Q1+Q2 + improvements list, **P10.5b** starts
immediately on the next branch. P10.6+ phase plans land as P10.5 PRs
close.

**STOP — chờ Henry review plan + decisions.**
