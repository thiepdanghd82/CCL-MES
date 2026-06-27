# CCL-MES Backup — Operator Guide (IBM 3-2-1)

> Automated backup ported from Ops Control v1.3. **3 copies** (live DB +
> SQLite snapshot + blob tarball), **2 media** (`.bak` + `.tar.gz`),
> **1 off-site** (rsync/robocopy to NAS/USB/share).

## Layout

```
<DATA_DIR>/
├── ccl_mes.db                                   # live SQLite (copy 1)
├── blobs/drawings/…                             # live drawings/CAD
├── Library/SystemConfig/backup-schedule.json    # admin-edited schedule
└── Backup/
    ├── SQLite/ccl_mes.db.bak.snapshot-YYYYMMDD-HHMMSS   # copy 2 (online snapshot)
    └── Blobs/blobs_YYYYMMDD.tar.gz                      # copy 2 (blob archive)
```
`<DATA_DIR>` = folder of the SQLite `Data Source` (override with `MES_DB_PATH` / `MES_DATA_DIR`).

## 1. In-process scheduler (local "3 copies")

`BackupSchedulerService` runs nightly inside the server. Each cycle:
snapshot SQLite (online, no lock) → tar `blobs/` → `PRAGMA integrity_check`
+ row-count anomaly check vs live → prune old → audit (`BACKUP_CYCLE` /
`BACKUP_FAILED`) → webhook alert on failure.

**Default OFF.** Enable one of two ways:

| Way | How | Survives restart |
|---|---|---|
| Admin UI | Settings → Backup → "Scheduled backup" → tick *Enable nightly backup* → Save. Also has **Run backup now**. | yes (writes `backup-schedule.json`) |
| Environment | `OPS_BACKUP_SCHEDULE=1` (first-boot fallback) | yes |

**Settings** (env first, `appsettings.json` `Ops:Backup:*` fallback; the UI/JSON override both):

| Env | appsettings | Default | Meaning |
|---|---|---|---|
| `OPS_BACKUP_SCHEDULE` | `Ops:Backup:Enabled` | `false` | enable nightly cycle |
| `OPS_BACKUP_HOUR` | `Ops:Backup:Hour` | `2` | run hour 0–23, **ICT** |
| `OPS_BACKUP_RETENTION_DAYS` | `Ops:Backup:RetentionDays` | `30` | prune older than N days… |
| `OPS_BACKUP_MIN_KEEP` | `Ops:Backup:MinKeep` | `10` | …but always keep N newest |
| `OPS_BACKUP_BLOBS` | `Ops:Backup:Blobs` | `true` | also tar `blobs/` |
| `OPS_BACKUP_WEBHOOK` | `Ops:Backup:WebhookUrl` | — | Slack/Teams alert URL |

## 2. Off-site copy ("1 off-site") — separate cron job

A network hang must never block the server, so the off-site copy is a
standalone script run AFTER the in-process backup (~02:30).

### macOS / Linux — `scripts/backup-offsite.sh`
```bash
MES_DATA_DIR=/opt/ccl-mes/data \
MES_OFFSITE_TARGET=backup@nas.local:/volume1/ccl-mes-backup \
MES_OFFSITE_SSH_KEY=~/.ssh/ccl_backup_id_ed25519 \
  bash scripts/backup-offsite.sh
```
`MES_OFFSITE_TARGET` may be an ssh form (`user@host:/path`) or a local
mount (`/Volumes/usb/ccl-mes-backup`). `MES_OFFSITE_DRY_RUN=1` to test.

**cron** (nightly 02:30):
```
30 2 * * * MES_DATA_DIR=/opt/ccl-mes/data MES_OFFSITE_TARGET=backup@nas:/volume1/ccl-mes-backup /opt/ccl-mes/scripts/backup-offsite.sh >> /var/log/ccl-offsite.log 2>&1
```

### Windows — `scripts/backup-offsite.ps1`
Target = external disk / mapped drive / UNC share (`\\nas\backup\ccl-mes`).
```powershell
$env:MES_DATA_DIR='C:\ccl-mes\data'; $env:MES_OFFSITE_TARGET='\\nas\backup\ccl-mes'
powershell -NoProfile -ExecutionPolicy Bypass -File C:\ccl-mes\scripts\backup-offsite.ps1
```
**Task Scheduler** (nightly 02:30):
```
schtasks /Create /TN "CCL-MES Off-site Backup" /SC DAILY /ST 02:30 ^
  /TR "powershell -NoProfile -ExecutionPolicy Bypass -File C:\ccl-mes\scripts\backup-offsite.ps1"
```

Both scripts: pick newest snapshot (excluding `-wal`/`-shm` sidecars) +
newest tarball, copy, verify by SHA256, prune local-target files older
than `MES_OFFSITE_RETAIN` (default 14d; remote/NAS retention is the NAS's
own job — the scripts never prune over ssh).

## 3. Restore

Console-only, deliberately destructive-gated (see `scripts/BackupRestore/`):
```bash
cd scripts/BackupRestore
dotnet run -- --from <snapshot-filename>     # prompts CONFIRM-RESTORE, auto pre-restore backup
```
Blobs: extract the matching `blobs_YYYYMMDD.tar.gz` into `<DATA_DIR>/`
(it contains the `blobs/` tree). Restore blobs from the SAME night as the
DB snapshot to keep drawing references consistent.

## Compliance note
Vietnam accounting law requires multi-year retention. The 30-day local
window + off-site mirror is the recovery mechanism; for 10-year retention
point `MES_OFFSITE_TARGET` at storage with its own long-term policy and do
**not** rely on the scripts' prune for the off-site copy.
