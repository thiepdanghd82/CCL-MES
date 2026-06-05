# Archive Manifest — 2026-06-05

> This dir holds three tar.gz archives created during the 2026-06-05 disk
> cleanup pass — Phase 2 LOW (Group A) + MEDIUM retention execution (Piles
> P + D). All archives verified via `tar -tzf` + entry-count check before
> originals were deleted.

## Archive 1 — Stray DB snapshots from `src/CCL.MES.Web/` (Phase 2 LOW Group A)

### Source

Stray SQLite snapshots accumulated in `src/CCL.MES.Web/` during the 2026-05-31 dev session. All gitignored per repo `.gitignore` (`*.db.bak*`, `*.db.testcopy*`, `*.imported-*` patterns). Identified in `docs/AUDIT-DISK-PHASE1-2026-06-04.md` row 10 as ~170 MB LOW-risk cleanup candidate (actual measured: 138 MB / 17 files).

### Archive

| Field | Value |
|---|---|
| File | `_archive/2026-06-05/ccl_mes_web_db_snapshots_20260605.tar.gz` |
| Created | 2026-06-05T03:07:XXZ |
| Compressed size | 21 MB (~21,584,258 bytes) |
| Raw size | 138 MB (17 files) |
| Compression ratio | ~6.4× (SQLite snapshots share most pages) |
| SHA-256 | `68b3a004b0b8f2fa87229d8eced75d63421788feebb527df29344cb8713fb4db` |
| Entry count | 17 |
| Verify | `tar -tzf` returned 17 entries, all paths match — see disk-phase2 log |

### Contents (17 files, all from `src/CCL.MES.Web/`)

| # | File | Bytes | Notes |
|---:|---|---:|---|
| 1 | `ccl_mes.db.bak.phase5rbac-20260531-111842` | 11,018,240 | RBAC sub-phase pre-state snapshot |
| 2 | `ccl_mes.db.bak.phase5hubauth-20260531-113445` | 11,018,240 | Hub auth sub-phase pre-state |
| 3 | `ccl_mes.db.bak.phase5errcodes-20260531-114918` | 11,018,240 | Error codes sub-phase pre-state |
| 4 | `ccl_mes.db.bak.phase5migr-20260531-120743` | 11,018,240 | Migration sub-phase pre-state |
| 5 | `ccl_mes.db.bak.phase5-close-20260531-121523` | 11,030,528 | Phase 5 close snapshot |
| 6 | `ccl_mes.db.bak.phase6-2a-20260531-125456` | 11,030,528 | Phase 6 step 2a pre-state |
| 7 | `ccl_mes.db.bak.phase6-2b-20260531-133948` | 11,030,528 | Phase 6 step 2b pre-state |
| 8 | `ccl_mes.db.bak.phase6-3-20260531-135133` | 11,030,528 | Phase 6 step 3 pre-state |
| 9 | `ccl_mes.db.bak.phase6-4-20260531-141805` | 11,030,528 | Phase 6 step 4 pre-state |
| 10 | `ccl_mes.db.bak.phase6-5-20260531-150441` | 11,030,528 | Phase 6 step 5 pre-state (final dev tip) |
| 11 | `ccl_mes.db.bak.phase6-5-20260531-150441-shm` | 32,768 | SQLite shared-memory companion |
| 12 | `ccl_mes.db.bak.phase6-5-20260531-150441-wal` | 0 | SQLite write-ahead-log companion (empty) |
| 13 | `ccl_mes.db.testcopy.tested` | 11,051,008 | Verified-import test copy |
| 14 | `ccl_mes.db.testcopy.tested-phase6-4` | 11,030,528 | Verified-import copy at phase 6.4 |
| 15 | `ccl_mes.imported-2026-05-31.db` | 11,104,256 | Successful import from xlsx |
| 16 | `ccl_mes.imported-2026-05-31.db-shm` | 32,768 | SQLite shared-memory companion |
| 17 | `ccl_mes.imported-2026-05-31.db-wal` | 0 | SQLite write-ahead-log companion (empty) |
|   | **Total** | **138,505,728** | |

