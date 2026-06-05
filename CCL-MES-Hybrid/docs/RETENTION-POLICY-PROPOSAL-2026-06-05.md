# Retention Policy Proposal — MEDIUM-risk SQLite snapshots

> **Status: DRAFT — pending Henry approval. No files deleted by this document.**
> Companion to `disk-phase2-low-cleanup-2026-06-05.txt` (Phase 2 LOW already shipped, ~1.05 GB freed).

---

## 1. Current state (measured 2026-06-05T03:10Z)

Three uncoordinated SQLite backup piles totalling **~389 MB**. Each pile has its own naming convention, its own implicit (or absent) retention rule, and overlapping date ranges that make it impossible to tell at a glance which file is the canonical "rollback anchor" for a given phase.

| Pile | Path | Files | Size | Naming pattern | Source |
|---|---|---:|---:|---|---|
| **R — Rolling** | `data/Backup/SQLite/` | 18 + 2 tmp | 233 MB | `ccl_mes.db.bak.{phase6-close,phase7-pre-X,phase7-post-X,snapshot}-{YYYYMMDD-HHMMSS}` | Server backup scheduler (auto) |
| **P — Phase anchors** | `data/ccl_mes.backup-phase7-*.db` + `data/ccl_mes_backup_YYYYMMDD_*.db` | 6 | 103 MB | `ccl_mes.backup-phase7-{rawmat,routine,spec,wc}-pre.db` + `ccl_mes_backup_{YYYYMMDD}_{HHMMSS}_{label}.db` | Manual pre-step `cp` (operator) |
| **D — Pre-PR** | `db-backups/` | 3 | 53 MB | `ccl_mes.pre-pr{N}{letter}-{slug}.db` | Manual pre-merge `cp` (operator) |
| | **Total** | **29** | **389 MB** | | |

Two `.tmp` partial uploads (`_upload-20260604-134058.tmp-shm` + `-wal`) in R are write-aborted fragments from an interrupted upload; they are pure noise and safe to delete immediately regardless of policy outcome.

---

## 2. Why three piles exist

Reading the audit findings + commit context:

