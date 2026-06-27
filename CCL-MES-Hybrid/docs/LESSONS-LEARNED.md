# CCL-MES — Lessons Learned (canonical index)

> **Source of truth** for every bug class this codebase has paid
> for. One row per lesson, fixed format:
>
> | Triệu chứng | Root cause | Fix | Cơ chế chặn tái phát |
>
> **Append-only.** Every new lesson MUST land here with a backing
> test / script / rule — otherwise it's prose and will recur. See
> the "Adding a new lesson" section at the bottom.
>
> **Cross-references**: this file is the index. Detailed RCAs live
> in companion docs; this file links them and codifies the guard
> mechanism for each so a session reading just this file can ship
> safely. Companions:
>
> - [`STACKED-PR-CHECKLIST.md`](./STACKED-PR-CHECKLIST.md) — 7 hard
>   rules for stacked-PR merges, scripts, and gate scripts. Rules
>   referenced as **R1**..**R7** below.
> - [`LESSONS-EF-SQLITE-P10.7a-1.md`](./LESSONS-EF-SQLITE-P10.7a-1.md)
>   — detailed RCA for EF Core + SQLite optimistic concurrency, the
>   source for lessons L11/L12/L13 below.
> - [`p10.6-screens/HOTFIX-RENDERER-DEAD-RECURRENCE.md`](./p10.6-screens/HOTFIX-RENDERER-DEAD-RECURRENCE.md)
>   — detailed RCA for the Razor renderer-dead trio (L1/L2/L3).
> - [`p10.6-screens/HOTFIX-SETTINGS-404-PROVEN.md`](./p10.6-screens/HOTFIX-SETTINGS-404-PROVEN.md)
>   — paste-the-output proof of root cause for L7 (stale binary).
> - [`/docs/LESSONS_LEARNED.md`](../../docs/LESSONS_LEARNED.md) —
>   legacy phase-by-phase notes from Phase 6/7/8. **Frozen**;
>   forward-looking lessons land HERE.

---

## Index (lessons newest-first within each cluster)

### Renderer + Razor host

