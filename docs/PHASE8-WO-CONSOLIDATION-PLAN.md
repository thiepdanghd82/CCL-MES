# Phase 8 — Work Order consolidation plan (`/workorders/shop` → `/workorders`)

> **Status**: plan only. Branch chưa tạo. Đợi anh duyệt Q1..Q12 +
> architecture option (a/b/c) + PR split trước khi vào code.
>
> **Reason**: `/workorders/shop` (PR #32a + #32b card view + scan +
> drawer) và `/workorders` (Phase 6 table + state-machine actions)
> trùng chức năng. Mục tiêu: 1 trang `/workorders` chứa toàn bộ
> capability — card-first kiosk UX + table fallback cho planner + drawer
> chứa toàn bộ action state-machine — với **100% chức năng Phase 6
> được bảo toàn** (mọi action / RBAC / SignalR / audit không mất).
>
> **Phase 6 GIỜ ĐƯỢC PHÉP SỬA** trong PR này (đây là mục tiêu hợp
> nhất). Nhưng feature audit + before/after must-pass list bắt buộc.

---

## §1. Audit `/workorders` Phase 6 (sẽ giữ 100%)

### 9 cột table

| # | Cột | Source |
|---|---|---|
| 1 | WO No | `w.WoNo` (monospace bold) |
| 2 | Customer | `w.Customer?.Name` |
| 3 | Product | `w.ProductName` |
| 4 | Machine | `w.MachineCode` |
| 5 | Target | `w.TargetQty + w.Uom` |
| 6 | Produced | `w.ProducedQty` |
| 7 | Current Step | Badge `StepName(w.CurrentStep)` — "1. Pre-press Check" … "7. OQC" / "Closed" |
| 8 | Progress | 7 dots flow (each: `done` / `current` / `todo` based on `(int)w.CurrentStep > (int)s`) |
| 9 | Actions | 7 buttons, RBAC + state-gated |

### 7 actions với matrix RBAC + state guards

| Action | Roles | Visible when | Service call | Audit |
|---|---|---|---|---|
| **Advance** | Admin / Supervisor | Always | `Wo.AdvanceAsync` → `WorkOrderStateMachine.CanAdvance` | implicit via service |
| **Unlock step (Helpers)** | Admin / Supervisor | Always | `Wo.UpdateFlagsAsync(MaterialsReady=true, SetupConfirmed=true, RohsOk=true)` | `WoFlagsUpdate` |
| **QC IPQC Pass** | Admin / Supervisor / QC | `CurrentStep == IpqcApproval` | `Qc.CreateAsync + ApproveAsync` (IPQC type) | implicit |
| **QC FQC Pass** | Admin / Supervisor / QC | `CurrentStep == Fqc` | `Qc.CreateAsync + ApproveAsync` (FQC) | implicit |
| **QC OQC Pass** | Admin / Supervisor / QC | `CurrentStep == Oqc` | `Qc.CreateAsync + ApproveAsync` (OQC) | implicit |
| **Start** | Admin / Supervisor / Operator | `CurrentStep == Running` | `Oee.StartAsync` | `ProductionLog` event |
| **Pause** | Admin / Supervisor / Operator | `CurrentStep == Running` | `Oee.PauseAsync` | `ProductionLog` event |
| **Resume** | Admin / Supervisor / Operator | `CurrentStep == Running` | `Oee.ResumeAsync` | `ProductionLog` event |
| **Finish** | Admin / Supervisor / Operator | `CurrentStep == Running` | `Oee.FinishAsync(GoodQty=TargetQty)` | `ProductionLog` event |

### State machine guards (`WorkOrderStateMachine.CanAdvance`)

| From → To | Guard | Error code |
|---|---|---|
| PrePressCheck → OpSetting | `ProductRevisionId is not null && MaterialsReady` | `RequiresSpecAndMaterials` |
| OpSetting → IpqcApproval | `SetupConfirmed` | `RequiresSetupConfirmed` |
| IpqcApproval → ReadyToRun | `LastQc(IPQC)?.Result == Pass` | `IpqcNotPassed` |
| ReadyToRun → Running | (no guard) | — |
| Running → Fqc | `ProducedQty > 0` | `NoProductionYet` |
| Fqc → Oqc | `LastQc(FQC)?.Result == Pass` | `FqcNotPassed` |
| Oqc → Closed | `LastQc(OQC)?.Result == Pass && RohsOk` | `OqcOrRohsNotMet` |

