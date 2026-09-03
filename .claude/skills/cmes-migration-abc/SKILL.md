---
name: cmes-migration-abc
description: >
  Protocol A→B→C bắt buộc cho MỌI thay đổi schema trong CCL-MES (thêm/sửa
  entity, cột, index, migration EF Core + SQLite). Bao gồm lệnh cấm tuyệt
  đối, cách generate migration trên DB cô lập, strip type-affinity, và bằng
  chứng phải dán. Dùng khi chạm Entities/, Migrations/, hoặc DbContext.
---

# CMES migration A→B→C

**Rule (enforced):** không có thay đổi schema nào được chạm live DB trước
khi đã chạy trọn vẹn trên DB cô lập ở `/tmp`.

## Hai lệnh CẤM TUYỆT ĐỐI

```
dotnet ef migrations remove      # ❌ connect live DB, chạy Down() THẬT
dotnet ef migrations add  <trỏ appsettings mặc định>   # ❌ inspect schema live
```

Sự cố có thật (2026-05-31, Bước 6.5): `ef migrations remove` đã **DROP TABLE
AuditLogs** trên SQLite live + xoá một dòng `__EFMigrationsHistory`. Phải
restore từ backup byte-identical. Undo migration = `rm` file thủ công +
`git checkout` snapshot. Không có ngoại lệ.

## Phase A — chụp baseline (trước khi gõ bất cứ thứ gì)

> **KHÔNG để backup ở `/tmp`** (L65). macOS dọn `/tmp`; bằng chứng thì phải sống
> lâu hơn phiên làm việc. P12 làm đúng Phase A nhưng backup đặt ở `/tmp` — vài
> ngày sau cả ba bản đã biến mất, migration thì đã nằm trên DB thật.

```bash
TS=$(date +%Y%m%d-%H%M%S)
cp data/ccl_mes.db data/Backup/SQLite/ccl_mes.db.before-<step>.$TS   # đã .gitignore
shasum -a 256 data/ccl_mes.db data/Backup/SQLite/ccl_mes.db.before-<step>.$TS
sqlite3 data/ccl_mes.db "SELECT 'WorkOrders', COUNT(*) FROM WorkOrders
                         UNION ALL SELECT 'WoLegs', COUNT(*) FROM WoLegs;"
sqlite3 data/ccl_mes.db "PRAGMA integrity_check; PRAGMA foreign_key_check;"
cp src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs \
   /tmp/snapshot-pre-<name>.cs     # snapshot code — /tmp được, có git lo
```

**Bắt buộc: chép SỐ LIỆU vào một nhật ký commit được** —
`CCL-MES-Hybrid/docs/pNN-migration-log.md`: đường dẫn backup · sha256 (live phải
khớp backup) · rowcount trước/sau · `integrity_check` · số `foreign_key_check`
trước/sau. File `.db` không vào git; **file nhật ký thì có**. Mẫu:
`CCL-MES-Hybrid/docs/p12-migration-log.md`.

## Phase B — generate + verify trên DB CÔ LẬP

```bash
rm -f /tmp/<name>-design.db
MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=/tmp/<name>-design.db" \
  dotnet ef migrations add <Name> \
  -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web -o Migrations --no-build

cat src/CCL.MES.Infrastructure/Migrations/*<Name>.cs     # đọc TRƯỚC khi apply

MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=/tmp/<name>-design.db" \
  dotnet ef database update -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web --no-build
sqlite3 /tmp/<name>-design.db ".schema <NewTable>"
```

**Strip type-affinity (bắt buộc):** xoá mọi `type: "TEXT|INTEGER|REAL"` và
`.HasColumnType(...)` khỏi migration mới — giữ cổng SQL Server provider-agnostic.

## Phase C — áp live + chứng minh

Chỉ sau khi B xanh. Bằng chứng phải dán vào PR:
`.schema` trước/sau · rowcount trước/sau · SHA256 · `__EFMigrationsHistory` mới.

## Bẫy SQLite đã trả giá

- **RowVersion per-row (L38):** dùng `.IsConcurrencyToken().ValueGeneratedNever()`
  + trigger `randomblob(8)`. **KHÔNG** `.IsRowVersion()` (EF bỏ giá trị lúc
  INSERT → NOT NULL fail). **KHÔNG** thêm default qua `AlterColumn` (SQLite
  rebuild bảng → **DROP mọi trigger**, và EF còn reorder `Sql(CREATE TRIGGER)`
  chạy *trước* rebuild).
- **Partial unique index**: SQLite hỗ trợ `WHERE`, dùng cho per-leg uniqueness.
- Cột thêm mới trên bảng đã có dữ liệu ⇒ **nullable** hoặc có default, không
  `NOT NULL` trần.

## Checklist

- [ ] Phase A backup + SHA + rowcount đã ghi lại
- [ ] Migration generate trên `/tmp/*.db`, **không** trỏ live
- [ ] Đã đọc nội dung file migration trước khi apply
- [ ] Type-affinity đã strip
- [ ] Additive: cột mới nullable / có default; **không** đổi nghĩa cột cũ
- [ ] Rollback path = `rm` + `git checkout` snapshot (viết rõ trong PR)
- [ ] `bash CCL-MES-Hybrid/scripts/gate-all.sh` xanh
- [ ] Bằng chứng Phase C đã dán

## Do NOT

- Chạy bất kỳ lệnh `ef` nào mà không set `MES_CONNSTR` trỏ `/tmp`.
- Dùng `ef migrations remove` để undo — kể cả khi "chắc chắn an toàn".
- Đổi giá trị số của enum đã lên production (append cuối, luôn luôn).