### Recovery procedure

If a snapshot is needed later (forensic debug, regression test fixture):

```bash
cd "/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/CCL-MES"
# 1. Verify archive integrity
shasum -a 256 _archive/2026-06-05/ccl_mes_web_db_snapshots_20260605.tar.gz
#   expected: 68b3a004b0b8f2fa87229d8eced75d63421788feebb527df29344cb8713fb4db
# 2. List contents
tar -tzf _archive/2026-06-05/ccl_mes_web_db_snapshots_20260605.tar.gz
# 3. Extract single file (paths preserve the original src/CCL.MES.Web/ prefix)
tar -xzf _archive/2026-06-05/ccl_mes_web_db_snapshots_20260605.tar.gz \
  src/CCL.MES.Web/ccl_mes.db.bak.phase6-5-20260531-150441
# 4. Open with sqlite3
sqlite3 src/CCL.MES.Web/ccl_mes.db.bak.phase6-5-20260531-150441 .tables
```

### Retention policy

Archives in `_archive/<date>/` are deleted after 90 days unless explicitly anchored by a Henry decision (e.g. tied to an open incident or compliance audit). Track anchor decisions in this MANIFEST under a `## Retention anchor` heading.

This archive: NO anchor — auto-eligible for deletion after 2026-09-03.

### Audit context

- Phase 1 audit: `CCL-MES-Hybrid/docs/AUDIT-DISK-PHASE1-2026-06-04.md` row 10
- Phase 2 LOW cleanup log: `CCL-MES-Hybrid/docs/disk-phase2-low-cleanup-2026-06-05.txt`
- Originals deleted post-archive-verify (tar -tzf returned 17 entries == expected count).

---

## Archive 2 — Pile P phase7 sub-step snapshots (MEDIUM retention)

### Source

Operator hand-snapshots of `data/ccl_mes.db` taken before risky phase 7 sub-step migrations + pre-PR snapshots from 2026-06-01. Per `RETENTION-POLICY-PROPOSAL-2026-06-05.md` Pile P rule: keep newest phase7-* file (`data/ccl_mes.backup-phase7-wc-pre.db` retained live), archive the rest.

### Archive

| Field | Value |
|---|---|
| File | `_archive/2026-06-05/phase7_snapshots_20260605.tar.gz` |
| Created | 2026-06-05T04:36:XXZ |
| Compressed size | 13 MB (~13,298,569 bytes) |
| Raw size | ~85 MB (5 files) |
| Compression ratio | ~6.4× |
| SHA-256 | `f21e55e3118dadd9f2161855b5e93b2caa9680850afb1b1590d305fe3fb7a1d7` |
| Entry count | 5 |
| Verify | `tar -tzf` returned 5 entries == expected |

### Contents (5 files)

| # | File | Bytes | Notes |
|---:|---|---:|---|
| 1 | `data/ccl_mes.backup-phase7-routine-pre.db` | 14,417,920 | Phase 7 routine pre-state |
| 2 | `data/ccl_mes.backup-phase7-rawmat-pre.db` | 18,616,320 | Phase 7 rawmat pre-state |
| 3 | `data/ccl_mes.backup-phase7-spec-pre.db` | 18,616,320 | Phase 7 spec pre-state |
| 4 | `data/ccl_mes_backup_20260601_210827_qcplan.db` | 18,616,320 | Pre-PR QC plan snapshot |
| 5 | `data/ccl_mes_backup_20260601_213217_qccapture.db` | 18,616,320 | Pre-PR QC capture snapshot |
|   | **Total** | **88,883,200** | |

Retention: no anchor — auto-eligible for deletion after 2026-09-03.

---

## Archive 3 — Pile D pre-PR snapshots (MEDIUM retention)