### SignalR contract

- Hub: `/hubs/shopfloor` (`ShopfloorHub : Hub`).
- Notifier: `ShopfloorNotifier.NotifyChangedAsync(reason)` → broadcast event `"shopfloorChanged"` với payload `reason` (`advance` / `flags` / `qc` / `oee`).
- Page subscribes via `HubConnection`, forwards `ccl_mes_auth` cookie via `HubCookieAccessor`, on event triggers `Reload()` + `StateHasChanged()`.
- `IAsyncDisposable.DisposeAsync` → `_hub.DisposeAsync()`.

### Service surface used (KHÔNG đụng)

- `WorkOrderService.GetAllAsync()` — list view
- `WorkOrderService.AdvanceAsync(id, user)` → returns `AdvanceResult(Ok, ErrorCode, CurrentStep)`
- `WorkOrderService.UpdateFlagsAsync(id, UpdateFlagsRequest, user)` → audit `WoFlagsUpdate`
- `QcService.CreateAsync(CreateQcRequest)` + `ApproveAsync(id, true, user)`
- `OeeService.StartAsync` / `PauseAsync(PauseRequest)` / `ResumeAsync` / `FinishAsync(FinishRunRequest)`
- All inject `IStringLocalizer<SharedResource>` for error key lookup via `WoErrorKeys.KeyFor(code)`.

---

## §2. Audit `/workorders/shop` Phase 8 PR #32a + #32b

### Header

- Scan input + 📷 Camera button + LOOKUP button (manual exact-match → drawer; non-match → toast).
- Camera: html5-qrcode 2.3.8 vendored, JS interop `ccl.qr.{isAvailable, start, stop}`; `IAsyncDisposable` tears down stream + DotNetObjectReference.
- HTTPS-aware: camera button disabled when `isSecureContext === false`.

### Active + Closed sections (card grid)

- `ShopOrderListResult { Active: List<WorkOrderCardItem>, Closed: List<WorkOrderCardItem> }` from `WorkOrderService.ShopOrderListAsync()`.
- `WorkOrderCardItem` carries 18 fields incl. derived `BadgeToken / BadgeCssClass / BadgeIcon / BadgeLabelKey` from `WorkOrderStatusBadge.From(wo)` (12-state palette: NEW / PRE-PRESS / SETTING / IPQC-WAIT / IPQC-OK / IPQC-FAIL / READY / RUNNING / PAUSED / QA-PENDING / DONE / CANCELLED).
- Card click → `OpenDrawerByWoNoAsync(wo.WoNo)`.

### Drawer (read-only 5 sections, 480px right-side)

- Loaded via `WorkOrderService.GetDrawerAsync(woNo)` → `WorkOrderDrawerView`.
- 5 sections: Header / Production / Materials (BOM `ManufacturingStructures.Where(ParentPart=ProductCode)`) / QC History (5 latest from `wo.Inspections`) / Action footer.
- **Action footer hiện tại**: 1 button "Open in Work Orders →" navigate plain `/workorders` (Q7 caveat — Phase 6 không nhận `?wo=`). **Sẽ rethink ở §3.**
- ZERO mutation surface today.

### Service surface (mới từ Phase 8)

- `WorkOrderService.ShopOrderListAsync()` → Active / Closed split (pre-flattened DTO + BOM count via grouped subquery)
- `WorkOrderService.GetDrawerAsync(woNo)` → DTO + Materials + QC list

---

## §3. Architecture options — 1 trang `/workorders` chứa tất cả

### Option (a) — View toggle "Card ↔ Table"

- Top-bar control toggles giữa card grid (kiosk default) và 9-cột table (planner view).
- Scan / camera ở mọi view; drawer opens trên cả 2 view.
- Actions render trong table row (Phase 6 cũ) + cũng có trong drawer (kiosk path).
- **Trade-off**: duplicate action UI giữa table row + drawer → state machine wiring 2 chỗ → bug surface lớn; operator hỏi "click action ở đâu?".

### Option (b) — 🌟 Card-first + drawer = WO action console (em recommend)