- [L1 — Heartbeat outer-guard + GlobalErrorLogger (background unhandled exception kills renderer dispatcher)](#l1)
- [L2 — `ChangeEventArgs` ↔ `<InputText>` throw on every keystroke (renderer-dead silent crash)](#l2)
- [L3 — `RendererCrashBoundary` must wrap every layout (without it L1/L2 surface as "click does nothing")](#l3)
- [L4 — Lessons codified ≠ documented (prose-only lessons recur; every lesson needs a canary test)](#l4)

### Merge + branch hygiene

- [L5 — Lessons don't reach main (merge/revert can silently drop the hardening commits)](#l5)
- [L6 — Stacked-PR mechanics (R1–R3: `--base` explicit, no `--delete-branch` mid-stack, replacement PR for cascade-close)](#l6)

### Binary / database deployment

- [L7 — Stale API binary mmap'd in memory while disk DLL is fresh (Settings 404 incident)](#l7)
- [L8 — Migrations not applied → blind HTTP 500 on operator UI (R5: Henry-action block + boot probe)](#l8)
- [L9 — Verify scripts assume baseline DB state (R6: self-prep on the copy)](#l9)

### Test-green / runtime-broken

- [L10 — Wire-path drift (DbContext-only tests miss URL rename, query-param rename, AdminOnly→Authenticate; R7.3 wire-mirror)](#l10)

### EF Core + SQLite

- [L11 — SQLite UPDATE trigger fires AFTER EF's `RETURNING` (stale RowVersion echoed back)](#l11)
- [L12 — `DbUpdateConcurrencyException` poisons the change tracker for downstream middleware](#l12)
- [L13 — `[Timestamp]` doesn't auto-populate `byte[]` on INSERT under SQLite (no INSERT trigger = empty ETag)](#l13)

### Operator scripts + DB drift

- [L14 — Operator script vs server-keep-alive DB drift (R7.1 `[ctx] DB=` + R7.2 self-managed lifecycle)](#l14)

### HTTP contract

- [L15 — HTTP status overload — 409 (concurrency) vs 422 (semantic guard) vs 428 (precondition missing)](#l15)

### Razor + tooling

- [L16 — Gate scripts must strip BOTH `@* *@` block + `//` line comments (R4)](#l16)
- [L17 — Seed early-exit guards (`db.X.AnyAsync()` short-circuit) skip kind-specific data when a different kind is already present](#l17)
- [L18 — `--urls` / `ASPNETCORE_URLS` overridden by hardcoded `Kestrel:Endpoints` in appsettings.json (API stuck on one port)](#l18)

### State machine + UI dispatch

- [L19 — Dispatch + display MUST key on canonical MesPhase, not legacy CurrentStep — and every phase that lacks a real dashboard needs a placeholder in a single phase→content map](#l19)
- [L21 — Phase-changing actions MUST auto re-fetch summary + re-dispatch; the dashboard that just acted does NOT own its replacement](#l21)

### Security flags + boot-time visibility

- [L20 — Security-critical default-ON flags MUST parse typo-safe + emit boot-probe log line; silent flip to OFF on a misspelled env value is a compliance hole](#l20)

### Checkpoint hygiene + binary freshness

- [L22 — Stale keep-alive binary returns silent 404; checkpoint MUST kill stale :5100 + build-sanity-probe before route exercise](#l22)
- [L23 — Checkpoint MUST walk the real materialisation path; shortcut INSERTs mask data-bed gaps (seed-trống / profile-trống) that operators WILL hit](#l23)
- [L24 — ALL data tables freeze their header on scroll via ONE global rule; never add per-table sticky CSS — and verify the maccatalyst build by exit code, never by grepping stdout](#l24)

---

## Lesson cards

<a id="l1"></a>
### L1 — Heartbeat outer-guard + GlobalErrorLogger

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Mac Catalyst app boots, but clicking buttons / typing in inputs silently does nothing. No spinner, no error banner. Renderer dispatcher dead. Henry blocked at Login step 0. |
| **Root cause** | `DeviceHeartbeatHostedService.RunLoopAsync` had no outer try/catch. `PeriodicTimer.WaitForNextTickAsync` threw on `OperationCanceledException` past the loop. Unhandled exception killed the Blazor dispatcher background thread, freezing all UI work. No global crash logger meant the failure was invisible — no Console.WriteLine, no `error.log` entry. |
| **Fix** | (a) `GlobalErrorLogger.Install()` BEFORE `MauiApp.CreateBuilder()` — wires `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` (with `SetObserved()` to prevent process kill); rolling 50-line file at `FileSystem.AppDataDirectory/logs/error.log`. (b) Outer try/catch wrapping `RunLoopAsync` end-to-end; new `SafeWaitNextTickAsync` wrap on `PeriodicTimer.WaitForNextTickAsync`. (c) `App.xaml.cs` per-service try/catch INSIDE the foreach (was wrapping the whole loop, so one bad service killed the rest). |
| **Cơ chế chặn tái phát** | `tests/CCL.MES.Hybrid.Client.Tests/.../BackgroundCrashContainmentTests.cs` — 6 fixtures: `MauiProgram` install ordering (BEFORE CreateBuilder), GlobalErrorLogger wires both handlers + sets SetObserved, heartbeat outer-guard + SafeWaitNextTickAsync present, App.xaml.cs per-service try INSIDE foreach. Any future PR that drops one of these arrangements fails CI with the offending file path. **Source RCA**: [`HOTFIX-RENDERER-DEAD-RECURRENCE.md`](./p10.6-screens/HOTFIX-RENDERER-DEAD-RECURRENCE.md) Layer 1. |

<a id="l2"></a>
### L2 — `ChangeEventArgs` ↔ `<InputText>` throw on every keystroke

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Login form: typing a single character throws `ChangeEventArgs cannot be converted to System.String`. The throw poisons the dispatcher → Tab, Enter, button click ALL stop working. Operator sees "click does nothing" — same outward symptom as L1, different root cause. |
| **Root cause** | `<InputText>` is the legacy Razor primitive bound to `string` via `@bind-Value`. Combining it with `@bind-Value:event="oninput"` + `@onkeydown="..."` makes Blazor pass `ChangeEventArgs` to a handler typed `string` — type mismatch throws on every keystroke. Live on main from before P10.5g; operators previously avoided it by paste-typing instead of character-by-character entry. |
| **Fix** | Replace every `<InputText>` with plain `<input> + @bind + @bind:event="oninput" + @onkeydown`. Drop the EditForm + DataAnnotationsValidator wrapper; inline the Required check in the submit handler. Pattern applied to Login.razor + every Settings input + every PREPRESS input. |
| **Cơ chế chặn tái phát** | (a) `tests/.../BackgroundCrashContainmentTests.cs` — repo-wide tripwire grep: any `.razor` file containing `<InputText` + `@bind-Value:event="oninput"` fails CI with file path. (b) **STACKED-PR-CHECKLIST R4**: gate scripts strip `@* *@` block + `//` line comments BEFORE grep so doc-strings restating "no `<InputText>`" don't false-positive. Canonical snippet in `STACKED-PR-CHECKLIST.md` R4. |

<a id="l3"></a>
### L3 — `RendererCrashBoundary` must wrap every layout

| Field | Detail |
| --- | --- |
| **Triệu chứng** | When L1 or L2 fires, the renderer hangs without surfacing ANY operator-visible signal. Investigation requires Safari → Develop → Mac Catalyst → Console — a path operators don't know. |
| **Root cause** | Blazor's default behaviour on unhandled component-render exception is "freeze the affected component subtree silently". Without an `ErrorBoundary`-derived component wrapping `@Body`, the crash is invisible. |
| **Fix** | New `RendererCrashBoundary.razor` subclasses `ErrorBoundaryBase`; `OnErrorAsync` logs sentinel `[renderer-crash]` to `console.error` + `Console.WriteLine`; renders VN fallback card with "Tải lại" (Reload) button. Wrap `@Body` in both `MainLayout.razor` AND `EmptyLayout.razor` (the latter covers Login.razor, otherwise crashes during login render stay invisible). |
| **Cơ chế chặn tái phát** | `tests/.../MacCatalystKeyboardFixRegressionTests.cs` — 6 fixtures asserting: both layouts contain `<RendererCrashBoundary>` wrap, RendererCrashBoundary inherits ErrorBoundaryBase, uses OnErrorAsync, emits `[renderer-crash]` sentinel, MacCatalystKeyboardFix carries `[js-uncaught]` + `[js-unhandled-rejection]` capture. **Source RCA**: [`HOTFIX-RENDERER-DEAD-RECURRENCE.md`](./p10.6-screens/HOTFIX-RENDERER-DEAD-RECURRENCE.md) Layer 2. |

<a id="l4"></a>
### L4 — Lessons codified ≠ documented (prose-only lessons recur)

| Field | Detail |
| --- | --- |
| **Triệu chứng** | The same renderer-dead bug surfaced THREE times across P10.5g, P10.6a, P10.6a-hotfix because the lessons were written in markdown but the fix was never converted to a canary test. The fourth time it hit production, the lessons file said "do X" — but X had been removed from main during a subsequent merge (see L5). |
| **Root cause** | Human-readable prose drifts. Markdown is not enforceable. A lesson that says "always wrap layout with RendererCrashBoundary" is only true until the next developer skips it — markdown doesn't fail CI. |
| **Fix** | Every lesson MUST land alongside a test that fails if the lesson's invariant is violated. The L1/L2/L3 fix shipped 12 canary tests inside `tests/.../Layout/` precisely for this — the markdown lesson now has a CI-enforced mirror. |
| **Cơ chế chặn tái phát** | This file's "Adding a new lesson" section at the bottom requires the test/rule mechanism column to be non-empty. PR review rejection if a new lesson is added with `Cơ chế chặn tái phát = (none yet)`. |

<a id="l5"></a>
### L5 — Lessons don't reach main (merge/revert can silently drop the hardening commits)

| Field | Detail |
| --- | --- |
| **Triệu chứng** | The 3 P10.5g hardening commits (`c4ad008` + `0604df0` + `3fff2d0`) were authored, reviewed, and the PR title implied they landed in #90. P10.6a Henry-verify on Catalyst showed every L1/L2/L3 symptom — the bugs they fixed were back. Grep on main confirmed: `0 of 3 lessons present`. |
| **Root cause** | Merge strategies (squash + rebase + revert sequences) can drop intermediate commits. PR titles describe intent, not what actually reached main. Only `git log main -- <file>` is authoritative. |
| **Fix** | Step 2 of every renderer-dead RCA: grep main for the lesson sentinel BEFORE writing new code. If absent on main, the lesson was never delivered — re-ship it. Example: `grep -c "SafeWaitNextTickAsync" CCL-MES-Hybrid/src/CCL.MES.Hybrid/Services/DeviceHeartbeatHostedService.cs` on main should return non-zero. |
| **Cơ chế chặn tái phát** | (a) Canary tests in `tests/.../Layout/` (L1/L2/L3) fail CI if the hardening drops out of any branch. (b) **STACKED-PR-CHECKLIST R2**: no `--delete-branch` mid-stack — branch deletion has cascade-close side effects (see L6). (c) Document grep-confirmation in the RCA: "lessons grep on main pre-PR #91 → 0 of 3 present" is the proof shape. |

<a id="l6"></a>
### L6 — Stacked-PR mechanics

| Field | Detail |
| --- | --- |
| **Triệu chứng** | (a) Stacked PR #92 had `base=main` instead of `base=feat/p10.6a-...`, silently breaking the stack invariant — no warning from `gh`. (b) Step 1 of P10.6 merge used `gh pr merge --rebase --delete-branch`, which cascade-closed PR #92 (its base branch was the just-deleted branch). (c) Cascade-closed PRs with force-pushed heads cannot be reopened — GitHub's `reopenPullRequest` API refuses. |
| **Root cause** | `gh pr create` defaults `--base` to the repo default (main) when omitted. `--delete-branch` deletes the remote branch immediately. Force-pushed heads invalidate the reopen path. |
| **Fix** | **R1** — every `gh pr create` for a stacked PR MUST pass `--base <prev-PR-head-branch>` explicitly. **R2** — never `--delete-branch` mid-stack; defer cleanup to a single post-merge sweep at the end. **R3** — when cascade-closed, jump straight to **Option Y** (replacement PR with new number pointing at the same head commit); don't waste time on Option X (recreate-base + reopen). |
| **Cơ chế chặn tái phát** | [`STACKED-PR-CHECKLIST.md`](./STACKED-PR-CHECKLIST.md) Rules 1/2/3 carry the exact commands + pre-flight `gh pr view <N> --json baseRefName` verification. CLAUDE.md links the checklist at the top so every session loads it before touching PR mechanics. |

<a id="l7"></a>
### L7 — Stale API binary mmap'd in memory while disk DLL is fresh

| Field | Detail |
| --- | --- |
| **Triệu chứng** | New API controller (`SettingsController`) returns HTTP 404 on every Settings route. Login works (`AuthController` exists in the running process). xUnit suite for SettingsController passes 100%. `dotnet build` shows no errors. The disk DLL has the controller — yet `curl localhost:5100/api/v2/settings/me` returns 404. |
| **Root cause** | `lsof -nP -iTCP:5100 -sTCP:LISTEN` showed PID `81851` started at 1:38PM. The disk DLL was rebuilt at 16:04 (commit `4c5068d`). macOS / Linux processes retain their mmap'd image — `dotnet build` rewrites the file but the running process keeps executing the version it loaded at boot. Restarting the build is NOT restarting the binary. |
| **Fix** | (a) Verify-script pattern: kill any process on the target port BEFORE running the build + start. (b) Boot probe in `Program.cs` that prints the commit SHA at startup — operator can eyeball "is this the binary I just built". (c) Paste-the-output discipline (see [`SKILLS.md`](./SKILLS.md) "RCA proven, not 'most likely'"). |
| **Cơ chế chặn tái phát** | `CCL-MES-Hybrid/scripts/verify-p10.X.sh` template: `lsof | xargs kill` at top, `dotnet build` + `dotnet CCL.MES.Api.dll &` fresh, then probe routes. **STACKED-PR-CHECKLIST R7.2** mandates self-managed API lifecycle on every checkpoint script. **Source RCA**: [`HOTFIX-SETTINGS-404-PROVEN.md`](./p10.6-screens/HOTFIX-SETTINGS-404-PROVEN.md). |

<a id="l8"></a>
### L8 — Migrations not applied → blind HTTP 500 on operator UI

| Field | Detail |
| --- | --- |
| **Triệu chứng** | P10.7a-1.3 Catalyst checkpoint failed login with a blind `HTTP 500 · http.non_success`. No diagnostic in the operator UI; the agent had to remote-debug the server log to discover a "no such column: WorkOrders.MesPhase" SQLite error. |
| **Root cause** | The shipped server expected schema from 3 new EF migrations that the operator's `data/ccl_mes.db` lagged. The PR's Henry-action block listed `git checkout <branch>` + `dotnet run` but omitted the `dotnet ef database update` step needed to apply the migrations. |
| **Fix** | **R5** — Henry's reproducibility block for any PR with EF migrations MUST be: `git fetch origin && git checkout <branch>` → `dotnet ef database update --connection "Data Source=..."` → verify-script. **Defence-in-depth**: pending-migration boot probe in `CCL.MES.Api/Program.cs` queries `Database.GetPendingMigrationsAsync()` at boot, logs `WARNING — DATABASE HAS UNAPPLIED MIGRATIONS`, and refuses to start if `Database:FailOnPendingMigrations=true`. |
| **Cơ chế chặn tái phát** | (a) Boot probe in `Program.cs` (already shipped) makes the failure mode loud at boot instead of mute at first request. (b) **STACKED-PR-CHECKLIST R5** mandates the EF update step in the Henry-action block. (c) PR template warns when migrations changed. |

<a id="l9"></a>
### L9 — Verify scripts assume baseline DB state

| Field | Detail |
| --- | --- |
| **Triệu chứng** | A verify script with a probe like "pre-migration: column X absent" works the day it's authored (dev DB sits at previous migration baseline) but breaks every subsequent re-run once the dev cycle advances dev DB past the target migration. False FAIL blocks the gate even though the actual round-trip test below works correctly. |
| **Root cause** | The script copies the live DB to `$TEST_DB` then probes without resetting baseline. Once dev DB advances, the probe sees the migration already applied and reports a spurious "Pre-migration: column already present" fail. |
| **Fix** | **R6** — every verify script that runs migration round-trips MUST insert `dotnet ef database update "$PREVIOUS_MIGRATION" --connection "Data Source=$TEST_DB"` between the DB copy and the first probe. Run with `--no-build` (the build step earlier already produced the assembly). |
| **Cơ chế chặn tái phát** | [`STACKED-PR-CHECKLIST.md`](./STACKED-PR-CHECKLIST.md) Rule 6 carries the canonical snippet + the cross-assembly caveat (mid-stack verify on a branch with partial migration sources). New verify scripts copy the snippet verbatim. |

<a id="l10"></a>
### L10 — Wire-path drift (DbContext-only tests miss URL rename)

| Field | Detail |
| --- | --- |
| **Triệu chứng** | P10.7a-2.2 force-phase endpoint genuinely persisted the SYS_RECOVERY audit row (confirmed via `sqlite3 SELECT * FROM AuditLogs`). 15 xUnit fixtures asserted the row via `db.AuditLogs.Where(...)` + passed. Catalyst checkpoint script queried the wire and reported "audit row missing". Apparent paradox: write OK, read empty. |
| **Root cause** | (a) Script queried `/api/v2/admin/audit/log` → 404; real route is `/api/v2/audit/log` (no `/admin` segment despite `[Authorize(Policy = "AdminOnly")]`). (b) Script sent `?targetType=WorkOrder&targetId=N`; endpoint accepts `?search/action/actor/from/to/page/pageSize` — extra params silently ignored. Both bugs invisible to DbContext-only tests. |
| **Fix** | **R7.3** — every wire-path probe in an operator script MUST be backed by an integration test hitting the SAME URL + SAME query params via TestServer. Example fix: `AdminWorkOrdersForcePhaseTests.Sys_recovery_audit_row_visible_via_wire_audit_log_endpoint` calls `client.GetAsync("/api/v2/audit/log?action=SYS_RECOVERY&page=1&pageSize=50")` and asserts the same substring shape. |
| **Cơ chế chặn tái phát** | [`STACKED-PR-CHECKLIST.md`](./STACKED-PR-CHECKLIST.md) Rule 7.3. Bunit tests in `PrepressDashboardTests.cs` follow the same pattern — every wire-touching fixture lists its TestServer mirror in a comment. **Source RCA**: [`LESSONS-EF-SQLITE-P10.7a-1.md`](./LESSONS-EF-SQLITE-P10.7a-1.md) Lesson 4. |

<a id="l11"></a>
### L11 — SQLite UPDATE trigger fires AFTER EF's `RETURNING`

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Advance endpoint returned 200 with an `ETag` HTTP header identical to the request's `If-Match`. Test `Advance_with_valid_IfMatch_returns_200_with_new_ETag_in_body_and_header` failed `Assert.NotEqual(oldEtag, body.ETag)` with both sides showing `"ctIqhIj/ME4="`. |
| **Root cause** | EF Core 10 + SQLite emits `UPDATE … RETURNING RowVersion`. SQLite per-row triggers fire AFTER the UPDATE statement executes but BEFORE row-level RETURNING reads. So EF gets back the application-set value (== old) not the trigger-bumped value. |
| **Fix** | Re-read via `AsNoTracking + Select(w => w.RowVersion)` AFTER `SaveChangesAsync`. The `AsNoTracking` is essential — without it the change tracker returns the cached stale instance. The `Select` projection avoids materialising a full entity. |
| **Cơ chế chặn tái phát** | (a) Detailed RCA in [`LESSONS-EF-SQLITE-P10.7a-1.md`](./LESSONS-EF-SQLITE-P10.7a-1.md) Lesson 1. (b) Integration test `Advance_with_valid_IfMatch_returns_200_with_new_ETag_in_body_and_header` locks the contract. (c) Pattern reused in `PrepressController.CommitAndAuditAsync` for the post-write RowVersion re-read. |

<a id="l12"></a>
### L12 — `DbUpdateConcurrencyException` poisons the change tracker

| Field | Detail |
| --- | --- |
| **Triệu chứng** | N=50 soak test threw the EF concurrency exception PAST the controller's `try/catch`. The `IdempotencyMiddleware`'s downstream `SaveChangesAsync` (writing the response envelope row) re-tried the failed UPDATE with the same tracked entity + same stale RowVersion, throwing again. TestServer surfaced the second throw as an unhandled exception. |
| **Root cause** | After `SaveChanges` throws `DbUpdateConcurrencyException`, EF DbContext STILL holds the failed entity in `EntityState.Modified`. Change tracker doesn't auto-detach on exception — assumption is calling code will `Reload()` + retry. Per-request scoped DbContext shared between controller + middleware means the middleware's subsequent `SaveChanges` re-attempts the failed entry. |
| **Fix** | In the controller's `catch (DbUpdateConcurrencyException)`: `if (_db is DbContext dbCtx) dbCtx.ChangeTracker.Clear();` BEFORE emitting the 409 response. Detaches every tracked entity in one call so downstream `SaveChanges` starts from an empty change set. |
| **Cơ chế chặn tái phát** | (a) Detailed RCA in [`LESSONS-EF-SQLITE-P10.7a-1.md`](./LESSONS-EF-SQLITE-P10.7a-1.md) Lesson 2. (b) Soak tests `Concurrent_advance_N_equals_50_yield_one_winner` + `Concurrent_prepress_row_updates_N_equals_10_yield_consistent_rollup` (Trait=Soak) lock the post-exception behaviour. (c) Pattern reused in `PrepressController.HandleConcurrencyAsync`. |

<a id="l13"></a>
### L13 — `[Timestamp]` doesn't auto-populate `byte[]` on INSERT under SQLite

| Field | Detail |
| --- | --- |
| **Triệu chứng** | bUnit tests + first wire probes for `Summary` returned **empty** `eTag` strings for freshly-seeded WOs. Wire log showed `eTag: ""`; the next advance's `If-Match: ""` → 428. |
| **Root cause** | EF Core `[Timestamp]` semantics rely on SQL Server auto-populating the column at INSERT time. SQLite has no equivalent. The earlier migration added an UPDATE trigger but not an INSERT trigger, so newly-inserted rows kept the EF default of an empty `byte[]`. |
| **Fix** | Migration `AddWorkOrderRowVersionInsertTrigger` with both backfill SQL + INSERT trigger: `AFTER INSERT WHEN length(NEW.RowVersion) = 0 BEGIN UPDATE … SET RowVersion = randomblob(8) … END`. The `length = 0` guard skips inserts that already populated the column (replication targets, test fixtures). |
| **Cơ chế chặn tái phát** | (a) Detailed RCA in [`LESSONS-EF-SQLITE-P10.7a-1.md`](./LESSONS-EF-SQLITE-P10.7a-1.md) Lesson 3. (b) Integration test `Newly_seeded_WO_returns_non_empty_ETag` locks the post-INSERT behaviour. (c) Standing rule: every new entity using `[Timestamp]` ships UPDATE + INSERT triggers TOGETHER in the same migration — don't repeat the "2 PRs to make it work" cycle. |

<a id="l14"></a>
### L14 — Operator script vs server-keep-alive DB drift

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Catalyst checkpoint script reported `audit row missing` while the live server (started in a separate terminal) showed the WO state correctly transitioned. SQLite Error 14 ("unable to open database file") in one direction, invisible state drift in the other. |
| **Root cause** | Operator manually coordinated `ConnectionStrings__Default` + `ASPNETCORE_URLS` env across two terminals. The checkpoint script printed no `[ctx] DB=` line so the mismatch wasn't visible until probes returned wrong data. |
| **Fix** | **R7.1** — every script touching a DB MUST print `[ctx] DB=<abs-path>` + `DB sha8=...` in its first 10 lines. Operator eyeballs two scripts' DB sha8 to confirm they're pointed at the same file. **R7.2** — every `checkpoint-*` script MUST self-manage its API lifecycle (probe `/health`, reuse if up, else auto-boot pinned to the same DB the script is mutating, trap EXIT to kill). `--keep-alive` flag leaves the process running for UI-verify use. |
| **Cơ chế chặn tái phát** | [`STACKED-PR-CHECKLIST.md`](./STACKED-PR-CHECKLIST.md) Rules 7.1 + 7.2. Templates in `CCL-MES-Hybrid/scripts/checkpoint-7a-2.sh` + `checkpoint-7b-2.sh` carry the canonical [ctx] header + auto-boot snippet. New scripts copy verbatim. |

<a id="l15"></a>
### L15 — HTTP status overload (409 vs 422 vs 428)

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Early P10.7a-1 commits returned 409 for both "ETag stale" AND "WO is in wrong phase for this action". Client UI had no way to distinguish "tap again" (concurrency drift) from "this action is impossible right now" (semantic guard). Operator banner conflated the two cases. |
| **Root cause** | RFC 7232 reserves 409 Conflict for concurrency-related conflicts (optimistic-locking RowVersion mismatch); semantic guards (state-machine "invalid phase for this transition") deserve 422 Unprocessable Entity, missing precondition headers deserve 428. Conflating overloads the status code. |
| **Fix** | Strict mapping shipped contract-wide: **409** = stale RowVersion / concurrency drift (operator retries with fresh ETag). **422** = semantic guard (operator can't proceed without different input — e.g. `wo.invalid_phase`, `prepress.invalid_reason_code`, `prepress.invalid_ng_note`). **428** = missing required precondition header (`If-Match` absent — operator must reload + retry). **400** = malformed request (missing `Idempotency-Key`). |
| **Cơ chế chặn tái phát** | (a) Integration tests per status code: `Put_material_stale_IfMatch_returns_409` + `Put_material_invalid_status_returns_422` + `Put_material_missing_IfMatch_returns_428` + `Put_material_missing_Idempotency_returns_400`. (b) Client-side `PrepressErrorLocaliser.cs` has separate banner copy per code so operator UX stays distinct. (c) Banner-bank xUnit tests lock every VN string. |

<a id="l16"></a>
### L16 — Gate scripts must strip BOTH `@* *@` block + `//` line comments

| Field | Detail |
| --- | --- |
| **Triệu chứng** | The L2 InputText repo-wide tripwire grep false-positived on `RecentScansWidget.razor` + `SettingsAuditLog.razor` because both contained intentional documentation strings restating "no `<InputText>` per the renderer-crash lesson". Naive grep counted doc-strings as code usages. |
| **Root cause** | `grep -rcE '<InputText\b' src/...` counts every occurrence including those inside `@* ... *@` Razor block comments and `// ...` C# line comments. |
| **Fix** | **R4** canonical snippet: `find src/... -name "*.razor" -print0 \| xargs -0 perl -0777 -pe 's{@\*.*?\*@}{}gs; s{//[^\n]*}{}g' \| grep -cE '<InputText\b'`. The `perl -0777` reads each file as a single string so `s{...}{}gs` can match across newlines. Strips both comment styles BEFORE grep. |
| **Cơ chế chặn tái phát** | [`STACKED-PR-CHECKLIST.md`](./STACKED-PR-CHECKLIST.md) Rule 4 carries the exact snippet. Every PR's gate script copies it verbatim. The 7b-3 PREPRESS UI PR ran this gate as part of its merge checklist and reported `0` matches. |

<a id="l17"></a>
### L17 — Seed early-exit guards skip kind-specific data

| Field | Detail |
| --- | --- |
| **Triệu chứng** | P10.7b-3 PREPRESS NG submission on Henry's Catalyst test failed with `Mã lỗi NG không có trong danh mục Scrap`. Investigation showed dev DB had only 6 Recovery-kind reason codes — NO Scrap codes, NO Pause codes. Server seed was supposed to insert 12+ ML-* / SC-* codes. Compounded by UI gap: free-text NG reason input let operator submit any string; server-validated against the (empty) Scrap catalog → 422. |
| **Root cause** | (a) **Seed gap**: `SeedReasonCodesAsync` used a single `if (await db.ReasonCodes.AnyAsync()) return;` short-circuit. After `SeedRecoveryReasonCodesAsync` populated 6 Recovery codes, the global `AnyAsync()` returned true on every subsequent boot — so Pause + Scrap seed never ran on existing DBs. (b) **Hybrid boot gap**: `CCL-MES-Hybrid/src/CCL.MES.Api/Program.cs` (Hybrid host shipped with the MAUI app) only called `SeedRecoveryDataAsync`; legacy `CCL.MES.Web/Program.cs` which calls full `SeedAsync` (Pause + Scrap included) isn't bundled. (c) **UI gap**: free-text `<input>` for NG reason let operator type garbage; no picker bound to a validated catalog. |
| **Fix** | (a) **Per-kind idempotency** in `src/CCL.MES.Infrastructure/DbSeeder.cs::SeedReasonCodesAsync`: read existing `ReasonCodes.Where(Kind ∈ {Pause, Scrap}).Select(Code).ToHashSet()`, compute `toAdd = canonical \\ existing`, AddRange the diff. Mirrors `SeedRecoveryReasonCodesAsync`'s per-code pattern. Method promoted to `public` so the Hybrid host can call it directly. (b) **Hybrid Api boot** at `CCL-MES-Hybrid/src/CCL.MES.Api/Program.cs` now calls both `SeedRecoveryDataAsync` AND `SeedReasonCodesAsync`, then emits boot probe `[seed] reason_codes pause=N scrap=M recovery=K`. (c) **New endpoint** `GET /api/v2/reason-codes?kind=Scrap` (`ReasonCodesController.cs`, any-auth, 422 on `reason_codes.invalid_kind`) + **`<select>` picker** in `WoMaterialsList.razor` + `WoPlateCheck.razor` + `WoCutterCheck.razor`. "Lưu NG" button disabled until a valid catalog code is chosen; "Đánh NG" arm button disabled when picker source is empty + tooltip "Danh mục mã NG trống — báo IT". Dashboard fetches Scrap codes on init; empty list surfaces a sticky VN banner above the row UI. |
| **Cơ chế chặn tái phát** | (a) Standing rule: any seed function that coexists with another seed of the same table MUST scope its existence check to the rows IT owns (by Kind / by Code / by composite key) — not a global `AnyAsync()`. (b) **Wire-mirror test (R7.3)** `tests/CCL.MES.Api.Tests/ReasonCodesControllerTests.cs::L17_regression_Recovery_present_does_not_block_Scrap_listing` — seeds a Recovery code, re-invokes `SeedReasonCodesAsync`, then asserts the wire `GET /api/v2/reason-codes?kind=Scrap` returns the SC-COLOR + SC-MAT-DAMAGE + SC-PLATE-WORN + SC-CUTTER-WORN codes. (c) **bUnit fixtures** in `tests/CCL.MES.Hybrid.Razor.Tests/PrepressDashboardTests.cs`: `Empty_scrap_reasons_disables_NG_arm_and_shows_banner` + `Material_Ng_arm_then_pick_then_confirm_sends_chosen_code` + `Plate_NG_arm_disabled_when_reasons_empty_even_with_view_loaded` + `Dashboard_fetches_Scrap_kind_from_reason_codes_endpoint_on_init` + `Picker_options_render_Code_and_LabelVi_text` — assert UI submit-gate is enforced when picker is empty AND that submit sends the chosen catalog code. (d) **Boot probe** `[seed] reason_codes pause=N scrap=M recovery=K` printed at every Hybrid Api boot — operator + agent eyeball drift instantly. (e) **Operator script** `CCL-MES-Hybrid/scripts/reset-prepress-for-wo.sh` (R7.1 `[ctx] DB=` header + `--commit` guard) lets Henry re-verify the NG-path picker flow against the same WO repeatedly. |

<a id="l18"></a>
### L18 — `--urls` / `ASPNETCORE_URLS` overridden by hardcoded `Kestrel:Endpoints`

| Field | Detail |
| --- | --- |
| **Triệu chứng** | P10.7b-4 `verify-p10.7b.sh` boots a fresh API copy on port 5101 (to avoid the operator's running dev server on 5100). Boot probe FAIL `API never reached /health 200` even though build + 4 suite + migration round-trip 8/8 PASS substantively. Log shows: `warn: Overriding address(es) 'http://localhost:5101'. Binding to endpoints defined via IConfiguration and/or UseKestrel() instead.` followed by `Failed to bind to address http://127.0.0.1:5100: address already in use.` Verify script can never run alongside an operator's dev session. |
| **Root cause** | `CCL-MES-Hybrid/src/CCL.MES.Api/appsettings.json` had a hardcoded `Kestrel:Endpoints:Http:Url = "http://localhost:5100"` block (lines 20-26 pre-fix). ASP.NET Core's URL binding priority: `Kestrel:Endpoints` (highest) > `--urls` CLI arg > `ASPNETCORE_URLS` env > `Urls` config key > framework default. The hardcoded `Kestrel:Endpoints` won every time, regardless of how the script tried to set the port; the warning explicitly says "Overriding address(es)". Operator could neither (a) run the dev server + verify-script concurrently nor (b) move the API to a different port for any reason. |
| **Fix** | Replace the `Kestrel:Endpoints` block with a single `"Urls": "http://localhost:5100"` key. The `Urls` key (configuration priority 4) is overridable by `--urls` (priority 2) AND `ASPNETCORE_URLS` (priority 3), so the default port-5100 convention stays for `dotnet run` with no args, but operators / verify scripts / Catalyst hosts can move it via the well-known mechanisms. Verified empirically: `dotnet run --no-build --no-launch-profile --urls http://127.0.0.1:5199` now binds 5199, no override warning, no 5100 collision. |
| **Cơ chế chặn tái phát** | (a) **Standing rule**: `Kestrel:Endpoints` is for explicit endpoint-level config (TLS / HTTP/2 / per-endpoint protocols) ONLY. For a single HTTP URL convention, use the `Urls` key so it stays overridable. Adding `Kestrel:Endpoints` back to appsettings.json without the equivalent override-respecting plumbing is the L18 regression. (b) **Probe assertion in verify scripts**: `verify-p10.7b.sh` + `checkpoint-7b-final.sh` both grep API log for `"Overriding address(es)"` post-boot — any match records FAIL `L18 regression`. (c) **lsof-bound-port assertion**: both scripts call `lsof -nP -iTCP:${PORT} -sTCP:LISTEN -t` after the /health probe + assert a PID is bound on the EXPECTED port (not 5100, not anything else). (d) **Pre-boot kill**: scripts kill stale listeners on the target port before `dotnet run` so a leftover process can't surface as a misleading FAIL. (e) **SKILLS.md S10** ("preserve debug artifacts on FAIL") — verify scripts no longer `rm -rf $TMP_DIR` when `FAIL>0`, so the failing probe's api.log survives for inspection. Without S10 the original RCA would have been invisible — the script auto-cleaned the log Henry needed to PROVE the override warning. |

---

<a id="l19"></a>
### L19 — Dispatch + display key on canonical MesPhase; deferred phases share ONE phase→content map

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Two-stage operator regression on PR #115 (the SETTING/RUNNING/PAUSED UI ship). Stage 1: after `POST /setting/done` the WO advances `MesPhase=SETTING → IPQC_WAIT` but the legacy `CurrentStep` projection stays at `"OpSetting"`. The Razor host dispatched routing on `CurrentStep="OpSetting"` → `SettingDashboard` rendered → the dashboard fetched `GET /running-surface` → saw `MesPhase=IPQC_WAIT` → surfaced its own `_invalidPhase` banner ("WO không ở giai đoạn SETTING — chọn WO khác"). Operator hit a dead-end despite a successful server-side transition. Stage 2 (Henry's WO-26-3686 follow-up): after `POST /run/finish` `MesPhase=FQC_PENDING` but the WO card chip + "Bước hiện tại" row STILL rendered legacy `CurrentStep="OpSetting"` (chip showed "SETTING"), and `FQC_PENDING` fell through to RunningDashboard's `_invalidPhase` dead-end because the dashboard's `IsValidRunningPhase` didn't recognise it. **Two sources of truth — legacy projection vs canonical phase — disagreed at every render site**. |
| **Root cause** | Legacy `WorkOrderSummary.CurrentStep` is a server-side projection from `ProcessStepCode` (8-step). Server controllers that mutate the WO update `MesPhase` (canonical) but DON'T necessarily update `CurrentStep` in lock-step — `/setting/done`, `/run/start`, `/run/qty`, `/run/pause`, `/run/resume`, `/run/finish` all leave `CurrentStep` frozen at whatever value `/advance` last set. Hybrid UI dispatching on `CurrentStep` is correct-looking but stale-by-design: the projection lags the canonical phase by every endpoint that lives outside `/advance`. Same trap on the display side: the WO card chip + status row read `CurrentStep` + a server-emitted `BadgeCssClass` derived from `CurrentStep`. Both lie about the actual state. Secondary trap: when a phase has no real dashboard (IPQC_WAIT pre-7d, FQC_PENDING / QA_PENDING / OQC_PENDING pre-7e, terminal DONE / CANCELLED), routing fell through to either the legacy `/advance` CTA (no-op for those phases) OR the running dashboard's dead-end error banner — both wrong. |
| **Fix** | **Three-prong canonical-MesPhase migration**: (1) **DTO + emission**: add `MesPhase` field to `WorkOrderSummary` ([WorkOrderDtos.cs](../src/CCL.MES.Shared/WorkOrders/WorkOrderDtos.cs)); the server's summary controller projects `wo.MesPhase` via the same AsNoTracking lookup that already returned RowVersion ([WorkOrdersController.cs:106-121](../src/CCL.MES.Api/Controllers/WorkOrdersController.cs#L106-L121)). (2) **Dispatch helpers key on canonical phase**: `IsPrepressPhase` / `IsSettingPhase` / `IsRunningSurfacePhase` / `IsDashboardOwnedPhase` in [WorkOrders.razor](../src/CCL.MES.Hybrid.Razor/Pages/WorkOrders.razor) check `summary.MesPhase` first; legacy `CurrentStep` fallback ONLY when MesPhase is empty (genuine pre-7c-3 cached summaries). Legacy `/advance` CTA + stale `_advanceResult` chrome all wrap inside `@if (!IsDashboardOwnedPhase(_summary))`. (3) **Display also keys on MesPhase**: WO card chip text = `MesPhaseDisplay(summary)`; chip CSS class = `MesPhaseCssClass(summary)` driving 13 `wo-phase-*` palette classes; "Bước hiện tại" row relabelled "Trạng thái MES". (4) **DeferredPhaseInfo map**: single `Dictionary<string, DeferredPhase>` in [RunningDashboard.razor](../src/CCL.MES.Hybrid.Razor/Shared/RunningDashboard.razor) holds title + body + hint per deferred phase (IPQC_WAIT / QA_PENDING / FQC_PENDING / OQC_PENDING / DONE / CANCELLED). Operators land on a read-only placeholder card with consistent layout instead of error or no-op CTA. 7d/7e plug in real dashboards by REMOVING the relevant map entry — the entry IS the deferred-state marker. |
| **Cơ chế chặn tái phát** | (a) **6 bUnit divergence fixtures in [WorkOrdersPageTests.cs](../tests/CCL.MES.Hybrid.Razor.Tests/WorkOrdersPageTests.cs)** — `Divergence_MesPhase_IPQC_WAIT_routes_to_RunningDashboard_not_SettingDashboard` (the EXACT WO-26-3685 state: `CurrentStep=OpSetting`, `MesPhase=IPQC_WAIT`), `Dashboard_owned_phase_hides_legacy_Advance_CTA_and_stale_error_banner`, `MesPhase_SETTING_routes_to_SettingDashboard_regardless_of_CurrentStep`, `Legacy_CurrentStep_fallback_used_when_MesPhase_is_empty`, `Card_chip_renders_canonical_MesPhase_not_legacy_CurrentStep` (WO-26-3686 chip wire-mirror), `FQC_PENDING_routes_to_placeholder_card_not_dead_end_error`. Plus 1 `[Theory]` parameterised over QA_PENDING / OQC_PENDING / DONE / CANCELLED. (b) **Server contract test**: `WorkOrdersAdvanceTests.Summary_returns_shape_for_existing_wo` asserts `MesPhase` non-empty on the wire — drops the L19 surface back to "no MesPhase field on summary" as a CI failure. (c) **RunningDashboard.IsValidRunningPhase + DeferredPhaseInfo.ContainsKey** — the IPQC_WAIT info card branch (now generalised to ANY map entry) catches every deferred phase including any future map insert. Adding a new phase to MesPhase enum without an entry in `DeferredPhaseInfo` (and without a real dashboard branch) means the WO falls into the dead-end — which the Theory test catches if the new phase is added to `IsRunningSurfacePhaseValue`. (d) **Checkpoint deferred-phase walk**: `checkpoint-7c-final.sh` Step 20 shims WO1 through QA_PENDING / IPQC_WAIT / OQC_PENDING / DONE / CANCELLED and asserts `GET /running-surface` returns the canonical phase verbatim. Any drift on the wire side surfaces in the per-step `[20/21] ✗` line. (e) **Standing rule**: any new server-side phase mutation MUST consider whether `CurrentStep` should advance with it — but the Razor UI MUST not rely on that. New dashboard surfaces dispatch on `MesPhase`; `CurrentStep` is for display in the legacy "step" diagnostic only. |
| **L19 amendment (Henry RCA on PR #120, 2026-06-07)** | The L19 fix added `MesPhase` to `WorkOrderSummary` (returned by `GET /by-no/{woNo}/summary`) but LEFT THE SIBLING DRAWER DTO `WorkOrderDrawerView` UNCHANGED. `checkpoint-7d-final.sh` Step 13 curled the bare `/by-no/{woNo}` drawer endpoint to perform the L21 auto-route wire assertion + read empty `mesPhase` (the field didn't exist on the drawer) + FAILed even though the WO was correctly in IPQC_APPROVED. Raw JSON: drawer returned `{ "currentStep": 3, "badgeToken": "ipqc_wait", ... }` (no `mesPhase`); `/summary` returned `{ "mesPhase": "IPQC_APPROVED", ... }`. The original L19 mechanism caught the SUMMARY drift but not the DRAWER drift. **Standing rule strengthened**: EVERY endpoint that returns a WO record MUST project canonical `MesPhase`. New cards in the prevention column: (f) `MesPhase` field added to `WorkOrderDrawerView` record + projected from `wo.MesPhase` in `WorkOrderService.GetDrawerAsync`. (g) New server contract fixture `WorkOrdersAdvanceTests.Drawer_by_no_projects_MesPhase_per_L19_amendment` reads the raw JSON via `JsonDocument` + asserts `mesPhase` property exists + is non-empty — a future refactor that drops the field breaks at CI rather than at operator runtime. (h) `checkpoint-7d-final.sh` Step 13 switched to `/by-no/{woNo}/summary` (matches what `CclApiClient.GetWorkOrderByNoAsync` actually calls, which is what the L21 auto-route hits) PLUS a bonus drawer probe that warns if the L19-amendment field is missing on the bare endpoint. |

<a id="l20"></a>
### L20 — Security-critical default-ON flags MUST parse typo-safe + emit boot-probe log line; silent flip to OFF on a misspelled env value is a compliance hole

| Field | Detail |
| --- | --- |
| **Triệu chứng** | P10.7d-1 design call surfaced the risk: `OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER` is the env var that enforces Q3 dual-sig — "the QA approver MUST be a different person than the IPQC submitter." If an ops engineer typos the value (e.g. `=trie` instead of `=true`, or `=ON` vs accepted `=on` lowercase, or sets it to nothing while expecting the default), a naive parse like `bool.Parse(envValue ?? "true")` throws on the unknown string OR silently returns `false`. Either way the compliance invariant is gone — a single IPQC operator can approve their own SpecialAccept escalation, no audit trail of the violation, and a post-incident compliance audit can't tell whether the policy was enforced for any given day in the past. Same trap class applies to ANY default-ON security flag (think `RequireSignedExports`, `RequireMfa`, `EnforceCorsAllowlist`). |
| **Root cause** | Two failures multiply: (1) **Brittle parse** — the conventional `bool.Parse` / `Convert.ToBoolean` throws on unrecognised strings, so a typoed value either crashes the boot OR (worse, in code that swallows the exception) falls back to a hardcoded value that diverges from the policy intent. (2) **No boot-time visibility** — even when the parse is correct, the resolved value isn't surfaced to the operator, so an env-var override that successfully turned the flag OFF leaves no trace until someone reads the source. Combined, an op-engineer who typoes a default-ON flag during a routine deploy can silently disable a compliance control. |
| **Fix** | **Two-prong pattern** codified for every default-ON security flag:<br>**(a) Typo-safe whitelist parse** — accept ONLY a small set of explicit OFF values (`"0"`, `"false"`, `"off"`, `"no"`); anything else (including null/empty/typos/uppercase variants/extra whitespace) keeps the flag ON. Implementation in [Program.cs:198-218](../src/CCL.MES.Api/Program.cs#L198-L218):<br>```csharp<br>builder.Services.Configure<IpqcDualSigOptions>(opts =><br>{<br>    var raw = Environment.GetEnvironmentVariable("OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER")<br>        ?? builder.Configuration["Features:IpqcRequireDistinctQaApprover"];<br>    opts.RequireDistinctQaApprover = !(raw is "0" or "false" or "off" or "no"<br>        ...case-insensitive normalised...);<br>});<br>```<br>**(b) Boot probe log line** — emit `[config] OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=on` (or `=off`) BEFORE the host starts so operators can grep the boot log + confirm at deploy time. The probe is also asserted by `verify-p10.7d.sh` Step 10 — a CI run that finds `=off` when the prod policy is `=on` FAILS the verify, refusing to ship the build. |
| **Cơ chế chặn tái phát** | (a) **Unit-locked parse table** in `tests/CCL.MES.Tests/Application/IpqcDualSigOptionsParseTests.cs` covers 12 cases: empty / null / whitespace / `"true"` / `"TRUE"` / `"yes"` / `"on"` / `"trie"` (typo) / `"0"` / `"false"` / `"off"` / `"no"`. Everything except the explicit OFF set returns `true`. Any future refactor that flips to permissive parsing breaks the table. (b) **`verify-p10.7d.sh` Step 10 boot probe** — script BOOTS the API without overriding the env var and asserts the log line says `=on` (default-ON enforced). (c) **`checkpoint-7d-final.sh` precondition** — refuses to run when `=off` is detected; prints "Q3 cannot be verified" + non-zero exit. Operator can't accidentally certify a build whose dual-sig is silently disabled. (d) **Standing rule when adding a new default-ON security flag**: copy the 3-piece kit verbatim — typo-safe whitelist parse, boot probe log line, verify-script Step asserting probe. No exception. |

---

<a id="l21"></a>
### L21 — Phase-changing actions MUST auto re-fetch summary + re-dispatch; the dashboard that just acted does NOT own its replacement

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Henry hardware verify on PR #119 (2026-06-07). Operator on Catalyst walked the IPQC → QA → IPQC_APPROVED flow successfully — every server response correct, audit log clean. BUT after each phase-changing tap (SpecialAccept judgment → QA_PENDING; QA Approve → IPQC_APPROVED; SETTING done → IPQC_WAIT; run/finish → FQC_PENDING) the dashboard the operator JUST acted on **kept rendering**. Worse: it rendered its own `_invalidPhase` dead-end card ("WO không ở giai đoạn IPQC_WAIT — Hiện tại: QA_PENDING") because the dashboard's own `ReloadAsync` correctly fetched the new view, saw the phase had changed, and set `_invalidPhase = true`. Operator had to tap "Tìm" (the manual lookup button) again to force `WorkOrders.razor` to re-fetch `_summary` + re-run dispatch. Shop-floor blocker. |
| **Root cause** | Each dashboard component owns its own view-fetching (`GetIpqcViewAsync`, `GetRunningSurfaceViewAsync`, etc.). After a successful mutation, the dashboard refetches ITS OWN view — that view correctly reflects the new MesPhase. But the **parent's `_summary` stays stale** because no callback bubbles up. The parent's dispatch (`@if (IsIpqcWaitPhase(_summary))` etc.) re-evaluates on every render but reads the stale summary, so the same dashboard re-mounts; meanwhile the dashboard's own `_view.MesPhase` flags `_invalidPhase` because IPQC_WAIT no longer matches. Two sources of truth (parent summary vs child view) drift and the parent loses. This is L19's mirror image: L19 was about routing on canonical phase (the *check*); L21 is about *getting the check to re-run after an action*. |
| **Fix** | **Pattern: every transition-emitting dashboard exposes `[Parameter] EventCallback OnPhaseChanged`; the parent (`WorkOrders.razor`) wires it to a single `HandleDashboardPhaseChangedAsync()` handler that re-fetches the summary via `GetWorkOrderByNoAsync` and triggers re-render**. The handler is centralised so the pattern is one-place-to-look-at and 7e/7f surfaces inherit it for free. Each dashboard invokes `OnPhaseChanged.InvokeAsync()` ONLY after a successful response that ACTUALLY changes phase: IpqcDashboard on judgment submit (Slot PUTs stay IPQC_WAIT — no bubble); QaApprovalDashboard on Approve+Reject; SettingDashboard on /setting/done; RunningDashboard on run/start + pause + resume + finish (Tap qty + correct stay in current phase — no bubble). 409 / 422 responses NEVER bubble — server didn't change phase, no parent re-fetch needed. The PrepressDashboard already had this pattern in disguise — `OnAdvanceRequested` callback wired to `OnAdvance` which calls `AdvanceOrchestrator.RunAsync` with a `refreshSummary` delegate. L21 generalises that. |
| **Cơ chế chặn tái phát** | (a) **4 per-dashboard pass + 4 per-dashboard skip fixtures** in `IpqcDashboardTests` / `QaApprovalDashboardTests` / `SettingDashboardTests` / `RunningDashboardTests` — each asserts `phaseChangedCount == 1` after a successful transition action AND `== 0` on (i) actions that don't change phase (slot PUT, tap qty) (ii) 409/422 responses. (b) **End-to-end dispatch fixture in `WorkOrdersPageTests`** — `Auto_refresh_after_IPQC_judgment_routes_to_QaApprovalDashboard_without_manual_lookup` + sibling for SETTING→IPQC_WAIT. Both walk the FULL flow: initial summary returns phase 1, user taps action button, mock returns Ok+new phase, summary mock returns phase 2 on second call, fixture asserts the dashboard for phase 2 mounted + the dashboard for phase 1 unmounted without intermediate user action. (c) **Standing rule when adding a new dashboard**: if it has at least one mutation that can change MesPhase, it MUST add a `[Parameter] EventCallback OnPhaseChanged` parameter AND `WorkOrders.razor` MUST wire it to `HandleDashboardPhaseChangedAsync`. No exception. (d) **7e-3 extension**: `FqcDashboard` invokes OnPhaseChanged on judgment (Pass → OQC_PENDING, Reject → PREPRESS) + photo upload/delete (item-set lazy materialise can race the parent's summary; safer to re-fetch). `OqcDashboard` invokes only on the Approver decision (Approve → SHIPPED, Reject → FQC_PENDING) — Inspector + Reviewer sigs stay in OQC_PENDING. `ShippedSummaryDashboard` is terminal (no OnPhaseChanged needed). |

<a id="l22"></a>
### L22 — Stale keep-alive binary returns silent 404; checkpoint MUST kill stale :5100 + build-sanity-probe before route exercise

| Field | Detail |
| --- | --- |
| **Triệu chứng** | PR #122 checkpoint-7e-2.sh hardware run (2026-06-07). Step 7 `POST /qc/oqc/inspect` returned empty body + HTTP 404. Operator confused: the controller landed in 7e-2; the route exists in source; why a 404? Henry's STOP: "L10 drift-guard không bắn → call này có thể không qua helper api_assert_routed". |
| **Root cause** | Two prongs: (1) The shell script was using its own `curl` wrappers for QC mutations that bypassed `api_assert_routed` (the L10 drift-detection helper). When the route 404'd, no helper fired, so the FAIL banner only showed empty body. (2) The keep-alive API on port 5100 was a PREVIOUS build (commit `f08b79d`, BEFORE the WoQcReviewController landed). ASP.NET Endpoint-Not-Found returns 404 with **empty body** — distinct from a route-handler 404 carrying RFC-7807 `ApiError` JSON. The empty-body 404 LOOKS identical to a typo'd URL but is structurally different: route exists in latest binary, just not in the running one. Once an agent is re-using a long-running keep-alive process (developer ergonomics on Mac DMG hot-loop), the "wire path = current code" assumption silently breaks. |
| **Fix** | **Three-prong** checkpoint hardening (committed to `checkpoint-7e-2.sh`, pattern to be reused for every future hardware checkpoint): **(a) `qc_post` helper** wraps every QC mutation curl with `LAST_HTTP` + `LAST_BODY` capture + auto-routes through `api_assert_routed` so the L10 drift-guard always fires. **(b) `qc_diag` helper** prints `[diag] $label — HTTP $X (expected $Y)` + the first 400 bytes of body on fail so the operator sees the difference between empty-body 404 (stale binary) and JSON-body 404 (route wired). **(c) Build-sanity probe** runs RIGHT AFTER admin login. It POSTs to a known route (`/api/v2/qc/oqc/inspect` with no body) and asserts the response is EITHER 200/422/400 (route exists) OR a JSON-body 404 (route wired but not registered) — but NEVER an empty-body 404. On empty-body 404 it prints the exact `kill <pid>` command for the stale listener + refuses to continue. |
| **Cơ chế chặn tái phát** | (a) **Build-sanity probe in `checkpoint-7e-2.sh`** + every future `checkpoint-7e-*.sh` (7e-final pattern; 7f patterns inherit). Pattern: kill anything on 5100 before checkpoint OR run the probe + abort with kill cmd if a stale binary answers. (b) **Standing rule** — every Bash helper that POSTs to a JSON endpoint MUST route through `api_assert_routed`. PRs that add new mutation steps without the helper fail review. (c) **Verify-script convention** — `verify-p10.7e.sh` (and later siblings) self-manages its API on port 5104 (distinct from kiosk port 5100) + kills any listener on that port before boot. Checkpoint scripts that piggyback on the kiosk port must include the build-sanity probe; verify-scripts that own their port don't need it. |

<a id="l23"></a>
### L23 — Checkpoint MUST walk the real materialisation path; shortcut INSERTs mask data-bed gaps (seed-trống / profile-trống) that operators WILL hit

| Field | Detail |
| --- | --- |
| **Triệu chứng** | PR #123 hardware verify on WO-26-3686 (FQC_PENDING). FqcDashboard rendered "Profile QC trống — Quản trị phải seed MES_FQC_PROFILE" with Item 0/0 + judgment row gated. Operator perma-stuck. checkpoint-7e-2.sh had reported 14/14 PASS on the SAME branch, so the green checkpoint masked the operator-visible dead-end. Same class as L17 (reason codes chưa seed) — the data-bed (default QC profile this time) was missing but the checkpoint test path never touched it. |
| **Root cause** | 7e-1 ship laid the Q3 data-driven schema (`WoQcChecks.ProfileSnapshotJson` + `WoQcCheckItem.ItemKey`) but did NOT seed the canonical MES_FQC_PROFILE (12 items) / MES_OQC_PROFILE (28 items, CCL-10-F6 R04) from SpecHub. The controller's lazy-materialise path created a Pending check row with `ProfileSnapshotJson = "{}"`; the dashboard rendered 0 items because the view's `Items` array projected from the empty rows collection. Reasonable code; wrong outcome because nobody primed the data bed. **The checkpoint-7e-2.sh step 6 made the gap invisible**: it directly `INSERT`ed a `WoQcChecks` row with a hand-rolled empty-profile snapshot + an `appearance` item (`INSERT INTO WoQcChecks ... VALUES ($WO, 'OQC', '{}', 'Pending', ...); INSERT INTO WoQcCheckItems ... VALUES ($CHECK, 'appearance', 'Ok', ...)`). That stub was perfectly shaped to satisfy the 3-sig Q5 fixtures even though no operator-visible code path ever hits the real GET/PUT flow. The READINESS rollup happily returned `ready=true` because the only persisted row (`appearance` = Ok) was non-Pending — pre-fix rollup was row-only, so 5 of 12 profile items "answered" said "ready" too. Cascade: real UI → 0 items → can't tap anything → can't advance. Checkpoint → 1 stub item → 3-sig passes → tag green. **L17 sibling**: reason codes needed seed because picker was empty without it; same shape — code is fine, data bed missing. |
| **Fix** | **Five-prong**:  **(a) `QcProfileSeed`** — embedded compile-time constants `FqcProfileJson` (12 items in 4 sections; matches SpecHub `MES_FQC_PROFILE` line 15938) + `OqcProfileJson` (28 items in 5 sections; matches `MES_OQC_PROFILE` line 16047 sourced from CCL-10-F6 R04). Per L17 pattern (per-kind helper, not a DB row to migrate), seed is code not data — no migration needed, ship the binary + profile travels with it.  **(b) Q4 3-level resolver in `WoQcReviewController.ResolveProfileSnapshotAsync`** — Product.QcProfileOverride (per-product) → QcProfileSeed (system default) → "{}" empty. Frozen at materialise time per Q3 contract.  **(c) GET view item merge** — Items array now synthesises one entry per profile-declared item key (Pending) and overlays persisted WoQcCheckItem rows; stragglers (persisted keys not in current snapshot) tail-append for forensic trail.  **(d) Profile-aware readiness** — `WoQcReadinessRollup.Compute` widened with `profileExpectedItemCount` parameter; when positive, readiness requires every profile item resolved (not just every persisted row). Legacy 0-arg overload preserved for backward compat.  **(e) Item-key validation** — `PutItem` rejects keys not declared in the current profile snapshot (with persisted-row fallback for legacy stragglers) — operators can't bypass seed via arbitrary key POSTs.  **(f) checkpoint-7e-2.sh real materialisation** — step 6 no longer `INSERT`s a stub. Instead: shim phase via SQL (no API for backward transitions yet), DELETE prior check row, then call `GET /qc/oqc` to lazy-materialise via QcProfileSeed → assert ≥28 items returned → loop `PUT /qc/oqc/items/{key}` for each → satisfy readiness via REAL operator-visible wire. Reuses the very paths the OqcDashboard hits. Any future regression in profile shape/items/keys trips the checkpoint as it WOULD trip the operator. |
| **Cơ chế chặn tái phát** | (a) **Server-side wire fixtures in `WoQcReviewControllerTests`**: `Get_fresh_fqc_view_materialises_12_items_from_seeded_profile`, `Get_fresh_oqc_view_materialises_28_items_from_seeded_profile`, `Get_view_heals_legacy_empty_snapshot_with_seeded_profile`, `Fqc_judgment_Pass_with_partial_items_returns_422_not_ready` — the last one is the L23 keystone, asserts a 5-of-12-completed FQC check stays gated (pre-fix would have said "ready" via row-only rollup). (b) **Boot probe + `verify-p10.7e.sh` assertion**: `[seed] qc_profiles fqc=12 oqc=28` emitted at startup; verify-script parses + asserts the 12/28 split. A future profile shrink (admin edits the const wrong) trips the verify rather than surfacing as silent 0/0 on the operator dashboard. (c) **Standing checkpoint rule** — when a checkpoint script needs to bring an entity to a state, it MUST drive the same write surface the UI uses. Direct `INSERT INTO ...` is only acceptable for *upstream prerequisites* that have no UI-driven endpoint (e.g. backward MesPhase transitions in this case). When a SQL shortcut is unavoidable, the next probe MUST round-trip the new state through the real read endpoint (`GET /qc/{kind}` here) so the data-bed gap is caught at this step rather than hiding until the next checkpoint or worse the operator's hardware test. (d) **Class lesson**: L17 (reason codes empty) + L23 (profile empty) are the same shape — code-path-OK, data-bed-empty. Future writers of new data-driven surfaces (next 7f, 7g) should ship the seed in the same PR as the schema, with a `[seed] <surface> n=N` boot line + a verify assertion both shipping together. |

---

<a id="l24"></a>
### L24 — ALL data tables freeze their header on scroll via ONE global rule; never add per-table sticky CSS — and verify the maccatalyst build by exit code, never by grepping stdout

| Field | Detail |
| --- | --- |
| **Triệu chứng** | (1) Operators asked for the Shop Order History / Machine List / NPI-data tables to keep the column-header row visible while scrolling. The first pass added a per-table `position: sticky` to `.md-list-tbl thead th`, but it only worked inside a bounded scroll container — every other table (`soh-tbl`, `prepress-table`, NPI `audit-table`, `qc-history-table`, …) still scrolled its header away. Doing it table-by-table = N edits + N chances to forget the next table. (2) Worse: while iterating on the List view, `dotnet build … -f net10.0-maccatalyst \| grep -E "error\|Build succeeded" \| head && open "$APP"` **relaunched a 2-day-stale .app** for several turns. The maccatalyst build had started failing with `NETSDK1147: workloads must be installed: maui-maccatalyst` (the SDK auto-bumped 10.0.300→10.0.301 between sessions, dropping the workload), but the `\| head` swallowed the missing-success line and `&&` ran `open` anyway. RCA only surfaced when the bundled `app.css` was grepped: it had `page-title-shine` (earlier change) but NOT `md-viewtoggle` (newest change) → the bundle was frozen at the last successful build. |
| **Root cause** | (1) `position: sticky; top: 0` pins to the **nearest scrolling ancestor**. The app's real scroll root was the document body (`.app-shell { min-height: 100vh }` let content grow past the viewport, so `.app-content`'s `overflow:auto` never engaged), so per-table sticky in non-scroll-container tables had nothing to pin to. (2) A pipeline's exit code is the **last** command's (`head`, always 0); `set -o pipefail` was absent; and `cmd \| grep \| head && open` lets a build failure fall through to relaunch. Grepping stdout for "Build succeeded" is not verification — `-v q` prints nothing for an up-to-date build and a hard `NETSDK1147` error prints to a line `head` can hide. |
| **Fix** | (1) **Make `.app-content` the single bounded scroll container** (`.app-shell { height: 100vh; grid-template-rows: 100vh }` + `min-height: 0` on `.app-main`/`.app-content` + `overflow-y:auto` on `.app-nav` so the sidebar scrolls itself). Then **ONE global zero-specificity rule** freezes every table header: `:where(.app-content thead th){ position: sticky; top: 0; z-index: 4; background: #f8fafc; }`. `:where()` = 0 specificity, so each table's own header colour/position still wins and tables with no header background get the tint fallback (rows never bleed through). This covers all current tables (Shop Order History, Machine List, every NPI-data table, QC History, audit log, accounts, backup, spec sub-tables) AND every future table automatically — no per-table CSS. Tables that ALSO need their own bounded vertical scroll (very long, e.g. Machine List) may add a local `.md-list-scroll`-style wrapper, but the header freeze itself is never re-implemented. (2) **Build verification by exit code**: `set -o pipefail` + read `${PIPESTATUS[0]}`/`$?`, or grep for `"error\|Error(s)"` and assert `0 Error(s)`; then **confirm the change actually reached the bundle** (`grep -c <new-token> "<App>/Contents/Resources/wwwroot/_content/CCL.MES.Hybrid.Razor/css/app.css"`) before `open`. Never gate `open` on a `\| head` of build stdout. |
| **Cơ chế chặn tái phát** | (a) **Standing CSS rule (this file + the comment block at `:where(.app-content thead th)` in `app.css`)**: frozen table headers are GLOBAL. Any PR that adds `position: sticky` to a `thead th` for an individual table, or wraps a table solely to get a sticky header, is redundant and rejected in review — the global rule already covers it. New tables inherit the freeze for free; the author does nothing. (b) **Standing build rule**: a maccatalyst rebuild is "done" only when (i) the build exit code is 0 / `0 Error(s)` AND (ii) a freshly-added token is grep-confirmed inside the `.app` bundle's static assets. The `cmd | grep | head && open` shape is banned — it hid a 2-day-stale bundle. If `NETSDK1147` appears, the workload is gone (SDK bumped) → `sudo dotnet workload restore` from `CCL-MES-Hybrid/src/CCL.MES.Hybrid` before any further "fixed it" claim. (c) **Class lesson**: cross-cutting UI affordances (sticky headers, focus rings, page-title styling) belong in ONE global rule keyed off the shell, not sprinkled per-component; and any "build + relaunch" one-liner must fail loudly, never silently relaunch stale bits. |

---

## Adding a new lesson

When a new bug class costs ≥2 hours of investigation:

1. Add an entry to the **Index** above under the closest cluster.
2. Add a new lesson card at the bottom following the 4-column template:
   - **Triệu chứng** — what the operator/agent saw (paste output if helpful).
   - **Root cause** — what was actually broken, PROVEN (see SKILLS.md "RCA proven").
   - **Fix** — the durable code/script/rule change.
   - **Cơ chế chặn tái phát** — name the specific test file, rule number, or boot-probe that fails CI if the invariant is violated. **MUST be non-empty.** Prose lessons don't ship.
3. If the lesson reuses a STACKED-PR-CHECKLIST rule, link by number (R1..R7). If it adds a new rule, edit `STACKED-PR-CHECKLIST.md` in the same PR.
4. If the lesson has a longer RCA, drop the detailed write-up next to the relevant `p10.X-screens/` or `pNN-screens/` folder and link it from the card.

**Append-only.** Don't rewrite history — close out a lesson with a strike-through if it's superseded, but leave the original visible. The next agent reads the index to learn what the project has paid for.