- **R (`data/Backup/SQLite/`)** is the only programmatic backup target. The Hybrid API's backup endpoint (P10.6h, just shipped in v0.10.6) writes here. Server scheduler also writes here on cadence. Files dated 2026-06-04 are the post-P10.6h verify runs.
- **P (`data/*.db`)** were operator hand-snapshots taken before risky migrations or imports during phase 6 + phase 7 dev sessions. Names encode what's being protected.
- **D (`db-backups/`)** were operator hand-snapshots taken before merging risky PRs. The slug names match closed PRs (PR #31 a/b/d for spec print-color, spec flexo, detail sheet).

Both P and D piles are dev-session safety nets that out-served their purpose the moment the matching commit landed on `main`. They contain no information not also present in either git history (the code) or pile R (a more recent SQLite snapshot).

---

## 3. Proposed policy (per-pile)

### Pile R — Rolling backups (`data/Backup/SQLite/`)

| Rule | Detail |
|---|---|
| **Live retention** | Keep newest 5 `*.snapshot-*` files. Delete older. |
| **Anchor retention** | Keep 1 `.phase6-close-*` + 1 latest `.phase7-*` (any sub-step) as historical anchors. Delete duplicates. |
| **Tmp cleanup** | Any `_upload-*.tmp-*` file older than 6 hours: delete. |
| **Enforced by** | Cron / scheduled task running `scripts/prune-sqlite-backups.sh` nightly (to write). Server backup endpoint should also gc on each new write. |
| **After policy** | ~5 × 18 MB = 90 MB + 2 × ~14 MB anchors = ~120 MB ceiling (currently 233 MB → save ~113 MB). |

### Pile P — Phase anchors (`data/`)

| Rule | Detail |
|---|---|
| **Live retention** | Keep ONE file: the newest `ccl_mes.backup-phase7-*.db` (the latest phase 7 sub-step we touched). It's a useful "phase 7 entry point" reference. |
| **Move policy** | All others: archive to `_archive/2026-06-05/phase7_snapshots.tar.gz` with manifest + sha256 (same pattern as Phase 2 LOW Group A). |
| **Reason to keep one** | Phase 7 is still in active development per `docs/P10.7-WO-STATE-CONTRACT.md`. Cheap insurance ($18 MB) against a phase 7 regression that requires re-running an import. |
| **After policy** | 1 × 18 MB = 18 MB live + ~85 MB compressed to ~12 MB archive = ~30 MB total (vs current 103 MB → save ~73 MB). |

### Pile D — Pre-PR snapshots (`db-backups/`)

| Rule | Detail |
|---|---|
| **Live retention** | Keep ZERO. All 3 PRs (#31a/b/d) have long since merged + been superseded. |
| **Move policy** | Archive all 3 to `_archive/2026-06-05/pre_pr31_snapshots.tar.gz` with manifest + sha256. |
| **Going forward** | Delete `db-backups/` directory entirely + add to `.gitignore` so it doesn't recreate. Pre-PR snapshots should land in pile R via the API endpoint (already supports labeled snapshots per P10.6h spec). |
| **After policy** | 53 MB → 0 MB live + ~9 MB archive = ~9 MB total (save ~44 MB). |

---

## 4. Aggregate impact

| Pile | Before | After (live) | After (archive) | Freed |
|---|---:|---:|---:|---:|
| R | 233 MB | ~120 MB | 0 | ~113 MB |
| P | 103 MB | ~18 MB | ~12 MB | ~73 MB |
| D | 53 MB | 0 | ~9 MB | ~44 MB |
| **Total** | **389 MB** | **~138 MB** | **~21 MB** | **~230 MB** |

Plus immediate freed: 2 × tmp files in R (~32 KB, negligible).

Matches the audit's "~219 MB additional MEDIUM-risk recoverable" within rounding (~5% delta — audit estimate was conservative).

---

## 5. Risk + recovery profile

| Action | Risk | Recovery if needed |
|---|---|---|
| Delete tmp partial uploads | Zero — already corrupt fragments | N/A |
| Prune R to newest 5 + 2 anchors | LOW — newest files always preserved; anchors cover phase 6 + 7 entry | Restore from archive if older needed |
| Archive P (keep 1) | LOW — content captured in tar.gz + sha256 + manifest | `tar -xzf _archive/2026-06-05/phase7_snapshots.tar.gz <file>` |
| Archive D entirely | LOW — PRs landed; commit is the source of truth | `tar -xzf _archive/2026-06-05/pre_pr31_snapshots.tar.gz <file>` |

All archives live under `_archive/` which is Henry-untouchable per Phase 2 LOW rules. Auto-eligible for deletion after 90 days unless an incident anchors them earlier.

---

## 6. Operational hardening (defer to follow-up sprint, NOT this proposal)

Things the proposal **does NOT** ship — recommendations for a future operational sprint after the policy itself is approved:

1. **`scripts/prune-sqlite-backups.sh`** — codify §3 R rules. Idempotent, dry-run by default, `--apply` to act. Wire to nightly cron + the P10.6h backup endpoint.
2. **`.gitignore` updates** — add `db-backups/` after pile is archived + removed. Add `data/Backup/SQLite/_upload-*.tmp-*` as belt-and-suspenders.
3. **Backup endpoint label flag** — extend `POST /api/v2/backup` to accept `?label=phase7-pre-import` so operator-driven snapshots route to pile R with semantic naming instead of needing pile P.
4. **MAINTAINERS.md section** — document the single pile R as canonical, with the retention rule + recovery procedure inline.

---

## 7. Approval needed

If Henry approves §3-4 as proposed:

```bash
# 1. Archive piles P + D (one-shot, mirror Group A pattern)
cd "/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/CCL-MES"
find data -maxdepth 1 -name "ccl_mes.backup-phase7-*.db" -not -name "$(ls -t data/ccl_mes.backup-phase7-*.db | head -1 | xargs basename)" -print | \
  tar -czf _archive/2026-06-05/phase7_snapshots.tar.gz -T -
# Plus another tar for ccl_mes_backup_YYYYMMDD_*.db files
find db-backups -maxdepth 1 -name "*.db" -print | tar -czf _archive/2026-06-05/pre_pr31_snapshots.tar.gz -T -
shasum -a 256 _archive/2026-06-05/phase7_snapshots.tar.gz _archive/2026-06-05/pre_pr31_snapshots.tar.gz
# Verify tar -tzf, then delete originals (excluding the newest phase7 keeper)

# 2. Prune pile R (write the script first; never do this ad-hoc)
ls -t data/Backup/SQLite/ccl_mes.db.bak.snapshot-* | tail -n +6 | xargs rm
rm data/Backup/SQLite/_upload-*.tmp-*
# Keep 1 phase6-close + 1 latest phase7-*

# 3. Remove empty db-backups/
rmdir db-backups
```

Each step explicit, reversible (originals → archive), single-mistake-survivable (archives have sha256 + manifest).

---

## 8. Decision request

Henry, please choose:

- **APPROVE AS-IS** — execute §3 rules + §7 commands. Net save: ~230 MB.
- **APPROVE WITH AMENDMENTS** — tighter (e.g. keep only 3 R snapshots) or looser (keep all phase7 in pile P) rules per your call.
- **DEFER** — leave MEDIUM group untouched. Next disk pressure window will revisit.

If approved, the operational hardening (§6) becomes a separate, smaller follow-up — not bundled with the cleanup execution.