- Top: scan + camera + LOOKUP + view-toggle "Card / Table".
- Default view = **Card** (kiosk-first); planner toggle "Table" để batch-review.
- Card click / scan / manual exact-match → **drawer** mở.
- **Drawer mở rộng thêm Action section** chứa tất cả 7 actions Phase 6, role-gated chính xác như Phase 6 row:
  - Advance (Admin/Supervisor)
  - Unlock step (Admin/Supervisor)
  - QC Pass (Admin/Supervisor/QC, only when step requires)
  - Start / Pause / Resume / Finish (Admin/Supervisor/Operator, only when CurrentStep=Running)
- Mọi action vẫn gọi cùng `WorkOrderService` + `OeeService` + `QcService` + `ShopfloorNotifier.NotifyChangedAsync` → state machine + audit + SignalR push đúng nguyên.
- **Table view** (toggle ON): render 9-cột Phase 6 NHƯ CŨ — row click cũng mở drawer thay vì button inline (consistency); HOẶC giữ inline buttons (Phase 6 muscle memory). Đề xuất ↓ Q4.
- **Drawer footer**: button "Open in Work Orders →" **bỏ** (drawer chính nó là "Work Orders"); thay bằng "Close" + optional "Refresh" + audit-trail link.
- **Trade-off**: single source of action UI (drawer); operator học workflow mới "click WO → drawer → action"; table view chỉ là viewing convenience.

### Option (c) — Phase 6 table giữ làm core + thêm card/scan phía trên

- Top thêm scan + camera; Active/Closed card grid render trước table.
- Click card → scroll vào row tương ứng trong table (hoặc highlight).
- Actions vẫn ở table row.
- **Trade-off**: table là core → operator scroll xuống mất scan ergonomics; drawer hiện tại lose-out (chỉ read-only nếu giữ); kiosk-first UX của #32a/#32b bị giảm.

### 🌟 Recommended: **Option (b)**

**Lý do**:
1. **Single source of action UI** → state machine wiring 1 chỗ → ít bug.
2. **Kiosk-first** giữ UX của PR #32a/#32b (scan workflow + per-WO focus).
3. **Phase 6 mọi action preserved 100%** — chỉ MOVE từ table row → drawer Action section. RBAC matrix giữ nguyên. SignalR notify giữ nguyên. State machine guards giữ nguyên (Phase 6 `Wo.AdvanceAsync` re-used).
4. **Table view** giữ làm fallback cho planner overview/audit — không bỏ.
5. Drawer "Open in Work Orders →" trỏ về chính nó → rethink rõ thành Close + Refresh; KHÔNG redirect quay vòng.

**Cost**: Drawer rộng hơn (5 → 6 sections); ShopOrder.razor merge vào WorkOrders.razor (delete ShopOrder.razor); WorkOrderDrawer.razor mở rộng với Action section + RBAC + state machine + Notifier wiring.

---

## §4. State machine + RBAC preservation map (Option b)

Mỗi action Phase 6 PHẢI exist trong drawer Action section với behavior **identical**:

| Phase 6 action | Drawer Action section button | Roles | Visibility guard | Service call (unchanged) | Notifier reason |
|---|---|---|---|---|---|
| Advance | "▶ Advance" primary | Admin / Supervisor | Always (server-side `CanAdvance` returns error if guard fails — surface inline error banner) | `Wo.AdvanceAsync(id, actor)` | `"advance"` |
| Unlock step | "↻ Unlock step" ghost | Admin / Supervisor | Always | `Wo.UpdateFlagsAsync(...)` | `"flags"` |
| QC IPQC Pass | "✓ QC IPQC Pass" pass | Admin / Supervisor / QC | `CurrentStep == IpqcApproval` | `Qc.CreateAsync + ApproveAsync(true)` | `"qc"` |
| QC FQC Pass | "✓ QC FQC Pass" pass | Admin / Supervisor / QC | `CurrentStep == Fqc` | same | `"qc"` |
| QC OQC Pass | "✓ QC OQC Pass" pass | Admin / Supervisor / QC | `CurrentStep == Oqc` | same | `"qc"` |
| Start | "▶ Start" run | Admin / Supervisor / Operator | `CurrentStep == Running` | `Oee.StartAsync(id, operatorId)` | `"oee"` |
| Pause | "⏸ Pause" warn | Admin / Supervisor / Operator | `CurrentStep == Running` | `Oee.PauseAsync` | `"oee"` |
| Resume | "▶ Resume" run | Admin / Supervisor / Operator | `CurrentStep == Running` | `Oee.ResumeAsync` | `"oee"` |
| Finish | "✅ Finish (+target)" finish | Admin / Supervisor / Operator | `CurrentStep == Running` | `Oee.FinishAsync(FinishRunRequest)` | `"oee"` |

