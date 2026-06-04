# CCL-CMES Phase 1 Disk Audit — 2026-06-04

Read-only inventory. Henry decides cleanup actions separately. All sizes are POSIX `du` output (allocated bytes ≈ logical bytes on this APFS volume).

---

## 1. Headline numbers

- **Total tree size**: **1.5 GB** (`/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/CCL-CMES/`)
- **Single git repo**: `CCL-MES/.git` (41 MB, all loose objects — 0 pack files yet)

### Composition

| Bucket | Size | % of tree | Recoverability |
|---|---:|---:|---|
| .NET `bin/` + `obj/` artifacts (15 dirs, 8 projects) | **916 MB** | ~61% | LOW — `dotnet clean` regenerates |
| SQLite live + snapshot DBs (~50 files, scattered) | **~310 MB** | ~21% | MEDIUM — review before delete |
| Source code + docs + tests (`*.cs`, `*.md`, `*.razor`, csv) | **~135 MB** | ~9% | KEEP — actual project |
| `.git` history (loose objects, no pack) | **41 MB** | ~3% | KEEP — `git gc` would shrink modestly |
| Operator data (`Data/IPQC results`, `Data/Specs`, `data/blobs`) | **~62 MB** | ~4% | HIGH — user data |
| Other (skills, tools, docs, .DS_Store, etc.) | **~25 MB** | ~2% | mixed |

> **Cleanup ceiling**: ~916 MB LOW-risk (bin/obj wipe) + ~280 MB MEDIUM-risk (stray .db snapshots gitignored but not deleted) ≈ **~1.2 GB recoverable**, leaving a ~300 MB working tree.

---

## 2. Per-subdirectory table (top-level)

| Path | Size | Role |
|---|---:|---|
| `CCL-MES/CCL-MES-Hybrid/` | 552 MB | New MAUI/Hybrid client + API (.NET 10 maccatalyst); ~308 MB of this is .NET bin/obj |
| `CCL-MES/src/` | 282 MB | Legacy Blazor Web project; 271 MB is `CCL.MES.Web/` with bin/obj + ~170 MB of stray `.db.bak` snapshots |
| `CCL-MES/data/` | 213 MB | Live SQLite + 7 snapshot DBs + 91 MB `Backup/SQLite/` rolling backups + `blobs/drawings/` |
| `CCL-MES/scripts/` | 182 MB | Two console tools (`RecoverAdmin/`, `BackupRestore/`) — 90 MB+ bin each |
| `CCL-MES/tests/` | 99 MB | `CCL.MES.Tests/bin/` (legacy test project) |
| `Data/` (sibling of CCL-MES) | 80 MB | Operator data — IPQC results xlsx, IPQC checklists, Specs |
| `CCL-MES/db-backups/` | 53 MB | Manual pre-PR DB snapshots (3 files, May–Jun) |
| `CCL-MES/docs/` | 2.4 MB | Markdown, design docs |
| Other (`tools/`, `skills/`, root files) | <1 MB | scripts + sln + READMEs |

---

## 3. Top heavy paths (≥ 25 MB)

| # | Path | Size | Recoverability |
|---:|---|---:|---|
| 1 | `CCL-MES-Hybrid/src/CCL.MES.Hybrid/bin/` | 208 MB | LOW — `dotnet clean` (MAUI Catalyst output) |
| 2 | `CCL-MES-Hybrid/src/CCL.MES.Hybrid/obj/` | 99 MB | LOW — `dotnet clean` |
| 3 | `CCL-MES-Hybrid/tests/CCL.MES.Api.Tests/bin/` | 110 MB | LOW — `dotnet clean` |
| 4 | `CCL-MES/tests/CCL.MES.Tests/bin/` | 98 MB | LOW — `dotnet clean` |
| 5 | `CCL-MES/src/CCL.MES.Web/bin/` | 97 MB | LOW — `dotnet clean` |
| 6 | `CCL-MES-Hybrid/src/CCL.MES.Api/bin/` | 96 MB | LOW — `dotnet clean` |
| 7 | `CCL-MES/scripts/RecoverAdmin/bin/` | 90 MB | LOW — `dotnet clean` |
| 8 | `CCL-MES/scripts/BackupRestore/bin/` | 90 MB | LOW — `dotnet clean` |
| 9 | `CCL-MES/data/Backup/SQLite/` (rolling DB bak files) | 91 MB | MEDIUM — auto-rotated, retain ≥ 1 per phase |
| 10 | `CCL-MES/src/CCL.MES.Web/*.db.bak*` + `*.testcopy.*` + `*.imported-*` (32 files) | 170 MB | MEDIUM — gitignored, all dated 2026-05-31 dev session |
| 11 | `CCL-MES/data/ccl_mes.backup-phase7-*.db` (4 files × 18 MB) | 74 MB | MEDIUM — pre-Phase7 snapshots, keep ONE |
| 12 | `CCL-MES/data/ccl_mes_backup_20260601_*_*.db` (2 files × 18 MB) | 36 MB | MEDIUM — pre-PR snapshots |
| 13 | `CCL-MES/db-backups/ccl_mes.pre-pr31*.db` (3 files × 18 MB) | 53 MB | MEDIUM — pre-PR snapshots, keep newest |
| 14 | `Data/IPQC results/2026/*.xlsx` (largest: 12 MB IPQC LABEL daily report) | ~50 MB | HIGH — operator source data, DO NOT TOUCH |
| 15 | `Data/RoutingOperations 260525-52014.csv` | 14 MB | HIGH — IFS export, keep |
| 16 | `CCL-MES/.git/objects/` (loose, unpacked) | 40 MB | KEEP — `git gc` would shrink ~30% |

