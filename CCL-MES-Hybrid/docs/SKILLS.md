# CCL-MES — Skills playbook (coding + debug)

> Proven-effective workflows for shipping + investigating in this
> codebase. Each skill explains: when to use, the canonical recipe,
> and a worked example.
>
> **Cross-references**: [`LESSONS-LEARNED.md`](./LESSONS-LEARNED.md) is
> the WHY (every bug class the project has paid for); this file is the
> HOW (the workflow each lesson induced). [`STACKED-PR-CHECKLIST.md`](./STACKED-PR-CHECKLIST.md)
> is the rule manual for PR/script mechanics; this file complements it
> with the discovery + debug rhythm.

---

## Index

### Debug + RCA discipline

- [S1 — RCA proven, not "most likely"](#s1)
- [S2 — Reproduce on a DB copy, not on the live dev DB](#s2)
- [S3 — Verify-script per PR + paste real output ("no output = not done")](#s3)

### Script + checkpoint mechanics

- [S4 — Checkpoint script is self-managed (boot its own API, pin its own DB)](#s4)
- [S5 — Catalyst checkpoint pattern (the 6-probe rhythm)](#s5)
- [S6 — Henry-action in 1 command, full chain](#s6)

### Session discipline

- [S7 — STOP-gate discipline (when to halt mid-task)](#s7)
- [S8 — Stash before context switch (preserve in-progress work cleanly)](#s8)

### UI / Razor

- [S9 — Design Rules (mandatory for every Razor/UI PR)](#s9)

### Tooling discipline

- [S10 — Preserve debug artifacts on FAIL (don't auto-clean api.log + TMP_DIR before the operator can read them)](#s10)
- [S11 — Assert-bound-port on every script that boots an API (lsof + log grep for "Overriding address")](#s11)
- [S12 — Checkpoint scripts: per-step `[N/total]` labels + always-print SUMMARY (silence = no verify)](#s12)
- [S13 — Data-driven QC auto-sync: resolver (routing→line) + lazy-materialize-from-library + freeze snapshot (Phương án C B3-B6)](#s13)

---

## Skill cards

<a id="s1"></a>
### S1 — RCA proven, not "most likely"

**When to use**: any time a bug surfaces that isn't immediately reproducible by reading code. Required before opening a fix PR.

**Recipe**:

1. Form a hypothesis ("most likely the migration didn't run").
2. Find the command that PROVES the hypothesis true or false.
3. Run it. Paste the real output into your write-up.
4. ONLY THEN write the fix.

**Worked example** (from `HOTFIX-SETTINGS-404-PROVEN.md`):

The Settings endpoints returned 404. Hypothesis: "stale running binary". Proof:

```bash
$ lsof -nP -iTCP:5100 -sTCP:LISTEN
COMMAND     PID    USER   FD   TYPE             DEVICE SIZE/OFF NODE NAME
CCL.MES.A 81851 thiepdt  287u  IPv4 0xc57f7f2e375fb731      0t0  TCP 127.0.0.1:5100 (LISTEN)

$ ps aux | grep "CCL.MES.Api" | grep -v grep
thiepdt 81851 ... 1:38PM 0:14.70  ...CCL.MES.Api
                  ^^^^^^^
                  process started 1:38PM today, BEFORE PR #91 commits were pushed.
```

The hypothesis became **proven** because the PID's start time predated the new controller's commit. The fix (kill + restart) shipped with the script that proved the cause + the script that re-proves it on Henry's box. Total RCA write-up: 4 paragraphs + paste. No speculation, no "most likely". See [L7](./LESSONS-LEARNED.md#l7).

**Anti-pattern**: opening a fix PR whose root-cause section reads "the bug is most likely caused by …". If you can't prove it, you don't know it — go find the command that proves it.

<a id="s2"></a>
### S2 — Reproduce on a DB copy, not on the live dev DB

**When to use**: any investigation that needs to mutate DB state to verify a hypothesis.

**Recipe**:

1. Copy live DB: `cp data/ccl_mes.db /tmp/repro-$(date +%s).db`
2. Point a one-shot `dotnet run` at it via `ConnectionStrings__Default="Data Source=/tmp/repro-XXX.db"`.
3. Run the mutation + probe.
4. Discard the copy when done.

**Why**: live dev DB is shared with the running Catalyst host, other terminals, the agent's own xUnit runs. Mutating it for investigation introduces state drift that masks the original symptom and prevents anyone else from reproducing.

**Worked example** (P10.7a-2.2 force-phase audit hunt — see [L10](./LESSONS-LEARNED.md#l10)):

Hypothesis: "audit row missing". To prove it, copy DB, run the failing endpoint, then both `sqlite3 SELECT * FROM AuditLogs WHERE Action='SYS_RECOVERY'` (DbContext-equivalent direct read) AND `curl /api/v2/audit/log?action=SYS_RECOVERY` (wire read). The direct read returned the row; the wire returned empty. Proved that the bug was in the WIRE READ path (script's URL was wrong), not the WRITE path (audit row was correctly persisted). Investigation done in 5 minutes on a copy without touching the live DB.

**Reference**: [`STACKED-PR-CHECKLIST.md`](./STACKED-PR-CHECKLIST.md) Rule 6 covers the related case for verify scripts (self-prep DB baseline on the COPY).

<a id="s3"></a>
### S3 — Verify-script per PR + paste real output

**When to use**: every PR that touches an HTTP route, an EF entity, or a permission gate.

**Recipe**:

1. Add `scripts/verify-pXX.X.sh` (~150-250 LOC, deps `dotnet` + `curl` + `python3` only).
2. Script: kill any stale process on the target port, build, start API fresh, hit every new route end-to-end (anon → 401, auth → 200, edge cases → 422), print per-row PASS/FAIL + final summary, exit non-zero on any FAIL.
3. Run the script BEFORE opening the PR.
4. Paste the real `=== SUMMARY ===` block into the PR description. NOT a paraphrase.

**Why**: "no output = not done". The PR description should let the reviewer ⌘-F for `PASS` and `FAIL` and see a count. Without paste, the reviewer has to re-run the script to know if the PR is alive — most reviewers won't.

**Worked example** (`HOTFIX-SETTINGS-404-PROVEN.md`):

```
============================  SUMMARY  ============================
  PASS  Build (commit 4c5068d)
  PASS  API boot (200 /health)
  PASS  GET   /api/v2/settings/me anon (got 401 expected 401)
  PASS  PATCH /api/v2/settings/me anon (got 401 expected 401)
  PASS  POST  /api/v2/settings/password anon (got 401 expected 401)
  PASS  Login admin (token_len=589)
  PASS  GET    /api/v2/settings/me auth (200, username=admin, role=Admin)
  PASS  PATCH  /api/v2/settings/me auth (DisplayName=Verify-160556)
  PASS  PATCH  /api/v2/settings/me long (422 profile.display_name_too_long)
  PASS  POST   /api/v2/settings/password wrong (422 auth.wrong_current)
  PASS  POST   /api/v2/settings/password short (422 auth.new_too_short)

  TOTAL: pass=11 fail=0
```

The reviewer now knows: 11 routes, all green, including the negative-path 422s. The PR is shippable. Without paste, the reviewer would have to grep PRs to verify.

**Anti-pattern**: PR description says "tested locally, works". Reviewer has zero idea what was tested or what the expected output looked like.

<a id="s4"></a>
### S4 — Checkpoint script is self-managed

**When to use**: every script in `CCL-MES-Hybrid/scripts/checkpoint-*.sh`.

**Recipe** (from [`STACKED-PR-CHECKLIST.md`](./STACKED-PR-CHECKLIST.md) Rule 7.2):

```bash
# 1. Print [ctx] header (Rule 7.1)
DB_ABS="$(cd "$(dirname "$DB_PATH")" && pwd)/$(basename "$DB_PATH")"
DB_SHA8="$(shasum -a 256 "$DB_PATH" | awk '{print substr($1,1,8)}')"
echo "[ctx] DB      = $DB_ABS"
echo "[ctx] DB sha8 = $DB_SHA8"

# 2. Probe → if up, reuse; else auto-boot pinned to OUR DB
trap cleanup EXIT INT TERM
if curl -s -m 3 -o /dev/null -w "%{http_code}" "$API_BASE/health" | grep -qE "^(200|401|503)$"; then
    echo "[boot] API_BASE responding — reusing"
else
    (cd "$REPO/path/to/api" && \
        ConnectionStrings__Default="Data Source=$DB_PATH" \
        ASPNETCORE_URLS="http://127.0.0.1:5100" \
        dotnet run --no-build --no-launch-profile > /tmp/checkpoint-api.log 2>&1) &
    AUTO_BOOT_PID=$!
    # wait for /health to respond, then proceed
fi

# 3. Run probes ...

# 4. --keep-alive: leave API running for UI verify; print kill command
if [[ "$KEEP_ALIVE" == 1 ]]; then
    trap - EXIT
    echo "[keep-alive] server still running on $API_BASE (pid $AUTO_BOOT_PID)"
    echo "[keep-alive] kill with: kill $AUTO_BOOT_PID"
fi
```

**Why**: the operator runs ONE command. The script either reuses a live server OR boots its own server on the same DB it's mutating. No coordination across terminals. Closes [L14](./LESSONS-LEARNED.md#l14).

**Anti-pattern**: script assumes a server is already running somewhere; operator has to start it manually in another terminal with the right env vars; DB drift makes diagnosis 30 minutes longer.

<a id="s5"></a>
### S5 — Catalyst checkpoint pattern (6-probe rhythm)

**When to use**: every PR that touches an operator-facing surface (Razor page, controller action, Catalyst native interop).

**Recipe**:

1. **Build probe** — `dotnet build` exits 0; capture commit SHA.
2. **Boot probe** — `curl /health` returns 200.
3. **Auth probe** — login as a known seeded user → JWT issued.
4. **Happy-path probe** — call the new endpoint with valid input → expected 200 + body shape.
5. **Edge-case probe** — call with the input that should 422 → expected 422 + expected error code in body.
6. **Audit probe** — `GET /api/v2/audit/log?action=NEW_AUDIT_EVENT&page=1&pageSize=10` → grep response body for the persisted row's substring. Wire-mirror (R7.3) covers this.

End with PASS/FAIL summary + `--keep-alive` so Henry can verify the same binary on Catalyst.

**Worked example**: `CCL-MES-Hybrid/scripts/checkpoint-7b-2.sh` covers 6 probes for the PREPRESS API endpoints (GET prepress view → PUT material happy → PUT material 422 invalid_reason → PUT plate happy → PUT cutter happy → audit log wire mirror). On Henry's 2026-06-06 verify: `8/8 PASS`.

**Why this rhythm**: every operator failure mode (build broken, server dead, auth broken, happy path regressed, edge case mis-classified, audit not persisted) maps to exactly one probe. When the script reports FAIL, the failing probe tells you which layer is broken.

<a id="s6"></a>
### S6 — Henry-action in 1 command, full chain

**When to use**: every PR description's "What Henry runs" section.

**Recipe**: the action MUST be a single chained command that gets the operator from `git pull` to a verified app, including ALL intermediate steps (build, migration, seed, server start, verify, Catalyst launch).

```bash
cd "/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/CCL-MES/CCL-MES-Hybrid"
git pull
# When the PR touches migrations (Rule 5):
dotnet ef database update \
  --connection "Data Source=$(pwd)/../data/ccl_mes.db" \
  --project src/CCL.MES.Infrastructure \
  --startup-project src/CCL.MES.Api
# Then verify + boot:
bash scripts/verify-p10.7X.sh --keep-alive
```

When the script prints `TOTAL: pass=N fail=0` + `[keep-alive] server still running`, Henry switches to Catalyst, hits the new surface, kills the API when done.

**Why**: when an intermediate step is missing (forgot `dotnet ef database update` → [L8](./LESSONS-LEARNED.md#l8) blind 500), the operator hits a runtime error with no idea what's wrong. Listing every step in the PR description prevents this. The checklist's Rule 5 codifies the migration-step requirement.

**Anti-pattern**: PR description says "see verify script". Operator has to navigate to the script, read it, infer the prerequisites, run them manually. Three friction points; each one a chance to skip a step.

<a id="s7"></a>
### S7 — STOP-gate discipline

**When to use**: when the agent has shipped a verifiable artifact and the next step requires operator hardware verification or an explicit go-ahead.

**Recipe**:

1. Finish the technical work (commit + push + open PR).
2. Run the full verify suite. Paste output.
3. Explicitly say **STOP** in the final message.
4. State what Henry needs to do next + what command to run + what success looks like.
5. DO NOT continue to the next PR / next sprint slice / "small follow-up improvement" until Henry replies.

**Worked example** (P10.7b-3 ship message ended with):
> PR #109 opened — STOP per breakdown. Waiting for your hardware verify on the PREPRESS flow before PR 7b-4 (test belt closeout → tag v0.10.7b).

**Why**: Henry's breakdown sets the sequence. Continuing past a STOP-gate means the next PR builds on unverified work. If the unverified PR has a bug, the cascade-fix is N PRs to revert + rebuild instead of 1 PR to fix. Especially important on Catalyst — hardware quirks (UA token shifts, scanner availability, license activation) can't be caught in CI.

**Anti-pattern**: "while waiting for Henry, I'll go ahead and start PR 7b-4". The stack is now mid-flight; if Henry's hardware verify fails the 7b-3 work, 7b-4 is also poisoned and must rebase.

<a id="s8"></a>
### S8 — Stash before context switch

**When to use**: when Henry interrupts an in-progress task with a new task that must NOT touch the same files.

**Recipe**:

```bash
# 1. Stash uncommitted WIP with a descriptive message
git stash push -m "WIP <feature> <what was in flight>"

# 2. Switch to the new task's branch (or create one)
git checkout main && git pull --ff-only origin main
git checkout -b <new-task-branch>

# 3. Do the new work, commit + push + PR.

# 4. After the new task ships, return:
git checkout <original-branch>
git stash pop          # resumes WIP
# Continue where you left off.
```

**Worked example** (this session): mid-NG-path-fix on `feat/p10.7b-3-prepress-ui`, Henry asked for the canonical LESSONS-LEARNED.md docs PR. Stashed the uncommitted `DbSeeder.cs` edit with message `"WIP p10.7b-3 NG-path picker fix — DbSeeder refactor for Pause/Scrap per-code idempotency"`, branched off main, shipped the docs PR. Return path: `git checkout feat/p10.7b-3-prepress-ui && git stash pop` to resume NG-path work.

**Why**: keeping WIP in the working tree across context switches risks accidentally committing it on the wrong branch, mixing concerns in PRs, or losing it to a `git reset --hard`. `git stash` makes the WIP survivable and discoverable (`git stash list`).

<a id="s9"></a>
### S9 — Design Rules (mandatory for every Razor/UI PR)

**When to use**: every PR that adds or modifies a Razor page or component.

These rules are non-negotiable. Catalyst checkpoint scripts that ship UI MUST add a "responsive full-screen verified (wide + narrow)" line per PR.

#### S9.1 — Responsive full-screen layout (bắt buộc)

- Every page/window MUST use the full available viewport width. Center the content with a sensible `max-width` (recommended 1400-1600 px); the layout MUST resize fluidly when the viewport resizes.
- **PROHIBITED**: a single-column left layout with the right half blank. This was the bug class on `/settings/audit` — desktop users saw a 600 px-wide column on the left and 1200 px of dead whitespace on the right.
- Filter / toolbar / action bars sit on ONE row at desktop widths; wrap to multiple rows ONLY when the viewport is too narrow to fit. Use `flex-wrap: wrap` or container queries.

**Canonical CSS pattern**:

```css
.page-shell {
    max-width: min(1600px, calc(100vw - 32px));
    margin: 0 auto;
    padding: 16px;
}

.toolbar {
    display: flex;
    flex-wrap: wrap;
    gap: 12px;
    align-items: center;
}
```

#### S9.2 — Tables

- `table-layout: fixed;` + explicit column widths (`<col>` or `<th style="width:X">`). Otherwise auto-layout will collapse skinny columns and over-expand wide ones.
- Long-content cells (JSON payload, sha hash, UUID, audit detail) MUST truncate to ONE line with `text-overflow: ellipsis; overflow: hidden; white-space: nowrap;` AND offer an expand/copy affordance (button → modal OR title attribute → tooltip). DO NOT bóp nát multi-line into the row.
- Wrap the table in `<div style="overflow-x: auto;">` for narrow viewports — when the cumulative `min-width` of columns exceeds the viewport, horizontal scroll is acceptable; collapsing readability is not.

**Canonical pattern**:

```html
<div class="table-wrap" style="overflow-x: auto;">
    <table class="data-table" style="table-layout: fixed; width: 100%; min-width: 960px;">
        <colgroup>
            <col style="width: 80px;" />  <!-- Id -->
            <col style="width: 140px;" /> <!-- Timestamp -->
            <col style="width: 120px;" /> <!-- Actor -->
            <col />                       <!-- Detail (flex) -->
        </colgroup>
        <thead>...</thead>
        <tbody>
            @foreach (var row in Rows)
            {
                <tr>
                    <td>@row.Id</td>
                    <td>@row.Timestamp</td>
                    <td>@row.Actor</td>
                    <td class="cell-truncate" title="@row.Detail">@row.Detail</td>
                </tr>
            }
        </tbody>
    </table>
</div>
```

```css
.cell-truncate {
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    max-width: 0;     /* tells the cell to participate in flex shrink */
}
```

#### S9.3 — Verify-UI checklist per PR

Add this row to every UI PR's checkpoint output:

```
PASS  responsive full-screen verified (wide ≥1400px + narrow ≤900px)
```

Before merging, manually:

1. Resize the Catalyst window WIDE (≥1400 px) — confirm content uses the full width, no dead whitespace strip.
2. Resize NARROW (≤900 px) — confirm content reflows, toolbar wraps, tables either resize OR offer `overflow-x` scroll.
3. Both states render without layout breakage. Screenshot both into the PR description.

**Why**: shipped PR #91 (Settings) hit production with a fixed left-column layout that wasted half the screen. Henry's first verify session caught it. A "responsive verified" checklist row would have made the PR description gate this before merge.

**Anti-pattern**: building at one viewport width + assuming it scales. Catalyst windows resize freely; operators routinely run with the Mac at 1920×1200 OR docked alongside Slack at 1024×768. Both must work.

<a id="s10"></a>
### S10 — Preserve debug artifacts on FAIL

**When to use**: every verify / checkpoint / repro script that creates a `mktemp -d` working dir or writes a `tmp` log.

**Recipe**:

```bash
TMP_DIR="$(mktemp -d -t myscript-XXXXXX)"
API_LOG="$TMP_DIR/api.log"

cleanup() {
    # ... kill any subprocess ...
    if [[ "$FAIL" -gt 0 ]]; then
        echo ""
        echo "[debug] TMP_DIR preserved for inspection: $TMP_DIR"
        echo "[debug] api log    : $API_LOG"
        # Print other interesting paths so the operator can `cat` them.
    else
        rm -rf "$TMP_DIR"
    fi
}
trap cleanup EXIT INT TERM
```

**Why**: this exact discipline saved the L18 RCA. The original `verify-p10.7b.sh` did `rm -rf "$TMP_DIR"` unconditionally on exit. Henry's first run FAILED on the boot probe; the api.log containing the `Overriding address(es)` warning had ALREADY been deleted by the time he tried to investigate. He had to re-run, watch the log in real time, and copy the warning text manually. With S10, the second FAIL would have preserved everything automatically.

**Anti-pattern**: cleanup `trap` runs unconditional cleanup. Even worse: cleanup that suppresses its own output so the operator doesn't see WHERE the preserved artifacts live. Always print the preserved paths so `cat $TMP_DIR/api.log` is one command away.

**Lifetime hygiene**: TMP_DIR lives until the next reboot OR until the operator manually `rm -rf`. macOS `/tmp` is reboot-cleaned; Linux `/tmp` varies. For long-running investigations, copy artifacts out of TMP_DIR before rebooting.

<a id="s11"></a>
### S11 — Assert-bound-port on every script that boots an API

**When to use**: every verify-script / checkpoint-script that auto-boots a `dotnet run` process (or any embedded server) on a target port.

**Recipe**:

```bash
TARGET_PORT=5101

# 1. Pre-boot — kill stale listeners so a leftover from a prior run
#    doesn't surface as a misleading FAIL.
STALE_PIDS=$(lsof -nP -iTCP:${TARGET_PORT} -sTCP:LISTEN -t 2>/dev/null)
if [[ -n "$STALE_PIDS" ]]; then
    echo "[boot] killing stale listeners on $TARGET_PORT: $STALE_PIDS"
    echo "$STALE_PIDS" | xargs -r kill -9 2>/dev/null
    sleep 1
fi

# 2. Boot. Use --urls (and ASPNETCORE_URLS as backup) — appsettings.json
#    "Urls" key is overridden, but Kestrel:Endpoints would NOT be (L18).
(cd "$API_PROJECT_DIR" && \
    dotnet run --no-build --no-launch-profile --urls "http://127.0.0.1:${TARGET_PORT}" \
    > "$API_LOG" 2>&1) &
API_PID=$!

# 3. Wait for /health 200 ...

# 4. Post-boot — assert the API actually bound the port we asked for.
BOUND_PID=$(lsof -nP -iTCP:${TARGET_PORT} -sTCP:LISTEN -t 2>/dev/null | head -1)
if [[ -z "$BOUND_PID" ]]; then
    record FAIL "API /health 200 but nothing listening on $TARGET_PORT"
fi

# 5. L18 regression guard — grep API log for the override warning.
if grep -q "Overriding address(es)" "$API_LOG"; then
    record FAIL "L18 regression — appsettings.json hardcoded Kestrel:Endpoints?"
fi
```

**Why**: prior P10.7b-4 verify-script merely curl'd `/health` and trusted any 200. But ASP.NET Core's URL binding has at least 5 priority layers (`Kestrel:Endpoints` > `--urls` > `ASPNETCORE_URLS` > `Urls` config key > framework default). A hardcoded `Kestrel:Endpoints` makes `--urls` look like it worked (no error) but the server actually binds the hardcoded port. `lsof` + log-grep are the only ways to PROVE which port was actually bound. See [L18](./LESSONS-LEARNED.md#l18) for the full RCA.

**Anti-pattern**: trusting `/health 200` alone. The health endpoint responds to whatever Kestrel is listening on, regardless of what `--urls` requested. Without the bound-port + log-grep assertions, the bug is invisible to scripted verification.

**Cross-platform note**: `lsof -nP -iTCP:${PORT} -sTCP:LISTEN -t` works on macOS + Linux. Windows scripts use `netstat -ano | findstr ":<port>"` instead.

<a id="s12"></a>
### S12 — Checkpoint scripts: per-step `[N/total]` labels + always-print SUMMARY

**When to use**: every `scripts/checkpoint-*.sh` and any verify-script that exercises a chain of HTTP probes.

**Recipe**:

```bash
TOTAL_STEPS=20
CURRENT_STEP=0
PASS=0
FAIL=0
SUMMARY=()

record() {
    CURRENT_STEP=$((CURRENT_STEP + 1))
    if [[ "$1" == "PASS" ]]; then
        PASS=$((PASS + 1))
        echo "[$CURRENT_STEP/$TOTAL_STEPS] ✓ $2"
        SUMMARY+=("  PASS  $2")
    else
        FAIL=$((FAIL + 1))
        echo "[$CURRENT_STEP/$TOTAL_STEPS] ✗ $2"
        SUMMARY+=("  FAIL  $2")
    fi
}

final_summary() {
    echo ""
    echo "============================  SUMMARY  ============================"
    if [[ ${#SUMMARY[@]} -eq 0 ]]; then
        echo "  (no steps recorded — early abort before first probe)"
    else
        printf '%s\n' "${SUMMARY[@]}"
    fi
    echo "  TOTAL: pass=$PASS fail=$FAIL"
    [[ $FAIL -gt 0 ]] && echo "  ✗ CHECKPOINT FAILED — wire path NOT proven."
}

cleanup() {
    final_summary    # ALWAYS runs, even on exit 1
    # ... kill subprocesses ...
}
trap cleanup EXIT INT TERM
```

**Why**: the P10.7c-2 hardware test exposed this exact anti-pattern. The original `checkpoint-7c-2.sh` had a `record FAIL ... && exit 1` deep inside the script when `/force-phase` rejected the IPQC_WAIT → IPQC_APPROVED cell. The exit fired BEFORE the trailing manual SUMMARY block, so the output looked like:

```
[ctx] WO Id      = 5
                                        ← nothing else, exit code 1
```

Henry saw 11 lines of output + no SUMMARY + the API log silent — looked identical to a successful early-bail. The actual failure (force-phase 422) was buried in a `$R` variable that only the error message inside `record FAIL` would surface. Without per-step labels, the operator couldn't tell **which** step failed without `bash -x` tracing.

The recipe above forces three properties:
1. **Per-step `[N/total] ✓` or `✗`** is printed as the step happens, so progress is visible in real time even if the script aborts halfway.
2. **`final_summary` runs in the EXIT trap**, so it prints whether the script exited 0, 1, or was killed by SIGINT. The empty-array guard handles the "early abort" case explicitly.
3. **`FAIL>0 → CHECKPOINT FAILED` line** at the bottom of the summary makes wire-failure unmistakable in a glance.

**Pair with S10**: preserve TMP_DIR / api.log on FAIL (the api.log is essential to diagnose the failing wire response — the SUMMARY shows WHICH step failed; the api.log shows WHY).

**Pair with S1**: when a checkpoint fails, the operator pastes the SUMMARY + the failing step's `$R` body + the relevant api.log line to PROVE the root cause. Without S12, even Step 1 of S1 (form a hypothesis) is fuzzy because the operator doesn't know which step failed.

**Anti-pattern (what was shipped initially in checkpoint-7c-2)**:

```bash
record FAIL "..."
exit 1   # ← summary never prints, output looks identical to success-early-bail
# ...later in the file:
echo "==== SUMMARY ===="   # ← dead code, never executes
```

The `exit 1` MUST go through the cleanup trap. NEVER bare-`exit` from inside a mid-script step.

**In-process integration tests vs wire checkpoint**: in-process xUnit fixtures (the 22 `RunningSurfaceControllerTests` for 7c-2) cover endpoint logic but DO NOT prove the wire path on the real DB. The checkpoint script is what produces the **audit log proof** Henry needs to see in the Settings UI. Both are required — in-process catches code regressions, checkpoint catches deploy + DB + integration regressions. See [L10](./LESSONS-LEARNED.md#l10) for the wire-mirror principle (R7.3 mandates each operator-facing wire probe has a parallel TestServer mirror; S12 is the operational discipline that makes the wire side reproducible).

---

<a id="s13"></a>
### S13 — Data-driven QC auto-sync: resolver (routing→line) + lazy-materialize-from-library + freeze snapshot

**When to use**: wiring a per-process / per-routing checklist (IPQC/FQC/OQC items) so a WO auto-loads the right item set instead of a hardcoded N-slot form. The pattern from Phương án C B3-B6.

**Recipe** (mirrors the FQC/OQC `WoQcReviewController` lazy-materialise):

1. **Resolver thuần** (`QcLineResolver`) — input = routing rows of the part, output = process-line set. Derive from the RELIABLE signals (`Operation Description` keywords + WorkCenter code prefix), NEVER from auto-derived fields like `WorkCenter.Area` (wrong on real data) or `RoutingType` (constant). Unclassified op → `Unmapped` list, log + ask — don't guess. Lock with a test that pins classification to REAL routing rows of a real part (e.g. `QcLineResolverTests` on 8064xxxx).
2. **Materializer thuần** (`IpqcLibraryMaterializer.Build(libraryRows, lines)`) — build `(ProfileSnapshotJson, items)` from the library subset; deterministic order (line → sort → id).
3. **Lazy-materialise on first GET** in the controller: resolve part → routing → lines → query library subset (stage + lines + product-scope) → materialize items + **FREEZE** the snapshot column. Guard: if already frozen OR no routing OR empty library → NO-OP (fall back to legacy behaviour) so every pre-existing WO + existing test is untouched (additive).
4. **Rollup overload** that prefers the data-driven items when present, falls back to legacy slots when empty — keeps the legacy-parity tests green.
5. **Scope NG codes by line** (B5): dropdown lists only `Scrap ∩ DefectCode(line)`; server still 422s a non-catalog code.
6. **Admin read API + page** (B6): list/lines/scoped-reason endpoints; edit is via the idempotent importer, not the UI (master data has its own lifecycle).

**Invariants to keep**: state-machine + dual-sig untouched (additive only); freeze = editing the library NEVER mutates an in-flight WO (prove live: old WO item-count unchanged after a library edit, new WO picks up the change).

**Cơ chế chặn**: `QcLineResolverTests`, `IpqcDataDrivenTests` (rollup parity + materializer), `IpqcAutoSyncTests` (end-to-end resolve→materialize→freeze + no-routing fallback), `CheckItemLibraryControllerTests` (scope), `IpqcDashboardItemsTests` (UI items-mode). See [L25](./LESSONS-LEARNED.md#l25) + `docs/lessons-learned/02-ipqc-data-driven-autosync.md`.

---

## S13 — Reusable window chrome: `<FloatingWindow>` for every showcard (L34)

A SHOWCARD (detail/monitor window a user drags, resizes, or keeps open alongside
others) MUST wrap `Shared/FloatingWindow.razor` — never hand-roll `.trace-win` /
`.fw-handle` / `.fw-traffic` or `cclMesFloat.*` interop. Reference impl:
`TraceabilityDetailDialog.razor` (host: `QualityTraceability.razor` for N-window
cascade + `IFloatingWindowStore` persistence).

Transactional surfaces (Create/Edit/Copy/Import forms, Pause/Finish/QtyCorrect
confirms) stay centred `<Modal>` — that IS the right pattern; don't float them.
`Modal` exposes opt-in `Float="true"` (renders through `<FloatingWindow>`) for the
rare case a modal genuinely becomes keep-open.

**Enforced**: `scripts/gate-floating-showcard.sh` fails a PR adding a
`*DetailDialog.razor` / `*Showcard*.razor` without `<FloatingWindow>` (allowlist
= inline Spec*Showcard cards + the primitive itself). Skill:
`.claude/skills/cmes-floating-showcard/SKILL.md`. Tests: `FloatingWindowTests` +
`QualityTraceabilityTests`. See [L34](./LESSONS-LEARNED.md#l34).

---

## S14 — Row actions: one shared `RowContextMenu`, never an "Actions" column (L35)

Per-row actions (Copy / Edit / Delete / …) use `Shared/RowContextMenu.razor`
opened THREE ways that share one state: right-click (`@oncontextmenu`), long-press
(~500ms, for touch / WKWebView), and a **⋯ kebab** button in a narrow trailing
column. RBAC by omission — build only permitted `ContextMenuItem`s; no items →
don't open + hide the kebab (server still enforces 403). Reference:
`NpiWorkCenters.razor`.

Do NOT add an inline `<th>Actions</th>` column of buttons. **Enforced**:
`scripts/gate-row-actions.sh` fails a new grid with an "Actions" header (allowlist
= grandfathered surfaces + AuditLog's data "Action" column). Skill:
`.claude/skills/cmes-row-context-menu/SKILL.md`. Tests: `RowContextMenuTests` +
`NpiWorkCentersTests`. See [L35](./LESSONS-LEARNED.md#l35).

---

## S9 addendum — full-screen surfaces must fill (no fix-width dead bands) (L36)

A full-screen page (login, lock, kiosk, splash) must FILL the viewport — never
centre a full-screen shell with `display:grid/flex + place-items:center`, whose
track sizes to CONTENT width and leaves dead bands. Use a **block/fill wrapper**
(`display:block; min-height:100vh`) with the child at `width:100%`. A fluid split
uses `flex` ratios + `clamp()` padding/type + the responsive matrix
(desktop / tablet-portrait / phone / short-height) + `env(safe-area-inset-*)` +
≥16px inputs. Screenshot the matrix when adding/altering any full-screen surface.
See [L36](./LESSONS-LEARNED.md#l36). Reference: `Login.razor` + `.login-*`.

---

## S15 — Colours go through design tokens; re-tone = swap token values, never find/replace hex (L37)

`app.css` is token-driven: a flat semantic layer in `:root`
(`--c-ink*/--c-bg/--c-card/--c-line*/--brand*/--accent/--indigo/--ok|ng|warn*`)
is defined once and **never re-scoped**, so `var()` resolves to a constant
everywhere. **New or changed colours edit a token, they do NOT introduce a raw
hex** in a rule. Re-toning the app = change the VALUES in `:root` only — that one
swap propagates to every surface (that is the entire payoff of token-ising).

**Do NOT** re-tone by find/replacing hex across the file — it leaves the app
un-token-ised (the next re-tone still edits 1000+ literals) and desyncs surfaces.

Migrating a hex-hardcoded stylesheet to tokens = **3 phases, each verified +
committed**: (1) add the token layer with values = CURRENT colours (unused defs →
byte-identical render); (2) route hex → `var(--token)` per role-group, one commit
each, token value = the hex it replaces → no-op; (3) swap `:root` values to the
new palette. If you script step 2, **strip CSS comments before parsing
declarations** — a comment containing `word:` (`RULE:`, `:root`) fools a naive
declaration parser into treating the next `--token: #hex` DEFINITION as a normal
property, producing a circular `--x: var(--x)` that silently kills the token
(grep `^\s*--[a-z0-9-]+\s*:\s*var\(` must return nothing).

**Enforced**: `scripts/gate-no-hardcoded-hex.sh` (ratchets raw hex in rule
usages — token DEFINITIONS, whether `:root` or a scoped re-scope like the dark
chrome, are exempt; a new hardcoded colour in a rule fails — route it to a token
or bump the baseline with a note). See [L37](./LESSONS-LEARNED.md#l37).