**Server-side state machine guard** — `Wo.AdvanceAsync` already returns `AdvanceResult(Ok, ErrorCode, CurrentStep)`. Drawer Action section displays error banner inline using `WoErrorKeys.KeyFor(code)` per Phase 6 i18n contract.

**`SignalR` rewire**:
- `WorkOrders.razor` consolidated page subscribes `"shopfloorChanged"` event → calls `ReloadShopOrderListAsync()` (replaces Phase 6 `Reload()` + ShopOrder `LoadAsync()` → 1 service call `Wo.ShopOrderListAsync()`).
- If drawer open at the time of push, **rebind drawer DTO** (re-fetch `GetDrawerAsync(_drawerWoNo)`) so card grid AND drawer both reflect new state.
- Notifier called by action handlers — moved into drawer event handlers; behavior identical to Phase 6 row handlers (`await Notifier.NotifyChangedAsync(reason)` after each mutation).

**Audit emit**: every service call already emits the right audit code. No change.

**Sample drawer Action section markup** (mirror Phase 6 row pattern):

```razor
<section class="wo-drawer-section wo-drawer-actions">
    <h3>@Loc["workorders.drawer.section.actions"]</h3>
    @if (_actionMessage is not null)
    {
        <div class="alert @(_actionOk ? "ok" : "err")">@_actionMessage</div>
    }
    <div class="wo-drawer-action-grid">
        <AuthorizeView Roles="Admin,Supervisor" Context="advanceCtx">
            <button class="btn" @onclick="AdvanceAsync">@Loc["workorders.btn.advance"]</button>
            <button class="btn ghost" @onclick="UnlockStepAsync">@Loc["workorders.btn.unlock_step"]</button>
        </AuthorizeView>
        @if (NeedsQc(_view.CurrentStep) is { } t)
        {
            <AuthorizeView Roles="Admin,Supervisor,QC" Context="qcCtx">
                <button class="btn pass" @onclick="@(() => QuickPassAsync(t))">QC @t Pass</button>
            </AuthorizeView>
        }
        @if (_view.CurrentStep == ProcessStepCode.Running)
        {
            <AuthorizeView Roles="Admin,Supervisor,Operator" Context="runCtx">
                <button class="btn run" @onclick="StartAsync">Start</button>
                <button class="btn warn" @onclick="PauseAsync">Pause</button>
                <button class="btn run" @onclick="ResumeAsync">Resume</button>
                <button class="btn finish" @onclick="FinishAsync">
                    Finish (+@_view.TargetQty)
                </button>
            </AuthorizeView>
        }
    </div>
</section>
```

Mirror lại verbatim từ Phase 6 — same AuthorizeView Context names, same i18n keys, same NeedsQc helper. Đảm bảo 0 drift.

---

## §5. Route + nav cleanup

- **Bỏ `ShopOrder.razor`** entirely. Logic merge vào `WorkOrders.razor`.
- **`/workorders/shop`**: 2 options
  - **A. 301 permanent redirect** via Razor `@page` shell: tạo `Pages/ShopOrderRedirect.razor` 5 LOC chỉ `Nav.NavigateTo("/workorders", forceLoad: true)` → KHÔNG dead-link cho bookmark cũ.
  - **B. Endpoint middleware**: Program.cs `app.MapGet("/workorders/shop", ctx => { ctx.Response.Redirect("/workorders", true); return Task.CompletedTask; })` — outside Blazor circuit, native HTTP 301.