Notable single binaries inside `bin/Debug/`: `Microsoft.MacCatalyst.dll` + `.pdb` (27 MB each, x3 copies in Hybrid project), `libmonosgen-2.0.a` (27 MB). These are MAUI/Catalyst runtime artifacts and disappear on `dotnet clean`.

---

## 4. Cleanup candidates ranked (highest impact first)

| # | Action | Freed | Risk | One-line | Suggested command |
|---:|---|---:|---|---|---|
| 1 | `dotnet clean` on the solution | **~916 MB** | LOW | Wipes every `bin/` + `obj/` across all 13 projects; rebuilt by next `dotnet build` | `cd "CCL-MES" && dotnet clean CCL.MES.sln` (run also in `CCL-MES-Hybrid/`) — OR `find … -type d \( -name bin -o -name obj \) -exec rm -rf {} +` |
| 2 | Delete stray `*.db.bak*` / `*.testcopy.*` / `*.imported-*` from `src/CCL.MES.Web/` | **~170 MB** | LOW | 32 dev-session snapshots from 2026-05-31; ALL gitignored per `.gitignore` (`*.db.bak*`, `*.db.testcopy*`) | `find "CCL-MES/src/CCL.MES.Web" -maxdepth 1 \( -name '*.db.bak*' -o -name '*.testcopy*' -o -name '*.imported-*' \) -delete` |
| 3 | Prune `data/Backup/SQLite/` to last 2 snapshots | **~73 MB** | MEDIUM | 7 rolling backups from 2026-05-31 dev session; keep newest 2 for safety | `ls -t "CCL-MES/data/Backup/SQLite"/*.bak.* \| tail -n +3 \| xargs rm` |
| 4 | Delete `data/ccl_mes.backup-phase7-*.db` (4 files) | **~74 MB** | MEDIUM | Pre-Phase7 sub-step snapshots; Phase 7 long shipped per CLAUDE.md | `rm "CCL-MES/data/ccl_mes.backup-phase7-"*.db*` |
| 5 | Delete `data/ccl_mes_backup_20260601_*.db` (qcplan + qccapture, 2 files) | **~36 MB** | MEDIUM | Pre-PR snapshots from June 1; PRs presumably merged | `rm "CCL-MES/data/ccl_mes_backup_20260601_"*.db` |
| 6 | Prune `db-backups/` to newest snapshot only | **~36 MB** | MEDIUM | 3 pre-PR-31a/b/d snapshots; keep newest as rollback anchor | `ls -t "CCL-MES/db-backups"/*.db \| tail -n +2 \| xargs rm` |
| 7 | `git gc --aggressive --prune=now` on `CCL-MES/.git` | **~12 MB** | LOW | 3,255 loose objects → packed; no history rewrite | `cd "CCL-MES" && git gc --aggressive --prune=now` |
| 8 | Delete 84 already-merged local branches | <1 MB disk, big mental load | LOW | `git branch --merged` shows 84/165 local branches already merged into HEAD | `cd "CCL-MES" && git branch --merged \| grep -v '\*' \| grep -vE 'main\|master' \| xargs -n1 git branch -d` |
| 9 | Remove 10 `.DS_Store` + 2 `Thumbs.db` | <100 KB | LOW | macOS / Windows clutter | `find "CCL-CMES" \( -name .DS_Store -o -name Thumbs.db \) -delete` |
| 10 | Audit `data/blobs/drawings/` (currently 1.8 MB, untracked) | varies | MEDIUM | Untracked PDF drawing uploads — verify they're already in DB blob store before delete | inspect first, then decide |

