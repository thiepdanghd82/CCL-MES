# Stacked-PR Merge Checklist

> Lessons captured from the **P10.6 7-PR stack merge** (2026-06-05).
> See `docs/p10.6-screens/log-10-merge-stack-to-main.txt` for the full incident log.

When shipping a feature in N sequential PRs (each based on the previous one's head branch),
follow these rules to avoid the cascade-close traps that bit us during P10.6.

---

## Rule 1 — Explicit `--base` on every `gh pr create`

Every `gh pr create` for a stacked PR **MUST** pass `--base <prev-PR-head-branch>` explicitly.

```bash
# CORRECT — stacked PR #2 explicitly based on PR #1's head branch
gh pr create \
  --base feat/p10.6a-settings-profile-password \
  --head feat/p10.6d-about-connection-mode \
  --title "P10.6d — Settings About + Connection Mode"

# WRONG — omits --base, gh may default to main and silently break the stack
gh pr create --head feat/p10.6d-about-connection-mode --title "..."
```

**Why**: P10.6 Pre-flight §1c discovered PR #92 had `base=main` instead of `base=feat/p10.6a-settings-profile-password`. Reviewing the original `gh pr create` command for #92 confirmed `--base` was omitted, and `gh` picked the repo default (main). The mis-based PR didn't fail validation, didn't surface a warning — it just quietly broke the stack invariant.

**How to apply**: when creating each stacked PR, the `--base` flag is the parent branch in your stack diagram. Mentally walk the stack before typing the command. The `gh pr view <N> --json baseRefName` of the previous PR is your source of truth.

---

## Rule 2 — Never `--delete-branch` mid-stack

`gh pr merge <PR> --rebase` for a stacked PR **MUST NOT** use `--delete-branch` until the entire stack has merged. Branch cleanup happens in a single post-merge sweep at the end.

```bash
# CORRECT — merge without deleting; cleanup deferred to end
gh pr merge 91 --rebase --admin
gh pr merge 92 --rebase --admin
# ... merge all stack PRs first ...
# Then in §5 sweep:
for branch in feat/p10.6a-... feat/p10.6d-... feat/p10.6f-... ...; do
  git push origin --delete $branch 2>/dev/null
done

# WRONG — cascade-closes the next PR in the stack
gh pr merge 91 --rebase --delete-branch  # ← deletes feat/p10.6a-...
                                          #   GitHub auto-closes PR #92
                                          #   because its base ref is gone
```

**Why**: P10.6 Step 1 used `--delete-branch` per the original plan. Deleting `feat/p10.6a-settings-profile-password` on origin cascade-closed PR #92 (which had that branch as its base). Without warning. The plan was right that the branch was no longer needed — wrong that mid-stack was the right time to clean up.

**How to apply**: deferred cleanup is a tiny extra step at the end of the merge run. The cost of forgetting one branch in the sweep loop is minimal (a `git push origin --delete` later). The cost of cascade-closing a stacked PR is severe — see Rule 3.

---

## Rule 3 — Cascade-closed stacked PR + force-pushed head = NOT reopenable

A PR auto-closed by base-branch deletion **cannot be reopened** if its head has been force-pushed since close (e.g. from rebasing onto the new main). Recovery path = **replacement PR** (Option Y), NOT base-branch recreation (Option X).

```bash
# ─── INCIDENT (Option X — what we tried first; FAILED) ───
# After Step 1 cascade-closed PR #92, we tried:
git push origin <main-tip>:refs/heads/feat/p10.6a-settings-profile-password
gh pr reopen 92
# ↑ FAILED with GraphQL error: "Could not open the pull request. (reopenPullRequest)"
#   Likely cause: GitHub permanently records base-delete close events, AND/OR
#   refuses reopen when tracked headRefOid (pre-rebase) ≠ current branch HEAD
#   (post force-push). We can't distinguish without GitHub support.

# ─── RECOVERY (Option Y — what worked) ───
# 1. Comment on closed PR with explanation + link to log
gh pr comment 92 --body "Auto-closed by base-delete cascade; recovery via PR #98"
# 2. Open replacement PR pointing at same head commit, base = current main
gh pr create \
  --base main \
  --head feat/p10.6d-about-connection-mode \
  --title "P10.6d — Settings About + Connection Mode (replaces #92)" \
  --body "Replaces #92 (cascade-closed). Head commit identical post-rebase. See log."
# 3. Verify diff is exactly the expected feature commit
git log --oneline origin/main..origin/feat/p10.6d-about-connection-mode  # expect 1 commit
# 4. Merge as normal
gh pr merge 98 --rebase --admin
```

**Why**: empirical observation during P10.6 Step 2 catch-22. Option X is the obvious recovery (just recreate the missing ref + reopen), but GitHub's reopen API refuses. Option Y costs you a discontinuous PR number in the history (#92 → #98) but is the only path that works.

**How to apply**: if Rule 2 is followed strictly, you should never need Rule 3. But if you slip and a PR cascade-closes, jump straight to Option Y — don't waste time on Option X.

---

## Rule 5 — When a PR adds a migration, the "1 Henry action" block MUST include `dotnet ef database update`

The 2026-06-05 P10.7a-1.3 Catalyst checkpoint failed with a blind `HTTP 500 · http.non_success` on login. Root cause: the shipped server expected schema from 3 new migrations that operator's `data/ccl_mes.db` lagged. The 500 had no diagnostic in the operator UI; the agent had to remote-debug the server log to even find out it was a missing-column error.

**Henry's reproducibility block for any PR with EF migrations MUST be:**

```bash
# Pulled from PR #N description
git fetch origin && git checkout <branch>
# ← THIS step is the one that was missing
dotnet ef database update \
  --connection "Data Source=$(pwd)/data/ccl_mes.db" \
  --project src/CCL.MES.Infrastructure \
  --startup-project src/CCL.MES.Web
# then verify + boot
cd CCL-MES-Hybrid && bash scripts/verify-p10.7a-<N>.sh --keep-alive
```

PRs that do NOT touch migrations skip the middle step.

**Defence-in-depth — agent-side:** when shipping a migration the agent MUST also add a pending-migration boot probe to the API host (see `CCL-MES-Hybrid/src/CCL.MES.Api/Program.cs` — Program.cs queries `Database.GetPendingMigrationsAsync()` at boot, logs a multi-line `WARNING — DATABASE HAS UNAPPLIED MIGRATIONS` block, and refuses to start if `Database:FailOnPendingMigrations=true` in config). The probe is the seat-belt; the documented Henry-action block is the steering wheel.

**Why:** blind 500s on the operator UI burn 30+ minutes of agent diagnostic time per incident, are worse than a hard "won't start" boot failure (which at least points at the cause), and degrade Henry's trust in the "1 command repro" model the checklist is built on.

---

## Rule 4 — Gate scripts must strip BOTH `@* *@` Razor block comments AND `//` C# line comments

When grepping Razor files for forbidden patterns (e.g. the `<InputText>` renderer-dead trap),
the gate script **MUST** strip Razor block comments AND C# `//` line comments before grepping. Otherwise documentation strings restating the forbidden pattern produce false positives.

```bash
# CORRECT — strip both comment styles, then grep
for f in $(find src/CCL.MES.Hybrid.Razor -name "*.razor"); do
  perl -0777 -pe 's{@\*.*?\*@}{}gs; s{//[^\n]*}{}g' "$f"
done | grep -cE '<InputText\b'

# WRONG — counts doc-strings as code usages
grep -rcE '<InputText\b' src/CCL.MES.Hybrid.Razor --include='*.razor'
```

**Why**: P10.6f's `RecentScansWidget.razor` and P10.6e's `SettingsAuditLog.razor` both contained intentional documentation strings restating "no `<InputText>` per the renderer-crash lesson". The naive grep counted those as deviations. Stripping both comment styles before counting drops the gate to 0 (correct) instead of 1-2 (false positive).

**How to apply**: the canonical gate snippet lives in `docs/p10.6-screens/log-10-merge-stack-to-main.txt` (search "perl -0777"). Copy it verbatim into any new gate script. Don't simplify to a plain grep — you'll re-discover this lesson the hard way.

---

## Quick reference — full stacked-PR merge protocol

```bash
# ─── PRE-FLIGHT ───
# 1. Tag a safety branch at current main
git push origin <main-tip>:refs/heads/backup/pre-<phase>-merge
# 2. Verify each PR's base + head + mergeable state
for PR in 91 92 93 94 95 96 97; do
  gh pr view $PR --json number,state,baseRefName,headRefName,mergeable
done
# 3. STOP if any base lệch expected — fix via `gh pr edit <N> --base <expected>`

# ─── PER PR ───
# 4. Retarget base to main (since previous PR has merged, prev-head is no longer the base for this PR's content)
gh pr edit <N> --base main
# 5. Local rebase onto main (patch-id dedup skips already-applied ancestor commits)
git fetch origin
git checkout feat/<branch>
git reset --hard origin/feat/<branch>
git rebase origin/main
# 6. Force-push so GitHub sees a clean merge
git push --force-with-lease origin feat/<branch>
# 7. Merge — NO --delete-branch
gh pr merge <N> --rebase --admin
# 8. Gate: verify-script + canary count + 4 hotfix layer files + 0 real <InputText>

# ─── POST-MERGE SWEEP ───
# 9. Run all verify scripts in sequence (regression belt)
for script in verify-p10.6a.sh ... verify-p10.6c.sh; do bash scripts/$script; done
# 10. Cleanup: delete all merged feature branches
for branch in feat/p10.6a-... feat/p10.6d-... ...; do
  git push origin --delete $branch
done
# 11. Tag the merged release
git tag -a v<X.Y.Z> -m "..."
git push origin v<X.Y.Z>
```

---

## Related incidents

- `docs/p10.6-screens/log-10-merge-stack-to-main.txt` — full P10.6 merge stack log
- PR #92 comment thread — Option X failure analysis + replacement PR rationale
- PR #98 description — Option Y execution