- 🌟 Em đề xuất **B** (HTTP 301 native) — cleaner, search engine + bookmark friendly.
- **`MainLayout.razor`**: bỏ entry `<a href="/workorders/shop">Shop Order</a>` (PR #32a thêm), keep `<a href="/workorders">Work Orders</a>`.

---

## §6. Migration + vùng cấm

**Migration**: KHÔNG. PR này chỉ là UI consolidation. Schema không đổi. Verify post-build:

```
ProductRevisions=6 WorkOrders=1 IqcInspections=3 IqcResultDetails=7
Users=5 ManufacturingStructures=20530 ProcessCatalogs=17 ReasonCodes=12
Drawings=1 DrawingVersions=1 DrawingApprovals=0
Latest migration: 20260601143151_AddSpecQcCaptureAndReasonCode (no new)
FK ProductRevision↔WO: WO-26-3683 → ProductRevisionId=1
```

**Vùng cấm SỬA**:
- ✅ `Pages/WorkOrders.razor` — sửa (mục tiêu hợp nhất; preserve every action)
- ✅ `Pages/ShopOrder.razor` — XÓA (logic merge vào WorkOrders.razor)
- ✅ `Components/WorkOrderDrawer.razor` — mở rộng (add Action section + RBAC + state-machine wiring + SignalR notifier hook)
- ✅ `Shared/MainLayout.razor` — bỏ "Shop Order" nav entry
- ✅ `Program.cs` — add HTTP 301 redirect endpoint
- ✅ Resources/Sharedresource.{resx,vi.resx} — i18n keys cho drawer Actions section

**KHÔNG đụng**:
- ❌ `WorkOrderStateMachine.cs` (logic guard unchanged)
- ❌ `WorkOrderService.AdvanceAsync / UpdateFlagsAsync / CreateAsync` (service surface unchanged)
- ❌ `OeeService.{Start,Pause,Resume,Finish}Async` (unchanged)
- ❌ `QcService.CreateAsync / ApproveAsync` (unchanged)
- ❌ `ShopfloorHub + ShopfloorNotifier` (contract unchanged; only callers move from Phase 6 row → drawer)
- ❌ `WorkOrdersController` API surface (unchanged)
- ❌ DbContext + entities (unchanged)
- ❌ Audit code (`WoFlagsUpdate`, etc.) — unchanged
- ❌ Spec 6 tab, 4 NPI tab, Machine, ProductionLog, IqcInspection
- ❌ Ops Control v1.2 / CMES / SpecHub / Old ver (READ-ONLY pattern reference only)

---

## §7. Q1..Q12 + defaults

| Q | Question | Default em đề xuất | Trade-off |
|---|---|---|---|
| **Q1** | Default view khi load `/workorders` | **Card view** (kiosk-first per #32a intent) | Table-first nếu operator nội bộ ưu tiên planner overview |
| **Q2** | View toggle UI | **Top-right segmented control "Card / Table"** + persist via `sessionStorage` key `workorders.view` | URL param `?view=table` cũng hợp lý nhưng pollute share-link UX |
| **Q3** | Drawer Action section visibility | **Always render** với buttons disabled (greyed) khi `CanAct` false, hover tooltip giải thích | Hide buttons hoàn toàn → cleaner nhưng operator hỏi "tại sao mất nút?" |
| **Q4** | Table view row interaction | **Row click → mở drawer** (consistency với card view); KHÔNG inline action buttons trong row → single source of action UI | Giữ inline row buttons Phase 6 → muscle memory cho old operator; double action UI surface |
| **Q5** | SignalR scope after consolidation | **GIỮ ShopfloorNotifier** (zero hub change); consolidated page subscribes 1 lần; mọi action handler call `NotifyChangedAsync(reason)`; broadcast triggers card grid + drawer re-fetch | Hub split per-view = over-engineering |
| **Q6** | Redirect `/workorders/shop` strategy | **HTTP 301 native** via `Program.cs MapGet` endpoint (outside Blazor circuit) | Razor shell page → trip qua Blazor circuit, lazy hơn |
| **Q7** | Drawer footer "Open in Work Orders →" | **BỎ** (drawer chính nó IS Work Orders surface); thay bằng "Close" button + audit-trail link (defer) | Giữ → broken UX (button trỏ về cùng page) |
| **Q8** | i18n key rename `shop_order.*` → `workorders.*` | **KHÔNG rename** (zero risk, key namespace stable); accept legacy `shop_order.*` keys alongside `workorders.*` | Rename → cleaner namespace but breaks translation memory + diff bigger |
| **Q9** | Status badge in table view (Step + 12-pill?) | **Table view giữ Step badge + 7-dot flow** (Phase 6 muscle memory); KHÔNG add 12-pill row col → row width OK | Add 12-pill → consistency với card view but row becomes wider |
| **Q10** | Page title | **"Work Orders"** (route `/workorders`, simpler) | "Shop Order" rebrand → confuses Phase 6 docs/translations |
| **Q11** | PR split | **1 PR consolidation** (cohesive change, atomic preservation verify) | 3 PRs (merge + redirect + cleanup) → bigger ceremony, easier review per chunk |
| **Q12** | Table view toggle persist | **sessionStorage** key `workorders.view` (operator choice survives tab refresh, not cross-device) | localStorage cross-device → noisy if shared device with multiple operators |

---

## §8. Files surface + delta estimate

| File | Status | Δ LOC |
|---|---|---|
| `Pages/WorkOrders.razor` | MODIFY (merge ShopOrder card + scan + drawer trigger; preserve 7-dot flow + Step badge + table) | +250 / -50 |
| `Pages/ShopOrder.razor` | DELETE (entirely) | -426 |
| `Components/WorkOrderDrawer.razor` | MODIFY (add Action section with 9 buttons + RBAC + state-machine wiring + Notifier + WoErrorKeys integration) | +200 / -10 |
| `Application/Services/WorkOrderService.cs` | (optional) MODIFY — `GetDrawerAsync` returns extra fields needed for action gating (no breaking change) | +20 |
| `Shared/MainLayout.razor` | MODIFY — bỏ 1 nav entry | -1 |
| `Program.cs` | MODIFY — add HTTP 301 redirect `/workorders/shop` → `/workorders` | +5 |
| `Resources/SharedResource.{resx,vi.resx}` | MODIFY — +~12 keys (Action section title + tooltips + view-toggle labels) | +24 each |
| `wwwroot/css/site.css` | MODIFY (Action section grid + view-toggle styling + drawer width adjustment) | +50 |
| `docs/LESSONS_LEARNED.md` | MODIFY — pin consolidation pattern (single-source action UI; sessionStorage view persistence; HTTP 301 native redirect) | +40 |
| `docs/PHASE8-WO-CONSOLIDATION-PLAN.md` | VENDORED | +(this file) |

**Net**: ~+200 / -200 LOC. **Effort**: M, 1 phiên.

---

## §9. Verify gates (V1..V20)

### Build + render

| # | Check |
|---|---|
| V1 | `dotnet build` clean (0/0) |
| V2 | `/workorders` renders 200 with cookie auth; default view = card |
| V3 | View toggle "Card / Table" segmented control visible top-right; click → switch view |
| V4 | sessionStorage `workorders.view` persists across tab refresh |
| V5 | `/workorders/shop` HTTP 301 → `/workorders` (no Blazor circuit hit) |
| V6 | MainLayout nav: "Shop Order" entry GONE; "Work Orders" stays |

### State machine + RBAC preservation (CRITICAL)

| # | Check | Method |
|---|---|---|
| V7 | Login as Admin → drawer shows 9 actions per matrix § 4 | Browser login + click drawer + visual inspect |
| V8 | Login as Supervisor → drawer shows 9 actions (same as Admin) | Browser |
| V9 | Login as QC → drawer shows only QC Pass buttons (when step requires) | Browser |
| V10 | Login as Operator → drawer shows only Start/Pause/Resume/Finish (when CurrentStep=Running) | Browser |
| V11 | Login as Engineer/User → drawer shows NO action buttons (just read-only sections) | Browser |
| V12 | Click "Advance" on PrePressCheck WO without MaterialsReady → error banner shows localized `RequiresSpecAndMaterials` message | Browser test |
| V13 | Click "Advance" on OpSetting WO without SetupConfirmed → error banner shows localized `RequiresSetupConfirmed` | Browser |
| V14 | Click QC IPQC Pass → `Qc.CreateAsync + ApproveAsync` fires; WO advances to ReadyToRun on next Advance click | Browser + verify audit log |
| V15 | Click Start (Running step) → `Oee.StartAsync` fires; ProductionLog row created | Browser + sqlite verify |
| V16 | `Notifier.NotifyChangedAsync` called after each mutation; second browser session auto-reloads card + drawer | 2 browser session test |

### Vùng cấm

| # | Check |
|---|---|
| V17 | `git diff main..HEAD` for these paths = EMPTY: `WorkOrderStateMachine.cs`, `WorkOrderService.{AdvanceAsync, UpdateFlagsAsync, CreateAsync}` (mutation methods only), `OeeService.cs`, `QcService.cs`, `WorkOrdersController.cs`, `ShopfloorHub.cs` |
| V18 | Baseline counts intact (ProductRevisions=6, WorkOrders=1, IqcInspections=3, etc.) |
| V19 | Latest migration unchanged (`20260601143151_AddSpecQcCaptureAndReasonCode`) |
| V20 | Restart no-op — boot 2× returns identical baseline |

---

## §10. Migration risk + rollback

**Risk profile**: M.

- Drawer Action section là code mới — bug risk ở RBAC visibility + state-machine guard wiring. Mitigation: copy verbatim từ Phase 6 row markup (AuthorizeView Context + NeedsQc helper + service calls). Service layer unchanged → backend behavior risk near-zero.
- ShopOrder.razor DELETE — irrecoverable in branch but git history preserves. Rollback: revert PR.
- View toggle sessionStorage UX — non-functional risk; nếu broken UX operator vẫn ship được vì single view always renders.

**Rollback path**: revert PR → restore ShopOrder.razor + WorkOrders.razor + drawer + nav + redirect endpoint. No data migration so no state to roll back.

---

## §11. STOP — chờ duyệt

Em sẽ KHÔNG tạo branch / KHÔNG code cho đến khi anh:

1. **Confirm architecture option**: **(a) toggle / (b) card-first + drawer console (RECOMMENDED) / (c) Phase 6 table + scan-prelude**.
2. **Chốt Q1-Q12** — em đề xuất defaults; flag specific:
   - Q1 Default view = Card
   - Q4 Table row click = open drawer (NO inline buttons in row)
   - Q7 Drawer "Open in Work Orders →" BỎ
   - Q11 1 PR consolidation
3. **Verify state machine preservation list** (§4 — 9 buttons + RBAC matrix + Notifier reason + guard mapping). Bất kỳ button nào anh không OK move sang drawer, em revise.
4. **Confirm redirect strategy**: HTTP 301 native (Program.cs) vs Razor redirect shell.

Sau khi anh chốt, em sẽ:
- `git checkout -b feat/phase8-wo-consolidation`
- Implement theo §3-§4 (Option b)
- V1-V20 verify (state-machine + RBAC + SignalR + vùng cấm)
- Pin lessons (single-source action UI + sessionStorage view + HTTP 301 native + state-machine wiring discipline)
- Open PR + STOP review.

---

## §12. Files surveyed (transparency)

CCL-MES current state:
- `src/CCL.MES.Web/Pages/WorkOrders.razor` (Phase 6, 230 LOC — sẽ MODIFY)
- `src/CCL.MES.Web/Pages/ShopOrder.razor` (Phase 8 #32a+#32b, 426 LOC — sẽ DELETE)
- `src/CCL.MES.Web/Components/WorkOrderDrawer.razor` (Phase 8 #32b, 206 LOC — sẽ MODIFY)
- `src/CCL.MES.Domain/StateMachine/WorkOrderStateMachine.cs` (79 LOC — untouched)
- `src/CCL.MES.Web/Hubs/ShopfloorHub.cs` (Notifier + Hub — untouched)
- `src/CCL.MES.Application/Services/WorkOrderService.cs` (`GetAllAsync`, `AdvanceAsync`, `UpdateFlagsAsync`, `CreateAsync`, `ShopOrderListAsync`, `GetDrawerAsync`)
- `src/CCL.MES.Web/Shared/MainLayout.razor` (top nav — bỏ 1 entry)
- `src/CCL.MES.Web/Program.cs` (FallbackPolicy = RequireAuthenticatedUser preserved; add 301 redirect endpoint)

READ-ONLY references (KHÔNG SỬA):
- Ops Control v1.2 / CMES / SpecHub / Old ver

---

*Plan tạo: 2026-06-02 — Phase 8 WO consolidation (`/workorders/shop` → `/workorders`) — NO branch yet.*