**Total recoverable, LOW-risk** (actions 1, 2, 7, 8, 9): **~1.10 GB**
**Total recoverable, MEDIUM-risk** (add actions 3, 4, 5, 6, 10): **~219 MB additional**

---

## 5. Findings worth flagging

1. **`.gitignore` is correct but enforcement is lax** — `.gitignore` already lists `*.db`, `*.db.bak*`, `*.db.testcopy*`, `data/Backup/SQLite/*`. The 170 MB of stray DBs in `src/CCL.MES.Web/` are NOT tracked, just left on disk after a multi-hour 2026-05-31 dev session. A `git clean -nXd` would surface every one of them (showing only ignored files).
2. **Backups are scattered across 3 locations** — `data/Backup/SQLite/`, `data/*.backup-*.db`, and `db-backups/` all contain ~18 MB SQLite snapshots from overlapping date windows. No clear retention policy. Consolidating to a single backup dir with a "keep newest N" rule would prevent re-bloat.
3. **165 local branches, 84 already merged** — Most are short-lived `feat/p10.5x` phase branches. The git pack is empty so each branch costs little, but `git branch -a` becomes unusable as a navigation tool.
4. **`.git/objects` is 100% loose, 0% packed** — 3,255 loose objects + 0 pack files. `git gc` would consolidate, ~30% size reduction, and is a no-brainer maintenance op.
5. **No `node_modules`, no DMG/MSI/PKG installers** — CCL-CMES is pure .NET; no JS bundling. The only `.app` bundles are MAUI Catalyst build outputs inside `bin/Debug/`, which `dotnet clean` handles.
6. **`Data/` (sibling tree, not under `CCL-MES/`)** holds 80 MB of operator IPQC xlsx / csv. Outside git, NOT a code artifact. Probably the canonical reference data set — leave alone.
7. **MAUI Catalyst build duplication** — `bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/CCL MES.app/` mirrors the linked `.dll`/`.pdb` files in a separate `obj/.../codesign/CCL MES.app/`. Each 27 MB binary appears ~3× across these directories. Normal MAUI behavior, but explains why `bin/` alone is 208 MB for one project.
8. **No untracked file > 50 MB** — no accidental large-blob commits in flight; the bloat is all gitignored dev artifacts on disk.

---

## 6. Recommended Phase 2 drill-down

1. **`git clean -nXd` walkthrough with Henry** — confirm every gitignored file is genuinely throwaway before mass deletion. Cheap dry-run.
2. **Backup retention policy** — agree a "keep N most recent per location" rule (3 in `data/Backup/SQLite/`, 1 in `db-backups/`, 0 in `src/CCL.MES.Web/`); codify as a script + cron/Hangfire task.
3. **Branch hygiene pass** — `git branch -d` the 84 already-merged branches; rebase or close the 5+ unmerged feature branches that are stale (`feat/p10.1-api-jwt-shared`, `feat/p10.2-maui-shell-npi-pilot`, `feat/phase8-wo-consolidation`).
4. **`git gc --aggressive`** + packing — one-shot cleanup of the loose-object store.
5. **CI / dev-loop hygiene** — add a pre-commit or `dotnet clean`-on-checkout hook so `bin/`/`obj/` never balloon back to 900 MB silently between sessions.
6. **`Data/` ↔ `CCL-MES/data/` cross-reference** — confirm the sibling `Data/` (IPQC xlsx) is the source-of-truth import set and `CCL-MES/data/blobs/` only holds rendered/imported derivatives; document in MAINTAINERS.

---

## TL;DR for Henry

- **~1.10 GB recoverable LOW-risk** — `dotnet clean` (916 MB) + delete 32 gitignored `.db.bak`/`.testcopy` stragglers in `src/CCL.MES.Web/` (170 MB) + `git gc` (12 MB) + 84 already-merged branches.
- **~219 MB recoverable MEDIUM-risk** — pruning 3 separate SQLite snapshot piles (`data/Backup/SQLite/`, `data/*.backup-phase7-*.db`, `db-backups/`) down to 1-2 anchors each. Verify retention need with Henry before deleting.
- **Biggest surprise**: the tree is only **1.5 GB total**, of which **61% is `bin/`+`obj/`** and **another ~20% is duplicated SQLite snapshots scattered across 3 directories**. There's no runaway git history, no committed large blobs, no `node_modules` — just .NET build artifacts and a half-dozen dev-session DB backups that nobody swept up. Single `dotnet clean` + one `find … -delete` recovers >1 GB without touching anything irreplaceable.
