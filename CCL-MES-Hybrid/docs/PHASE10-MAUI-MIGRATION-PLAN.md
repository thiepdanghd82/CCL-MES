# Phase 10 — MAUI Blazor Hybrid + Central API + Offline Sync — PLAN

> **Approved by Henry 2026-06-03.** Q1–Q13 accepted with 2 overrides:
> **Q4 = strict online-only** (no stateful offline-queue in Phase 10; offline
> writes restricted to append-only entities), **Q6 = Win + macOS desktop first**
> (defer Android/iOS to a post-Phase-10 phase). Other Qs follow the defaults
> proposed below.
>
> Audit data backing every claim below comes from a read-only sweep of the
> legacy `src/CCL.MES.*` projects performed 2026-06-03; line-anchor citations
> use repo-relative paths.

---

## 0. Folder name (chosen)

**`CCL-MES-Hybrid/`** — top-level sibling of `src/` and `tests/` inside the
existing CCL-MES git repo. Shares branch + PR + CI workflow with the legacy
web app; legacy source under `src/CCL.MES.*` is treated as a READ-ONLY
baseline for the duration of Phase 10.

Rationale: keeps GitHub PR + review history in one place while preserving
the clean separation between legacy (`src/CCL.MES.*`) and new
(`CCL-MES-Hybrid/src/CCL.MES.*`) project trees. The legacy solution
(`CCL.MES.sln` at repo root) and the new one (`CCL-MES-Hybrid/CCL-MES-Hybrid.sln`)
build independently.

Considered + rejected: `CCL-MES-App/` (too generic), `CCL-MES-Native/`
(misleading — we keep Blazor not native UI), `CCL-MES-Client/` (loses the API
half of the picture), a sibling repository (fragments PR history).

---

## 1. Feasibility (verdict: GREEN with one HIGH-RISK area)

| Aspect | Verdict | Why |
| --- | --- | --- |
| Backend → API extraction | **Green** | Existing `CCL.MES.Application` services already take `IMesDbContext` ctor-injected + return DTOs. Lifting them behind ASP.NET Core controllers is mechanical, not architectural. Audit found **only 1 page** (`Settings/About.razor`) holds DbContext directly — every other page already calls through Application services. |
| Cookie → JWT auth | **Green** | `Program.cs:164–176` cookie config is standard (`ExpireTimeSpan=8h`, `SlidingExpiration=true`). RBAC policies (`AdminOnly`, `NpiRead`, `NpiSpecRead`, `QcRead`) already claim-based — port to JWT claims is 1:1. |
| Blazor Hybrid UI reuse | **Green** | Razor pages + components are SSR but use standard Blazor primitives. MAUI Blazor Hybrid renders the same `.razor` files inside a `WebView2`/`WKWebView` shell. The page-side code that subscribes to ShopfloorHub (Dashboard + WorkOrders) needs a SignalR-over-WSS connection instead of in-process — already a connection abstraction in Phase 9. |
| Hardware abstraction | **Green** | `Settings/Hardware.razor` + `Settings/Mode.razor` are **placeholders today** (audit confirmed Lorem-ipsum stubs, no real impl). QR scanner is the only working hardware path — JS bridge to `html5-qrcode` library; MAUI replaces with `MediaPicker`/native camera. Greenfield. |
| SignalR push | **Green** | `ShopfloorHub` is empty + `ShopfloorNotifier` does broadcast-on-mutation. Reusable as-is. Clients reconnect via Phase 9 4-state banner already shipped. |
| Cross-platform packaging | **Yellow** | Win+Mac smooth (MAUI mature on desktop). iOS+Android need Apple Developer ($99/yr) + Play Console ($25), provisioning, code signing, store review cycles. Estimable, not risky. |
| **Offline-write + sync** | **HIGH RISK** | New domain entirely. No precedent in the legacy codebase. Conflict resolution, idempotency, outbox durability, crash recovery, server-side dedupe — all new code. Per Henry's Q4=strict-online-only, Phase 10 limits offline writes to **append-only entities** (ProductionLog/scan/QcCapture/Oee events). Stateful state-machine entities (WorkOrder.Advance, Spec.Approve) stay strictly online and wait for reconnect. Dedicated sub-plan in §6. |

**Bottom-line:** the migration is mostly mechanical refactor + 1 hard new
subsystem (offline sync). Web Blazor Server keeps running in parallel through
every phase except the final cutover, so rollback is free.

---

## 2. Architecture (target state)

