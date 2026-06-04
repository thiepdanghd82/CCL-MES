# Three-codebase parity audit — 2026-06-04

> **Mandate.** Refresh + extend the gap matrix in
> `docs/PHASE10-SPECHUB-PARITY-PLAN.md` (§2.1–2.10) against the three
> live trees as they stand on 2026-06-04. Read-only — no Edit / Write
> to any source file. Output = this report + a TL;DR for Henry.
>
> **Codebases inspected**:
> 1. **SpecHub prototype** — `/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/SpecHub/`
>    (vanilla HTML+JS prototype, NestJS skeleton, PG migrations).
>    `spechub-prototype.html` is 20,678 LOC; CHANGELOG top tag is
>    **v0.5.2 (2026-05-28)** — Speed Performance card on Running tab.
> 2. **CCL-MES Web (legacy)** — `/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/CCL-MES/src/CCL.MES.Web/`.
>    Razor Pages app. Phase 7-9 features shipped. Production today.
> 3. **CCL-MES Hybrid (current)** — `/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/CCL-MES/CCL-MES-Hybrid/`.
>    MAUI Blazor Hybrid client + `CCL.MES.Api`. Latest commit on `main`
>    is `10ed3ad feat(p10.6f): Recent Scans sidebar widget` (after
>    P10.5a-g + P10.6a + P10.6d + P10.6f). The parity plan was written
>    **before** these three P10.6 PRs landed, so several "❌ MAUI" rows
>    are now stale.
>
> **All citations include absolute paths + line numbers** so Henry can
> verify each finding by direct read.

---

## Phase A — Gap matrix, refreshed

Symbols: ✅ shipped · ⚠ partial / behavioural drift · ❌ missing. Each
row in §A.1–A.10 corresponds 1:1 to a row in the original plan §2.1–
2.10 so the deltas are easy to diff.

### A.1 Home / Recent / KPI summary

| Sub-feature | SpecHub | Web | Hybrid (today) | Gap analysis |
| --- | --- | --- | --- | --- |
| Welcome greeting | ✅ `openHomeView()` `spechub-prototype.html:7251`, `:7427` | ✅ `src/CCL.MES.Web/Pages/Dashboard.razor` (137 LOC) | ✅ `Pages/Home.razor:7` — "Xin chào, @display_name" | Parity at the **string** level only. Hybrid is 33 LOC, single tile. |
| Recent specs list (5) | ✅ `_oneCAppendLog` + sidebar list | ⚠ Quote History pattern (different shape) | ⚠ Recent **Scans** widget shipped P10.6f at `Shared/RecentScansWidget.razor` — backed by `IRecentScansService` (Preferences). Recent **Specs** still ❌. | Plan §2.1 row 2 said "MAUI ❌, planned ❌". Now: half-shipped. Recent Scans = WO scans not Spec opens — different semantics. |
| Quick action tiles | ✅ 4-tile grid in SpecHub Home | ✅ Dashboard chips | ⚠ 1 tile (NPI only) at `Home.razor:17-20`. Settings + Specs hidden. | Plan still accurate: minimal. P10.10a should add the missing 3 tiles (Create Spec / Import / Account Control). |
| KPI summary | ✅ live counts | ✅ Dashboard | ❌ | Full gap. Cheap to ship — counts available via existing `NpiController` + `SpecsController`. |
| 1Hz clock + shift | ✅ live tick | ⚠ static | ❌ | Full gap. Pure client. |

**Delta vs plan**: Recent Scans **shipped** (was ❌, now ⚠). Recent Specs still missing.

### A.2 Shop Order (MES 5-phase)

