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

### Data-driven QC engine (Phương án C)

- [L25 — Suspected regression? Prove it against a stashed baseline before blaming your change — a `Category=Soak` SQLite-macOS flake fails the same way whether your code is present or not](#l25)
- [L26 — Classification rules (process→line) belong in a DATA table, not hardcoded keyword/StartsWith lists; and a lazy-materialise that can no-op MUST report a status, never fall back silently](#l26)
- [L27 — A new taxonomy value (QC line) added with ZERO migration when stored as a string token; and re-verify GATE numbers against the current MAP, not a frozen baseline from the old logic](#l27)
- [L28 — Username lookup + uniqueness were case-sensitive (BINARY collation); an admin-reset user typing a different case got 401 "invalid credentials", masking a CORRECT password. Fix at the SCHEMA (NOCASE column), not by sprinkling provider-specific COLLATE into app code](#l28)
- [L29 — Traceability that JOINs the LIVE source drifts when master data is later edited (esp. computed links like QPA = QtyRequired/targetQty); freeze a DEAD self-describing JSON snapshot at confirm time (immutable, idempotent by ContentHash, versioned) and read ONLY that — a generic header/items renderer then serves every phase + variant with zero per-phase columns](#l29)
- [L30 — Real-time on an IMMUTABLE store: split a MUTABLE index (1 row/WO, upserted on scan/phase-change/freeze) from the immutable snapshots; push notify-then-pull over the EXISTING hub (server says "changed", client re-pulls + debounces); WO shows up on scan before any freeze; add fallback polling + Live/Offline when the socket drops; backfill pre-existing WOs idempotently](#l30)
- [L31 — Draggable/resizable floating windows in a WKWebView (Mac Catalyst): use Pointer Events + setPointerCapture, NOT HTML5 draggable/dragstart (flaky there); keep ALL geometry math in JS (transform + rAF, no per-pixel Blazor round-trip), report only the final rect on pointer-up; and ALWAYS dispose listeners + release capture in IAsyncDisposable so no listener outlives the renderer](#l31)
- [L32 — Resize handles dead while drag works: the handles sit at NEGATIVE offsets (straddling the edge) but their container had overflow:hidden → the grab-area is clipped to nothing. Split the clip: card = overflow:VISIBLE holds the handles; an inner wrapper owns overflow:hidden + rounded corners + scroll. Handles need z-index ABOVE the inner chrome + setPointerCapture on the HANDLE itself](#l32)
- [L33 — Part Description didn't sync: it was derived from the SCAN string (remainder after '/') so a bare code ("30030491-0145" with no "/desc") yielded "" → "—". Resolve it from the MATCHED BOM row (Row.MaterialDescription), falling back to the scan remainder only for a legacy row without one. And persist part_scan + resolved description as REAL columns (not just audit detail) so the Product freeze snapshot can carry them](#l33)
- [L34 — Window chrome (drag/resize/traffic-lights) was inlined in ONE dialog → every future showcard would re-implement it. Extract a reusable <FloatingWindow> component (chrome + JS interop + keyboard + rect persistence); showcards WRAP it, transactional modals stay centred (Modal gains an opt-in Float mode). Enforce with a CI grep gate + a skill so new *Showcard/*DetailDialog that forget it fail review](#l34)
- [L35 — Per-row actions as an inline "Actions" column of buttons eat width + don't scale. Use ONE shared RowContextMenu (right-click + long-press + ⋯ kebab, all sharing one state) with RBAC-by-omission + viewport clamp + a11y. Enforce with a CI gate that fails a new grid adding a `<th>Actions</th>` header](#l35)
- [L36 — A full-screen page (login) left dead bands because its layout wrapper was display:grid + place-items:center → the single track sized to CONTENT width, not the viewport. A full-bleed page needs a BLOCK/fill wrapper (child fills width) + min-height:100vh; never centre a full-screen shell. New full-screen surfaces MUST pass the S9 responsive matrix (no fix-width dead bands)](#l36)
- [L37 — Re-toning a whole app whose CSS is ~1250 hardcoded hex / ~10 tokens: DON'T find/replace hex (leaves it un-token-ised, next retheme still edits 1000+ literals). Build a flat SEMANTIC token layer (values = current colours → no-op), route hardcodes → var(--token) per role-group (each a no-op commit), THEN retheme = swap token VALUES only. Pitfall: a hex→var replacer MUST strip comments first — a comment containing `word:` (e.g. `RULE:`, `:root`) fools a declaration parser into eating the following `--token: #hex` DEFINITION → circular `--x: var(--x)` silently breaks the token. New colours MUST use a token, no raw hex (gate)](#l37)

### Kiến trúc tầng + hệ thiết kế + vòng lặp agent (audit 2026-08-18)

- [L38 — SQLite per-row RowVersion: `IsRowVersion()` bỏ giá trị lúc INSERT → NOT NULL fail](#l38)
- [L39 — Spec-sheet print/PDF WYSIWYG: `window.print()` chết trong WKWebView; bảng rộng fixed+wrap](#l39)
- [L40 — Luật nghiệp vụ nằm trong controller HTTP (22 `SaveChangesAsync` + 20/33 controller chạm DbContext + một file 1.460 dòng) → không test được ngoài WebApplicationFactory, không tái dùng cho job/ERP adapter, hai endpoint gần giống nhau phân kỳ. Kéo xuống Application service + Domain policy; ratchet đi xuống bằng gate](#l40)
- [L41 — CSS chỉ token-hoá MÀU (L37) mà không token-hoá KÍCH THƯỚC → 6 commit liên tiếp chỉnh tay một bảng (0.9→1.08rem, nới cột 3.4%, clamp/vw). Thiếu thang chữ/khoảng cách VÀ thiếu density mode cho người đeo găng. Dựng thang + hai density bằng đúng chiến thuật P1 của L37 (giá trị hiện tại → no-op)](#l41)
- [L42 — 99 dòng `.razor` còn chuỗi tiếng Việt trần ngoài catalog → người dùng EN thấy tiếng Việt; và `Dictionary.Add` trùng key làm app chết NGAY LÚC KHỞI ĐỘNG chứ không phải lúc dùng. Bắt tĩnh ở CI rẻ hơn bắt lúc mở app](#l42)
- [L43 — Audit là thứ duy nhất trả lời "ai làm gì lúc nào" khi có sự cố; mutation im lặng làm sự cố không điều tra được, còn detail chứa hash/token thì rò bí mật qua CSV export. Cả hai đều bắt được bằng grep ở CI](#l43)

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

<a id="l25"></a>
### L25 — Prove a suspected regression against a stashed baseline before blaming your change; a `Category=Soak` flake fails the same way regardless

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Sau khi land Phương án C (B2-B4), `dotnet test CCL.MES.Api.Tests` báo 1 fail: `Concurrent_run_qty_add_N_equals_10_exactly_one_winner` — kỳ vọng 1 winner, nhận 4–8. Chạy isolated vẫn fail (số winner dao động 7/5/4). Test ở `RunningSurfaceController` — KHÔNG phải surface mà Plan C đụng (IPQC), nhưng "fail ngay sau thay đổi của mình" → dễ kết luận nhầm là regression do mình. |
| **Root cause** | Đây là flake interleaving SQLite-trên-macOS đã được tài liệu hoá (CLAUDE.md §P10.7c-4: "soak filter inversion as dedicated Step 2.5 with 2-attempt policy for the documented SQLite macOS interleaving flake"), gắn `[Trait("Category","Soak")]`. Optimistic-lock qua WorkOrders RowVersion trigger không serialize tuyệt đối dưới 10 request song song trong WebApplicationFactory + SQLite — số winner phụ thuộc timing, không phụ thuộc code Plan C. |
| **Fix** | `git stash push -u` toàn bộ thay đổi → chạy CHÍNH test đó trên baseline 4 lần: kết quả 1✅ / 1❌(4) / 1✅ / 1❌(7) → baseline cũng flaky. `git stash pop` khôi phục. Kết luận: KHÔNG phải regression. CI/verify chạy soak riêng (`--filter Category!=Soak` cho suite chính; soak 2-attempt). Báo cáo "đã chạy/đã xanh" của Plan C dùng `Category!=Soak` cho con số ổn định. |
| **Cơ chế chặn tái phát** | (a) **Quy trình bắt buộc**: nghi regression mà test nằm NGOÀI vùng mình sửa → `git stash -u` + chạy ≥3 lần trên baseline TRƯỚC khi tuyên bố. Nếu baseline cũng fail → flake, không phải mình. (b) Suite chính chạy `--filter Category!=Soak`; soak tests luôn tách + 2-attempt (đã có trong `verify-p10.7c.sh` Step 2.5). (c) Bài học phụ — **commit data-driven đụng EF shared files** (MesDbContext/IMesDbContext/MesDbContextModelSnapshot) KHÔNG path-split được khi không có `git add -p`; gộp theo compile-unit (B1+B2 chung 1 commit) thay vì cố tách theo bước → mỗi commit vẫn biên dịch. |

---

<a id="l26"></a>
### L26 — Classification rules belong in a DATA table, not hardcoded keyword lists; a lazy-materialise that can no-op MUST report a status, never silently fall back

| Field | Detail |
| --- | --- |
| **Triệu chứng** | `/code-review` của Phương án C ra finding #3 + #7: `QcLineResolver` map process→QC line bằng ~30 `ContainsAny`/`StartsWith` hardcode trong code. (#7) `wc.StartsWith("SS") && !wc.StartsWith("SSC")` quá rộng — mã WC mới 'SS01'/'SSX' bị phân loại SILK sai. Thêm máy/đổi quy ước WC = sửa code resolver mỗi lần (không scale 10 nhà máy). Finding #2: auto-sync materialize no-op (không routing/library) IM LẶNG lùi về 4-slot → operator không biết bộ item chưa nạp; nếu routing tới sau, WO kẹt 4-slot vĩnh viễn (materialize chỉ chạy lúc tạo row). |
| **Root cause** | (a) Luật phân loại là DỮ LIỆU vận hành (thay đổi theo nhà máy/thiết bị) nhưng bị nhốt trong code. (b) `WorkCenter.Area` auto-derive trong import sai (Power press PPSC1 → "SILKSCREEN") nên không dùng được → resolver buộc đoán bằng substring lỏng. (c) Lazy-materialise chỉ có 2 nhánh (materialize / no-op) + không trả trạng thái → caller/UI không phân biệt "đã nạp" vs "bỏ qua vì unmapped" vs "thư viện trống". |
| **Fix** | (a) **Bảng `ProcessLineMap`** (MatchType ProcessCode/WorkCenterPrefix/OpKeyword → QcLine LABEL/DIGITAL/SILK/PRESS_CNC/NONE, Sort, Active) + `ProcessLineMapSeed.DefaultEntries()` dùng CHUNG cho DbSeeder (upsert idempotent) và unit test. Resolver `Resolve(ops, map)` tra bảng: ProcessCode → WorkCenterPrefix (DÀI nhất) → OpKeyword (chứa, Sort nhỏ) → Unmapped. Gỡ TOÀN BỘ keyword hardcode + cái `StartsWith("SS")` rộng → 'SS01' nay Unmapped. NONE = hợp lệ (pre-press/sấy/FQC/OQC), khác Unmapped. (b) Map theo định danh máy (WC prefix) thay vì động từ "CUT" → tránh nhập nhằng 'SheetCut(SS)'. (c) `TryAutoSyncAsync` trả status; GET self-heal khi check rỗng + pristine; DTO `autoSyncStatus` ∈ {Materialized, SkippedUnmapped, SkippedNoLibrary, LegacyManual} + UI banner cảnh báo; KHÔNG đè dữ liệu operator (slot ≠ Pending → LegacyManual). |
| **Cơ chế chặn tái phát** | `QcLineResolverTests` (data-driven qua `MapFromSeed()`; khoá 'SS01'/'SSX'/'SS7'→Unmapped + routing thật 8064) · `IpqcAutoSyncTests.ProcessLineMap_seed_is_idempotent` · `IpqcAutoSyncControllerTests` (F2a self-heal khi routing tới sau · F2b SkippedUnmapped không nạp item · F2c không đè slot legacy) · `CheckItemLibraryControllerTests.ProcessMap_endpoint_*`. Quy tắc: luật phân loại mới → THÊM DÒNG vào `ProcessLineMapSeed`, KHÔNG thêm `if/StartsWith` vào resolver (PR review reject). |

---

<a id="l27"></a>
### L27 — Taxonomy value added with zero migration (string token); re-verify GATE numbers against the MAP, not a frozen baseline

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Sau khi seed map data-driven (F6) lên live, re-verify GATE A: 2/4 mã 8064 LỆCH baseline hardcode (80645392: 42→67, 80640044: 52→86). Thoạt nhìn như regression F6. Đồng thời cần thêm "line" QC thứ 5 (FINISHING) cho công đoạn cán — lo phải thêm migration + enum. |
| **Root cause** | (a) Số 42/52 baseline là kết quả của resolver KEYWORD CŨ (desc "SILK"/"CUT" thắng máy). Map data-driven ưu tiên ĐỊNH DANH MÁY nên phân loại khác — đúng QC hơn nhưng khác số cũ. So GATE với baseline cũ = báo "sai" cho một mapping thực ra ĐÚNG. (b) `ProcessLineMap.QcLine` + `CheckItemLibrary.ProcessLine` + `WoIpqcCheckItem.ProcessLine` đều lưu **string** (HasMaxLength, KHÔNG HasConversion&lt;enum&gt;) → thêm giá trị "FINISHING" chỉ là dữ liệu seed, KHÔNG cần migration/đổi schema. |
| **Fix** | (a) Sửa GIÁ TRỊ map cho khớp QC thật (QĐ#6 SheetCut(SS)→PRESS_CNC; QĐ#7 Laminate/Slit/Magic→line mới FINISHING + 5 item FIN-* trong thư viện v3). (b) Thêm FINISHING = thêm const + dòng seed + item thư viện — 0 migration. (c) Re-verify GATE bằng cách: mỗi delta vs baseline cũ PHẢI giải thích được bằng một dòng map; delta KHÔNG giải thích được mới là regression. Kết quả mới: 61/42/57/32 — mọi delta khớp QĐ#6/#7. |
| **Cơ chế chặn tái phát** | `QcLineResolverTests.Q1_sheetcut_is_presscnc_not_silk` + `Q2_lamination_is_finishing_not_label_or_silk` (khoá phân loại mới) · `Real_library_parses_to_106_items_across_5_lines` (khoá 5 line + FINISHING=5) · `IpqcAutoSyncControllerTests`/live GATE A (số khớp map). Quy tắc: taxonomy QC mở rộng bằng DỮ LIỆU (string token + seed), không enum-migration; GATE verify theo map hiện hành, baseline cũ chỉ để truy delta. |

---

<a id="l28"></a>
### L28 — Case-sensitive username lookup + uniqueness (BINARY collation) masks a correct password behind a 401; fix at the SCHEMA (NOCASE column), not with provider-specific COLLATE in app code

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Admin reset mật khẩu tài khoản `OQC` (Account Control) → user gõ `oqc` / `cclmes2026` → **401 `auth.invalid_credentials`**, dù hash đã verify khớp `cclmes2026`. "Reset xong không đăng nhập được" — nhìn như bug reset, thực ra mật khẩu ĐÚNG bị che. Bằng chứng DB: `SELECT COUNT(*) FROM Users WHERE Username='oqc'` → **0**; `... WHERE Username='oqc' COLLATE NOCASE` → **1**. |
| **Root cause** | Cột `Users.Username` mang collation mặc định **BINARY** (case-sensitive) và `IX_Users_Username` UNIQUE cũng BINARY. Login `FirstOrDefaultAsync(u => u.Username == username)` (Hybrid `AuthController.Login` + Web `Login.cshtml.cs`) so khớp case-sensitive → `oqc` ≠ `OQC` đã lưu → 0 dòng → trả 401 TRƯỚC cả khi kiểm mật khẩu. Cùng lỗi ở check trùng khi tạo (`AccountControlService.CreateAsync` / `UserAdminService`) → `OQC` và `oqc` có thể thành 2 tài khoản khác nhau. |
| **Fix** | **Schema, không phải app code.** Migration `20260720135222_UsernameCaseInsensitive`: `AlterColumn Username collation:"NOCASE"` (SQLite rebuild bảng + dựng lại `IX_Users_Username` theo NOCASE) — model khai báo `b.Entity<User>().Property(x=>x.Username).UseCollation("NOCASE")`. SQLite áp collation cột cho cả `=` (login CI) lẫn unique index (uniqueness CI) → một nguồn sự thật, cả Web + Hybrid + mọi query hưởng lợi, dùng đúng index, GIỮ nguyên hoa/thường để hiển thị. **KHÔNG** rải `EF.Functions.Collate(...,"NOCASE")` vào C#: thừa (cột đã NOCASE) và phá provider-agnostic (§3 SQL Server gate — NOCASE là collation riêng SQLite). Chính sách lỗi-chung ở response login giữ nguyên (không thêm oracle). §4.5 type-affinity đã strip. Phase A→B→C: backup live (`ccl_mes.before-username-nocase-*.db`) → generate + round-trip trên `/tmp` → apply live (Users=15 nguyên vẹn, integrity ok). |
| **Cơ chế chặn tái phát** | `AuthControllerTests.Login_username_is_case_insensitive` (`[Theory]` lower/upper/mixed → 200, trả về casing đã lưu) · `AccountControlControllerTests.Create_duplicate_username_different_case_returns_422_username_in_use` · model snapshot khoá `UseCollation("NOCASE")` trên `Username` (đổi = migration mới, `has-pending-model-changes` bắt). Quy tắc: matching/uniqueness cần case-insensitive thì đặt **collation ở cột** (schema), đừng dựa vào chuẩn-hoá rải rác trong query. |

---

<a id="l29"></a>
### L29 — A traceability/audit view that JOINs the live source silently drifts; freeze a DEAD self-describing JSON snapshot at confirm time and render it generically

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Yêu cầu: xem lại dữ liệu truy xuất theo từng mã chạy (Product/IPQC/FQC/OQC). Nếu màn hình đọc THẲNG entity nguồn (WoMaterial/WoIpqcCheck/WoQcChecks/BOM), thì sửa master data hay entity nguồn về sau sẽ **đổi luôn lịch sử đã "chốt"** — sai bản chất audit. Đặc biệt QPA(m²) là **link động** (`PrepressController`: `Qpa = QtyRequired / targetQty`) → đổi target/BOM là số cũ biến mất. Thêm nữa mỗi phase/variant có tập hạng mục khác nhau → nếu hardcode cột theo phase thì phải sửa code cho mỗi biến thể. |
| **Root cause** | Đọc-live = không có ranh giới thời gian; dữ liệu hiển thị luôn phản ánh trạng thái HIỆN TẠI của nguồn, không phải lúc xác nhận. Và cấu trúc dữ liệu kiểm khác nhau giữa phase/variant nên mô hình cột-cứng không mở rộng được. |
| **Fix** | **Snapshot chết + renderer generic.** (a) 1 bảng append-only `WoTraceSnapshot(WoId,WoNo,Phase,Version,SchemaVersion,FrozenAtUtc,FrozenBy,ContentHash,PayloadJson)`, **unique (WoId,Phase,Version)**, KHÔNG FK cứng sang nguồn. (b) `ITraceFreezeService.FreezeAsync(woId,phase,actor)` **dùng chung** — serialize giá trị LITERAL (kể cả QPA thành SỐ cố định) vào `PayloadJson` tại đúng mốc confirm OK (Product=rời PREPRESS · IPQC=GoRun/QA-approve · FQC=Pass · OQC=approve), hook best-effort (không làm vỡ confirm). **Idempotent theo ContentHash** (hash CHỈ phần nội dung, KHÔNG gồm timestamp → retry = NOOP); re-confirm khác nội dung → **Version++** (không đè). (c) Đọc Traceability CHỈ deserialize `PayloadJson` — KHÔNG JOIN nguồn. (d) Payload **self-describing** `{header:[{label,value}], items:[{key,label,status,ngReason,ngNote,extra{}}]}` → **1 renderer generic** (header key-value + bảng cột suy từ khóa item + union `extra`) chạy cho cả 4 phase + mọi variant, chỉ chứa hạng mục THỰC kiểm. **Tuyệt đối không lưu ảnh/blob** — tối đa `photoCount` (số). Migration `AddWoTraceSnapshot` theo §4 (generate /tmp, §4.5 strip, backup live Phase A→C). |
| **Cơ chế chặn tái phát** | `TraceabilityControllerTests`: `Frozen_product_snapshot_is_immutable_when_source_material_changes` (sửa WoMaterial sau freeze → payload GIỮ nguyên) · `Freeze_is_idempotent_by_content_hash_then_bumps_version_on_real_change` · `Payload_only_contains_items_actually_inspected_per_wo` (flexible 2 WO) · `Not_frozen_phase_is_null_in_detail` (empty-state) · `List_search_is_case_insensitive` · auth 401/403/404. bUnit `QualityTraceabilityTests`: list + dblclick mở dialog · renderer generic hiện cột từ `extra` (2 variant) · tab chưa freeze → empty-state. Quy tắc: view audit/traceability = **freeze snapshot literal**, không đọc-live; cột động = **payload self-describing + renderer generic**, không hardcode cột theo phase. |

---

<a id="l30"></a>
### L30 — Real-time updates on top of an immutable store: split a MUTABLE index from the immutable snapshots, and push notify-then-pull over the EXISTING hub

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Traceability dùng snapshot CHẾT (L29) → WO chỉ hiện khi có phase freeze; nhưng nghiệp vụ cần: **mọi WO vừa SCAN lên đã hiện + cập nhật real-time** (kể cả trước khi freeze), không refresh tay. Không thể "cập nhật real-time" một bảng immutable, và không được phá immutability. |
| **Root cause** | Trộn 2 nhu cầu ngược nhau vào 1 bảng: (a) audit bất biến, (b) trạng thái sống cập nhật liên tục. |
| **Fix** | **Tách 2 tầng.** (a) `WoTraceIndex` — bảng **MUTABLE, 1 row/WO** (WoNo unique), chỉ metadata nhẹ (product/customer/CurrentMesPhase/scan-times/FrozenFlags/LatestFrozenAt); danh sách đọc bảng này → WO hiện ngay khi scan. `ITraceIndexService.TouchAsync(woNo)` upsert idempotent (unique WoId/WoNo) + recompute FrozenFlags TỪ snapshot (read-only, KHÔNG đụng snapshot). (b) `WoTraceSnapshot` bất biến (L29) giữ nguyên — dialog chi tiết đọc nó. **Hook Touch** vào: scan/find (`GET .../by-no/{woNo}/summary`), advance (phase change), và `TraceFreezeService` (sau freeze). (c) **Notify-then-pull tái dùng hạ tầng có sẵn**: sau SaveChanges thành công → `ShopfloorNotifierV2.NotifyChangedAsync("trace_updated:{woNo}")` (KHÔNG hub mới; server chỉ báo "có đổi", client tự pull). (d) **Client**: 1 `IShopfloorLiveService` dùng chung (HubConnection `/hubs/shopfloor?access_token=`, `AccessTokenProvider` đọc `ITokenStore` → rotation-safe, `WithAutomaticReconnect`); trang subscribe `shopfloorChanged` → **debounce ~400ms** gộp burst scan → re-pull; **fallback polling 20s** khi mất kết nối + badge **Live/Offline**. (e) **Backfill idempotent** (AdminOnly): index mọi WO + freeze các phase đã kết luận (data present) để WO cũ có dữ liệu. Migration `AddWoTraceIndex` theo §4 (generate /tmp, §4.5 strip, backup live Phase A→C). |
| **Cơ chế chặn tái phát** | `TraceabilityControllerTests`: `Scan_touch_lists_the_wo_before_any_phase_is_frozen` · `Touch_is_idempotent_no_duplicate_index_row` · `Freeze_sets_index_flags_without_touching_the_snapshot` (tách tầng — index đổi, snapshot bất biến) · `Backfill_requires_admin_and_indexes_plus_freezes_concluded_phases`. bUnit `QualityTraceabilityTests`: `Live_signal_repulls_the_list` (notify → debounced re-pull) · `Live_offline_badge_reflects_connection`. Quy tắc: real-time trên store immutable = **tách MUTABLE index + notify-then-pull qua hub sẵn có**, KHÔNG mutate snapshot, KHÔNG mở hub/connection mới mỗi trang. |

---

<a id="l31"></a>
### L31 — Floating (drag/resize/multi) windows in a WKWebView: Pointer Events + geometry-in-JS, and dispose every listener

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Cửa sổ chi tiết Traceability cần thành **floating window kiểu desktop app**: kéo di chuyển bằng header, kéo giãn 8 hướng, mở nhiều cửa sổ cùng lúc + bring-to-front, double-click maximize, nhớ vị trí. Nếu làm bằng HTML5 `draggable`/`dragstart` thì trong **WKWebView / Mac Catalyst** `dragstart` hay KHÔNG fire + drag-image ghost; và nếu round-trip mỗi `pointermove` qua Blazor interop thì giật/lag + layout-thrash. |
| **Root cause** | (a) HTML5 native drag không đáng tin trong WKWebView (cùng họ lesson pointer/focus Catalyst — luôn ưu tiên DOM primitive, tránh component/API "sang"). (b) Đặt state hình học ở .NET → mỗi pixel kéo là 1 lượt interop + re-render. (c) JS listener gắn tay dễ **mồ côi** khi component dispose → leak / RendererCrashBoundary thấy handler chết. |
| **Fix** | Module JS `wwwroot/js/floating-window.js` (`window.cclMesFloat`, cùng pattern global-scoped như backup.js/clipboard.js, nạp qua `<script>` trong `index.html`). **Chỉ dùng Pointer Events** (`pointerdown/move/up` + `setPointerCapture/releasePointerCapture`) cho cả drag (từ header) lẫn 8 resize handle (N/S/E/W + 4 góc, cursor tương ứng). **Toàn bộ toán hình học ở JS**: rect áp bằng `transform: translate()` + inline `width/height`, gộp trong `requestAnimationFrame` (không layout-thrash); clamp min 480×320, max = viewport, **luôn giữ header trong màn hình**. Blazor **chỉ nhận rect cuối** (pointer-up / resize-end / maximize / keyboard-nudge) qua `DotNetObjectReference` → `OnRectChanged_JS`, KHÔNG interop mỗi pixel. Drag bỏ qua khi target là `button/input/tab/[data-fw-nodrag]` (✕/đổi tab/bôi đen không kéo). Bring-to-front = 1 stack z-index tăng dần trên `pointerdown` (capture-phase). Nhiều cửa sổ: parent (`QualityTraceability`) giữ `List<OpenWin>` (mỗi cái 1 `Id` + rect), **cascade offset 24px**, **cap 6** + thông báo khi vượt, mở lại WO đang mở → `bringToFront` (không nhân bản). Nhớ vị trí theo phiên qua singleton `IFloatingWindowStore` (keyed by WoNo) + nút "Reset windows" (`recenter`). Bàn phím: Esc đóng, mũi tên move, Shift+mũi tên resize; `role="dialog"` + `aria-label`. **`dispose(id)` gọi trong `IAsyncDisposable.DisposeAsync`** → removeEventListener tất cả + releasePointerCapture + xoá khỏi Map (không listener mồ côi). Non-modal: không scrim → danh sách nền vẫn thao tác được. |
| **Cơ chế chặn tái phát** | bUnit `QualityTraceabilityTests`: `List_renders_and_double_click_opens_dialog` (khẳng định `role=dialog` + `aria-label` + KHÔNG có `.trace-modal-scrim`) · `Multiple_showcards_open_independently` (2 cửa sổ, 16 = 8×2 resize handle) · `Reopening_same_wo_focuses_not_duplicates` · `Opening_beyond_the_cap_is_blocked_with_a_notice` (cap 6 + "tối đa 6") · `Closing_a_showcard_removes_it` · `Rect_callback_persists_to_the_store_and_restores_on_reopen` (OnRectChanged_JS → `IFloatingWindowStore` → Rect param khi mở lại). Checklist thủ công phần JS thuần: [`docs/floating-window-checklist.md`](./floating-window-checklist.md). Quy tắc: floating window trong WKWebView = **Pointer Events (không HTML5 drag) + geometry-in-JS (rAF, rect cuối) + dispose listener bắt buộc**. |

---

<a id="l32"></a>
### L32 — Resize handle "chết" trong khi drag chạy: `overflow:hidden` trên lớp bọc CẮT vùng bắt chuột của handle mép/góc

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Floating showcard (L31): **kéo header di chuyển OK**, nhưng **kéo 8 handle để resize KHÔNG ăn** (cả N/S/E/W lẫn 4 góc). Logic JS resize (`setPointerCapture` trên handle, công thức N/W đổi cả pos lẫn size) đúng, nhưng `pointerdown` không bao giờ tới handle. |
| **Root cause** | **PROVEN từ CSS**: 8 handle đặt offset ÂM để "cưỡi" lên mép card (`.fw-n{top:-3px}` … `.fw-ne{top:-4px;right:-4px}`) — cố ý, để tóm được cả rìa ngoài. NHƯNG container `.trace-win` lại có **`overflow:hidden`** → toàn bộ phần handle nhô ra ngoài padding-box bị **clip**; góc (âm cả 2 chiều) bị cắt gần hết → hit-area ≈ 0; cạnh chỉ còn dải mỏng lấn vào body, bắt chuột chập chờn. Đây là "handle bị lớp bọc `overflow:hidden` nuốt", KHÔNG phải drag nuốt pointer (drag chỉ bind trên header, handle là sibling). |
| **Fix** | **Tách lớp clip khỏi lớp handle.** `.trace-win` (card) đổi thành **`overflow:visible`** + chỉ giữ `border-radius`/`box-shadow`; bọc chrome (head+tabs+body) vào **`.trace-win-inner`** MỚI — lớp này giữ `overflow:hidden` + bo góc + scroll body (`min-height:0` để flex-column cuộn đúng). Vì handle là **con trực tiếp của `.trace-win`** (không nằm trong `.trace-win-inner`) và card không còn clip → handle **không bao giờ bị cắt**, hit-area đầy đủ và còn nhô ra ngoài mép cho dễ tóm. Handle `z-index:6` > lớp inner. Tăng hit-area: cạnh 12px (offset −5px), góc 18px (−6px). Giữ nguyên logic JS (setPointerCapture trên chính handle + `stopPropagation`/`preventDefault`; công thức E:`w=ow+dx` · W:`w=ow−dx,x=ox+dx` · S:`h=oh+dy` · N:`h=oh−dy,y=oy+dy` · góc = ghép cặp; clamp min 480×320 khóa vị trí khi chạm min ở N/W). Thêm: `endResize` huỷ rAF đang chờ + `apply()` frame cuối trước `notify()` để rect ghi về Blazor khớp pixel. Dispose vẫn gỡ listener + releasePointerCapture (L31). |
| **Cơ chế chặn tái phát** | bUnit `QualityTraceabilityTests.Multiple_showcards_open_independently` khẳng định **16 = 8×2** `.fw-handle` render (mỗi cửa sổ đủ 8 handle). Checklist thủ công 8 hướng: [`docs/floating-window-checklist.md`](./floating-window-checklist.md) (kéo từng handle → đổi đúng cạnh/góc; N/W không "nhảy"; chạm min dừng không trôi). **Quy tắc: handle resize đặt offset âm thì container KHÔNG được `overflow:hidden` — đẩy clip xuống lớp inner riêng; handle phải `z-index` trên lớp chrome + `setPointerCapture` trên CHÍNH handle.** |

---

<a id="l33"></a>
### L33 — Part Description không sync: lấy từ CHUỖI SCAN thay vì DÒNG BOM match được; và part_scan chỉ nằm trong audit nên freeze không có

| Field | Detail |
| --- | --- |
| **Triệu chứng** | PREPRESS: cột **Part Scan có mã** nhưng **Part Description = "—"** (trống). Mã trần dạng `30030491-0145` (không có `/mô tả`) thì Description luôn rỗng. Ngoài ra khi freeze Product data, `partScan`/`partDescription` không xuất hiện trong snapshot (chỉ có trong audit detail). |
| **Root cause** | **PROVEN**: Part Description resolve từ `MaterialBarcodeMatcher.ExtractDescription(scan)` = phần sau dấu `/` đầu tiên của **chuỗi scan**. Mã trần không có `/` → `""` → "—". Cả nhánh scan (`SubmitScanAsync`) lẫn gõ tay (`OnPartScanCommit`) đều lấy mô tả từ CHUỖI, không từ **dòng BOM đã khớp** (vốn đã biết `MaterialDescription` thật). Và `part_scan` chỉ được ghi vào `AuditLog.detail`, KHÔNG persist trên `WoMaterial` → `TraceFreezeService.BuildProductAsync` không có gì để đóng băng. |
| **Fix** | **(1) Resolve từ BOM row**: `MaterialBarcodeMatcher.ResolveDescription(row, scan)` = `row.MaterialDescription` khi có row + có mô tả, else fallback `ExtractDescription(scan)` (giữ desc khi chuỗi có `/desc`; "—" khi mã trần không khớp). `Match()` set `Description` theo ResolveDescription cho Single/AllOk; `OnPartScanCommit` cũng resolve từ chính row đang gõ. **(2) Persist**: thêm 2 cột nullable `WoMaterial.PartScan` + `PartScanDescription` (migration `AddWoMaterialPartScan` theo §4: generate /tmp isolated, strip type-affinity §4.5, Phase A→C backup live, KHÔNG `ef migrations remove`). `SetPrepressMaterialRequest` + `MaterialSetIntent` mang thêm PartScan/PartScanDescription; `PrepressController.PutMaterial` persist khi request có (không ghi đè khi vắng); `SpecialAcceptMaterial` persist PartScan + resolve desc **server-side** từ `row.MaterialDescription`. **(3) Freeze**: `BuildProductAsync` BỎ `scrapFactor`/`scrapPercent`, THÊM `no`/`partScan`/`partDescription` vào Extra; QPA(m²) đóng băng 6-dp. **(4) UI**: tab Product dùng **layout cố định 11 cột** (No. | Part No | Description | QPA(m²) | Qty.Required | UoM | Part Scan | Part Description | Lot | Status | NG — reason·note), No. tách khỏi Part No; IPQC/FQC/OQC vẫn generic renderer. Immutability giữ nguyên (đọc snapshot literal, sửa nguồn không đổi). |
| **Cơ chế chặn tái phát** | `MaterialBarcodeMatcherTests`: `Bare_code_still_gets_description_from_matched_bom_row` · `Matched_row_description_wins_over_noisy_scan_remainder` · `Falls_back_to_scan_remainder_when_bom_row_has_no_description` · `No_match_bare_code_yields_empty_description_no_crash` · `ResolveDescription_prefers_row_then_scan`. `TraceabilityControllerTests.Product_freeze_carries_part_scan_and_description_and_drops_scrap` (payload có partScan/partDescription/no + KHÔNG có scrapFactor/scrapPercent; immutability qua `Frozen_product_snapshot_is_immutable...`). bUnit `QualityTraceabilityTests`: `Product_tab_uses_fixed_layout_no_scrap_columns` (11 cột đúng thứ tự, No.≠Part No, không Scrap) · `Product_special_accept_row_shows_distinct_status_and_ng` · `Ipqc_tab_still_uses_the_generic_renderer`. **Quy tắc: field "mô tả theo mã" phải resolve từ MASTER/BOM đã khớp, không parse từ chuỗi scan; muốn freeze được thì persist thành CỘT, không để riêng trong audit.** |

---

<a id="l34"></a>
### L34 — Window chrome tự-vẽ trong 1 dialog → mỗi showcard mới lại làm lại: TÁCH `<FloatingWindow>` dùng chung + gate CI

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Toàn bộ chrome floating-window (drag header + resize 8 hướng + traffic-light min/max/close + keyboard + persist rect + JS interop `cclMesFloat`) nằm INLINE trong `TraceabilityDetailDialog.razor`. Bất kỳ showcard/detail-dialog mới nào (đang có ~13 modal + các dashboard) muốn "cửa sổ nổi" sẽ phải COPY lại toàn bộ → drift + tái phạm L31/L32 (pointer/overflow) ở mỗi bản sao. |
| **Root cause** | Chrome tái sử dụng bị nhốt trong 1 component nghiệp vụ; không có primitive dùng chung + không có rào chặn "showcard mới phải dùng nó". |
| **Fix** | **(A) Tách `Shared/FloatingWindow.razor`** — đóng gói `_rootRef/_headRef`, `cclMesFloat.init/dispose/nudge/toggleMax/Min`, 8 handle, cụm traffic-light (SVG glyph + maximize⇄restore theo state đồng bộ qua `OnRectChanged_JS`), keyboard (Esc/mũi tên/Shift), `WindowId`/`Rect`/`CascadeIndex`/`OnClose`/`OnRectChanged` + persist qua `IFloatingWindowStore`. Slot: `HeaderContent`/`HeaderExtra`/`TabBar`/`ChildContent`. `TraceabilityDetailDialog` refactor để **BỌC** `<FloatingWindow>` (chỉ còn cung cấp NỘI DUNG 4 tab) — parity 100%, không hồi quy. **(B) Audit** mọi surface: SHOWCARD (giữ-mở-song-song, xem/giám sát) → BẮT BUỘC `<FloatingWindow>`; **transactional** (form Create/Edit/Copy/Import + confirm Pause/Finish/QtyCorrect) → GIỮ modal căn giữa (đúng pattern; ép float làm hại UX). `Modal.razor` thêm **opt-in `Float="true"`** (render qua `<FloatingWindow>`) mặc định OFF → 13 modal không đổi. **(C) Chặn tái phạm**: skill `.claude/skills/cmes-floating-showcard/SKILL.md` + gate CI `scripts/gate-floating-showcard.sh` (PR thêm `*DetailDialog/*Showcard*.razor` mà KHÔNG có `<FloatingWindow>` → fail; allowlist các Spec*Showcard inline + FloatingWindow.razor kèm lý do). |
| **Cơ chế chặn tái phát** | Gate `scripts/gate-floating-showcard.sh` (đã test: PASS trên cây hiện tại, FAIL khi chèn `FakeDetailDialog.razor` không có `<FloatingWindow>`). bUnit `FloatingWindowTests`: `Renders_full_window_chrome` (8 handle + 3 traffic-light + role/aria + ChildContent) · `Close_button_raises_OnClose` · `Rect_callback_reports_rect_and_toggles_maximize_icon` · `Minimize_and_maximize_toggles_can_be_hidden` · `Modal_default_is_a_centred_scrim_not_a_floating_window` · `Modal_float_mode_renders_the_floating_window_chrome`. `QualityTraceabilityTests` (169) chứng minh parity sau refactor. **Quy tắc: showcard/detail-dialog mới PHẢI bọc `<FloatingWindow>`, KHÔNG tự vẽ chrome; transactional giữ `<Modal>` (float là opt-in).** |

**L34 addendum (P11 — 2026-07-26): showcard INLINE lọt gate filename-based.** IPQC per-leg
inspector ban đầu bị hand-roll INLINE trong `LegsDashboard.razor` (`<div role="dialog">`
chỉ có `× Close`, không chrome) → gate cũ (chỉ quét tên file `*Showcard*/*DetailDialog*`)
KHÔNG bắt. **Vá:** gate nay **quét markup** — mọi `.razor` có literal `role="dialog"` mà
KHÔNG bọc `<FloatingWindow>` → FAIL kèm `file:line` (allowlist 2 primitive
`FloatingWindow.razor` + `Modal.razor`; page dùng component `<Modal>` không có literal
`role="dialog"` nên không báo nhầm). Đã chứng minh: PASS cây đã sửa; chèn fake inline
`role="dialog"` vào `Home.razor` → FAIL(`Home.razor:187`), gỡ → PASS. Thêm **hook
`UserPromptSubmit`** (`.claude/settings.json` → `scripts/hook-showcard-reminder.sh`) nhắc
quy trình khi prompt nhắc showcard/inspector. **Fix inline→showcard:** tách body ra
component bọc `<FloatingWindow>` (`IpqcLegShowcard.razor`) + parent giữ multi-window state
qua `IFloatingWindowStore` (`LegsDashboard._ipqcWins`, mirror `QualityTraceability`).

---

<a id="l35"></a>
### L35 — Hành động trên dòng: 1 `RowContextMenu` dùng chung (chuột phải + long-press + ⋯) thay cột "Actions"

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Grid Work Centers thêm cột "Actions" chứa nút Edit/Delete mỗi dòng — tốn width, không mở rộng khi nhiều hành động, và mỗi grid lại tự chế cột riêng (như `SpecContextMenu` bó cứng cho Spec). |
| **Root cause** | Không có primitive menu-ngữ-cảnh dùng chung + không có rào chặn "grid mới đừng thêm cột Action". |
| **Fix** | **`Shared/RowContextMenu.razor`** TỔNG QUÁT: `Open` · `Anchor(X,Y)` · `IReadOnlyList<ContextMenuItem>` (`Label/Icon/Danger/Disabled/OnClick` + `Divider`) · `OnClose`. Định vị + **clamp trong viewport** + focus item đầu + auto-close (scroll/zoom/blur) trong `wwwroot/js/context-menu.js`; đóng khi click-ngoài/Esc/chọn item; a11y `role=menu/menuitem` + Arrow Up/Down roving + Enter/Space (native `<button>`). **BA lối vào chung 1 state**: (1) chuột phải `@oncontextmenu:preventDefault`, (2) **long-press ~500ms** (touch/WKWebView không phải lúc nào cũng bắn `contextmenu`), (3) **nút ⋯ kebab** ở cột cuối hẹp (affordance dễ thấy). **RBAC-by-omission**: chỉ build item user được phép; không có item → không mở menu + ẩn ⋯ (server vẫn enforce 403). `NpiWorkCenters` BỎ cột Actions; Copy = mở modal Add prefilled (Code trống → unique). |
| **Cơ chế chặn tái phát** | Gate `scripts/gate-row-actions.sh` (đã test: PASS cây hiện tại; FAIL khi chèn grid có `<th>Actions</th>`; allowlist các surface cũ + cột "Action" DATA của AuditLog). bUnit `RowContextMenuTests` (render items/divider/disabled · chọn item gọi OnClick+OnClose · Esc/scrim đóng) + `NpiWorkCentersTests` (chuột phải + ⋯ mở menu · chọn Edit/Copy/Delete · non-admin không có ⋯/menu). Skill `.claude/skills/cmes-row-context-menu/SKILL.md`. **Quy tắc: hành động trên dòng = `RowContextMenu` (right-click/long-press/⋯), KHÔNG cột "Actions".** |

---

<a id="l36"></a>
### L36 — Full-screen page để dải trống 2 bên: wrapper `display:grid; place-items:center` sizing theo NỘI DUNG, không theo viewport

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Màn login split (LMS) đúng thiết kế nhưng để **dải trống tối 2 bên** — nội dung bị bó chiều rộng, không lấp đầy cửa sổ desktop/tablet dù kéo rộng. |
| **Root cause** | HAI lỗi chồng nhau: (1) **Ngang**: `.empty-layout` dùng `display:grid; place-items:center` → cột ngầm `auto` sizing theo **max-content**, căn giữa → thừa 2 bên. (2) **Dọc**: comment CSS phía trên `.login-shell` chứa **comment lồng** `(/* login */ block)` — CSS KHÔNG cho comment lồng, `*/` đầu tiên đóng comment sớm, phần `block). … */` còn lại phá parse → **cả rule `.login-shell` bị nuốt** (computed `min-height:0; display:block`), nên `min-height:100vh` KHÔNG áp → shell = chiều cao nội dung (~660px), dải tối ĐÁY. Debug bằng probe log ancestor-chain qua `cclLog` bridge mới lộ ra (`empty-layout minH=1109px` OK nhưng `login-shell minH=0px`). |
| **Fix** | (1) **Xoá comment lồng** trong CSS (không bao giờ đặt `/* … */` bên trong một comment khác) → rule `.login-shell` áp lại → `min-height:100vh` lấp đủ cao. (2) Full-bleed page = **BLOCK wrapper fill**, không centre: `.empty-layout { display:block; min-height:100vh; }` → con block `.login-shell { width:100%; min-height:100vh }` lấp KÍN chiều ngang + cao. Bỏ centre ở wrapper an toàn vì Lock tự căn giữa trong `.lock-shell` riêng. Split fluid: hero `flex:1.1`, form `flex:1`; padding/type dùng `clamp()`; breakpoints S9 (≥1024 2 cột · <900 hero thu gọn dải trên · <600 ẩn hero, form full-width + safe-area · max-height:560 form cuộn); input ≥16px (chống zoom iOS) + ≥44px cao; `env(safe-area-inset-*)`. **Lưu ý test**: resize cửa sổ MAUI qua AppleScript để viewport height của WKWebView bị stale (~load-size) → hiện dải trống ĐÁY GIẢ trong harness; kéo tay thật thì `min-height:100vh` lấp đúng (ảnh gốc user chứng minh full-height). |
| **Cơ chế chặn tái phát** | Rule S9 (SKILLS.md/CLAUDE.md): **màn full-screen mới PHẢI qua responsive matrix + KHÔNG centre một full-screen shell bằng grid/flex place-items** (→ dead bands). Screenshot matrix desktop/tablet/phone khi thêm/đổi màn full-screen. |

---

<a id="l37"></a>
### L37 — Retone toàn app khi CSS toàn hex hardcode: dựng LỚP TOKEN semantic (no-op) → route theo nhóm → retheme = swap giá trị token

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Yêu cầu đổi tone toàn app sang corporate-blue. `app.css` có **~1252 hex hardcode / chỉ ~10 token** (dùng 75 lần) → "chưa token-hoá". Một lần thử retone bằng **find/replace hex trực tiếp** (tool ngoài) làm app *nhìn* xanh nhưng vẫn còn ~1146 hex hardcode → **mục đích token layer KHÔNG đạt** (retheme lần sau vẫn phải sửa >1000 literal); audit chấm ~8% token-hoá. |
| **Root cause** | (1) Token layer gốc chỉ phục vụ **dark-surface** (`--navy/--ink/--line` = giá trị TỐI ở `:root`, `.app-nav` **re-scope** cùng tên sang sáng cho sidebar) nên **content sáng để hex thô** (`color:#1f2937` cứng, không phải `var(--ink)` vì `--ink` là chữ-trên-nền-tối). (2) Đổi màu bằng find/replace không tạo lớp trừu tượng → không tái dùng được. |
| **Fix** | Quy trình **3 phase, mỗi phase verify + commit riêng**: **P1** — thêm LỚP TOKEN SEMANTIC PHẲNG mới (`--c-ink*/--c-bg/--c-line/--brand*/--accent/--indigo/--ok|ng|warn*`) định nghĩa 1 lần ở `:root`, **KHÔNG re-scope** → `var()` phân giải hằng số ở mọi nơi; giá trị = **màu hiện tại** ⇒ render y hệt (defs chưa dùng = byte-identical). **P2** — thay hex→`var(--token)` **theo nhóm vai trò** (neutral-ink/surface/brand/accent-indigo/status-ok|ng|warn/dark), mỗi nhóm 1 commit; token = giá trị hex thay ⇒ no-op (`1279→89` hex còn lại = defs + one-off). **P3** — retheme = **đổi GIÁ TRỊ token trong `:root`** (34 token → blue #2E5BFF/#1E3A8A/#38BDF8, content #F4F7FF/#1E2A3A/#E3E9F5) → lan toả toàn app 1 swap. **BẪY tool**: replacer hex→var phải **strip comment TRƯỚC khi parse declaration** — comment chứa `chữ:` (vd `RULE:`, `:root value`) đánh lừa parser coi định nghĩa `--token: #hex` NGAY SAU là property thường → nuốt hex vào "pseudo-value" → sinh **`--x: var(--x)` VÒNG** phá token âm thầm (đã dính `--navy`, `--c-ink`; phát hiện qua grep `^\s*--[a-z-]+:\s*var\(`). |
| **Cơ chế chặn tái phát** | Gate `scripts/gate-no-hardcoded-hex.sh` (đã test: PASS cây hiện tại; FAIL khi thêm hex thô vào 1 RULE trong `app.css` — dòng ĐỊNH NGHĨA token `--x: #hex` dù ở `:root` hay khối re-scope như dark-chrome đều được MIỄN) — màu mới PHẢI qua token. Rule trong `:root` (comment `RULE: no raw hex (L37)`) + SKILLS.md S15. Invariant grep `^\s*--[a-z0-9-]+\s*:\s*var\(--\1\)` = 0 (không định nghĩa token vòng). **Quy tắc: đổi/thêm màu = sửa/ thêm TOKEN, KHÔNG hardcode hex trong `app.css`; retone = swap giá trị token, không find/replace hex.** |

### L38 — SQLite per-row RowVersion: `IsRowVersion()` bỏ giá trị lúc INSERT → NOT NULL fail; fix bằng `IsConcurrencyToken().ValueGeneratedNever()`, KHÔNG thêm DB default qua AlterColumn (rebuild drop trigger)

| Field | Detail |
| --- | --- |
| **Triệu chứng** | P11-2: 11/13 RoutingController test đỏ với `SqliteException 19: NOT NULL constraint failed: WoLegs.RowVersion` khi controller INSERT một `WoLeg` mới. Bảng `WoLegs.RowVersion BLOB NOT NULL` + 2 trigger `randomblob(8)` (copy đúng pattern WorkOrders) mà vẫn fail. |
| **Root cause** | EF với `.IsRowVersion()` coi cột là **store-generated** (`ValueGeneratedOnAddOrUpdate`) → **KHÔNG đưa RowVersion vào câu INSERT**. SQLite kiểm `NOT NULL` NGAY lúc insert row — TRƯỚC khi trigger `AFTER INSERT` chạy → NULL → fail. `WorkOrders` không dính vì migration cũ thêm cột với `defaultValue: new byte[0]` (→ `DEFAULT X''`); `WoLegs` tạo qua `CreateTable` KHÔNG có default. **Bẫy thứ 2 khi thử fix bằng default**: `AlterColumn` thêm `defaultValueSql:"X''"` trên SQLite = **table-rebuild** (`ef_temp` → copy → DROP → RENAME) → **DROP mọi trigger** gắn trên bảng; tệ hơn, EF **reorder** `migrationBuilder.Sql(CREATE TRIGGER)` chạy TRƯỚC rebuild → trigger vừa tạo bị DROP ngay (verify: `ef migrations script` cho thấy CREATE TRIGGER ở trên, `DROP TABLE WoLegs` ở dưới). |
| **Fix** | Bỏ hướng DB-default. Cấu hình `b.Entity<WoLeg>().Property(x=>x.RowVersion).IsConcurrencyToken().ValueGeneratedNever()` → EF **GỬI** giá trị app (`Array.Empty<byte>()` = `X''`) trong INSERT ⇒ cột NOT NULL không NULL ⇒ trigger `randomblob(8)` bump. `IsConcurrencyToken` vẫn đưa RowVersion vào **WHERE của UPDATE** → optimistic-lock (soak 1-winner/N-conflict) còn nguyên. **Live schema KHÔNG đổi** (cột vẫn BLOB NOT NULL) → migration sync chỉ là snapshot (Up rỗng), áp xuôi, không rollback (rollback Down bị auto-classifier chặn — đúng, playbook cấm revert live). |
| **Cơ chế chặn tái phát** | `RoutingControllerTests` 13 fixture (materialize INSERT 4 leg + `Concurrent_advance_same_leg_N_equals_10_one_winner` soak = 1×200/9×409) — mọi INSERT/UPDATE leg đi qua đường EF thật; nếu ai đổi lại `.IsRowVersion()` → materialize test đỏ ngay với NOT NULL. Unit `WoLegRowVersion` + `p11-live-verify.sh` §concurrency. **Quy tắc: cột RowVersion per-row trên SQLite = `IsConcurrencyToken().ValueGeneratedNever()` (EF gửi X'') + trigger randomblob; KHÔNG `IsRowVersion()` (omit → NOT NULL fail) và KHÔNG thêm default qua `AlterColumn` (rebuild drop trigger + EF reorder Sql trước rebuild).** |

### L39 — Spec-sheet print/PDF WYSIWYG: `window.print()` chết trong WKWebView; bảng rộng `table-layout:fixed`+wrap → hàng 2–3 dòng; scoped print-CSS chết → in lệch on-screen

| Field | Detail |
| --- | --- |
| **Triệu chứng** | (1) Nút "In PDF" trên maccatalyst không mở hộp in nào (`window.print()` no-op). (2) In được (native) thì tờ Spec Full ra **3 trang dọc cồng kềnh**. (3) Bảng "Print Process — 6 colors" (21 cột) khi in **wrap 2–3 dòng/hàng**, cỡ chữ trông không đều — KHÁC on-screen (mỗi hàng 1 dòng). (4) PDF server MigraDoc **cắt còn 8/21 cột** (mất Ink Name/Retarder/Visc/…). |
| **Root cause** | (a) MAUI BlazorWebView trên maccatalyst **nuốt `window.print()`** — WKWebView không mở panel. (b) `@media print` đặt trong scoped `.razor.css` **chết trên maccatalyst** (chỉ `wwwroot/css/app.css` global được load). (c) Trong `@media print`, bảng rộng để `table-layout: fixed` → **chia bề rộng ĐỀU** → cell dài ("VIC-710 BLACK (1can=18kg)") bị bóp → **wrap**; cỡ chữ rải mỗi cột → trông không đều. (d) MigraDoc `BuildDetailSheet` dựng **A4 Portrait** → khổ hẹp nên tác giả cũ **cắt cột** cho vừa (data loss) + tờ xếp dọc tràn trang. |
| **Fix** | (a) **Native print**: `IPrintService` (Client) + `CatalystPrintService` dùng `UIPrintInteractionController` + `wkWebView.ViewPrintFormatter` (OS raster DOM sống = WYSIWYG; panel A4/A3 + hướng + scale + Save-PDF). WKWebView ref bắt ở `MainPage.OnBlazorWebViewInitialized`; bridge `cclMesPrint` + `print.js` cho `window.cclMesPrint.print()`/Cmd+P. Nút gọi `IPrintService`, fallback MigraDoc khi `!IsNativePrintSupported`. (b) `@media print` để trong **global app.css**; ẩn chrome, chỉ hiện `.spec-showcard-full`, nén dọc, `@page A4 landscape`. (c) Bảng rộng: **`table-layout: auto` + `white-space: nowrap` + MỘT token `--spec-print-table-fs`** (header+body cùng cỡ) → mỗi hàng 1 dòng, font đều; hẹp quá thì HẠ token 1 chỗ, KHÔNG wrap. On-screen cùng `nowrap` + `.spec-table-scroll` cuộn ngang → WYSIWYG. (d) MigraDoc: **A4 Landscape mặc định + đủ 21 cột (không cắt) + auto-fit `PdfDocument.PageCount` ≤2 (render→hạ bậc `DetailLayout`→render lại) + hairline 0.25pt + `KeepWithNext`**. |
| **Cơ chế chặn tái phát** | `scripts/gate-spec-print.sh` (block-aware awk): FAIL nếu (1) `CatalystPrintService` mất `UIPrintInteractionController`/`ViewPrintFormatter`, (2) app.css mất `@media print`, (3) `.spec-print-table-full` quay lại `table-layout: fixed` **hoặc** mất `white-space:nowrap`/token `--spec-print-table-fs`, (4) `BuildDetailSheet` mặc định không Landscape. Đã chứng minh PASS→FAIL(inject fixed)→PASS. Skill `.claude/skills/cmes-spec-print/SKILL.md` + hook `hook-spec-print-reminder.sh`. Test: `SpecPdfDispatchTests` (landscape · fits-2-pages · long-spec auto-fit ≤2 · hairline ≤0.25) + `StubPrintServiceTests` + 2 bUnit fallback. **Quy tắc: in trên maccatalyst = native `IPrintService`, KHÔNG `window.print()`; print-CSS = global app.css; bảng rộng = auto+nowrap+1 token, KHÔNG fixed+wrap; MigraDoc = fallback landscape đủ-cột auto-fit.** |

---

<a id="l40"></a>
### L40 — Luật nghiệp vụ sống trong controller HTTP → không test được, không tái dùng, và hai endpoint gần giống nhau sẽ phân kỳ

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Audit kiến trúc 2026-08-18 đo trên `CCL-MES-Hybrid/src/CCL.MES.Api/Controllers/`: **22** lần `SaveChangesAsync` gọi thẳng trong controller (`WoQcReviewController` 6, `RoutingController` 4, `SemiStockController` 3, `IpqcReviewController` 3…), **20/33** controller `using` thẳng `MesDbContext`, `WoQcReviewController.cs` = **1.460 dòng**. Luật nghiệp vụ nặng nhất của hệ (gate QC, quy tắc 3 chữ ký Inspector≠Reviewer≠Approver, join leg) nằm ở tầng HTTP. Mâu thuẫn trực tiếp với Clean Architecture mà `MINDMAP.md` §2 tuyên bố. |
| **Root cause** | Đường ngắn nhất để ship một endpoint là viết thẳng trong action: có sẵn `_db`, có sẵn `User`, không phải nghĩ tên service. Không có gate nào phản đối, nên mỗi PR thêm một chút. Hệ quả kép: (1) muốn test luật phải dựng `WebApplicationFactory` (chậm + giòn) nên nhiều nhánh guard **không được test**; (2) luật không gọi lại được từ background job / ERP adapter / API thứ hai cho máy — tức là **cổng tích hợp ERP bị chặn bởi chính cách xếp tầng này**; (3) hai endpoint "gần giống nhau" copy-paste rồi phân kỳ âm thầm. |
| **Fix** | Hình dạng bắt buộc: `Controller (bind · [Authorize] · gọi service · map ApiError)` → `Application/Service (orchestration · transaction · emit audit)` → `Domain/Policy (luật thuần, không I/O, unit-test được)`. Tách một `*Policy` khi luật thoả ≥2 điều: ≥3 nhánh điều kiện · dùng ở ≥2 nơi · sai thì hỏng dữ liệu. Ứng viên đã xác định: `SignaturePolicy`, `QcGate`, `LegAdvancePolicy`, `SemiStockPolicy`. Vì con số hiện tại là **nợ**, gate đặt ở chế độ **ratchet đi xuống**: code mới không được làm tệ hơn, PR chạm vùng cũ kéo BASELINE xuống trong cùng PR. |
| **Cơ chế chặn tái phát** | Gate `scripts/gate-thin-controller.sh` — ratchet `SaveChangesAsync` trong controller (BASELINE **22**) + số controller > **400** dòng (BASELINE **8**). Đã chứng minh PASS → FAIL (inject `SaveChangesAsync`) → PASS qua `--self-test`. Skill `.claude/skills/cmes-thin-controller/SKILL.md`. Agent `cmes-implementer` mang luật này trong định nghĩa. **Quy tắc: controller chỉ bind/authorize/gọi/map lỗi; 0 `SaveChangesAsync`, 0 truy vấn `DbContext` trong controller MỚI.** |

<a id="l41"></a>
### L41 — Token-hoá màu mà không token-hoá kích thước: 6 commit chỉnh tay một cái bảng, và không có density nào cho người đeo găng

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Sau L37, `app.css` có token màu đầy đủ nhưng vẫn tốn 6 commit liên tiếp chỉ để chỉnh **một** bảng QC library: `tăng size nhãn cột tick 0.9→1.08rem` · `nới cột tick 3.4%` · `table-layout:fixed + colgroup %` · `bảng full-width + nhãn cột to hơn` · `chữ to hơn + trình bày thoáng` · `fluid scaling clamp/vw`. Đo lúc đó: **527/530** khai báo `font-size` đặt bằng số, không qua biến. Song song: **không có bất kỳ chế độ hiển thị nào** cho người đứng máy — cùng một bộ số 28px/13px dùng cho cả kỹ sư ngồi bàn lẫn operator đeo găng dưới ánh sáng xưởng. |
| **Root cause** | Token layer của L37 chỉ phủ **một chiều** của hệ thiết kế (màu). Không có thang chữ / thang khoảng cách nên khi cần một cỡ, lựa chọn duy nhất là gõ số theo cảm giác trên đúng cỡ màn hình đang mở → lần sau mở màn khác lại chỉnh tiếp. `clamp()`/`vw` được dùng như cách **né** việc chọn bậc thang, không phải như fluid type có chủ đích. Và vì không có khái niệm density, mọi ràng buộc vật lý của xưởng (găng tay ⇒ vùng chạm ≥44px; nhìn xa ⇒ chữ ≥16px; ánh sáng xưởng ⇒ tương phản cao hơn) không có chỗ nào để biểu đạt trong CSS. |
| **Fix** | Áp **đúng chiến thuật P1 của L37** cho kích thước: thêm lớp thang vào `:root` với **giá trị = số đang dùng phổ biến** ⇒ thêm vào là no-op thị giác, không big-bang. Thang: chữ `--fs-xs…--fs-2xl` (7 bậc) · khoảng cách `--sp-1…--sp-7` (lưới 4px) · bo góc `--r-*` · đổ bóng `--el-1..3` · chuyển động `--mo-*` + `--ease-std` · `--focus-ring`. Density: `--d-font/--d-row-h/--d-control-h/--d-tap/--d-gap/--d-pad-x` định nghĩa ở `:root` (office = trạng thái hiện tại) và re-scope trong `:root[data-density="shopfloor"]` (row 56px, control/tap **44px**, font 16px). **Một component, hai bộ số — KHÔNG fork màn hình riêng cho xưởng** (fork = nhân đôi bug). Kèm 4 class tiện ích opt-in (`.u-row/.u-tap/.u-stack/.u-focus`) để surface mới có đường đúng sẵn. Route rule cũ sang `var()` làm dần theo PR. |
| **Cơ chế chặn tái phát** | Gate `scripts/gate-design-tokens.sh` — (A) **hard fail** nếu `:root` mất bất kỳ token bắt buộc nào (`--fs-md`, `--fs-base`, `--sp-4`, `--r-md`, `--mo-base`, `--focus-ring`, `--d-tap`, `--d-row-h`, `--d-font`) hoặc mất khối `[data-density="shopfloor"]` (= hệ thiết kế bị tháo); (B) **ratchet** số `font-size` không dùng `var()` (BASELINE **527**). Đã chứng minh PASS → FAIL (inject `font-size: 13px`) → PASS qua `--self-test`. Kiểm bất biến L37 sau khi chèn: braces 1993/1993 cân, `--x: var(--x)` vòng = 0. Skill `.claude/skills/cmes-design-tokens/SKILL.md`. Agent `cmes-shopfloor-ux`. **Quy tắc: cỡ chữ mới dùng `var(--fs-*)`/`var(--d-font)`, khoảng cách mới dùng `var(--sp-*)`; mọi surface Operator chạm phải screenshot ở CẢ HAI density trong PR.** |

<a id="l42"></a>
### L42 — Chuỗi hiển thị bỏ quên ngoài catalog (người dùng EN thấy tiếng Việt), và key trùng giết app NGAY LÚC KHỞI ĐỘNG

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Audit 2026-08-18: **99 dòng** trong 36 file `.razor` của `CCL.MES.Hybrid.Razor` còn chuỗi tiếng Việt nằm trần trong markup — không đi qua `TranslationCatalog` ⇒ người dùng chọn EN vẫn thấy tiếng Việt ở những chỗ đó. Ngoài ra hệ đang chạy **hai** cơ chế i18n song song: `TranslationCatalog` in-code (Hybrid, 2.171 key trong 80 partial) và `SharedResource[.vi].resx` (legacy Web) — dễ thêm nhầm chỗ. |
| **Root cause** | (1) i18n bị coi là **một task riêng làm sau**, trong khi thực tế nó là **thuế của mọi task chạm UI**; không có gate nên chuỗi bỏ quên tích luỹ im lặng. (2) `TranslationCatalog` dùng `Dictionary.Add` với `StringComparer.Ordinal` — thêm trùng key **throw ngay trong constructor**, mà catalog là singleton dựng lúc khởi động ⇒ triệu chứng không phải "thiếu chữ" mà là **app chết khi mở**, rất tốn thời gian truy nguyên nếu chỉ phát hiện lúc chạy. (3) `aria-label` / `title` / `placeholder` hay bị quên vì "không nhìn thấy". |
| **Fix** | Chốt: code mới **luôn** dùng `TranslationCatalog`, không thêm key mới vào `.resx` nữa (legacy chờ cutover). Key `lower.dotted`, namespace theo surface, duy nhất toàn hệ. Server trả **`WoErrorCode`**, không trả câu tiếng Việt — map code → i18n key ở client (đây là lý do guard state machine trả enum). Cấm dịch bằng nối chuỗi (trật tự từ khác nhau giữa hai ngôn ngữ). Chuỗi tiếng Việt trần hiện có = **nợ có baseline**, kéo xuống dần. |
| **Cơ chế chặn tái phát** | Gate `scripts/gate-i18n-parity.sh` — (A) key trùng **hard fail 0** (bắt tĩnh trước khi nó thành crash lúc boot); (B) key thiếu VI hoặc EN **hard fail 0**; (C) ratchet dòng `.razor` còn chuỗi tiếng Việt trần (BASELINE **99**). Đã chứng minh PASS → FAIL (inject `Add("nav.home", …)` trùng) → PASS qua `--self-test`. Skill `.claude/skills/cmes-i18n-parity/SKILL.md`, và `cmes-loop` bắt buộc kéo skill này theo **mọi** work-class chạm UI. **Quy tắc: không chuỗi hiển thị nào hardcode trong `.razor`, kể cả `aria-label`/`title`/`placeholder`; mỗi `Add(key, vi, en)` đủ hai ngôn ngữ trong cùng commit.** |

<a id="l43"></a>
### L43 — Mutation im lặng làm sự cố không điều tra được; audit detail chứa hash/token thì rò bí mật qua CSV export

| Field | Detail |
| --- | --- |
| **Triệu chứng** | Chưa xảy ra sự cố — đây là lesson **phòng ngừa** dựng từ audit 2026-08-18, khi phát hiện luật audit chỉ tồn tại dạng prose trong `CLAUDE.md` §6 mà không có cơ chế chặn nào. Hai lớp rủi ro: (1) một controller ghi DB nhưng không emit audit ⇒ khi có tranh chấp ca kíp hoặc dữ liệu sai, không có gì để truy; (2) một `detail` JSON tiện tay `JsonSerializer.Serialize(user)` sẽ kéo theo `PasswordHash` — mà `AuditLogs` **xuất được ra CSV** qua `AuditLogExportController` và **đọc được** ở `Settings → System Log`. |
| **Root cause** | Luật đúng nhưng chỉ nằm trong văn xuôi. Theo đúng L4 ("lesson được ghi ≠ lesson được chặn"), prose không fail CI nên sẽ trôi. Rủi ro rò bí mật đặc biệt âm thầm vì code trông hoàn toàn hợp lý ở chỗ viết — chỉ khi ghép với đường export mới thành lỗ hổng. |
| **Fix** | Codify hai bất biến: (A) `detail` không được chứa `password/pwd/hash/salt/token/cookie/authorization/bearer/secret/apiKey/connectionString` — serialize **field tường minh**, không ném cả entity; (B) controller có `SaveChangesAsync` **phải** tham chiếu audit writer. Emit **sau** khi `SaveChangesAsync` thành công (không emit trong `catch` rồi nuốt exception — audit sẽ nói "thành công" khi đã fail). Làm rõ trong skill ranh giới `AuditLogs` (ai làm gì lúc nào, append-only, cho Admin điều tra) **≠** `WoTraceSnapshot` (sản phẩm được làm ra thế nào, đóng băng, cho khách hàng audit) — không gộp hai thứ. |
| **Cơ chế chặn tái phát** | Gate `scripts/gate-audit-emit.sh` — cả hai kiểm **hard fail 0** (cây hiện tại đang sạch: 0 rò, 0 mutation im lặng — giữ nguyên vậy). Đã chứng minh PASS → FAIL (inject `detail:` chứa `passwordHash` **và** controller ghi DB không audit, bắt được cả hai) → PASS qua `--self-test`. Skill `.claude/skills/cmes-audit-emit/SKILL.md`. **Quy tắc: mọi mutation emit qua `IAuditWriter.EmitAsync` sau khi lưu thành công; `detail` serialize field tường minh, không bao giờ cả entity.** |


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