```
┌─────────────────────────────────────────────────────────────────────────┐
│  src/CCL.MES.*   (READ-ONLY baseline; legacy web stays up on its port)  │
│  ┌────────────────────────┐  ┌────────────────────────┐                  │
│  │ CCL.MES.Domain         │  │ CCL.MES.Application    │                  │
│  │ (entities)             │  │ (services + DTOs)      │                  │
│  └─────────┬──────────────┘  └─────────┬──────────────┘                  │
│            │                            │                                 │
│            │              ┌─────────────┴────────────┐                    │
│            │              │ CCL.MES.Infrastructure   │                    │
│            │              │ (DbContext, blob, audit) │                    │
│            │              └──────────┬───────────────┘                    │
│            │                         │                                    │
│            │              ┌──────────┴───────────────┐                    │
│            │              │ CCL.MES.Web              │                    │
│            │              │ (Blazor Server, cookie)  │  ← stays live      │
│            │              └──────────────────────────┘                    │
└────────────┼────────────────────────────────────────────────────────────┘
             │ relative <ProjectReference>   (no file modification)
             ▼
┌─────────────────────────────────────────────────────────────────────────┐
│  CCL-MES-Hybrid/  (NEW top-level sibling of src/ + tests/)               │
│  ┌────────────────────────┐                                              │
│  │ CCL.MES.Shared (NEW)   │  ← DTO/contract library                      │
│  │ - WorkOrderDto         │     (extracted from Application/Dtos.cs)     │
│  │ - SpecDto              │     consumed by both API + Client            │
│  │ - SyncEnvelope         │                                              │
│  └─────────┬──────────────┘                                              │
│            │                                                              │
│  ┌─────────┴──────────────┐    ┌────────────────────────────────┐        │
│  │ CCL.MES.Api (NEW)      │    │ CCL.MES.Hybrid (NEW)           │        │
│  │ ASP.NET Core Web API   │    │ MAUI Blazor Hybrid client      │        │
│  │ - JWT issuer + refresh │    │ - Win + Mac + Android + iOS    │        │
│  │ - REST + SignalR Hub   │◄───┤ - Local SQLite outbox/cache    │        │
│  │ - Idempotency ledger   │    │ - IHardware* per-platform impl │        │
│  │ - Audit emit           │    │ - API client + sync engine     │        │
│  └─────────┬──────────────┘    └────────────────────────────────┘        │
│            │                                                              │
│  ┌─────────┴──────────────┐                                              │
│  │ CCL.MES.SyncEngine     │  ← NEW: outbox + conflict + idempotency      │
│  │ (server-side counter-  │     (reusable by API + future bg workers)    │
│  │  part lives in Api)    │                                              │
│  └────────────────────────┘                                              │
└─────────────────────────────────────────────────────────────────────────┘
```

### Components in the new folder

| Project | Purpose | Notes |
| --- | --- | --- |
| `CCL.MES.Shared` | DTOs + contract envelopes (e.g. `SyncEnvelope<T>`, `IdempotencyKey`) | Pure POCO, no EF, no HTTP. Referenced by both `Api` and `Hybrid`. |
| `CCL.MES.Api` | ASP.NET Core 10 Web API host | References legacy `Application` + `Infrastructure` for service reuse. Owns JWT issuance, SignalR Hub, idempotency ledger, audit emit. |
| `CCL.MES.SyncEngine` | Sync protocol primitives (outbox writer/reader, conflict resolver, op-id generator, retry/backoff) | Used by both Api (server-side dedupe) and Hybrid (client-side outbox). |
| `CCL.MES.Hybrid` | MAUI Blazor Hybrid client | Multi-target: `net10.0-windows`, `net10.0-maccatalyst`, `net10.0-android`, `net10.0-ios`. Local SQLite via `Microsoft.Data.Sqlite`. Reuses Razor pages from a future `CCL.MES.Hybrid.Razor` class library (decided per Q3). |
| `CCL.MES.Hybrid.Razor` (optional, P10.2) | Razor class library holding pages shared between Hybrid + (eventually) a Blazor WebAssembly variant | TBD per Q3. |
| `CCL.MES.Hybrid.Tests` | xUnit, IsolatedSqliteFixture pattern from Phase 9 | Reuse `tests/CCL.MES.Tests` infra. |

---

## 3. Code-sharing strategy with legacy `src/CCL.MES.*` projects

Audit found 3 legacy projects that the new folder must reach into:

- `CCL.MES.Domain` — entities (stable, low churn after Phase 6+7)
- `CCL.MES.Application` — services (wrapped by API controllers)
- `CCL.MES.Infrastructure` — DbContext, EF migrations, blob storage

### Chosen: **Option A — relative project-reference**

`CCL-MES-Hybrid/src/CCL.MES.Api/CCL.MES.Api.csproj`:

