# CCL-MES Permission Matrix

**Status**: live audit at HEAD = `b1e320e` (post PR-L3 merge, 2026-06-02).
This document is **descriptive** — it mirrors what the code actually does.
Do NOT edit policy here; change the source then refresh the matrix.

Scope: every RBAC decision visible in the codebase (Program.cs policies +
Razor `@attribute [Authorize]` + `<AuthorizeView>` + controller
`[Authorize]` + server-side role/department checks inside services).
Anomalies that may warrant a fix are listed at the bottom under
[Known tensions / mismatches](#known-tensions--mismatches) — NONE were
modified by this audit.

---

## 1. Role + Department glossary

### Roles

Source: [src/CCL.MES.Domain/Auth/UserRole.cs:11-21](../src/CCL.MES.Domain/Auth/UserRole.cs#L11-L21).

| Role         | String literal | Notes                                                  |
| ------------ | -------------- | ------------------------------------------------------ |
| `Admin`      | `"Admin"`      | God mode                                               |
| `Supervisor` | `"Supervisor"` | Oversight; can act as Production chip on drawings      |
| `Engineer`   | `"Engineer"`   | NPI + WI writer; department-scoped for drawings        |
| `QC`         | `"QC"`         | Quality gates (IQC / IPQC / FQC / OQC)                 |
| `Operator`   | `"Operator"`   | Run actions on WO                                      |

### Department (free-form string on `User.Department`)

Source: [src/CCL.MES.Domain/Entities/User.cs:55](../src/CCL.MES.Domain/Entities/User.cs#L55).

Used only for **drawing approval chips** (PR-D-5c). Recognised values:
`npi` · `production` · `qc` · anything else / null → cannot act as any chip.

---

## 2. Page-level policies (Program.cs)

Source: [src/CCL.MES.Web/Program.cs:177-199](../src/CCL.MES.Web/Program.cs#L177-L199).

| Policy             | Roles allowed                                       | Used by                                                                                                                                                                                                                                                                                                          |
| ------------------ | --------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **FallbackPolicy** | any authenticated user                              | every page + controller that does NOT specify `[Authorize(Policy=…)]` or `[Authorize(Roles=…)]`                                                                                                                                                                                                                  |
| `AdminOnly`        | Admin                                               | `/settings/account` · `/settings/backup` · `/settings/syslog` (via `@attribute [Authorize(Policy="AdminOnly")]`)                                                                                                                                                                                                 |
| `NpiRead`          | Admin · Supervisor · Engineer · **QC**              | `/npi/engineer-routine` · `/npi/engineer-structure` · `/npi/workcenter` · `/npi/raw-materials`                                                                                                                                                                                                                   |
| `NpiSpecRead`      | Admin · Supervisor · Engineer  *(QC excluded — see [tension §6.1](#61-qc-role-excluded-from-npispecread-but-spec-qc-capture-semantically-belongs-to-qc))* | `/npi/engineer-spec` · `/npi/engineer-spec/{id}` (per-rev detail page hosting Spec / Drawings / QC Plans / QC Capture / Artwork / Setup tabs)                                                                                                                                                                     |
| `QcRead`           | Admin · Supervisor · QC                             | `/qcqa/iqc` · `/qcqa/ipqc` · `/qcqa/oqc`                                                                                                                                                                                                                                                                         |

---

## 3. Sidebar visibility

Source: [src/CCL.MES.Web/Shared/MainLayout.razor:8-93](../src/CCL.MES.Web/Shared/MainLayout.razor#L8-L93).

Anything outside an `<AuthorizeView>` is visible to **any authenticated user**.

| Section / item                                | Gate                                                  |
| --------------------------------------------- | ----------------------------------------------------- |
| Home · Dashboard · Work Orders · Work Instructions | any authenticated                                     |
| **NPI** group                                 | `Admin,Supervisor,Engineer,QC` (`MainLayout.razor:16`) |
| ↳ NPI Spec link                               | `Admin,Supervisor,Engineer` (`MainLayout.razor:22`)    |
| ↳ NPI Routine · Structure · Raw Materials · Work Center | inherits NPI group                                 |
| **QC/QA** group (IQC · IPQC · OQC)            | `Admin,Supervisor,QC` (`MainLayout.razor:34`)         |
| Settings (Profile · MyPwd · Appearance · Hardware · Mode · About) | any authenticated                                     |
| Settings → Account Control                    | `Admin` (`MainLayout.razor:64`)                       |
| Settings → Data Backup · Syslog               | `Admin` (`MainLayout.razor:68`)                       |
| Swagger                                       | any authenticated                                     |

---

## 4. Feature × Role matrix

`✓` = allowed. `✗` = blocked. `R` = read-only (view but cannot mutate).
`(dept)` = additionally gated on `User.Department`. Cells reflect the
deepest enforcement layer (server > page > UI).

### 4.1 Work Orders (`/workorders`)

Per-action button gating in [Components/WorkOrderDrawer.razor:230-265](../src/CCL.MES.Web/Components/WorkOrderDrawer.razor#L230-L265).
Server controller [WorkOrdersController.cs](../src/CCL.MES.Web/Controllers/WorkOrdersController.cs) has **NO** `[Authorize]` → inherits FallbackPolicy (any authenticated). See [tension §6.2](#62-workorderscontroller-inherits-fallbackpolicy-but-ui-gates-roles).

| Action                          | Admin | Supervisor | Engineer | QC | Operator | Source                                                                                              |
| ------------------------------- | :---: | :--------: | :------: | :-: | :------: | --------------------------------------------------------------------------------------------------- |
| View `/workorders` list + drawer | ✓     | ✓          | ✓        | ✓  | ✓        | FallbackPolicy                                                                                      |
| Advance step                    | ✓     | ✓          | ✗        | ✗  | ✗        | `<AuthorizeView Roles="Admin,Supervisor">` ([WorkOrderDrawer.razor:230](../src/CCL.MES.Web/Components/WorkOrderDrawer.razor#L230))            |
| Unlock step (Flags update)      | ✓     | ✓          | ✗        | ✗  | ✗        | Same `advanceCtx` group                                                                             |
| QC IPQC/FQC/OQC Pass            | ✓     | ✓          | ✗        | ✓  | ✗        | `<AuthorizeView Roles="Admin,Supervisor,QC">` ([WorkOrderDrawer.razor:240](../src/CCL.MES.Web/Components/WorkOrderDrawer.razor#L240))         |
| Start / Pause / Resume / Finish | ✓     | ✓          | ✗        | ✗  | ✓        | `<AuthorizeView Roles="Admin,Supervisor,Operator">` ([WorkOrderDrawer.razor:248](../src/CCL.MES.Web/Components/WorkOrderDrawer.razor#L248))   |
| Demo create (POST `/api/workorders/demo/{tpl}`) | ✓ | ✓     | ✗        | ✗  | ✗        | `[Authorize(Roles="Admin,Supervisor")]` ([DemoWorkOrdersController.cs:46](../src/CCL.MES.Web/Controllers/DemoWorkOrdersController.cs#L46)) + UI `<AuthorizeView Roles="Admin,Supervisor">` ([WorkOrders.razor:180](../src/CCL.MES.Web/Pages/WorkOrders.razor#L180))             |
| Export CSV/XLSX                 | ✓     | ✓          | ✓        | ✓  | ✓        | `[Authorize]` any authenticated ([WorkOrdersExportController.cs:33](../src/CCL.MES.Web/Controllers/WorkOrdersExportController.cs#L33))         |

### 4.2 NPI Spec library — list + detail page

Server gates from `SpecsController` ([SpecsController.cs](../src/CCL.MES.Web/Controllers/SpecsController.cs)) + UI gates from `EngineerSpec.razor` / `SpecContextMenu.razor` / `EngineerSpecDetail.razor`.

| Action                                              | Admin | Supervisor | Engineer | QC | Operator | Source                                                                                                                                                                                  |
| --------------------------------------------------- | :---: | :--------: | :------: | :-: | :------: | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| View `/npi/engineer-spec` list                      | ✓     | ✓          | ✓        | ✗  | ✗        | Page policy `NpiSpecRead` ([EngineerSpec.razor:2](../src/CCL.MES.Web/Pages/Npi/EngineerSpec.razor#L2))                                                                                  |
| View `/npi/engineer-spec/{id}` detail               | ✓     | ✓          | ✓        | ✗  | ✗        | Page policy `NpiSpecRead` ([EngineerSpecDetail.razor:2](../src/CCL.MES.Web/Pages/Npi/EngineerSpecDetail.razor#L2))                                                                       |
| Filter chip `[Active / Trash / All]`                | ✓     | ✓          | ✓        | ✗  | ✗        | Inherits page; query param `view=…` ([SpecsController.cs:38-49](../src/CCL.MES.Web/Controllers/SpecsController.cs#L38-L49))                                                              |
| Create spec (POST `/api/specs`)                     | ✓     | ✗          | ✓        | ✗  | ✗        | `[Authorize(Roles="Admin,Engineer")]` ([SpecsController.cs:69](../src/CCL.MES.Web/Controllers/SpecsController.cs#L69)) + UI `<AuthorizeView Roles="Admin,Engineer">` ([EngineerSpec.razor:88](../src/CCL.MES.Web/Pages/Npi/EngineerSpec.razor#L88)) |
| Approve / Promote (POST `/api/specs/revisions/{id}/approve`) | ✓ | ✗ | ✓     | ✗  | ✗        | Same Authorize group; UI button gate `<AuthorizeView Roles="Admin,Engineer">` ([EngineerSpec.razor:132](../src/CCL.MES.Web/Pages/Npi/EngineerSpec.razor#L132))                                                  |
| Copy (PR-L1)                                        | ✓     | ✗          | ✓        | ✗  | ✗        | `[Authorize(Roles="Admin,Engineer")]` ([SpecsController.cs:106](../src/CCL.MES.Web/Controllers/SpecsController.cs#L106)) + UI ctx menu ([SpecContextMenu.razor:37](../src/CCL.MES.Web/Shared/SpecContextMenu.razor#L37)) |
| Edit Draft only (PR-L1)                             | ✓     | ✗          | ✓        | ✗  | ✗        | Same controller + UI; server-side Draft-only gate in `SpecService.UpdateAsync`                                                                                                          |
| Revise → new rev + auto-supersede (PR-L2)           | ✓     | ✗          | ✓        | ✗  | ✗        | `[Authorize(Roles="Admin,Engineer")]` ([SpecsController.cs:149](../src/CCL.MES.Web/Controllers/SpecsController.cs#L149)); UI gate `Status ∈ {Approved, Released}` in ctx menu              |
| Mark Superseded (PR-L2)                             | ✓     | ✗          | ✓        | ✗  | ✗        | Same Authorize group; server validates typed SpecCode confirm                                                                                                                            |
| Trash (PR-L3)                                       | ✓     | ✗          | ✓        | ✗  | ✗        | `[Authorize(Roles="Admin,Engineer")]` ([SpecsController.cs:192](../src/CCL.MES.Web/Controllers/SpecsController.cs#L192)) + server WO-active blocker                                                          |
| Restore (PR-L3)                                     | ✓     | ✗          | ✓        | ✗  | ✗        | `[Authorize(Roles="Admin,Engineer")]` ([SpecsController.cs:229](../src/CCL.MES.Web/Controllers/SpecsController.cs#L229))                                                                |
| Export CSV/XLSX/PDF (PR #31c)                       | ✓     | ✓          | ✓        | ✗  | ✗        | `[Authorize(Roles="Admin,Supervisor,Engineer")]` ([SpecsExportController.cs:42](../src/CCL.MES.Web/Controllers/SpecsExportController.cs#L42))                                                                 |
| Import xlsx (PR #31a) — `SPEC_IMPORT`               | UI-gated `<AuthorizeView Roles="Admin,Engineer">`; no direct controller endpoint (Blazor-server only)                                                                                  |
| Purge (background)                                  | n/a — server-side `SpecTrashPurgeService` runs as `"system"` actor; no user-triggered surface                                                                                            |

### 4.3 Spec detail tabs (inside `/npi/engineer-spec/{id}`)

Page policy `NpiSpecRead` already restricts to Admin/Supervisor/Engineer; below is the per-tab + per-action gate.

| Tab            | Admin | Supervisor | Engineer | QC | Operator | Notes                                                                                                                                  |
| -------------- | :---: | :--------: | :------: | :-: | :------: | -------------------------------------------------------------------------------------------------------------------------------------- |
| Spec           | ✓     | R          | ✓        | ✗  | ✗        | Spec content + Edit modal; Edit gated `<AuthorizeView Roles="Admin,Engineer">` ([EngineerSpecDetail.razor:173](../src/CCL.MES.Web/Pages/Npi/EngineerSpecDetail.razor#L173)) |
| Drawings       | ✓     | R          | ✓ (dept) | ✗  | ✗        | Upload + chip-decide gated; see §4.4                                                                                                   |
| QC Plans       | ✓     | R          | ✓        | ✗  | ✗        | UI `<AuthorizeView Roles="Admin,Engineer">` + server `SpecQcWindowService._editorRoles = {Admin, Engineer}` ([SpecQcWindowService.cs:35-39](../src/CCL.MES.Application/Services/SpecQcWindowService.cs#L35-L39))                                            |
| QC Capture     | ✓     | R          | ✓        | ✗  | ✗        | UI `<AuthorizeView Roles="Admin,Engineer">` + server `SpecQcCaptureService._editorRoles = {Admin, Engineer}` ([SpecQcCaptureService.cs:38-42](../src/CCL.MES.Application/Services/SpecQcCaptureService.cs#L38-L42)). See [tension §6.1](#61-qc-role-excluded-from-npispecread-but-spec-qc-capture-semantically-belongs-to-qc). |
| Artwork        | ✓     | R          | ✓        | ✗  | ✗        | Read-only SVG renderer; no mutation surface                                                                                            |
| Setup          | ✓     | R          | ✓        | ✗  | ✗        | Read-only press / tolerances cards                                                                                                     |

### 4.4 Drawings — upload + 3-role approval chain (PR-D-5b + PR-D-5c)

Source: [src/CCL.MES.Application/Services/DrawingsService.cs:343-369](../src/CCL.MES.Application/Services/DrawingsService.cs#L343-L369) + [DrawingsController.cs:31](../src/CCL.MES.Web/Controllers/DrawingsController.cs#L31).

| Action                                              | Admin | Supervisor              | Engineer  | QC | Operator | Notes                                                                                                                                                                |
| --------------------------------------------------- | :---: | :---------------------: | :-------: | :-: | :------: | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Read drawing list + open viewer                     | ✓     | ✓                       | ✓         | ✗  | ✗        | Inherits page `NpiSpecRead`                                                                                                                                          |
| Upload version (POST `/api/specs/{rev}/drawings/{kind}/upload`) | ✓ | ✓               | ✓         | ✗  | ✗        | `[Authorize(Roles="Admin,Supervisor,Engineer")]` at controller class ([DrawingsController.cs:31](../src/CCL.MES.Web/Controllers/DrawingsController.cs#L31))                                                                                |
| Decide chip — **NPI**                               | ✓     | ✗                       | ✓ (dept=`npi`)        | ✗  | ✗        | `DrawingsService.CanActAs` ([line 358-360](../src/CCL.MES.Application/Services/DrawingsService.cs#L358-L360))                                                        |
| Decide chip — **Production**                        | ✓     | ✓ (any dept)            | ✓ (dept=`production`) | ✗  | ✗        | `DrawingsService.CanActAs` ([line 361-363](../src/CCL.MES.Application/Services/DrawingsService.cs#L361-L363))                                                        |
| Decide chip — **QC**                                | ✓     | ✗                       | ✓ (dept=`qc`)         | ✗  | ✗        | `DrawingsService.CanActAs` ([line 364-366](../src/CCL.MES.Application/Services/DrawingsService.cs#L364-L366)). See [tension §6.1](#61-qc-role-excluded-from-npispecread-but-spec-qc-capture-semantically-belongs-to-qc) — QC role itself cannot reach this page. |

Chip-decision is enforced **only** server-side (`UnauthorizedAccessException` from `CanActAs`). UI hides buttons via `CanActAsChip` helper at [EngineerSpecDetail.razor:1733-1734](../src/CCL.MES.Web/Pages/Npi/EngineerSpecDetail.razor#L1733-L1734) for ergonomics.

### 4.5 QC/QA pages — IQC / IPQC / OQC

| Action                                              | Admin | Supervisor | Engineer | QC | Operator | Source                                                                                                                                                                              |
| --------------------------------------------------- | :---: | :--------: | :------: | :-: | :------: | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| View `/qcqa/iqc` · `/qcqa/ipqc` · `/qcqa/oqc`       | ✓     | ✓          | ✗        | ✓  | ✗        | Page policy `QcRead`                                                                                                                                                                |
| New IQC / Approve IQC                               | ✓     | ✓          | ✗        | ✓  | ✗        | `<AuthorizeView Roles="Admin,Supervisor,QC">` ([Iqc.razor:24](../src/CCL.MES.Web/Pages/QcQa/Iqc.razor#L24), [:84](../src/CCL.MES.Web/Pages/QcQa/Iqc.razor#L84)). IqcService has NO server-side role check — passes role through to audit only. See [tension §6.3](#63-iqc--qc-services-rely-on-page-gate-only-server-side-role-not-checked). |
| POST `/api/qc/inspections` (Phase 6 controller)     | ✓     | ✓          | ✓        | ✓  | ✓        | `QcController` has **NO** `[Authorize]` → FallbackPolicy. Any authenticated user can curl. See [tension §6.2](#62-workorderscontroller-inherits-fallbackpolicy-but-ui-gates-roles).  |

### 4.6 NPI Routine / Structure / Raw Materials / Work Center

Page policy `NpiRead` → Admin / Supervisor / Engineer / QC.

| Action                                              | Admin | Supervisor | Engineer | QC | Operator | Source                                                                                                                                  |
| --------------------------------------------------- | :---: | :--------: | :------: | :-: | :------: | --------------------------------------------------------------------------------------------------------------------------------------- |
| View pages                                          | ✓     | ✓          | ✓        | ✓  | ✗        | Page policy `NpiRead`                                                                                                                   |
| Mutation buttons (Create / Edit / Save)             | ✓     | ✗          | ✓        | ✗  | ✗        | `<AuthorizeView Roles="Admin,Engineer">` on each mutation button (see e.g. [EngineerRoutine.razor:56](../src/CCL.MES.Web/Pages/Npi/EngineerRoutine.razor#L56)) |
| WorkCenter context menu                             | ✓     | ✗          | ✓        | ✗  | ✗        | `<AuthorizeView Roles="Admin,Engineer">` ([WorkCenterContextMenu.razor:28](../src/CCL.MES.Web/Shared/WorkCenterContextMenu.razor#L28))   |

### 4.7 Settings

| Action                                              | Admin | Supervisor | Engineer | QC | Operator | Source                                                                                                                                  |
| --------------------------------------------------- | :---: | :--------: | :------: | :-: | :------: | --------------------------------------------------------------------------------------------------------------------------------------- |
| Profile · MyPwd · Appearance · Hardware · Mode · About | ✓ | ✓        | ✓        | ✓  | ✓        | FallbackPolicy                                                                                                                          |
| Account Control (Users + Permission Groups)         | ✓     | ✗          | ✗        | ✗  | ✗        | `@attribute [Authorize(Policy="AdminOnly")]` ([Account.razor:2](../src/CCL.MES.Web/Pages/Settings/Account.razor#L2))                    |
| Data Backup / Restore                               | ✓     | ✗          | ✗        | ✗  | ✗        | `[Authorize(Policy="AdminOnly")]` ([Backup.razor:2](../src/CCL.MES.Web/Pages/Settings/Backup.razor#L2))                                 |
| Syslog                                              | ✓     | ✗          | ✗        | ✗  | ✗        | `[Authorize(Policy="AdminOnly")]` ([Logs.razor:2](../src/CCL.MES.Web/Pages/Settings/Logs.razor#L2))                                     |

### 4.8 Misc controllers (no `[Authorize]` attribute)

These rely on **FallbackPolicy** = any authenticated user. Listed for awareness.

| Controller                              | Route                          | Endpoints                                                  | Gate                |
| --------------------------------------- | ------------------------------ | ---------------------------------------------------------- | ------------------- |
| `NpiController`                         | `/api/npi/workcenters`, `/api/npi/raw-materials` | GET only                                                   | FallbackPolicy      |
| `OeeController`                         | `/api/oee/machines`, `/api/oee/machines/{id}`    | GET only                                                   | FallbackPolicy      |
| `QcController`                          | `/api/qc/inspections`          | POST Create + POST `{id}/approve`                          | FallbackPolicy ⚠   |
| `WorkInstructionsController`            | `/api/workinstructions`        | GET + POST Create                                          | FallbackPolicy ⚠   |
| `WorkOrdersController`                  | `/api/workorders`              | GET + POST Create + POST `{id}/advance` + POST `{id}/flags` | FallbackPolicy ⚠   |

See [tension §6.2](#62-workorderscontroller-inherits-fallbackpolicy-but-ui-gates-roles).

---

## 5. Audit trail of decision sites

For convenience, every policy/role/department decision in the code base
keyed by file:line. Use this when reviewing PRs that touch RBAC.

| Decision                                                              | Source                                                                                                                                |
| --------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------- |
| 5 role whitelist                                                      | [src/CCL.MES.Domain/Auth/UserRole.cs:11-21](../src/CCL.MES.Domain/Auth/UserRole.cs#L11-L21)                                            |
| Department field on User                                              | [src/CCL.MES.Domain/Entities/User.cs:55](../src/CCL.MES.Domain/Entities/User.cs#L55)                                                  |
| FallbackPolicy = RequireAuthenticatedUser                             | [src/CCL.MES.Web/Program.cs:179-181](../src/CCL.MES.Web/Program.cs#L179-L181)                                                         |
| `AdminOnly` policy                                                    | [src/CCL.MES.Web/Program.cs:187](../src/CCL.MES.Web/Program.cs#L187)                                                                  |
| `NpiRead` policy                                                      | [src/CCL.MES.Web/Program.cs:193-194](../src/CCL.MES.Web/Program.cs#L193-L194)                                                         |
| `NpiSpecRead` policy                                                  | [src/CCL.MES.Web/Program.cs:195-196](../src/CCL.MES.Web/Program.cs#L195-L196)                                                         |
| `QcRead` policy                                                       | [src/CCL.MES.Web/Program.cs:197-198](../src/CCL.MES.Web/Program.cs#L197-L198)                                                         |
| SpecsController class-level — none                                    | [src/CCL.MES.Web/Controllers/SpecsController.cs](../src/CCL.MES.Web/Controllers/SpecsController.cs) (per-method `[Authorize]`)        |
| SpecsController per-method `Admin,Engineer`                           | Copy [:69](../src/CCL.MES.Web/Controllers/SpecsController.cs#L69) · Update [:106](../src/CCL.MES.Web/Controllers/SpecsController.cs#L106) · Revise [:149](../src/CCL.MES.Web/Controllers/SpecsController.cs#L149) · Supersede [:192](../src/CCL.MES.Web/Controllers/SpecsController.cs#L192) · Trash [:229](../src/CCL.MES.Web/Controllers/SpecsController.cs#L229) · Restore [:260](../src/CCL.MES.Web/Controllers/SpecsController.cs#L260) |
| SpecsExportController `Admin,Supervisor,Engineer`                     | [SpecsExportController.cs:42](../src/CCL.MES.Web/Controllers/SpecsExportController.cs#L42)                                            |
| DrawingsController `Admin,Supervisor,Engineer`                        | [DrawingsController.cs:31](../src/CCL.MES.Web/Controllers/DrawingsController.cs#L31)                                                  |
| DrawingsService `CanActAs` (chip permission)                          | [DrawingsService.cs:351-369](../src/CCL.MES.Application/Services/DrawingsService.cs#L351-L369)                                        |
| DrawingsController reads `department` claim                           | [DrawingsController.cs:133](../src/CCL.MES.Web/Controllers/DrawingsController.cs#L133)                                                |
| EngineerSpecDetail reads `department` claim                           | [EngineerSpecDetail.razor:1746](../src/CCL.MES.Web/Pages/Npi/EngineerSpecDetail.razor#L1746)                                          |
| EngineerSpecDetail `CanActAsChip` helper                              | [EngineerSpecDetail.razor:1733-1734](../src/CCL.MES.Web/Pages/Npi/EngineerSpecDetail.razor#L1733-L1734)                               |
| DemoWorkOrdersController `Admin,Supervisor`                           | [DemoWorkOrdersController.cs:46](../src/CCL.MES.Web/Controllers/DemoWorkOrdersController.cs#L46)                                      |
| WorkOrdersExportController `[Authorize]`                              | [WorkOrdersExportController.cs:33](../src/CCL.MES.Web/Controllers/WorkOrdersExportController.cs#L33)                                  |
| SpecQcCaptureService `_editorRoles = {Admin, Engineer}`               | [SpecQcCaptureService.cs:38-42](../src/CCL.MES.Application/Services/SpecQcCaptureService.cs#L38-L42)                                  |
| SpecQcWindowService `_editorRoles = {Admin, Engineer}`                | [SpecQcWindowService.cs:35-39](../src/CCL.MES.Application/Services/SpecQcWindowService.cs#L35-L39)                                    |
| Sidebar NPI group `Admin,Supervisor,Engineer,QC`                      | [MainLayout.razor:16](../src/CCL.MES.Web/Shared/MainLayout.razor#L16)                                                                 |
| Sidebar NPI Spec sub-link `Admin,Supervisor,Engineer`                 | [MainLayout.razor:22](../src/CCL.MES.Web/Shared/MainLayout.razor#L22)                                                                 |
| Sidebar QC/QA group `Admin,Supervisor,QC`                             | [MainLayout.razor:34](../src/CCL.MES.Web/Shared/MainLayout.razor#L34)                                                                 |
| Sidebar Admin-only links                                              | [MainLayout.razor:64-71](../src/CCL.MES.Web/Shared/MainLayout.razor#L64-L71)                                                          |
| WorkOrderDrawer Advance `Admin,Supervisor`                            | [WorkOrderDrawer.razor:230](../src/CCL.MES.Web/Components/WorkOrderDrawer.razor#L230)                                                 |
| WorkOrderDrawer QC Pass `Admin,Supervisor,QC`                         | [WorkOrderDrawer.razor:240](../src/CCL.MES.Web/Components/WorkOrderDrawer.razor#L240)                                                 |
| WorkOrderDrawer Run actions `Admin,Supervisor,Operator`               | [WorkOrderDrawer.razor:248](../src/CCL.MES.Web/Components/WorkOrderDrawer.razor#L248)                                                 |
| Demo WO section `Admin,Supervisor`                                    | [WorkOrders.razor:180](../src/CCL.MES.Web/Pages/WorkOrders.razor#L180)                                                                |
| Iqc page mutation buttons `Admin,Supervisor,QC`                       | [Iqc.razor:24](../src/CCL.MES.Web/Pages/QcQa/Iqc.razor#L24), [:84](../src/CCL.MES.Web/Pages/QcQa/Iqc.razor#L84)                       |

---

## 6. Known tensions / mismatches

> ⚠  Items below were **identified during this audit** but NOT modified.
> Decide per-item whether to file as follow-up tickets (the audit was
> doc-only).

### 6.1 QC role excluded from `NpiSpecRead` but Spec QC Capture semantically belongs to QC

- **Source policy**: `NpiSpecRead` allows `Admin / Supervisor / Engineer` only (no `QC`) ([Program.cs:195-196](../src/CCL.MES.Web/Program.cs#L195-L196)).
- **Source comment** acknowledging the tension: [SpecQcCaptureService.cs:22-25](../src/CCL.MES.Application/Services/SpecQcCaptureService.cs#L22-L25) — *"RBAC: server-side role check — only Admin or Engineer can capture (default per Q11, matches PR-D-3 contract). QC role bypassed because route policy NpiSpecRead doesn't grant QC role anyway."*
- **Effect**: a user with `role=QC` cannot reach `/npi/engineer-spec/{id}` at all → cannot use the QC Capture tab even though the semantic owner of capture results is the QC team. The drawing **QC chip** (`Engineer` + `dept=qc`) is similarly unreachable by a `role=QC` user; it can only be acted on by an Engineer who happens to have `Department="qc"`.
- **Plan owner**: PR-D-5d is the planned follow-up to widen `NpiSpecRead` to include `QC` (or to introduce a dedicated `NpiSpecReadWithQc` policy). Until then, the workaround is to assign QC personnel `role=Engineer` + `Department="qc"`.
- **Action requested**: confirm follow-up scope. Audit does NOT change this.

### 6.2 `WorkOrdersController` inherits FallbackPolicy but UI gates roles

- **Source**: [WorkOrdersController.cs](../src/CCL.MES.Web/Controllers/WorkOrdersController.cs) — no `[Authorize]` attribute → FallbackPolicy (any authenticated).
- **UI counterpart**: `WorkOrderDrawer.razor` gates each action behind specific role groups (`Advance`: `Admin,Supervisor`; `QC Pass`: `Admin,Supervisor,QC`; `Run`: `Admin,Supervisor,Operator`).
- **Effect**: a curl as any authenticated user (e.g. Operator) hitting `POST /api/workorders/{id}/advance` directly is **NOT** blocked by role — the only barrier is the server-side `WorkOrderStateMachine.CanAdvance` business guard (state-machine, not role). Same for `POST /api/workorders/{id}/flags` and `POST /api/workorders` Create.
- **Same pattern in**: `WorkInstructionsController` (Create) and `QcController` (`POST /api/qc/inspections` + `{id}/approve`). UI hides buttons for the wrong role; server doesn't validate role.
- **Action requested**: consider whether to add `[Authorize(Roles=…)]` to these controllers to match the UI gates. Audit does NOT change this.

### 6.3 IQC / QC services rely on page gate only; server-side role NOT checked

- **Source**: `IqcService.CreateAsync` ([IqcService.cs:32-92](../src/CCL.MES.Application/Services/IqcService.cs#L32-L92)) + `IqcService.ApproveAsync` ([:98-121](../src/CCL.MES.Application/Services/IqcService.cs#L98-L121)) take `actorRole` as a parameter but **only pass it through to the audit emit** — there is no `_editorRoles` check like in `SpecQcCaptureService` / `SpecQcWindowService`.
- **Path-1 (Blazor page)**: `Iqc.razor` is gated by `Authorize(Policy="QcRead")` + per-button `<AuthorizeView Roles="Admin,Supervisor,QC">`, so the Blazor-server caller is always one of those 3 roles. Safe.
- **Path-2 (controller)**: `QcController.Create` (which calls `QcService`, not `IqcService`) has no `[Authorize]`. While that's a different service, it's the same systemic pattern — server `_editorRoles` check is inconsistent across QC services.
- **Effect**: the current 2-layer defense (page policy + UI button gate) is fine for the Blazor surface. If a future PR introduces an IQC HTTP controller, the server gate would need to be added explicitly (no failsafe).
- **Action requested**: optional — extract a shared `_editorRoles` pattern to QC services for consistency. Audit does NOT change this.

### 6.4 Supervisor read-only on Spec mutations

- Not a mismatch; documenting intent. `NpiSpecRead` includes Supervisor (page view OK) but every Spec mutation endpoint is `[Authorize(Roles="Admin,Engineer")]` → Supervisor sees the list + detail but cannot Copy/Edit/Revise/Supersede/Trash/Restore. Spec **Export** does include Supervisor ([SpecsExportController.cs:42](../src/CCL.MES.Web/Controllers/SpecsExportController.cs#L42)).

---

## 7. Audit refresh procedure

When RBAC changes, re-run this audit:

```bash
cd "3. PROJECTS/CCL-CMES/CCL-MES"

# 1. Policies in Program.cs
grep -n "AddPolicy\|FallbackPolicy\|RequireRole" src/CCL.MES.Web/Program.cs

# 2. Razor pages with @attribute [Authorize]
grep -rn "@attribute \[Authorize" src/CCL.MES.Web/Pages/

# 3. AuthorizeView Roles= gates
grep -rn "AuthorizeView Roles=" src/CCL.MES.Web/

# 4. Controller [Authorize] attributes
grep -rn "\[Authorize" src/CCL.MES.Web/Controllers/

# 5. Server-side role/department checks in services
grep -rn "Department\|_editorRoles\|actorRole\|UserRole\." src/CCL.MES.Application/Services/
```

Update each table cell + audit-trail file:line, then commit with a
`docs(rbac):` message.
