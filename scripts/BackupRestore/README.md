# BackupRestore

Console-only DB restore. Phase 6 Bước 5.

> **Why not via the web UI?** Restore overwrites the live DB. Doing it
> from a Razor page risks SQLite file-lock contention with the running
> server, mid-restore failures with no rollback, and accidental clicks.
> Done as a console script with the server stopped, restore is a clean
> atomic file swap with a deterministic audit row.

## When to use

- Roll back to a snapshot taken earlier via `Settings → Backup / Restore →
  Create snapshot` (or any `ccl_mes.db.bak.snapshot-*` file).
- Roll back to a backup taken outside the app (manual `cp` of the SQLite
  file is supported — just pass the filename via `--from`).

## Pre-restore safety rail

Before the byte-overwrite the script automatically copies the current
DB to `ccl_mes.db.bak.pre-restore-<utc-ts>` next to the live DB. If
the script crashes mid-restore the pre-restore backup is still intact.

The script also requires the literal string `CONFIRM-RESTORE` to be
typed at the interactive prompt before any byte is written. Anything
else aborts cleanly.

## Trust model

Trust boundary = OS user with write access to the SQLite DB file.

On production setups:
- `chmod 600 ccl_mes.db`
- Only the deploy account should be able to run this script.

A `BACKUP_RESTORE` audit row is appended to the (restored) DB on
success; `Source = "Console"`, `ActorUsername = $USER`, `Detail`
carries snapshot filename + pre-restore backup filename.

## Usage

From `scripts/BackupRestore/`:

```bash
# Restore from a snapshot file sitting next to ccl_mes.db
dotnet run -- --from ccl_mes.db.bak.snapshot-20260601-101530

# Or pass an absolute path
dotnet run -- --from /backups/ops/ccl_mes.db.bak.snapshot-20260601-101530
```

Steps:

1. **Stop the web app first.** SQLite holds a file lock while the app
   is running.
2. Run the command above.
3. Compare the snapshot's row counts (printed before confirmation) with
   the current live DB's row counts — confirm this is the snapshot you
   want.
4. Type `CONFIRM-RESTORE` at the prompt.
5. Pre-restore backup is taken automatically.
6. Live DB is overwritten with the snapshot.
7. Audit row is written.
8. Post-restore row counts are printed for verification.
9. Restart the web app.

## SQL Server

Not supported by this script. For SQL Server use:
- `BACKUP DATABASE … TO DISK = '…'` / `RESTORE DATABASE … FROM DISK = '…'`
- SSMS GUI → Tasks → Restore
- Maintenance plans

The web Backup tab also shows a guidance card pointing operators at SSMS
for SQL Server deployments.

## Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 2 | Invalid arguments / usage |
| 3 | Active DB file not found |
| 4 | Snapshot file not found |
| 5 | Aborted (`CONFIRM-RESTORE` not typed) |
| 6 | Pre-restore backup failed (DB untouched) |
| 7 | Overwrite copy failed (DB potentially partial; pre-restore backup intact) |

## Override DB path

```bash
MES_DB_PATH=/var/lib/ccl/ccl_mes.db dotnet run -- --from snapshot.db
```