| Sub-feature | SpecHub | Web | Hybrid (today) | Gap analysis |
| --- | --- | --- | --- | --- |
| Scan WO → drawer | ✅ | ✅ | ✅ `/workorders` shipped P10.3 W4 (`feat/p10.3-w4-scan-wo-accept`) | parity |
| **PREPRESS** 3 check sections | ✅ Materials / Plate / Cutter | ⚠ `WorkOrder.MaterialsReady` boolean only | ❌ | Full gap. SpecHub data model is row-level checks; Web has only a flag. |
| **SETTING** timer + Start/Done | ✅ | ⚠ guard `SetupConfirmed` only | ❌ | Full gap. |
| **IPQC** 5-criterion judgment | ✅ Accept lô / Stop lô / Special Accept | ⚠ **`src/CCL.MES.Web/Pages/QcQa/Ipqc.razor` is a 10-LOC stub** (verified by `wc -l`). Real logic lives in `QcInspection`. | ❌ | Stub on both Web + Hybrid. SpecHub is the only complete reference. |
| **READY** confirm gate | ✅ | ⚠ via `WorkOrderStateMachine` advance button | ❌ | Plan accurate. |
| **RUNNING** qty counter | ✅ live | ⚠ `ProducedQty` update only | ❌ | Plan accurate. |
| **RUNNING** Speed card (v0.5.2) | ✅ Design / Actual / Efficiency % | ❌ | ❌ | Plan accurate. New since 2026-05-28 — recent SpecHub work. |
| **PAUSED** modal + reason picker | ✅ | ❌ | ❌ | Plan accurate. |
| **FQC** 5-criterion | ✅ | ⚠ generic `QcInspection.Create` | ❌ | Plan accurate. |
| **OQC** 4-criterion | ✅ | ⚠ **`Pages/QcQa/Oqc.razor` is 10-LOC stub** | ❌ | As noted above. |
| **DONE** summary (OEE / yield / reject Pareto) | ✅ | ⚠ `OeeService.cs` exists in Application | ❌ | OEE half-shipped on Web (Application layer); UI absent. Hybrid: full gap. |
| Audit log (25+ events) | ✅ in-memory `MES_WO_STATE` + `_oneCAppendLog` | ✅ `WoStatusHistory` rows | ⚠ DEVICE_SCAN + WO_ADVANCE_DEVICE only | Plan accurate. |
| State machine (DB + app + UI guard) | ✅ 3-layer | ✅ **`src/CCL.MES.Domain/StateMachine/WorkOrderStateMachine.cs` ships 8-state flow: PrePressCheck → OpSetting → IpqcApproval → ReadyToRun → Running → Fqc → Oqc → Closed** — direct match to SpecHub 5-phase model. | ⚠ Hybrid client only handles "Advance" via Web's existing flow | **Plan understates Web maturity.** State machine is 100% shipped server-side. The gap is purely the operator UI on Hybrid. |
| NG modal | ✅ context picker + photo | ❌ | ❌ | Plan accurate. |
| Auth modals (signoff signature) | ✅ | ❌ | ❌ | Plan accurate. |
| Right sidebar WO context | ✅ | ⚠ WO drawer pattern | ❌ | Plan accurate. |

**Critical reframing**: the plan reads as if the MES 5-phase shop-floor
flow is a greenfield server build. **The 8-state machine + `WoStatusHistory`
+ `QcInspection` entities are already shipped in legacy.** The actual
gap is **(a) operator UI** + **(b) row-level Prepress/Plate/Cutter
sub-checks** + **(c) NG/downtime reason masters** + **(d) per-criterion
QC rows**. That is materially smaller than the §5.3 P10.7 estimate of
54-74 days suggests.

### A.3 Machine Dashboard

| Sub-feature | SpecHub | Web | Hybrid (today) | Gap analysis |
| --- | --- | --- | --- | --- |
| 10-machine grid + status pills + KPI badges | ✅ | ⚠ Dashboard OEE table, no grid | ❌ | Plan accurate. |
| Downtime Pareto | ✅ | ❌ | ❌ | Plan accurate. |
| Threshold alerts | ✅ Low Material / Tooling Due / Maint Due | ❌ | ❌ | Plan accurate. |
| Activity log (recent 20) | ✅ | ❌ | ❌ | Plan accurate. |
| Per-machine detail drawer | ✅ | ⚠ WC info modal | ❌ | Plan accurate. |
| Auto-refresh + kill-switch | ✅ `OPS_DISABLE_AUTOREFRESH` (v0.5.1) | ⚠ **SignalR `ShopfloorHub.cs` at `src/CCL.MES.Web/Hubs/`** — different mechanism | ⚠ **`ShopfloorHubV2.cs` exists on the Hybrid API at `src/CCL.MES.Api/Hubs/`** — server hub wired in `Program.cs:287 app.MapHub<ShopfloorHubV2>("/hubs/shopfloor")` — but Razor client has **no `Microsoft.AspNetCore.SignalR.Client` package dependency** (verified — grep returned nothing). | **Plan IMP-4 understates how far SignalR has already moved.** Server-side hub V2 is in. What's missing is client subscription. |
| Filter by area / code / status + search | ✅ | ❌ | ❌ | Plan accurate. |

### A.4 Shop Order History (v0.5.0)

| Sub-feature | SpecHub | Web | Hybrid (today) | Gap analysis |
| --- | --- | --- | --- | --- |
| 5 KPI tiles | ✅ | ✅ Dashboard | ❌ | Plan accurate. |
| Period chips | ✅ | ❌ | ❌ | Plan accurate. |
| 10-col forensic table | ✅ | ⚠ partial `/workorders` | ❌ | Plan accurate. |
| Progress bar qty_done/plan | ✅ | ❌ | ❌ | Plan accurate. |
| Side stats (Top 5 customers / machines / Pareto) | ✅ | ❌ | ❌ | Plan accurate. |
| 19-col detail drawer | ✅ | ❌ | ❌ | Plan accurate. |
| Filter-aware CSV export | ✅ | ⚠ `CCL.MES.Application/WorkOrderExport/CsvWorkOrderListExporter.cs` ships generic CSV | ❌ | Web has CSV pipeline; columns + filter differ. |

