---
name: cmes-backup-wal
description: >
  Sao lưu / restore SQLite CCL-MES đúng WAL và 3-2-1. Dùng khi snapshot,
  Backup/Restore, backup-offsite, disaster recovery, sha256 bản sao, hoặc
  operator hỏi "copy file .db được không". Restore chỉ console, server tắt.
---

# CMES backup — WAL + 3-2-1

**Rule:** bản sao hợp lệ = SQLite backup API hoặc `.backup` / `VACUUM INTO`,
không phải `cp` khi server đang serve. Restore **không** làm từ Razor UI.

## Chụp snapshot (server có thể đang chạy)

UI: `Settings → Backup / Restore → Create snapshot`  
(`SqliteConnection.BackupDatabase`, an toàn khi serving).

Đích: `$MES_DATA_DIR/Backup/SQLite/ccl_mes.db.bak.snapshot-*`  
Blob: `$MES_DATA_DIR/Backup/Blobs/*.tar.gz`

Bật lịch: `OPS_BACKUP_SCHEDULE=1` hoặc Settings. Off-site **tách process**:

```bash
# cron sau backup in-process (~02:30). Bắt buộc hai env.
MES_DATA_DIR=/opt/ccl-mes/data \
MES_OFFSITE_TARGET=user@nas:/volume1/ccl-mes-backup \
  bash scripts/backup-offsite.sh
```

`MES_OFFSITE_DRY_RUN=1` khi thử. Prune remote qua SSH = quá rủi ro (script
chỉ prune mount local).

## Restore (P0 — ghi đè live)

1. **Tắt API / Hybrid** — SQLite lock.  
2. `cd scripts/BackupRestore && dotnet run -- --from <snapshot>`  
3. So rowcount in ra với live.  
4. Gõ đúng `CONFIRM-RESTORE`.  
5. Script tự copy `ccl_mes.db.bak.pre-restore-<utc>` trước khi overwrite.  
6. Audit `BACKUP_RESTORE` Source=Console.  
7. Khởi động lại; dán `[boot] API SQLite DB path`.

`MES_DB_PATH=/abs/ccl_mes.db` nếu không phải layout mặc định.

SQL Server: không dùng script này — SSMS / `BACKUP DATABASE`.

## Bằng chứng SHA

Sau restore hoặc trước migration (kèm `cmes-migration-abc`):

- API **dừng** hoặc đã `wal_checkpoint(TRUNCATE)` rồi mới `shasum`.  
- Nhật ký commit được (L65): **không** để bản `.db` bằng chứng ở `/tmp`.
  Đặt `data/Backup/SQLite/` (gitignore) + file `.md` rowcount/sha.

## Checklist drill (1 lần/quý)

- [ ] Snapshot mới tồn tại + size > 0  
- [ ] Restore lên **bản copy** `/tmp` hoặc máy lab, không phải xưởng  
- [ ] Rowcount WorkOrders / Users / AuditLogs khớp snapshot  
- [ ] Off-site rsync + sha khớp (nếu NAS đã cấu hình)

## Do NOT

- Nút Restore trên web (cố ý không có).  
- `cp` live `.db` lúc API chạy rồi gọi là backup.  
- Restore khi PID còn listen :5100.  
- Bỏ qua pre-restore backup khi script báo lỗi giữa chừng (exit 7).