```xml
<ItemGroup>
  <!-- Legacy projects — READ-ONLY reference, build-time only.
       We DO NOT modify any file under ../../../src/CCL.MES.*. -->
  <ProjectReference Include="..\..\..\src\CCL.MES.Domain\CCL.MES.Domain.csproj" />
  <ProjectReference Include="..\..\..\src\CCL.MES.Application\CCL.MES.Application.csproj" />
  <ProjectReference Include="..\..\..\src\CCL.MES.Infrastructure\CCL.MES.Infrastructure.csproj" />

  <ProjectReference Include="..\CCL.MES.Shared\CCL.MES.Shared.csproj" />
</ItemGroup>
```

**Pros:**
- Zero duplication. Single source of truth for entities + services.
- Legacy web app and new API run the same business logic until cutover.
- "Read-only" promise upheld at the file level: we add `<ProjectReference>` to
  new csproj files, never edit legacy csproj files.

**Cons:**
- The new folder's CI must check out the legacy folder too (`dotnet restore`
  walks the relative path). Mitigation: monorepo-style git layout, both folders
  in the same repo.
- Any *unilateral* change to legacy Domain/Application breaks the new build.
  Mitigation: Phase 6+7 close-out hardened these areas; Phase 9 test framework
  protects further refactors; new folder pins legacy version via git submodule
  if migration drags past 6 months.
- Coupling makes the eventual "delete legacy folder" cutover slightly heavier
  — we'll have to move the legacy projects into `CCL-MES-Hybrid/legacy/` at
  cutover. Estimable.

### Considered + rejected

- **Option B — fork copy.** Drift becomes inevitable; every legacy bugfix needs
  a parallel patch. Rejected.
- **Option C — move shared projects into a third folder.** Violates "CCL-MES
  KHÔNG đụng" — moving a project IS modification at the git-history level.
  Rejected for Phase 10; revisit at cutover.
- **Option D — pack as NuGet.** Heavy ceremony (private feed, version bumps,
  packaging in CI) for what is effectively a monorepo. Rejected.

### Drift guardrails (mandatory if Option A chosen)

1. **Test suite runs on every PR landing in either folder.** xUnit suite in
   `tests/CCL.MES.Tests` covers Domain + Application + Infrastructure; if a
   legacy PR breaks an Application contract, the new folder's CI catches it.
2. **No new public API in `CCL.MES.Application` without DTO mirror in
   `CCL.MES.Shared`.** This prevents the API + Client drifting from the web app.
3. **Quarterly contract review** during migration window: list every public
   method on every Application service; confirm DTO parity; flag entity leaks.

---

## 4. Auth migration (Cookie → JWT)

| Surface | Today | Target | Cutover |
| --- | --- | --- | --- |
| Web Blazor Server | Cookie `ccl_mes_auth`, 8h sliding | UNCHANGED in P10.x | At final cutover, retire web → redirect to Hybrid app |
| Hybrid client (MAUI) | n/a | JWT access (15m) + refresh (7d), rotation on use | New from P10.1 |
| API | n/a | Issues both cookie (for legacy) + JWT (for clients) at login | New from P10.1 |

### Concrete moves

- **P10.1:** new `POST /api/v2/auth/login` endpoint in `CCL.MES.Api` returns
  `{access, refresh}` JWT pair. Reuses `CCL.MES.Infrastructure.Auth` user store.
  Legacy `/login` cookie path **untouched**.
- **P10.1:** `POST /api/v2/auth/refresh` rotates refresh token (one-time use,
  revocation list in `idempotency_ledger`-style table).
- **P10.1:** all new API endpoints require `[Authorize(AuthenticationSchemes = "Bearer")]`.
- **JWT claims** mirror cookie claims 1:1 (`role`, `userId`, `userName`,
  `language`). RBAC policies (`AdminOnly`, `NpiRead`, etc.) re-registered for
  Bearer scheme. Same policy implementations reused.

### Refresh-token storage on device

- **Win + Mac:** `Microsoft.Maui.Storage.SecureStorage` (Keychain on Mac, DPAPI
  on Win).
- **Android:** Keystore via SecureStorage wrapper.
- **iOS:** Keychain via SecureStorage wrapper.

No plaintext tokens on disk.

---

## 5. Hardware abstraction (per-platform)

Greenfield. `Settings/Hardware.razor` + `Settings/Mode.razor` are placeholders
today.

### Interfaces (in `CCL.MES.Shared`)

```csharp
public interface IBarcodeScannerService {
    Task<bool> IsAvailableAsync();
    Task<string?> ScanOnceAsync(CancellationToken ct);
    IAsyncEnumerable<string> ScanStreamAsync(CancellationToken ct);
}
public interface ILabelPrinterService {
    Task<bool> IsConnectedAsync();
    Task PrintZplAsync(string zpl, CancellationToken ct);
}
public interface IWeighScaleService {
    Task<bool> IsConnectedAsync();
    IAsyncEnumerable<decimal> WeightStreamGramsAsync(CancellationToken ct);
}
public interface IDeviceModeService { // /mode page
    StationMode GetMode(); // Kiosk | Interactive | Headless
    Task SetModeAsync(StationMode mode);
}
public enum StationMode { Kiosk, Interactive, Headless }
```