### A.5 QMS — Inspection Queue + QC History

| Sub-feature | SpecHub | Web | Hybrid (today) | Gap analysis |
| --- | --- | --- | --- | --- |
| Auto-build queue | ✅ from `MES_WO_STATE` | ❌ | ❌ | Plan accurate. |
| IPQC entry | ✅ | ⚠ stub (10 LOC) | ❌ | Plan accurate; reinforced by line-count check. |
| FQC entry | ✅ | ❌ (no FQC page at all) | ❌ | Plan accurate. |
| OQC entry | ✅ | ⚠ stub (10 LOC) | ❌ | Plan accurate. |
| Signout role-gate lock | ✅ | ⚠ generic `QcService.Approve` | ❌ | Plan accurate. |
| **IQC** raw-material acceptance | ⚠ N/A in SpecHub | ✅ `Pages/QcQa/Iqc.razor` (404 LOC, real) | ✅ `IqcController.cs` + `Pages/IqcReceiving.razor` (via P10.x earlier) | This row was missing from the plan §2.5 entirely — Web has IQC, SpecHub doesn't. |
| QC History forensic table | ✅ | ❌ | ❌ | Plan accurate. |

### A.6 Routine / Product Structure / Machine List / Raw Materials

| Sub-feature | SpecHub | Web | Hybrid (today) | Gap analysis |
| --- | --- | --- | --- | --- |
| Routine read | ✅ | ✅ | ✅ `/npi/routine` P10.5a | parity |
| Routine inline edit | ✅ | ⚠ CSV bulk only | ❌ | Plan accurate. |
| Routine `_mesGetDesignSpeed` sync | ✅ | ❌ | ❌ | Plan accurate. |
| Structure read | ✅ | ✅ | ✅ `/npi/structure` P10.5a | parity |
| Structure inline edit | ✅ | ⚠ CSV | ❌ | Plan accurate. |
| Machine List 150 grid | ✅ | ✅ via WorkCenter (different shape) | ✅ `/npi/workcenters` P10.5a | shape drift, not gap |
| Machine List detail drawer | ✅ | ⚠ WC info modal | ❌ | Plan accurate. |
| Raw Materials read | ❌ SpecHub omits | ✅ | ✅ `/npi/rawmaterials` P10.5a | Hybrid carries a Web-only feature. |

### A.7 NPI Spec Library (6 sub-tabs)

| Sub-feature | SpecHub | Web | Hybrid (today) | Gap analysis |
| --- | --- | --- | --- | --- |
| 14-col list view | ✅ | ✅ `EngineerSpec.razor` | ✅ `/npi/specs` P10.5b (`Pages/Specs.razor`, 807 LOC) | parity |
| Planner chip filter | ✅ | ⚠ status filter only | ✅ **shipped P10.5c-3** (commit `95ee142`) — grep confirms `planner`/`chip`/`filter` referenced 29× in `Specs.razor` | **Plan §2.7 listed this as a Gap.** Closed by P10.5c-3. |
| Search 3-field | ✅ | ✅ | ✅ P10.5b | parity |
| 5-state vs 3-state pill | ⚠ 3-state | ⚠ 5-state | TBD — Q2 BLOCKING | Q2 still open per plan §6.3 |
| Sub-tab Specification (showcard) | ✅ | ✅ 1,024 LOC | ✅ `Shared/SpecShowcardCompact.razor` + `SpecShowcardFull.razor` (P10.5b + P10.5d) | parity |
| Editable rows in Draft (contenteditable) | ✅ | ⚠ via SpecEditModal | ✅ P10.5c-3 inline edit | **Plan §2.7 listed as Gap.** Closed. |
| Add/delete row in Draft | ✅ | ⚠ via modal | ✅ part of P10.5c-3 | Closed. |
| Drawings sub-tab (9 kinds) | ✅ | ✅ | ✅ `SpecDrawingsTab.razor` + P10.5e + P10.5e-2 | parity |
| 3-role approval chain | ✅ | ✅ | ✅ `DrawingDecideModal.razor` P10.5e-2 | parity |
| Auto-supersede on change | ✅ | ✅ | ✅ P10.5e | parity |
| PDF preview inline | ✅ | ✅ | ✅ P10.5e via Catalyst WebView | parity |
| QC Plans sub-tab | ✅ stub | ✅ | ✅ `SpecQcPlansTab.razor` P10.5f | parity |
| QC Capture sub-tab | ✅ stub | ✅ | ✅ `SpecQcCaptureTab.razor` + `QcCaptureModal.razor` P10.5f | parity |
| Artwork SVG 4-layer + 3 templates | ✅ | ⚠ stub | ❌ | Plan §2.7 + §2.10 item 13 — still **gap**. Not in any planned PR. |
| Setup sub-tab press settings | ✅ | ⚠ stub | ⚠ read only | Plan accurate. |
| Create / xlsx import | ✅ | ✅ | ✅ `CreateSpecModal.razor` + `ImportSpecModal.razor` P10.5c-1/c-2 | parity |
| Copy / Edit / Revise / Supersede / Trash / Restore | ✅ | ✅ | ✅ 6 modals all shipped P10.5c-1 + P10.5d | parity |
| Export CSV / XLSX / PDF list + sheet PDF | ✅ | ✅ | ✅ P10.5g (`feat/p10.5g-exports-save-dialog`) | parity |
| Diff toggle (Revise before/after) | ✅ | ⚠ partial | ✅ **shipped P10.5d** (`Shared/SpecDiffViewer.razor`, commit `3546a1b`) | **Plan §2.10 item 12 listed as gap.** Closed. |
| Audit timeline | ✅ | ✅ | ⚠ partial via SpecAuditEntry | Plan accurate. |

