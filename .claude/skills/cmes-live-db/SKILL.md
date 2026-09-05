---
name: cmes-live-db
description: >
  Pin và xác minh SQLite nhà máy CCL-MES. Dùng khi chạy API, nghiệm thu,
  sửa dữ liệu, RCA "sửa rồi mà UI không đổi", đặt MES_DB_PATH / MES_DATA_DIR,
  hoặc đụng data/ccl_mes.db. Cấm đoán file DB từ cổng hay tên tiến trình.
---

# CMES live DB — đúng file, đúng WAL

**Rule:** trước mọi nghiệm thu trên "DB thật", đọc dòng boot
`[boot] API SQLite DB path` của **tiến trình đang listen**. Không suy từ
tên file trên đĩa, không suy từ `:5100` đang mở.

Nguồn sự thật: `CCL-MES-Hybrid/docs/BAN-GIAO-2026-08-19.md` (ba bẫy).

## Hai file — một cái là mìn

| File | Vai trò |
|---|---|
| `data/ccl_mes.db` | **DB THẬT** (xưởng) |
| `data/demo/p11-tape-demo.db` | Bản thử. **Cấm** `MES_DB_PATH` prod/staging |

File 0 byte `CCL-MES-Hybrid/data/ccl_mes.db` là stub. API từng dừng walk
tại đây → login 500 `no such table: Users`. Bỏ qua file `Length == 0`.

## Env (ưu tiên cao → thấp)

1. `ConnectionStrings:Default` (test factory)  
2. `MES_DB_PATH` = file tuyệt đối  
3. `MES_DATA_DIR` + `ccl_mes.db`  
4. Walk ancestor tìm `data/ccl_mes.db` **đã có và > 0 byte**  
5. Fallback tạo mới — chỉ fresh clone

Launchd / script nhà máy **phải** set `MES_DB_PATH` tường minh.

## WAL — `shasum` file `.db` không phải vân tay

`journal_mode = wal`. API đang chạy: ghi mới nằm `ccl_mes.db-wal`.
`cp` / `shasum -a 256 data/ccl_mes.db` lúc serving **bỏ sót WAL**.

```bash
# Chỉ khi đã hiểu hệ quả; tốt hơn: tắt API rồi:
sqlite3 data/ccl_mes.db "PRAGMA wal_checkpoint(TRUNCATE);"
# Sao lưu an toàn (API dừng hoặc connection mode=ro):
sqlite3 data/ccl_mes.db ".backup '/abs/path/ccl_mes.backup.db'"
```

**KHÔNG** `cp data/ccl_mes.db ...` khi Kestrel còn giữ file.

## App frozen :5050

Không boot Blazor Server "để lấy bằng chứng" trên live DB.
`SpecTrashPurgeService` xóa hồ sơ khi start. Launcher đòi
`MES_LEGACY_WEB_FORCE=1`. Gate: `MES_LEGACY_WEB_DRYRUN=1`.

## Checklist nghiệm thu

- [ ] Dán nguyên dòng `[boot] API SQLite DB path : .../data/ccl_mes.db`
- [ ] Path **không** chứa `demo/` hay `CCL-MES-Hybrid/data/` stub
- [ ] `lsof -nP -iTCP:5100 -sTCP:LISTEN` khớp PID vừa đọc log
- [ ] Không `cp` live DB giữa chừng API

## Do NOT

- `MES_DB_PATH=.../p11-tape-demo.db` rồi tuyên bố "10 route 200".
- Checkpoint WAL trên live rồi quên API đang ghi tiếp.
- Tạo `ccl_mes.db` rỗng gần API hơn repo-root.
