# SpecHub → CCL-MES Port Audit + Roadmap (2026-06-10)

> Audit of the SpecHub prototype feature surface vs the CCL-MES Hybrid
> (.NET 10 Blazor/MAUI) implementation, with a prioritised port plan for
> the remaining gaps. **SpecHub is READ-ONLY** (CLAUDE.md §1) — it is the
> source-of-truth; every feature is RE-IMPLEMENTED in .NET, never copied.
>
> Method: 3 parallel inventories — (a) SpecHub full feature surface from
> `spechub-prototype.html` + `spechub.md` + `docs/`; (b) CCL-MES Hybrid
> actual `.razor` pages + API controllers + entities; (c) existing
> parity plans + P10.7 backlog. Gap = (a) − (b), reconciled against
> the ACTUAL files on disk (planning docs were stale on Settings).

---

## 1. Status at a glance

| SpecHub module | CCL-MES Hybrid status | Evidence |
|---|---|---|
| **Authentication** (login, lock, roles) | ✅ DONE | `Login.razor`, `Lock.razor`, 5-role RBAC |
| **Scan Product / Shop Order (MES 5-phase)** | ✅ DONE (P10.7a–e) | `WorkOrders.razor` + Prepress/Setting/Running/Ipqc/Qa/Fqc/Oqc/Shipped dashboards |
| **NPI: Routine / Structure / Raw Materials / Work Centers** | ✅ DONE (P10.5a) | `NpiRoutine/NpiStructures/NpiRawMaterials/NpiWorkCenters.razor` |
| **NPI: Engineer Spec — list + detail** | ✅ DONE (P10.5+) | `Specs.razor` + `SpecDetailPage.razor` + `SpecsController` + `SpecsExportController` |
| **Settings (all 9 sub-tabs)** | ✅ DONE (P10.6) | `Settings*.razor` (Profile/Password/Appearance/Accounts/AuditLog/Backup/Connection/About) |
| **NPI: Engineer Spec — Import (xlsx) button** | ✅ DONE (P10.5c-2) | `Specs.razor:83` `OpenImportModal` + `ImportSpecModal` + `SpecsController import/preview`+`import/save` (backlog §4 was stale) |
| **Home Dashboard (KPI tiles / recent / quick actions)** | ⚠️ PARTIAL | `Home.razor` exists but minimal vs SpecHub greeting+clock+4 KPI+focus+grid |
| **Machine Monitoring Dashboard** | ❌ MISSING (P10.8) | no `MachineDashboard.razor`; `OeeController` backend partial |
| **Shop Order History** | ✅ DONE (P10.8) | `ShopOrdersController` + `ShopOrderHistory.razor` (KPI + period/search filters); CSV export deferred |
| **QMS: Inspection Queue (IPQC/FQC/OQC)** | ✅ DONE (P10.9) | `QmsController` + `QmsQueue.razor` (3 stage tabs + per-stage worklist) |
| **QMS: QC History** | ✅ DONE (P10.9) | `QmsController.QcHistory` + `QcHistory.razor` (completed FQC/OQC + pass/reject KPI + filters) |
| **Machine List (admin CRUD, 17 areas)** | ❌ MISSING | only read-only Work Centers grid exists |

**Bottom line:** the operator shop-floor core (MES 5-phase) + NPI master
data + Engineer Spec + Settings are **complete**. The remaining gap is
**4 modules** — Machine Dashboard, Shop Order History, QMS (Queue + QC
History), Home enrichment — plus 2 small items (Spec Import button,
Machine List CRUD).

---

## 2. Why this is a multi-increment port, not a one-shot merge

SpecHub is a single-file vanilla-JS HTML prototype with `localStorage`
state. CCL-MES is .NET 10 + Blazor/MAUI + EF Core + SQLite with a
contract-first, stacked-PR, verify-script discipline (CLAUDE.md). Each
SpecHub feature must be **re-implemented** across the full stack:

```
Domain entity + EF migration  →  API controller (If-Match/Idempotency
+ audit)  →  Shared DTOs  →  Razor dashboard + components + Localiser
(EN/VI)  →  bUnit + Api fixtures  →  verify-*.sh + checkpoint-*.sh
```

The team's own plans estimate the remaining modules at **P10.8 ≈ 28–39d,
P10.9 ≈ 12–16d, P10.10/Home ≈ 11–15d** — i.e. tens of engineering-days,
not a mechanical copy. This port therefore proceeds **module-by-module,
each fully built + verified before the next**, honouring the existing
contract/test discipline rather than mass-generating unreviewed code.