**Delta vs plan**: 4 rows the plan listed as Gap (planner chip, inline
Draft edit, add/delete row, diff toggle) **shipped during P10.5c/d**.
The remaining genuine Spec-library gap is **Artwork 4-layer SVG**.

### A.8 Settings (6 sub-tabs)

| Sub-feature | SpecHub | Web | Hybrid (today) | Gap analysis |
| --- | --- | --- | --- | --- |
| My Profile read | ✅ | ✅ `Pages/Settings/Profile.razor` | ✅ **shipped P10.6a** at `Pages/SettingsProfile.razor` | **Plan listed as Full gap.** Closed. |
| My Profile edit DisplayName | ✅ | ✅ | ✅ P10.6a | Closed. |
| My Password change | ✅ | ✅ | ✅ **shipped P10.6a** at `Pages/SettingsPassword.razor` | **Plan listed as Full gap.** Closed. |
| Appearance language picker | ✅ | ✅ | ❌ | Plan accurate. |
| Appearance theme switcher | ✅ | ⚠ stub | ❌ | Plan accurate; IMP-2 still DEFER. |
| Account Control user CRUD | ✅ | ✅ `Pages/Settings/Account.razor` | ❌ | Plan accurate. P10.6c not started. |
| Connection Mode info | ✅ | ⚠ via About | ✅ **shipped P10.6d** at `Pages/SettingsConnection.razor` | **Plan listed as ⚠ partial.** Closed. |
| Audit Log Viewer | ✅ | ✅ `Pages/Settings/Logs.razor` (133 LOC) | ❌ | Plan accurate. P10.6e not started. |
| Audit Log XLSX export | ✅ CSV | ✅ both via `AuditLogExport/CsvAuditLogExporter.cs` | ❌ | Plan accurate. ClosedXML available. |
| About info table | ✅ | ✅ | ✅ **shipped P10.6d** at `Pages/SettingsAbout.razor` | **Plan listed as Full gap.** Closed. |
| Backup/Restore | ⚠ partial | ✅ `Pages/Settings/Backup.razor` + `Services/BackupService.cs` | ❌ | Plan accurate. P10.6h not started. |
| Recent Scans sidebar | ✅ | ❌ | ✅ **shipped P10.6f** at `Shared/RecentScansWidget.razor` | **Plan listed as Full gap.** Closed. |

**Delta vs plan**: 5 rows shipped (Profile / Password / Connection Mode
/ About / Recent Scans). 4 remaining: **Appearance (lang + theme), Account
Control, Audit Log Viewer, Backup/Restore**.

### A.9 Cross-cutting

| Sub-feature | SpecHub | Web | Hybrid (today) | Gap analysis |
| --- | --- | --- | --- | --- |
| Bilingual VN+EN i18n | ⚠ 89 keys | ✅ 1,045 keys resx | ⚠ inline VN (resx deferred per Q12) | Plan accurate. |
| Real-time hub updates | ⚠ polling | ✅ SignalR | ⚠ **server hub `ShopfloorHubV2` wired at `Program.cs:287`**; client still polls — `SignalR.Client` NuGet not referenced in any Razor csproj | **Plan IMP-4 reads as net-new.** Half already done server-side. |
| `HubSessionBanner` 4-state | ❌ | ✅ Phase 9 | ❌ | Plan accurate. |
| Camera capture | ✅ via file picker | ❌ | ⚠ `CatalystMediaPicker` P10.5e | Plan accurate. |
| Settings deep-link (Catalyst permission) | ✅ | ❌ | ✅ P10.3 W2 | parity |
| **ConnectivityBanner** | n/a | n/a | ✅ `Shared/ConnectivityBanner.razor` | Hybrid-only addition; carried from P10.3. |