### Implementations (per-platform partial class pattern, MAUI standard)

| Interface | Win | Mac | Android | iOS |
| --- | --- | --- | --- | --- |
| `IBarcodeScannerService` | USB HID + camera (MediaPicker) | Camera | Camera (CameraX) | Camera (AVFoundation) |
| `ILabelPrinterService` | TCP/USB to Zebra ZPL printers | TCP only | TCP only (USB-OTG later) | TCP only |
| `IWeighScaleService` | Serial port (System.IO.Ports) | Serial (USB-Serial) | USB-OTG serial (later) | n/a |
| `IDeviceModeService` | Preferences API | Preferences | Preferences | Preferences |

Settings pages (`/hardware`, `/mode`) become real once these interfaces ship in
P10.3. They render config UI per platform (e.g. "Choose camera 0/1/2" on Win,
"Pair Bluetooth scanner" on Android).

---

## 6. OFFLINE-WRITE + SYNC (deep section — HIGH RISK)

> **Sub-plan candidate.** This section is dense enough to split into
> `docs/PHASE10-SYNC-SUBPLAN.md` once P10.4 starts. Below is the master
> architecture; the sub-plan will own per-entity conflict policies + test matrix.

### 6.1 Local store on client

- **SQLite** via `Microsoft.Data.Sqlite` (NOT EF Core — too heavy on mobile
  start-up). Lightweight schema, manual SQL or Dapper.
- **Location:** `FileSystem.AppDataDirectory/ccl-mes-hybrid.db` per platform.
- **Tables:**
  - `outbox` — pending writes (see 6.3)
  - `cached_<entity>` — read-side cache of entities flagged offline-safe (WO
    list current shift, Spec current revisions, RawMaterials, Routings)
  - `idempotency_log` — local record of op-ids the server has confirmed
    (avoids re-send after restart if outbox flush was acked but row not deleted)
  - `metadata` — last-sync-timestamp, JWT user, device-id

### 6.2 Outbox pattern (write path)

```
User taps "Finish Op" in MAUI UI
   │
   ▼
1. Generate op-id (GUID v7, time-sortable)
2. Persist row to local `outbox`
       - op_id, entity_type='ProductionLog', operation='insert',
         payload={...}, created_at=now, attempted_at=null, retry_count=0
3. Update local cached view optimistically
4. Return to UI immediately (no network wait)
   │
   ▼
   (sync engine runs in background)
   │
   ▼
5. POST /api/v2/sync/apply with body
       { op_id, entity_type, operation, payload }
       Header: Idempotency-Key: <op_id>
6. Server:
       - Look up op_id in idempotency_ledger
         - HIT  → return cached result (200/4xx as before)
         - MISS → apply operation, write ledger row with result, return
7. Client on 2xx:
       - Mark outbox row as done (or delete + write idempotency_log)
       - Emit local "sync ok" event for UI
8. Client on 4xx (business error):
       - Mark outbox row as poisoned
       - Notify user with reason (e.g. "WO already advanced; reload")
9. Client on network failure:
       - Increment retry_count, backoff exponential (1s, 5s, 30s, 5m, 30m)
       - Retry on next online + on app foreground
```

**Key invariants:**

1. **Outbox write precedes UI ack.** Crash between steps 2–3 = on next launch,
   sync engine drains outbox before any new user action.
2. **Op-id is client-generated + persistent.** Retry uses SAME op-id. Server
   dedupes via `idempotency_ledger`. Double-send is harmless.
3. **No outbox row is deleted before server ack.** Crash before delete = next
   send is a no-op via idempotency_ledger.

### 6.3 Conflict resolution policy (per entity class)

| Class | Examples | Policy |
| --- | --- | --- |
| **Append-only** | `ProductionLog`, `QcCapture` (data rows, not approval gates), scan events, `OeeStartStop`, audit-trail emits | **Offline-safe.** No conflict possible — server just inserts. Op-id dedupe catches double-send. |
| **Stateful with state machine** | `WorkOrder` state advance (Released → InProduction → Completed), `Spec.IsTrashed` flip, `Spec.IsApproved` flip | **Online-only by default; optionally queued with version check.** If queued: client includes `expected_state` in payload; server checks current state; mismatch → 409 with `current_state`, client surfaces "reload required" toast + drops outbox row. |
| **Master data edit** | `Spec.Content`, `Routing.Operations`, `Structure`, `RawMaterial`, `WorkCenter` | **Online-only.** No offline edit of master data; cached for read only. Conflict surface too risky for shop-floor pilot. |
| **Approval gates** | OQC outbound gate, Spec approval, MOQ override approval | **Online-only.** Compliance requires real-time RBAC + audit; never queue. |