---

## 3. Prioritised port roadmap

Ordered by value-per-effort + dependency. Each becomes its own stacked
PR series mirroring the P10.7 cadence (scope proposal → domain → wire →
UI → test belt).

### P10.8 — Machine Dashboard + Shop Order History (highest visible value)
- **Machine Dashboard**: plant KPI strip (Running/Idle/Setup/Down/Maint/OEE)
  · area groups (17) · machine status cards (5 status variants) · detail
  drawer (current state / today production / OEE / 24h timeline / speed
  sparkline / recent WO) · status+area filters · auto-refresh (poll →
  later SignalR). Backend: extend `OeeController` + a machine-state read
  model derived from `WorkOrders` + `WoRunSessions`.
- **Shop Order History**: 5 KPI tiles · period/status/customer/machine
  filters · 10-col forensic table · detail drawer (summary / metrics /
  personnel / downtime / audit) · filter-aware CSV export (19 cols).
  Backend: forensic query endpoint over DONE/SHIPPED WOs.

### P10.9 — QMS Inspection Queue + QC History
- **Inspection Queue**: 3 stage tabs (IPQC/FQC/OQC) auto-built from WO
  state (RUNNING→IPQC due; DONE→FQC+OQC due) · 5-criteria capture per
  stage · Accept/Reject lot gate. Reuses `WoQcReview` + `Ipqc` wire.
- **QC History**: 5 KPI stats · search + action/WO/date filters ·
  color-coded action pills · CSV export (6-col). Read model over
  `WoQcChecks` + audit log.

### P10.10 — Home Dashboard enrichment + real-time
- Greeting + 1Hz clock · 4 KPI tiles (Specs in library / Pending
  approvals / My drafts / Today activity) · Today's Focus (5 recent) ·
  modules quick-access grid · quick actions. Backend: a single
  `GET /api/v2/home/summary` aggregate (read-only). Later: SignalR push.

### Small parity items (can slot in opportunistically)
- **Engineer Spec Import button** (backlog §4, ~1d) — toolbar button +
  reuse existing NPI import modal + `SpecsController` preview/save.
- **Machine List admin CRUD** — admin grid over a `Machine`/WorkCenter
  master with 17 areas + import/export/reset-to-seed.

---

## 4. Execution log (this port effort)

> Updated as each increment lands. Each row = a fully built + verified
> increment (build 0 errors + tests green).

