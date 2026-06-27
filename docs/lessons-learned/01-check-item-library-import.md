# LL-PAC-01 — Thư viện hạng mục kiểm + import idempotent (Phương án C · Bước 1)

> Tóm tắt 2 phút cho người sau. Chi tiết code: nhánh `feat/phuong-an-C`.

## Bối cảnh & mục tiêu
Phương án C cần một **master data thư viện hạng mục kiểm** (IPQC/FQC/OQC) scope theo
process line (LABEL · DIGITAL · SILK · PRESS_CNC) làm nguồn cho resolver (B3) + auto-sync (B4).
Bước 1: dựng model + import từ `IPQC_Library_CMES_v2.csv` (101 item / 4 line), idempotent.

## Quyết định thiết kế (+ lý do)
- **Bảng mới `CheckItemLibrary`** (quyết định #4) thay vì nhồi vào QcProfileSeed — master data
  có vòng đời riêng (admin sửa/import), còn `ProfileSnapshotJson` của check thì **đóng băng**.
- **Natural key `ItemId`** (unique index) → upsert idempotent theo ItemId (không DELETE+refill
  như import_npi, để giữ Id ổn định + cho phép update field khi import bản sửa).
- **Mở rộng `ReasonCode`** (Kind=Scrap) theo Defect code thư viện (quyết định #4: KHÔNG tạo
  bảng DefectCode mới). 28 defect distinct → 28 ReasonCode.
- **Hai đường import**: seeder C# (`DbSeeder.SeedCheckItemLibraryAsync`, gọi lúc boot, best-effort
  resolve CSV) cho dev + test; `tools/import_qc_library.py` (mẫu import_npi.py) cho ops.

## Cạm bẫy đã gặp & cách sửa
- **Counter "update" giả khi re-run**: SQLite `ON CONFLICT DO UPDATE` luôn ghi → re-run báo
  `updated=101` dù không đổi gì. Sửa: thêm `... DO UPDATE SET ... WHERE "Col" IS NOT excluded."Col" OR ...`
  (loại `UpdatedAt` khỏi điều kiện) → re-run = `0/0/0` thật. Seeder C# dùng `ApplyRow` so field trước khi set.
- **CSV header đa dòng song ngữ + ô có dấu phẩy/xuống dòng** → tự viết parser RFC-4180 (state machine
  quote-aware), map theo **vị trí cột 0..18** (đã đối soát v2), bỏ BOM + dòng ItemId rỗng.
- **Migration SQLite**: theo CLAUDE.md §4 — generate trên isolated `/tmp` DB (KHÔNG trỏ live),
  **strip `type:`** khỏi `.cs` (giữ cổng SQL Server provider-agnostic, khớp practice migration gần nhất).
- **`ReasonCode.Kind` lưu STRING** (EF `.HasConversion<string>()` → "Scrap"), KHÔNG phải int. Python tool ban
  đầu dùng `Kind=1` → khi verify trên DB live (đã C#-seed) lộ ra: tạo trùng + sai kiểu. Sửa: `Kind='Scrap'`.
  → **Bài học: query/verify trên DB THẬT sau seed, không chỉ tin /tmp test.** (CLAUDE.md §enum: nhớ HasConversion.)
- **WAL khi copy DB để test tool**: phải copy cả `.db-wal`/`.db-shm` (hoặc checkpoint) — copy mỗi `.db`
  thiếu ghi gần nhất → "bảng chưa tồn tại" giả.

## Ranh giới đã giữ
- KHÔNG đụng state-machine / dual-sig / `ProfileSnapshotJson` (Bước 1 chỉ thêm master data + seed).
- Seed **idempotent** (per-row, HashSet/WHERE) — đúng pattern DbSeeder hiện có.
- Migration up/down sạch (Down DROP TABLE), type-affinity stripped.

## Cơ chế chặn tái phát (test — fail CI nếu vi phạm)
`tests/CCL.MES.Tests/Integration/QcCheckLibrarySeederTests.cs`:
- `Real_library_parses_to_101_items_across_4_lines` — khóa số lượng 101 + (LABEL34/DIGITAL15/SILK25/PRESS_CNC27).
- `Seed_is_idempotent_and_extends_reason_codes` — chạy 2 lần: lần 2 `0/0/0`, không nhân đôi, mọi defect có ReasonCode.
- `Reseed_updates_changed_field_only` — sửa 1 field → đúng 1 update.
- `Parser_handles_quoted_multiline_and_maps_columns` — parser ô đa dòng/dấu phẩy.
- Python tool: chạy 2 lần trên /tmp DB → run1 `101/0/28`, run2 `0/0/0` (output đính kèm acceptance sau).

## Verify
```
dotnet test tests/CCL.MES.Tests --filter FullyQualifiedName~QcCheckLibrary   # 4 xanh
dotnet test tests/CCL.MES.Tests                                              # 960 xanh, 0 regression
python3 tools/import_qc_library.py --csv IPQC_Library_CMES_v2.csv --db <db>  # run2 = 0/0/0
```

## File chạm
- `src/CCL.MES.Domain/Entities/CheckItemLibrary.cs` (new)
- `src/CCL.MES.Infrastructure/MesDbContext.cs` (DbSet + index)
- `src/CCL.MES.Infrastructure/Migrations/*_AddCheckItemLibrary.cs` (new, type-stripped)
- `src/CCL.MES.Application/Services/QcCheckLibraryCsv.cs` (new — parser + row)
- `src/CCL.MES.Infrastructure/DbSeeder.cs` (SeedCheckItemLibraryAsync + hook)
- `tools/import_qc_library.py` (new — ops importer)
- `tests/CCL.MES.Tests/Integration/QcCheckLibrarySeederTests.cs` (new)