### 6.4 Offline-safe vs Online-only classification

| Entity / Action | Offline-safe | Notes |
| --- | --- | --- |
| `ProductionLog` insert (qty, op-id, start/finish) | ✓ | **Pilot module for P10.4.** Append-only, high-frequency, perfect first target. |
| `QcCapture` (in-process inspection data) | ✓ | Append-only. Approval is separate. |
| Scan events (label scan to identify WO/Op) | ✓ | Append-only audit. |
| `WorkOrder.Advance` (state change) | △ optional | Queue with version check OR force online. **Recommend: force online in P10.4**, revisit in P10.5. |
| `OeeStartStop` (machine start/pause/resume/finish) | ✓ | Append-only event stream. |
| `Spec.Approve` | ✗ | Compliance gate. Always online. |
| `Spec.Edit` / `Spec.Revise` | ✗ | Master data conflict surface. Always online. |
| `Spec.Trash` / `Spec.Restore` | ✗ | Stateful, low frequency, can require online. |
| `WorkOrder.Create` | ✗ | Master schedule; planner workflow not shop-floor. |
| `NpiImport` | ✗ | Bulk admin operation; not offline. |
| `Drawings.Upload` | ✗ | Blob storage; large payloads; not safe to queue offline. |
| Audit-log queries | ✗ (read) | Read direct from server; not cached offline. |

**Read-side cache** (always-available even offline): WO list for current shift,
my active Op, Spec revision active for my WO, Routing for my WO, RawMaterial
codes for my WO. Refresh on every successful sync + on demand.

### 6.5 Sync triggers