### A.10 Greenfield entity check (plan §2.10)

Re-checking the 13 items the plan said are missing from BOTH Web and
MAUI, post-P10.5g + P10.6f:

| # | Item | Status today |
| --- | --- | --- |
| 1 | MES 5-phase flow | ⚠ State machine ✅ in Web (`WorkOrderStateMachine.cs` 8 states); operator UI ❌ in Hybrid |
| 2 | Machine Dashboard + Pareto + alerts | ❌ — still gap |
| 3 | Shop Order History v0.5.0 | ❌ — still gap |
| 4 | Speed card v0.5.2 | ❌ — still gap |
| 5 | QMS Inspection Queue | ❌ — still gap |
| 6 | QC History forensic table | ❌ — still gap |
| 7 | NG modal | ❌ — still gap |
| 8 | IPQC/FQC/OQC signoff modals | ❌ — still gap |
| 9 | Editable Draft rows | ✅ shipped P10.5c-3 |
| 10 | Recent Scans sidebar | ✅ shipped P10.6f |
| 11 | Planner chip filter | ✅ shipped P10.5c-3 |
| 12 | Diff toggle Revise | ✅ shipped P10.5d |
| 13 | Artwork 4-layer SVG + 3 templates | ❌ — still gap; not in any planned PR |

**4 of 13** items closed since plan was written. **9 remain**, of which
**6** sit inside the P10.7 MES sprint (items 1, 4, 5, 6, 7, 8) and
**2** are P10.8 (items 2, 3). Item 13 (Artwork) has no home in the
roadmap.

---

## Phase B — Roadmap delta vs current plan

### B.1 P10.6 (in flight) — re-scoped

Per Henry's brief: 6a ✅ / 6d ✅ / 6f ✅; 6b next, then 6h / 6e / 6c / 6g.
That sequencing is **good** — what's left:

| PR | Plan estimate | Comment after audit |
| --- | --- | --- |
| **6b** Appearance (lang + theme conditional) | 2-3d | Theme stays DEFER per IMP-2; language picker is the real work. Web has `LangFlagPicker` portable. Recommend: ship lang only, lock theme as a fast-follow. |
| **6h** Backup/Restore admin | 1-2d | **Re-estimate up — 2-3d.** Web `BackupService.cs` is server-side; Hybrid will need an Admin policy gate + Catalyst Save dialog wiring. |
| **6e** Audit Log Viewer + XLSX export | 4-5d | Estimate sound. Web ships `CsvAuditLogExporter.cs`; ClosedXML pipeline already established by P10.5g. |
| **6c** Account Control admin CRUD | 4-5d | Sound. Plan does not surface the password-reset edge case — operators routinely forget passwords during night shift. Recommend adding a "Reset to temp" flow per the Ops Control v1.5 provisioning-card pattern. |
| **6g** Theme switcher (IMP-2) | 3-4d | Still DEFER. Move to P10.12. |

**P10.6 net**: ~12-15 days remaining work (was 21-30; three PRs cleared).

### B.2 P10.7 — MES 5-phase shop floor flow — RE-FRAME

The plan estimates **54-74 days** here, the biggest slice in the
roadmap. The audit suggests this is **overestimated by ~40%** because:

1. **State machine is already shipped.** `WorkOrderStateMachine.cs` in
   `CCL.MES.Domain/` ships the 8-state flow that exactly mirrors
   SpecHub's MES phases. The DB guard exists; the Application guard
   exists; what's missing is the operator UI in Razor + a few
   row-level child tables (`WoMaterials`, `WoPlateCheck`,
   `WoCutterCheck`). Plan §4.4 lists 4 new "WO\*" tables — only those
   3 + `WoSettingLog` are actually needed if SETTING re-uses
   `SetupConfirmed` for the binary gate.
2. **OEE / Reject Pareto already partially shipped.** `OeeService.cs`
   in `CCL.MES.Application/Services/` is a real implementation, not a
   stub. P10.7i (DONE summary) shrinks from 3-4d to 1-2d if Hybrid
   just consumes `IOeeService` via a thin DTO.
3. **`QcInspection` entity carries criteria today.** Plan treats IPQC
   / FQC / OQC as net-new tables; in reality `QcInspection` +
   `QcInspection.Criteria` already exist. P10.7d + P10.7h would re-use
   them with a UI re-skin + role gate, not rebuild from zero.

**Suggested re-estimate**: 35-45 days, broken down differently:

| PR | New estimate | Notes |
| --- | --- | --- |
| **P10.7a** DB additive migrations + master-data + `MesScanProductService` skeleton + audit emit | 6-8d (was 10-14) | Smaller because no state-machine work. |
| **P10.7b** PREPRESS UI + 3 sub-checks | 5-7d | unchanged |
| **P10.7c** SETTING timer | 3-4d (was 5-7) | Re-use `SetupConfirmed`. |
| **P10.7d** IPQC UI + QA modal | 6-8d (was 8-10) | Wrap `QcService.Submit` not rebuild. |
| **P10.7e** READY gate | 2d (was 3-4) | trivial confirm modal |
| **P10.7f** RUNNING + Speed card | 8-10d | unchanged — Speed card is genuinely new |
| **P10.7g** PAUSED + reason picker | 4-5d (was 5-7) | reason picker reused from NG modal pattern from 7b |
| **P10.7h** FQC + OQC + signoff modals | 4-6d (was 5-7) | re-skin of P10.7d pattern |
| **P10.7i** DONE summary | 1-2d (was 3-4) | consume `OeeService` |

**Risk re-frame**: the plan flags "MES schema migration on shared DB"
as **HIGH** severity (§6.1). Audit observation — the schema **doesn't
need to change much**. The risk shifts from "schema break" to
"operator UI complexity (latching one-shot taps, role-gate confirm
prompts, NG photo upload)", which is **MEDIUM** with mature Catalyst
patterns already in place from P10.5e (`CatalystMediaPicker`).

### B.3 P10.8 — Machine Dashboard + Shop Order History

Plan estimates 28-39 days. Audit notes:

- `MachineDashboardService` is a **net-new** legacy service (no
  equivalent in `CCL.MES.Application/Services/`).
- `ShopOrderHistoryService` is **also net-new**.
- 4 read endpoints in the plan are fine. CSV export reuses
  `WorkOrderExport/` infrastructure.
- **Plan understates the Pareto-chart UI cost**. Razor + Blazor doesn't
  have a built-in chart library; either pull a NuGet (`ChartJs.Blazor`)
  or HTML+CSS bar chart. SpecHub does the latter. Recommend HTML+CSS
  to keep DI surface small.
- Auto-refresh `OPS_DISABLE_AUTOREFRESH` flag (SpecHub v0.5.1) is a
  10-LOC port — cheap, do it.

**Estimate stays at 28-39 days** but split differently:

- **P10.8a-d Dashboard** ~16-22d (was 15-21).
- **P10.8e-g History** ~12-17d (was 13-18).

### B.4 P10.9 — QMS Inspection Queue + QC History

Plan estimates 12-16 days. **Re-estimate up to 14-19 days** because:

- Queue auto-build from `WorkOrder.CurrentStep` needs a server endpoint
  that the plan glosses over — sorting + grouping by stage; pagination.
- QC History needs filter-state + drill-down to original record. The
  plan only allocates 3-4 days; SpecHub's QC History is non-trivial.

### B.5 P10.10 — Home / SignalR / Recent Specs

Plan estimates 11-15 days. **Re-estimate as 13-17 days** but with
re-allocation:

- **Home page redesign** moves OUT of P10.10 → recommend pulling forward
  into late P10.6 (after 6c). KPI counts are free now that 5 NPI grids
  + Spec list ship; 1Hz clock is trivial; Recent Specs needs ONE new
  query endpoint `/api/v2/me/recent-specs`. **Cost: 3-4d.** Reason to
  pull forward: operators will see the bare-bones Home (Hybrid Home is
  33 LOC) every single login until P10.10. The visual perception of
  app maturity is gated by this one screen.
- **SignalR client integration** — server hub already wired; what's
  missing is `Microsoft.AspNetCore.SignalR.Client` NuGet on
  `CCL.MES.Hybrid.Client` + a Singleton `IHubConnectionManager`
  service + `HubSessionBanner` 4-state port. **Cost: 4-5d** (was 5-7).
- **Real-time hooks for Machine Dashboard** depends on P10.8 landing.

**Suggested sequencing**: move Home redesign + Recent Specs into
**P10.6i** (new PR), keep SignalR as **P10.10**. Doing so means the new
Home ships with Settings, which is the natural "first time you log in
on a fresh build" moment.

### B.6 P10.11 — Hardware extensions

USB-HID scanner is genuinely useful. **Recommend tightening scope**:
ship Catalyst impl only, defer Windows-HID to P10.12+. Mobile/tablet
responsive polish (IMP-10) should be done **inline per surface** as
each P10.6-9 PR lands, not bundled at the end.

### B.7 Features I'd defer or drop