### Source

Operator hand-snapshots taken before merging PRs #31a/b/d (spec print-color, spec flexo, detail sheet). All PRs landed long ago; commit history is the source of truth. Per `RETENTION-POLICY-PROPOSAL-2026-06-05.md` Pile D rule: archive all 3 + remove `db-backups/` directory entirely.

### Archive

| Field | Value |
|---|---|
| File | `_archive/2026-06-05/pre_pr31_snapshots_20260605.tar.gz` |
| Created | 2026-06-05T04:36:XXZ |
| Compressed size | 8 MB (~8,203,564 bytes) |
| Raw size | ~53 MB (3 files) |
| Compression ratio | ~6.6× |
| SHA-256 | `a6ae92781a899276bd9dd77937571eea103a4c73cfa2b814dd1ccc140d741c48` |
| Entry count | 3 |
| Verify | `tar -tzf` returned 3 entries == expected |

### Contents (3 files)

| # | File | Bytes | Notes |
|---:|---|---:|---|
| 1 | `db-backups/ccl_mes.pre-pr31a-specprintcolor.db` | 18,616,320 | Pre-PR #31a (spec print color) |
| 2 | `db-backups/ccl_mes.pre-pr31b-specflexo.db` | 18,616,320 | Pre-PR #31b (spec flexo) |
| 3 | `db-backups/ccl_mes.pre-pr31d-detail-sheet.db` | 18,616,320 | Pre-PR #31d (detail sheet) |
|   | **Total** | **55,848,960** | |

Post-archive cleanup: 2 orphan SQLite sidecar files (`.db-shm`, `.db-wal` of pre-pr31a) were deleted separately (~32 KB combined, empty/near-empty), then `db-backups/` directory was `rmdir`'d. Going forward, add `db-backups/` to `.gitignore` per §6 hardening recommendation (deferred to follow-up sprint).

Retention: no anchor — auto-eligible for deletion after 2026-09-03.

---

## Policy gap found during execution

The retention proposal §3 Pile R rule named two categories explicitly: `*.snapshot-*` (newest 5) and `*.phase{6-close,7-*}-*` (1 anchor each). During execution a third category was discovered in `data/Backup/SQLite/`:

- `pre-restore-20260604-134058-ccl_mes.db.bak.snapshot-20260604-134058` (18 MB, Jun 4 13:40)

This is a safety snapshot the P10.6h backup endpoint writes before performing a restore. Forensic-significant: it captures the exact pre-restore state of an actual operator restore operation. Kept as a forensic anchor (decision deviating slightly from proposal — proposal didn't anticipate the category). Pile R landed at 131 MB vs proposed ~120 MB ceiling (+11 MB / 9% over).

**Follow-up recommendation for §6 hardening sprint**: when codifying `scripts/prune-sqlite-backups.sh`, add `pre-restore-*` as a recognized category with rule "keep all (forensic anchors), prune only if older than 180 days AND restore operation deemed successful per audit log".

---

## Aggregate impact (Phase 2 LOW + MEDIUM retention)

| Group | Live freed | Archived |
|---|---:|---:|
| Phase 2 LOW Group A (`src/CCL.MES.Web/`) | 116 MB | 21 MB |
| Phase 2 LOW Group B (bin+obj wipe) | 917 MB | — |
| Phase 2 LOW Group C (`git gc`) | 17 MB | — |
| Phase 2 LOW Group D (85 branches pruned) | <1 MB | — |
| MEDIUM Pile R (data/Backup/SQLite/) | 102 MB | — |
| MEDIUM Pile P (data/*.backup-phase7-*) | 85 MB | 13 MB |
| MEDIUM Pile D (db-backups/) | 53 MB | 8 MB |
| **Total** | **~1.29 GB live freed** | **42 MB archived** |

Whole tree: 1.6 GB (audit baseline) → **381 MB** as of 2026-06-05T04:37Z.
