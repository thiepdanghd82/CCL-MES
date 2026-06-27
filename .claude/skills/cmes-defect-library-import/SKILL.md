---
name: cmes-defect-library-import
description: >
  Thêm một bảng MASTER DATA tham chiếu (vd thư viện hạng mục kiểm / defect library)
  scope theo process + import idempotent từ CSV vào CCL-MES (.NET 10 · EF Core · SQLite),
  kèm mở rộng ReasonCode. USE WHEN: cần dựng/mở rộng thư viện lỗi QC, bảng danh mục
  scope theo process/sản phẩm, hoặc importer CSV idempotent kiểu import_npi cho CMES.
license: Proprietary - CCL Design internal
---

# Thêm master-data + importer idempotent cho CMES

Đóng gói cách làm Phương án C · Bước 1 (`CheckItemLibrary`). Tái dùng cho mọi bảng
tham chiếu mới cần seed/import từ CSV, idempotent, đúng quy ước repo.

## Khi nào dùng
- Thêm bảng danh mục/thư viện (master data) đọc từ CSV/xlsx.
- Cần import idempotent (chạy 2 lần ra cùng kết quả) + có thể update khi import bản sửa.
- Scope dữ liệu theo process line / sản phẩm; mở rộng `ReasonCode` (Kind=Scrap).

## Các bước chuẩn
1. **Entity** ở `src/CCL.MES.Domain/Entities/<Name>.cs : BaseEntity` — có **natural key** (vd `ItemId`)
   để upsert idempotent. Field như cột CSV; giữ chuỗi gốc cho field mơ hồ (Severity…).
2. **EF config** ở `src/CCL.MES.Infrastructure/MesDbContext.cs`: thêm `DbSet<>` +
   `b.Entity<T>().HasIndex(x => x.NaturalKey).IsUnique()` + index lookup (vd `(ProcessLine, QcStage)`).
3. **Migration** (CLAUDE.md §4 — BẮT BUỘC):
   - `cp Migrations/MesDbContextModelSnapshot.cs /tmp/snapshot-pre-<name>.cs`
   - `MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=/tmp/<name>-design.db" dotnet ef migrations add <Name> -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web -o Migrations`
   - **Strip type-affinity** trong file `.cs`: `sed -i '' 's/type: "[A-Za-z0-9]*", //g' <migration>.cs` (migration repo có 0 `type:`).
   - Verify trên isolated DB: `MES_CONNSTR="Data Source=/tmp/<name>-verify.db" dotnet ef database update ...` → `sqlite3 ... ".schema <Table>"`.
   - KHÔNG `dotnet ef migrations remove` (revert live DB — §4.1).
4. **Parser** thuần ở `src/CCL.MES.Application/Services/<Name>Csv.cs`: RFC-4180 quote-aware (ô đa dòng/dấu phẩy),
   bỏ BOM + header + dòng khóa rỗng, map theo **vị trí cột** (đối soát file thật trước).
5. **Seeder idempotent** ở `DbSeeder.cs`: upsert theo natural key — so field trước khi set
   (`ApplyRow` trả `changed`) để re-run = 0 update; mở rộng `ReasonCode` theo HashSet code đã có.
   Hook vào `SeedAsync` best-effort (resolve path: env `MES_*_CSV` > walk-up tìm file).
6. **Ops importer** `tools/import_<name>.py` (mẫu `import_npi.py`): SQLite UPSERT
   `ON CONFLICT(<key>) DO UPDATE SET ... WHERE "Col" IS NOT excluded."Col" OR ...` (loại UpdatedAt
   khỏi điều kiện) → re-run `0/0/0`; in BEFORE/AFTER + counters làm audit.
7. **Test** `tests/CCL.MES.Tests/Integration/<Name>SeederTests.cs` (IsolatedDbFixture): khóa số lượng từ
   file thật + chạy seed 2 lần (lần 2 NOOP) + reseed-update-1-field + parser unit.

## Code anchors
- BaseEntity: `src/CCL.MES.Domain/Entities/BaseEntity.cs`
- Mẫu seeder idempotent: `DbSeeder.SeedReasonCodesAsync` / `SeedCheckItemLibraryAsync`
- Mẫu importer: `tools/import_npi.py` · `tools/import_qc_library.py`
- Mẫu migration §4: `CCL-MES/CLAUDE.md` §4.3–4.5 · `ef-migrate.sh`
- IsolatedDbFixture: `tests/CCL.MES.Tests/Integration/_Support/IsolatedDbFixture.cs`

## Checklist verify
- [ ] `dotnet build src/CCL.MES.Infrastructure` xanh
- [ ] Migration `.cs` có **0** `type: "` ; `.schema` đúng + index unique
- [ ] Seed/import chạy **2 lần** → lần 2 `0/0/0`, không nhân đôi
- [ ] `dotnet test tests/CCL.MES.Tests` xanh, 0 regression
- [ ] Không đụng state-machine / dual-sig / ProfileSnapshotJson

## Lỗi thường gặp
- Quên strip `type:` → lệch với SQL Server gate.
- UPSERT mù → re-run báo update giả (thiếu `WHERE ... IS NOT excluded`).
- Map cột sai vì header CSV đa dòng → luôn `python3 csv.reader` đối soát index trước khi code.
- Trỏ `dotnet ef` vào live DB → dùng `MES_CONNSTR=/tmp/...` (§4).