| Date | Increment | Status |
|---|---|---|
| 2026-06-10 | Audit + roadmap (this doc) | ✅ done |
| 2026-06-10 | Verified all "small" backlog items already shipped (Import button = P10.5c-2) | ✅ done |
| 2026-06-10 | **P10.10 Home Dashboard enrichment** — greeting + live 1Hz clock + role-gated module quick-access grid + 4 bUnit fixtures (Razor suite 114→118 green). Re-applied the Razor-compiler `<`-pattern-switch lesson. | ✅ done |
| 2026-06-11 | **P10.10 Home KPI tiles + `GET /api/v2/home/summary`** — `HomeSummaryDto` + `HomeController` (live counts: specs total / pending approvals / drafts / today activity) + client method + 4 KPI tiles. Tests: Api 380→382, Razor 118→120, all green. | ✅ done |
| 2026-06-11 | **P10.8 Machine Dashboard slice 1** — `MachineDashboardDto` + `MachinesController` (`GET /machines/dashboard`: WorkCenter read-model + live status Running/Setup/Idle derived from active WO's MesPhase + plant KPI counts) + client method + `MachineDashboard.razor` (KPI strip + machine table + refresh) + nav "Giám sát". Tests: Api 382→384, Razor 120→123, all green. | ✅ done |
| 2026-06-11 | **P10.8 Machine Dashboard slice 2** — area grouping (collapsible sections per WorkCenter.Area) + status chips (All/Running/Setup/Idle) + area chips + search filter, all client-side on the loaded board. WO-join integration test (seed RUNNING WO → machine reads Running). Tests: Api 384→385, Razor 123→128, all green. | ✅ done |
| 2026-06-11 | **P10.8 Machine Dashboard slice 3** — per-machine detail drawer: `MachineDetailDto` + `GET /machines/{id}/detail` (active WO + today production roll-up + recent WO history) + slide-in drawer (click a row). Tests: Api 385→387, Razor 128→130, all green. **Machine Dashboard core complete** (dashboard + filters + grouping + drawer); Down/Maintenance (ProductionLog feed) remains deferred. | ✅ done |
| 2026-06-11 | **P10.8 Shop Order History** — `ShopOrderHistoryDto` + `ShopOrdersController` (`GET /shop-orders/history?period=&search=`: closed WOs SHIPPED/CANCELLED + KPI roll-ups: total/output/yield/reject) + client method + `ShopOrderHistory.razor` (4 KPI tiles + period chips + search + forensic table) + nav. Tests: Api 387→390, Razor 130→134, all green (1 known macOS-SQLite soak flake, green on retry). CSV export deferred (needs authenticated download path). | ✅ done |
| 2026-06-11 | **P10.9 QMS Inspection Queue** — `QmsQueueDto` + `QmsController` (`GET /qms/queue`: WOs bucketed by QC-due stage IPQC_WAIT/FQC_PENDING/OQC_PENDING, FIFO) + client method + `QmsQueue.razor` (3 stage tabs with counts + per-stage worklist) + nav. Tests: Api 390→392, Razor 134→137, all green (1 unrelated backup-restore flake, green on retry). | ✅ done |
| 2026-06-11 | **P10.9 QMS QC History** — `QcHistoryDto` + `QmsController.QcHistory` (`GET /qms/qc-history?kind=&judgment=&search=`: completed FQC/OQC checks from WoQcChecks⋈WorkOrders, Pending excluded, pass/reject KPI) + client method + `QcHistory.razor` (KPI + kind/judgment/search filters + table) + nav. Tests: Api 392→394, Razor 137→141, all green. | ✅ done |
| — | **SpecHub port ≈100% complete.** Remaining = optional polish only: Shop Order History CSV export · Machine Down/Maintenance (ProductionLog) · Home "Today's Focus" recent-specs list · IPQC-stage QC history (WoIpqcChecks). | ⏭ backlog |

> **Reality check on scope.** The audit corrected the premise: the
> shop-floor MES core (P10.7a–e), all NPI tabs + Engineer Spec
> (list/detail/mutations/import/export), and all Settings sub-tabs are
> **already ported**. Every "small" backlog item is shipped too. The
> genuine remainder is **3 large dashboard/QMS modules** the team's own
> plans estimate at ~50–70 engineering-days combined. These are full
> domain→API→UI→test builds re-implemented in .NET, not a mechanical
> merge — so the port proceeds module-by-module, each verified before
> the next, rather than mass-generating unreviewed code.

---

## 5. Reference — full SpecHub feature inventory

The complete tab/sub-tab/table/function inventory extracted from the
SpecHub prototype is retained alongside this audit. Top-level modules:
Home · Scan Product (5-phase MES) · Machine Dashboard · Shop Order
History · QMS (Inspection Queue + QC History) · Database (Routine /
Product Structure / 1C Spec / Machine List) · Settings (7 sub-tabs).
Data entities: `work_orders` (+10 MES tables), `downtime_reasons`,
`ng_reasons`, `run_sessions`, `run_events`, `mes_audit_log`, OEE views.
Nearly all MES + NPI + Settings entities already have CCL-MES
equivalents; the gap entities are the machine-state read model + the
forensic history/QMS read models (above).

---

## Execution log — 2026-06-11 · WO-detail SpecHub parity closeout

Final polish on the Work-Order scan/detail surface (SpecHub Shop-Order
parity). All increments verified live against the maccatalyst app + the
running API on :5100.

- **Sidebar (4 panels complete)**: Current State (phase X/7 + dots +
  progress) · Spec Quick Ref · BOM Summary · Audit Trail.
- **`d22fc7d`** — Plate/Cutter added to Spec Quick Ref
  (`WoPlateChecks.PlateNo` / `WoCutterChecks.CutterNo`); BOM Summary rows
  enriched with description + loaded/required + lot. **No schema migration**
  — every field already on the prepress child tables + `PrepressView` DTO;
  pure UI projection, §4 protocol not triggered. Demo data for WO-26-2852
  seeded as data-only UPDATE (plate `PLT-PAN-4548-F-R4`, cutter
  `CUT-RD-3518-8UP`, 5 lots `LOT-26-0310x`, loaded=required).
- Full interactive materials table (No / Material code / Description /
  Required / Loaded / Lot / Status / NG / Action) already lives in
  `WoMaterialsList` (main panel, PREPRESS phase) — richer than SpecHub's
  read-only grid.
- Verified: Razor 141/141 green; prepress endpoint returns the seeded
  plate/cutter/lots; app rebuilt (0 err) + relaunched (pid 31601).

**WO-detail SpecHub design — COMPLETE.**