| Trigger | When | Notes |
| --- | --- | --- |
| Network up | `Connectivity.ConnectivityChanged` → `Internet` | Drain outbox immediately. |
| Periodic | 60s when online + outbox not empty | Background timer (foreground only on mobile; iOS doesn't allow background indefinitely). |
| User action | "Sync now" button in app header | Manual trigger; surface progress + errors. |
| Server hint | SignalR push `dataChanged` → client pulls deltas | Reuses ShopfloorNotifier pattern. |
| App foreground | `App.OnResume` | First action on return from background. |

### 6.6 Crash recovery

| Failure point | Recovery |
| --- | --- |
| Crash after UI tap, before outbox write | Lost (UI was optimistic only). Acceptable — user retries. **Mitigation:** make outbox write synchronous before optimistic UI update; round-trip ~5ms on SQLite. |
| Crash after outbox write, before HTTP | On next launch, drain outbox before any new UI. |
| Crash after HTTP 200, before outbox row delete | Next send is no-op via server idempotency_ledger; row deleted on retry ack. |
| Server crash mid-apply | Idempotency_ledger written transactionally with the apply; partial state impossible. |
| Network partition mid-batch | Each op atomic via Idempotency-Key. Partial batch = some applied, some retried. No fan-out coordination needed. |

### 6.7 Idempotency ledger (server-side)

New table in `CCL.MES.Infrastructure` (added via migration; legacy web app
ignores it). Schema:

```
idempotency_ledger
  op_id        text PK
  user_id      int  not null
  entity_type  text not null
  operation    text not null
  applied_at   datetime not null
  result_code  int  not null    -- HTTP status
  result_hash  text not null    -- sha256 of response body
  retention    datetime not null -- TTL (default 30d post-apply)
```

Background sweep deletes rows past retention. 30d default = covers any
realistic offline window (operator on leave with device pocketed) without
unbounded growth.

### 6.8 Sync engine — own vs library?

Options surveyed:

| Option | Pros | Cons |
| --- | --- | --- |
| **Custom (recommended)** | Full control over conflict policy per entity. Reuses our outbox + idempotency primitives. Lightweight. | More code to write + test. |
| **Microsoft.Datasync.Client (Azure Mobile Apps)** | Mature, MS-supported. | Tied to Azure backend conventions; conflict model doesn't match our state-machine entities. |
| **Couchbase Lite** | Excellent offline-first story. | Heavy SDK; doesn't reuse our SQL DbContext. License: free community ed; check redistribution terms. |
| **Realm Mobile** | Great DX. | License (Atlas Device Sync) is paid past tier. Vendor lock-in. |

**Recommendation:** custom. Our schema is small (`outbox` + `idempotency_ledger`),
our conflict model is policy-per-entity (not generic CRDT), and our test
framework already supports the pattern. License risk = 0.

### 6.9 Test matrix (P10.4 entry gate)

Sync engine must pass before any pilot module ships offline:

1. **Idempotency** — same op-id POSTed N times = single apply + N same responses.
2. **Concurrency** — same entity, 2 clients send conflicting writes → policy
   applies (append-only both apply, stateful 1 wins + 1 gets 409).
3. **Crash mid-write** — kill process between outbox write and HTTP; next
   launch drains correctly.
4. **Crash mid-ack** — kill process between HTTP 200 and outbox delete; next
   launch hits ledger cache + clears.
5. **Network partition** — drop 50% of requests randomly; outbox eventually
   drains within retry budget.
6. **TTL expiry** — ledger row past retention is purged; resending old op-id
   re-applies (acceptable: op older than retention treated as new event).
7. **Order preservation** — same entity ops applied in client-creation order
   per session (op-id GUID v7 sorts time-ascending).
8. **Pilot end-to-end** — 4-hour shift simulation with ProductionLog inserts +
   30% packet loss + 2 random app kills = 100% data delivered.

---

## 7. Roadmap — phased (web Blazor Server stays live through P10.5)

| Phase | Scope | Duration | Risk | Web app status |
| --- | --- | --- | --- | --- |
| **P10.1 — Server API + JWT + Shared** | New `CCL.MES.Api` project hosting existing services. JWT auth issuance + RBAC port. `CCL.MES.Shared` DTO library extracted. **1 endpoint cut over** (`GET /api/v2/workorders`) to prove the pattern + drift guardrails. Idempotency_ledger migration applied. System log viewer endpoint (pending #1) lands here. | 3-5 weeks | Low | Untouched, still serving traffic |
| **P10.2 — MAUI shell + 1 module pilot ONLINE** | `CCL.MES.Hybrid` MAUI project boots on Win + Mac. Login screen → JWT. WorkOrders list page (read-only, online). Reuses Phase 9 4-state reconnect banner. ShopfloorHub WS connection. Templates 3-row pattern (pending #4) ported here as the canonical responsive baseline. | 3-4 weeks | Low | Still live; ops can A/B click "open in hybrid" link |
| **P10.3 — Hardware native** | `IBarcodeScannerService` + `ILabelPrinterService` per-platform. `/hardware` + `/mode` pages real (pending #5). Scanner integration in WorkOrder context. | 2-3 weeks | Medium (per-platform quirks) | Still live |
| **P10.4 — Offline sync engine + ProductionLog pilot** | Full sync architecture (§6) shipped. ProductionLog insert is the **first offline-safe entity**. Test matrix §6.9 must pass before ship. Sub-plan `PHASE10-SYNC-SUBPLAN.md` owns details. | 4-6 weeks | **HIGH** | Still live; offline mode hidden behind feature flag until pilot proves stable |
| **P10.5 — Expand offline + QC modal + role=Qc** | Append-offline for QcCapture (data-only), scan events, OeeStartStop. WorkOrder.Advance with version check (decision per Q-list). IPQC/OQC modal (pending #2) implemented mobile-first. Role=Qc claim policy (pending #3) added. | 3-4 weeks | Medium | Still live; cutover decision deferred until P10.6 |
| **P10.6 — Mobile polish + store + cutover** | Android + iOS UX polish (touch targets, gesture, splash). Code signing, store submissions. Auto-update wired. **Web Blazor Server cutover decision** based on adoption + outstanding bugs. If green: retire legacy web; redirect to Hybrid web variant if needed. | 4-6 weeks | Medium (store review unknowns) | Decision phase — retire vs keep parallel |

**Total estimate:** 19–28 weeks (5–7 months). Buffer ~20% on top.

### Gating between phases

- P10.1 → P10.2: API health endpoints + 1 endpoint round-trip green for 1 week
  on dev; Phase 9 test suite green on legacy folder.
- P10.2 → P10.3: Login + WorkOrders list works on Win + Mac for 2 operators
  in shadow mode (parallel use, no production data writes from hybrid yet).
- P10.3 → P10.4: Hardware integration proven on 1 station with 1 scanner.
- P10.4 → P10.5: Sync test matrix §6.9 green; pilot ProductionLog ran 2 weeks
  on 1 station with 0 data loss.
- P10.5 → P10.6: Full feature parity with web app on offline-safe entities;
  operator UAT signed off.
- P10.6 cutover: 30-day parallel-run dashboards show < 1 incident/week
  attributable to hybrid; rollback plan documented.

---

## 8. Risks + mitigations

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| Sync engine ships with subtle conflict bug → silent data loss | M | **Critical** | Test matrix §6.9 is mandatory gate. Pilot one append-only entity first. Server-side dedupe via idempotency_ledger covers double-send class entirely. |
| MAUI tooling regression on .NET 10 (preview/RC) | M | High | Pin SDK version (`global.json`). Track MAUI release notes weekly. Fall back to .NET 9 LTS if blockers. |
| App Store / Play Store review delays at P10.6 | H | Medium | Submit early dev builds in P10.4 to surface review issues. Reserve 2-week buffer per platform. |
| Legacy + new drift via uncoordinated Application service edits | M | High | Drift guardrails §3. Quarterly contract review. Test suite catches breaks. |
| Cookie + JWT both issued at login = double auth surface to attack | L | High | JWT lifetime short (15m access). Refresh rotation one-time use. Cookie unchanged from current security posture. |
| Hardware abstraction can't unify enough → per-platform code explosion | M | Medium | Start narrow (scanner only in P10.3). Add printer + scale only when concrete request lands. |
| Operator UX regression on mobile (touch targets, off-screen menus) | M | Medium | Pending #4 (3-row template pattern) baked into P10.2 as canonical layout. Touch target audit required at P10.2 exit. |
| Refresh token theft on shared kiosk | M | High | Kiosk mode (P10.3 `/mode`) uses short-lived tokens + auto-logout on idle. Operator role can't access master data anyway. |
| Sync engine perf collapses with > 10k outbox rows | L | High | Outbox flush capped per-tick (200 rows). UI surfaces "many pending" warning at 1k. Architectural ceiling: 100k rows = abort with admin alert. |
| Code-sharing via project-reference breaks under monorepo CI checkout | L | Low | Verify on first CI run. Fall back to git submodule if needed. |

---

## 9. Pending smaller items folded in

| Pending item | Folded into | Notes |
| --- | --- | --- |
| **System log viewer** (admin-only timeline of audit + error log) | P10.1 | New `GET /api/v2/admin/system-log` endpoint. UI port to Hybrid in P10.2. Web app gets the endpoint too via shared API (read-only, no impact). |
| **IPQC / OQC modal** (in-process / outbound QC capture UI) | P10.5 | Mobile-first design; offline-safe for IPQC data (append-only); OQC approval stays online. |
| **Role = Qc** (RBAC role for QC-only personas) | P10.5 | New claim type wired into JWT in P10.1; UI gates added per page in P10.5. Legacy web app picks it up via cookie claim same time. |
| **Templates 3 dòng** (3-row responsive layout pattern Henry asked for in Phase 9) | P10.2 | First page that lands in MAUI (WorkOrders list) ships with this baseline; all subsequent pages reuse the pattern. |
| **/hardware + /mode** (Settings placeholders) | P10.3 | Now have real context (per-station config + kiosk mode). Implementation lands with hardware abstraction. |

---

## 10. Constraints (binding for every PR landing in this folder)

1. Legacy `src/CCL.MES.Domain/`, `src/CCL.MES.Application/`,
   `src/CCL.MES.Infrastructure/`, `src/CCL.MES.Web/` are **READ-ONLY baseline**
   — no source-file modification. `<ProjectReference>` to legacy projects is
   allowed; that's the chosen sharing strategy.
2. Project folders outside this repo are forbidden: `Ops Control v1.2/`,
   `SpecHub/`, `CMES/`, `Old ver/`. Pattern study allowed (e.g. Ops Control
   Lesson 27 responsive); copy/edit forbidden.
3. Reuse Phase 9 test framework patterns for every new service / sync component.
   IsolatedSqliteFixture + InMemoryAuditWriter scale fine; if sync engine needs
   a faster fixture, file a dedicated PR for the fixture FIRST.
4. **A→B→C SAFE** for any data path change — add new schema, dual-write,
   migrate, drop old. P10.1 specifically: refresh tokens live in an **in-memory
   store** (no DB schema touched); persistent store deferred to a separate PR.
5. JWT migration does not weaken RBAC. Policies port 1:1; new policies require
   explicit review.
6. **No data loss in sync engine** — outbox durable across crashes, idempotent
   replay, server-side dedupe.
7. Web Blazor Server keeps running until P10.6 cutover decision. No
   intermediate "we're partway, please switch" prompts.
8. **Henry's Q4 override (strict online-only):** Phase 10 ships ZERO stateful
   offline-queue paths. Offline writes are restricted to append-only entities
   (ProductionLog / scan / QcCapture / Oee events). WorkOrder.Advance +
   Spec.Approve + master-data edits stay strictly online — clients wait for
   reconnect. Revisit in a post-Phase-10 phase if operator UX demands it.
9. **Henry's Q6 override (Win + Mac desktop first):** MAUI target frameworks in
   P10.2 are `net10.0-windows10.0.19041.0` + `net10.0-maccatalyst` only.
   `net10.0-android` + `net10.0-ios` deferred to a post-Phase-10 mobile phase.

---

## 11. Q-list — locked 2026-06-03

| # | Question | Decision |
| --- | --- | --- |
| **Q1** | Folder name | **`CCL-MES-Hybrid/`** (top-level sibling of `src/` + `tests/` inside the CCL-MES git repo) |
| **Q2** | Code sharing | **Option A** — relative `<ProjectReference>` to legacy `src/CCL.MES.{Domain,Application,Infrastructure}` |
| **Q3** | Razor reuse | **Razor class library** (`CCL.MES.Hybrid.Razor`) shared between MAUI Hybrid + future WASM variant — lands in P10.2 |
| **Q4** | Offline policy | **Strict online-only for stateful entities.** Offline writes restricted to append-only (ProductionLog/scan/QcCapture/Oee events). WorkOrder.Advance + Spec.Approve + master-data edits wait for reconnect. No offline-queue stateful in Phase 10. |
| **Q5** | Sync library | **Custom** (license=0, schema fit, reuses Phase 9 test infra) |
| **Q6** | Target platforms | **Win + Mac desktop only in Phase 10.** `net10.0-windows10.0.19041.0` + `net10.0-maccatalyst`. Android/iOS tablet defer to post-Phase-10 phase. |
| **Q7** | JWT lifetime | Access 15m + refresh 7d, sliding refresh-token rotation on every use (one-time-use refresh tokens) |
| **Q8** | Local read-cache scope | Current shift in P10.4; expand to 7-day in P10.5 |
| **Q9** | Sync trigger | SignalR push hint + 60s poll fallback |
| **Q10** | Pilot module for offline (P10.4) | ProductionLog (highest frequency, append-only) |
| **Q11** | Idempotency ledger retention | 30 days |
| **Q12** | System log viewer rollout | API endpoint in P10.1, Hybrid UI in P10.2, web UI port deferred |
| **Q13** | Web app cutover decision | Criteria locked now: parallel-run < 1 incident/week for 30 consecutive days |

---

## P10.1 status — implemented 2026-06-03

P10.1 ships the foundation:

1. **`CCL-MES-Hybrid.sln`** at folder root — separate from legacy
   `CCL.MES.sln` (which still builds the production web app).
2. **`src/CCL.MES.Shared/`** — DTO + envelope POCO library. Auth DTOs
   (`LoginRequest`, `LoginResponse`, `RefreshTokenRequest`, `UserInfo`),
   pagination envelope (`PagedResponse<T>`), error envelope (`ApiError`),
   sync envelope placeholder (`SyncEnvelope<T>` for P10.4).
3. **`src/CCL.MES.Api/`** — ASP.NET Core 10 Web API.
   - JWT Bearer auth with HS256 signing. Access token 15m, refresh 7d
     (Q7). Refresh token rotation on use (one-time use).
   - In-memory `IRefreshTokenStore` (P10.1 default; persistent store
     deferred per Q4 = no DB schema touched in P10.1).
   - RBAC policies (`AdminOnly`, `NpiRead`, `NpiSpecRead`, `QcRead`)
     ported 1:1 from `src/CCL.MES.Web/Program.cs:177–199` for Bearer scheme.
   - Endpoints for 9 legacy Application services + audit (system-log viewer
     per Q12). Thin wrappers; controllers translate Application DTOs.
   - SignalR `ShopfloorHubV2` mirrors the legacy `ShopfloorHub` broadcast
     pattern; new clients connect over WSS with `?access_token=` query.
     Legacy in-process Web Hub stays untouched.
   - Default port `5100` (legacy keeps `5000`/`5001`).
4. **`tests/CCL.MES.Api.Tests/`** — xUnit + `WebApplicationFactory`
   integration suite. JWT round-trip, refresh-rotation, RBAC denial,
   ShopfloorHub broadcast smoke.

Out-of-scope for P10.1 (deferred to later phases):

- MAUI Hybrid client project + Razor class library (P10.2).
- Hardware abstraction (P10.3).
- Offline-sync engine + outbox + idempotency ledger persistent table (P10.4).
- Refresh-token persistence to DB (early P10.4 or earlier under separate PR).
- Audit-log export endpoint AdminOnly (already shipped in legacy PR #66 —
  the legacy controller is reachable via cookie auth; API mirror lands when
  audit-log UI ports to Hybrid in P10.2).

---

## Next: P10.2 entry decision

Pilot module pick (Henry chooses at P10.1 merge): WorkOrder list (drawer +
card UI already built in legacy) or NPI grid read-only (lighter risk).
Default suggestion captured in P10.1 PR description.