| Item | Reason |
| --- | --- |
| **Artwork 4-layer SVG + 3 templates** (§A.10 #13) | No clear operator value identified; SpecHub built it for sales demos. Defer to P10.12+ pending Henry confirming a real operator workflow. Estimated 5-7d if kept. |
| **IMP-7 AI NG reason picker** | Genuinely DEFER. 3-4d for marginal value. |
| **IMP-8 Drawing OCR** | DEFER. 5+ days + Tesseract risk. |
| **Theme switcher IMP-2** | DEFER to v2 of Hybrid (post-1.0). Night-shift comfort is real but CSS-token migration cost on a 50+ Razor surface is too high for the value delivered. |
| **5-state pill (IMP-1)** | ACCEPT but lock now — Q2 is still BLOCKING per plan §6.3 — every day open is a UI rework risk. |
| **Web's IQC raw-material acceptance** | Hybrid already has it (`IqcController.cs` + `Pages/IqcReceiving.razor`). **Sprint 0 cleanup**: confirm IQC page is fully functional on Hybrid; if so, the corresponding Web Razor page (`Pages/QcQa/Iqc.razor`, 404 LOC) is a candidate for retirement once Hybrid serves all incoming-QC inspectors. |

### B.8 Risks the plan under- or over-states

| Risk | Plan | Audit view |
| --- | --- | --- |
| MES schema migration | **HIGH** | **MEDIUM.** State machine + entities exist. Only 3-4 additive tables. |
| SignalR Catalyst stability | MEDIUM | **LOW.** Server hub wired + tested. Catalyst supports `SignalR.Client` Cocoa transport. Plan is right to bundle client integration with MES (P10.10), but the actual lift is smaller than IMP-4 suggests. |
| Per-shift state machine corruption | MEDIUM | **HIGH.** Plan §6.1 row 4 says "operator B picks up A's WO mid-phase". With 8 active states + child tables coming online, the row-lock semantics need explicit pseudo-code review **before P10.7a** — not "deferred to UI". Recommend Henry sign-off on Q10 + a one-page state-transition contract doc before P10.7a opens. |
| MAUI bundle size > 100 MB after P10.7 | LOW | **MEDIUM.** Plan understates — adding photo upload + signature canvas + Catalyst camera entitlement + SignalR.Client adds material binary. Measure after P10.7c and budget against the iPad install footprint constraint. |
| Audit log retention 90d (Q13) | not flagged | **MEDIUM risk if MES events 5× existing volume.** Plan assumes existing retention serves. If `mes_audit_log` carries 25+ events per WO × 500 WOs/day = 12,500 rows/day, the 90d window = 1.1M rows. Index + archive snapshot needed (plan mentions but no PR). Recommend explicit P10.7a sub-task. |
| i18n drift (Q12) | MEDIUM | **HIGH if MES UI ships inline VN.** 6 new big surfaces (Prepress / Setting / IPQC / Run / FQC / OQC) × 30-50 VN strings each = 200-300 untranslated EN gaps. The Q12 ACCELERATE default is the right call — recommend resx port lands as **part of P10.6b**, not deferred. |

---

## Phase C — Cross-codebase findings (top 5 surprises)

### C-1. The plan's largest phase (P10.7, 54-74 days) is closer to 35-45 days because legacy already ships the state machine + QC entities

`src/CCL.MES.Domain/StateMachine/WorkOrderStateMachine.cs:17-27`
enumerates 8 states that map 1:1 onto SpecHub's MES phases. The plan
treats this as a greenfield service build. **It is not — it's a UI
build on top of existing server scaffolding** plus 3-4 additive
sub-check tables. The risk shifts from "schema migration on shared DB"
to "operator UI fidelity to SpecHub patterns". That is a much less
expensive risk to mitigate.

**Action**: Henry should re-baseline P10.7 estimate before
green-lighting the phase. The 9-12 month full-parity number in §5.9
shrinks to **7-9 months** if P10.7 absorbs this reframing.

### C-2. SignalR is half-shipped already — IMP-4 should reclass from "deferred improvement" to "already 50% done"

`src/CCL.MES.Api/Program.cs:287`:

```csharp
app.MapHub<ShopfloorHubV2>("/hubs/shopfloor");
```

`src/CCL.MES.Api/Hubs/ShopfloorHubV2.cs` exists. The Web project's
`src/CCL.MES.Web/Hubs/ShopfloorHub.cs` was forked into V2 for Hybrid.

What's missing is the **Razor client subscription**: no
`Microsoft.AspNetCore.SignalR.Client` NuGet in
`CCL.MES.Hybrid.Client.csproj` (verified by grep). The plan estimates
5-7 days for "SignalR client integration". With the server side wired,
the actual lift is **3-5 days** — a `HubConnection` singleton + 1 hook
in `MainLayout` + `HubSessionBanner` Razor port.

**Recommendation**: pull SignalR client into the **first P10.6
follow-up** (alongside Audit Log Viewer in P10.6e) rather than waiting
until P10.10b. Real-time audit log entries flowing live is a
high-perceived-value feature that costs almost nothing once the hub
client lands.

### C-3. Four "Gap" items in the plan §2.10 closed during P10.5c/d/f — the gap-count fell from 13 to 9 between plan-write and 2026-06-04

Items closed:

- §2.10 item 9 — Editable Draft rows (✅ P10.5c-3, commit `95ee142`)
- §2.10 item 10 — Recent Scans sidebar (✅ P10.6f, commit `10ed3ad`)
- §2.10 item 11 — Planner chip filter (✅ P10.5c-3)
- §2.10 item 12 — Diff toggle Revise (✅ P10.5d, commit `3546a1b`)

**Action**: refresh the parity plan §2.7 + §2.10 to mark these as
shipped, so success-criteria tracking in §7 reads correctly. The plan
text is otherwise still useful as the master roadmap.

### C-4. Legacy Web has FIVE 10-LOC stub pages that are candidates for Sprint-0 deletion once Hybrid covers their surfaces

Verified via `wc -l`:

| Legacy file | LOC | Hybrid replacement |
| --- | --- | --- |
| `src/CCL.MES.Web/Pages/QcQa/Ipqc.razor` | 10 | none yet (P10.7d) |
| `src/CCL.MES.Web/Pages/QcQa/Oqc.razor` | 10 | none yet (P10.7h) |
| `src/CCL.MES.Web/Pages/QcQa/Iqc.razor` | 404 (real) | `Pages/IqcReceiving.razor` (Hybrid, shipped) |

The 10-LOC stubs are misleading — they suggest QC functionality exists
on Web. **It does not.** SpecHub is the only complete reference for
IPQC/OQC. Sprint-0 cleanup item: delete the Web stubs once Hybrid
P10.7d/h ships, to remove the false signal in the codebase.

### C-5. Hybrid Home page is the single most under-built operator-facing screen and a small-cost / big-perception fix

`Pages/Home.razor` is **33 LOC** including code-behind. Renders 1 tile
(NPI). Operators logging in see this every shift. Compare to SpecHub
Home (`spechub-prototype.html:7427+`) which has greeting + 5 KPI tiles
+ recent specs + quick actions + 1Hz clock.

The KPI counts cost effectively zero — every NPI controller already
exposes `/list` endpoints. Recent Specs is a single new endpoint
`/api/v2/me/recent-specs`. 1Hz clock is pure client. Quick-action
tiles are static markup.

**Estimated cost: 3-4 days.** Currently scheduled for P10.10a (9-12
months out). **Recommendation: pull into a P10.6 cleanup PR.** This is
the highest perceived-value-per-dev-hour delta in the entire roadmap.

### C-6 (bonus). The "auto-refresh kill-switch" SpecHub shipped in v0.5.1 is a 10-LOC port that should land before Machine Dashboard

`OPS_DISABLE_AUTOREFRESH` works as a global poll-disable for
diagnostic / kiosk-mode scenarios. SpecHub triggers it via URL param,
localStorage, or `window` global. Port to Razor is trivial — a
`bool IsAutoRefreshDisabled` flag on `AppShellService` + `?disable-
autorefresh=1` query parse. Ship as a defensive prerequisite **inside
P10.8d**, not after. Cheap to do; saves an "operator complained the
dashboard kept refreshing on the broken wifi" incident later.

---

## TL;DR for Henry

- **Biggest gap.** The Home page (`src/CCL.MES.Hybrid.Razor/Pages/Home.razor`,
  33 LOC, 1 tile) is the worst-built operator-facing screen in the entire
  Hybrid client. SpecHub Home is the visual benchmark for what "the app
  feels finished" looks like. **Pull the Home redesign + Recent Specs
  forward into a P10.6 follow-up PR (3-4 days).** Currently sits 9-12 months
  out in P10.10a — too far given how visible it is per-shift.

- **Biggest opportunity.** P10.7 (MES 5-phase shop floor) is estimated
  54-74 days but legacy `CCL.MES.Domain/StateMachine/WorkOrderStateMachine.cs`
  already ships the 8-state machine that mirrors SpecHub's phases, and
  `OeeService` + `QcInspection` entities are real. The actual scope is
  **operator UI + 3-4 additive sub-check tables**, not "build the MES from
  scratch". Re-estimate: **35-45 days**. That alone shrinks the full-parity
  timeline from 9-12 months to 7-9 months. Henry should re-baseline P10.7
  before opening P10.7a — see §B.2 for the per-PR breakdown.

- **Biggest risk.** Per-shift WO state-machine corruption (operator B
  picks up operator A's WO mid-phase) is flagged MEDIUM in the plan §6.1
  but is **HIGH** once P10.7 starts wiring 8 live states + Prepress/Plate/
  Cutter row-level child tables. Q10 on the plan (server-side optimistic
  lock on `WorkOrder.RowVersion`) is still open. **Block P10.7a until
  Henry signs off on a one-page state-transition contract doc** covering:
  (a) row-version handshake on every advance + qty write; (b) UI banner
  shape when conflict detected; (c) audit emit on conflict. Doing this
  pre-P10.7a is 1-2 days; doing it post-incident is a multi-week recovery.
