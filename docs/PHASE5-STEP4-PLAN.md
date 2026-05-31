# Phase 5 — Bước 4: EF Migrations cho SQLite (KHẢO SÁT + PHƯƠNG ÁN)

> **Trạng thái: KHẢO SÁT (read-only).** Chưa code, chưa tạo branch.
> Đây là **bước rủi ro mất data CAO NHẤT** trong Phase 5 vì đụng cơ chế khởi tạo DB.
> Sau khi chọn phương án em sẽ tạo `feat/phase5-ef-migrations` để triển khai.

---

## 1. Khảo sát hiện trạng

### 1.1 Hai nhánh khởi tạo DB hiện tại

| File:line | Trích |
|---|---|
| `src/CCL.MES.Web/Program.cs:96-109` | Comment "// SQLite dev: EnsureCreated() / SQL Server prod: Migrate()" |
| `src/CCL.MES.Web/Program.cs:103-106` | `if (provider == "SqlServer") db.Database.Migrate(); else db.Database.EnsureCreated();` |

- **SQLite (dev)** → `EnsureCreated()`: tạo schema thẳng từ ModelSnapshot, **bỏ qua hoàn toàn cơ chế migrations** + **KHÔNG ghi vào `__EFMigrationsHistory`**.
- **SQL Server (prod)** → `Migrate()`: yêu cầu thư mục `Migrations/` phải có sẵn migration; áp dụng tuần tự + ghi history.

### 1.2 Hạ tầng migration đã chuẩn bị sẵn (nhưng chưa generate)

| File:line | Vai trò |
|---|---|
| `src/CCL.MES.Infrastructure/MesDbContextFactory.cs:14-32` | `IDesignTimeDbContextFactory<MesDbContext>` đã có — `dotnet ef` chạy được. Đọc `MES_PROVIDER` env (Sqlite mặc định). |
| `src/CCL.MES.Infrastructure/DependencyInjection.cs:22-28` | Cả 2 nhánh provider đều set `b.MigrationsAssembly("CCL.MES.Infrastructure")` → migration sẽ landed đúng project. |
| `src/CCL.MES.Infrastructure/CCL.MES.Infrastructure.csproj:10` | `Microsoft.EntityFrameworkCore.Design@10.0.8` đã có (cần cho design-time tools). |
| `ef-migrate.sh:1-35` | Script tiện ích — hiện chỉ `MES_PROVIDER=SqlServer`, gọi `dotnet ef migrations add Init` nếu chưa có. SQLite branch không có. |

### 1.3 Schema hiện tại trên DB

```
$ sqlite3 ccl_mes.db "SELECT name FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'"
(empty — table absent)

$ sqlite3 ccl_mes.db ".tables"
Customers, DowntimeReasons, Machines, ManufacturingStructures, ProductionLogs,
Products, QcInspections, QcResultDetails, RawMaterials, RoutingOperations,
SpecParameters, SpecVersions, Specs, Users, WiStepDetails, WoStatusHistories,
WorkCenters, WorkInstructions, WorkOrders, sqlite_sequence
```

- **19 entity table** khớp 19 `DbSet<T>` trong `MesDbContext.cs:11-29`.
- **`__EFMigrationsHistory` KHÔNG tồn tại** — chứng minh DB này đi qua `EnsureCreated()`, chưa từng dính migrations.
- `sqlite_sequence` = SQLite auto helper, không phải entity.

### 1.4 Entity model (19 DbSet → `MesDbContext.OnModelCreating`)

- 10 enum-to-string conversion: WO.CurrentStep, WO.Status, SpecVersion.Status, QcInspection.Type, QcInspection.Result, WoStatusHistory.FromStep, WoStatusHistory.ToStep, Machine.CurrentState, ProductionLog.EventType, WorkInstruction.Status + ProcessStep.
- 6 index: WO.WoNo (unique), Machine.Code (unique), WorkCenter.Code, RawMaterial.PartNo, RoutingOperation.PartNo, ManufacturingStructure.ParentPart, User.Username (unique).
- 2 Ignore: `WorkOrder.LastQc`, `ProductionLog.DurationMinutes`.

### 1.5 Tooling state

```
$ dotnet ef --version
(error: dotnet-ef does not exist)
```

`dotnet-ef` **chưa cài** trên dev box hiện tại. Implementation step sẽ cần: `dotnet tool install --global dotnet-ef --version 10.*`.

### 1.6 Data hiện có trên `ccl_mes.db` (rủi ro mất nếu drop/recreate)

| Bảng | Row count |
|---|---|
| WorkCenters | 43 |
| RawMaterials | 2 127 |
| RoutingOperations | 38 441 |
| ManufacturingStructures | 20 530 |
| Users | 2 (admin + operator) |
| WorkOrders | 1 (seed WO-26-3683) |

**Tổng ~61k row NPI thật** + 1 WO mẫu + 2 user demo. **Mất sẽ phải re-import 4 bảng NPI từ Data/** (Phase 1 đã làm — không muốn lặp lại).

---

## 2. Vấn đề cốt lõi: baseline existing DB

`EnsureCreated()` tạo schema KHÔNG ghi gì vào `__EFMigrationsHistory`. Nếu giờ ta:

1. Generate `Init` migration (chỉ tạo file C# trong `Migrations/`, không chạm DB).
2. Đổi `Program.cs` → gọi `db.Database.Migrate()` cho cả SQLite.
3. Restart app trên DB hiện tại → EF đọc `__EFMigrationsHistory` (không có) → coi Init là **chưa áp dụng** → thử `CREATE TABLE Customers ...` → **FAIL** vì table đã tồn tại.

Đây chính là **rủi ro mất data cao nhất**: nếu xử lý sai, restart sẽ crash hoặc tệ hơn — nếu có ai đó "fix" bằng cách drop tables, mất 61k row NPI.

→ Cần **baseline strategy** = đánh dấu Init là "đã áp dụng" trên DB hiện có để EF skip CREATE TABLE.

---

## 3. Phương án (3 lựa chọn)

### Phương án A — Generate Init + Baseline detect-and-insert + Migrate ⭐ đề xuất

**Cách làm**:

1. **Cài tooling**: `dotnet tool install --global dotnet-ef --version 10.*` (1 lần / dev box).

2. **Generate Init migration** (chỉ tạo file C#, không chạm DB):
   ```bash
   MES_PROVIDER=Sqlite dotnet ef migrations add Init \
     -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web -o Migrations
   ```
   Sản phẩm: 3 file mới
   - `src/CCL.MES.Infrastructure/Migrations/<ts>_Init.cs` (~600 LOC auto-gen: `CreateTable` cho 19 table + 6 index)
   - `src/CCL.MES.Infrastructure/Migrations/<ts>_Init.Designer.cs` (auto)
   - `src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs` (auto)

3. **Thêm baseline helper** — `src/CCL.MES.Infrastructure/DbInitializer.cs` (mới, ~50 LOC):
   ```csharp
   public static class DbInitializer
   {
       /// <summary>
       /// Migrate-with-baseline: nếu DB đã có tables nhưng chưa có
       /// __EFMigrationsHistory, INSERT baseline cho mọi migration hiện
       /// có rồi mới Migrate(). New install + existing install đều an toàn.
       /// </summary>
       public static async Task InitializeAsync(MesDbContext db)
       {
           var historyExists = await TableExistsAsync(db, "__EFMigrationsHistory");
           var anyEntityTable = await TableExistsAsync(db, "Customers");

           if (anyEntityTable && !historyExists)
           {
               // Existing DB từ EnsureCreated() — baseline insert
               await db.Database.ExecuteSqlRawAsync(
                   "CREATE TABLE __EFMigrationsHistory (" +
                   "  MigrationId TEXT NOT NULL PRIMARY KEY," +
                   "  ProductVersion TEXT NOT NULL);");
               foreach (var m in db.Database.GetMigrations())
               {
                   await db.Database.ExecuteSqlInterpolatedAsync(
                       $"INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) " +
                       $"VALUES ({m}, '10.0.8')");
               }
           }

           // Sau baseline: pending = 0 → Migrate() no-op.
           // New install: history rỗng → Migrate() tạo tất cả.
           await db.Database.MigrateAsync();
       }

       private static async Task<bool> TableExistsAsync(MesDbContext db, string name)
       {
           var conn = db.Database.GetDbConnection();
           if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
           using var cmd = conn.CreateCommand();
           cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name=@n LIMIT 1";
           var p = cmd.CreateParameter(); p.ParameterName = "@n"; p.Value = name; cmd.Parameters.Add(p);
           var result = await cmd.ExecuteScalarAsync();
           return result is not null;
       }
   }
   ```

4. **Sửa `Program.cs:103-106`** — bỏ branching, gọi DbInitializer:
   ```csharp
   await DbInitializer.InitializeAsync(db);
   await DbSeeder.SeedAsync(db);
   await SeedAdminUserAsync(...);
   ```

5. **Xử lý SQL Server**: để nguyên `ef-migrate.sh` (SQL Server-only). Trên SQL Server, `DbInitializer.InitializeAsync` cũng chạy được vì:
   - SQL Server table-exists query khác (`INFORMATION_SCHEMA.TABLES`) — sẽ cần if/else theo provider HOẶC dùng `db.Database.IsSqlite()` / `IsSqlServer()` extension.
   - Nhưng SQL Server thực tế chưa được deploy bao giờ — có thể defer baseline-for-SqlServer sang sprint sau, hiện tại chỉ chạy SQLite branch.

   **Đơn giản hơn**: dùng `db.Database.GetService<IRelationalDatabaseCreator>().Exists()` + check sqlite-specific cho table; SQL Server branch giữ `Database.Migrate()` thuần (vì SQL Server prod chưa có DB → migrate sẽ tạo từ đầu, đúng).

6. **README update** §6 (Chuyển sang SQL Server): không cần đổi lớn, vẫn flow cũ. Thêm §6f nói "SQLite cũng dùng Migrations từ Phase 5".

7. **`ef-migrate.sh`** mở rộng: thêm SQLite branch optional, hoặc tách `ef-migrate-sqlite.sh` riêng. Đề xuất: 1 file `ef-migrate.sh` 2 mode, switch qua arg `--sqlite | --sqlserver`.

**Ưu**:
- **Idempotent**: chạy lại trên DB nào cũng OK — existing DB nhận baseline, new DB migrate sạch.
- DB hiện tại 61k row NPI **GIỮ NGUYÊN** — không có lệnh nào drop/recreate.
- Mở đường cho migrations tương lai (vd Phase 6+ thêm field) — chỉ cần `dotnet ef migrations add <Name>` + restart app.
- Tooling chính thống của EF Core, không hack.

**Nhược / rủi ro**:
- **HIGH-RISK step**: nếu SQL trong DbInitializer sai (vd schema cột không khớp), restart sẽ crash. Mitigation: backup tường minh trước, test trên copy DB trước khi áp main DB.
- Auto-gen migration file (~600 LOC) cần review trước khi commit — đảm bảo khớp chính xác schema hiện tại (nếu lệch dù 1 column type, Migrate sau-baseline sẽ generate ALTER bất ngờ).
- `dotnet-ef` chưa cài → step 1 cần manual install (không tự động trong dev box).

**Độ phức tạp**: ⭐⭐⭐ (3/5)
**LOC ước tính**: ~50 hand-written (DbInitializer) + ~5 Program.cs + ~10 ef-migrate.sh + ~600 auto-gen migration C#

---

### Phương án B — Two migration sets (Migrations/Sqlite/ + Migrations/SqlServer/)

**Cách làm**:
- Tạo 2 thư mục migration riêng theo provider.
- Cần custom `MigrationsContext` cho từng provider hoặc dùng `DbContext` constructor riêng — phức tạp setup.
- Mỗi khi schema đổi → phải generate 2 lần.

**Ưu**:
- "Đúng sách" cho multi-provider.
- Mỗi provider có migration SQL tối ưu.

**Nhược / rủi ro**:
- Nhân đôi maintenance.
- SQL Server prod **chưa từng deploy** — over-engineering cho usecase chưa có.
- Setup phức tạp (DI cho 2 ModelSnapshot).
- LOC + thời gian gấp đôi.

**Độ phức tạp**: ⭐⭐⭐⭐ (4/5)
**LOC**: ~120 hand-written + 1200 auto-gen.

→ Defer phương án này sang sprint khi thực sự deploy SQL Server.

---

### Phương án C — `EnsureCreated()` legacy fallback + Migrate cho install mới

**Cách làm**:
- Detect: nếu `__EFMigrationsHistory` tồn tại → `Migrate()`.
- Nếu không tồn tại nhưng tables tồn tại → giữ `EnsureCreated()` no-op (DB đã có schema), KHÔNG generate Init.
- Chỉ install mới (DB không tồn tại) mới chạy `Migrate()` qua migration mới.

**Ưu**:
- Đỡ phải baseline existing DB.
- Existing dev box không bị đụng.

**Nhược / rủi ro**:
- **Anti-pattern**: prod existing DB không bao giờ nhận migration tương lai → schema drift theo thời gian.
- Phase 6+ thêm column sẽ phải document "operator phải drop DB" — quay lại đúng vấn đề `EnsureCreated()` ban đầu.
- Không đóng được TODO này cho dài hạn.

**Độ phức tạp**: ⭐⭐ (2/5)
**LOC**: ~30 + 600 auto-gen.

→ Đề xuất KHÔNG dùng — không giải quyết gốc rễ.

---

## 4. Đề xuất

**Chọn Phương án A** (Generate Init + baseline detect-and-insert + Migrate).

Lý do:
1. **Mở đường tương lai**: bất kỳ schema change nào sau Phase 5 (Phase 6+ thêm field, đổi index) chỉ cần `dotnet ef migrations add <Name>` rồi restart, không cần ai drop DB.
2. **An toàn cho DB hiện có**: baseline insert chỉ ADD `__EFMigrationsHistory`, không đụng table nghiệp vụ.
3. **Idempotent**: chạy n lần kết quả như chạy 1 lần.
4. **Tooling chuẩn EF Core**: không hack, dễ đọc cho dev tương lai.
5. **SQL Server giữ nguyên flow**: chưa deploy prod, để dạng prepared (`ef-migrate.sh` cũ).

Phương án B defer; Phương án C không khuyến nghị.

---

## 5. Rủi ro mất data (HIGH) + mitigation

| Hạng mục | Rủi ro | Mức | Giảm thiểu |
|---|---|---|---|
| Migrate() sau baseline lệch schema → ALTER table bất ngờ | DB hiện tại có thể có nullable/index nhỏ khác auto-gen migration | **CAO** | Trước khi áp main DB, áp lên `ccl_mes.db.bak.test-step4` (copy của main); verify row count + `.schema` `==` trước/sau |
| Lỗi SQL trong DbInitializer (cú pháp SQLite) | Restart crash app | **TRUNG BÌNH** | Test trên copy DB; chỉ chạy main sau khi pass copy |
| `dotnet ef migrations add` đụng entity layout sai | Auto-gen migration thiếu/thừa cột | **TRUNG BÌNH** | Review thủ công file Init.cs trước khi commit; cross-check với `.schema <table>` từng table |
| Backup quên → mất 61k row NPI nếu fix sai | DR | **CAO** | **TƯỜNG MINH**: backup `ccl_mes.db.bak.phase5migr-<ts>` TRƯỚC bất kỳ smoke test nào; verify MD5 |
| Restart fail do migration history sai format | App không boot | **THẤP** | History row format = `(MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL)` — EF docs spec rõ; test khôi phục backup nếu fail |
| SQL Server branch bị break | Prod gãy | **THẤP** | SQL Server chưa deploy prod bao giờ, giữ flow Migrate() cũ + DbInitializer skip-if-not-sqlite |
| Phase 1-4 i18n/RBAC/hub-auth/error-code bị ảnh hưởng | Cross-phase regression | **THẤP** | Smoke 5 chức năng + verify NPI rows post-migrate |

**Mitigation tổng thể**:
1. Backup DB tường minh + verify SHA256 trước smoke.
2. Test migration baseline trên **copy DB** (`cp ccl_mes.db ccl_mes.db.testcopy` → apply → verify) trước khi áp main.
3. Verify row count 4 bảng NPI + Users post-restart KHÔNG đổi.
4. Restart proof: kill server, restart, verify `__EFMigrationsHistory` chứa Init row + DB serve `/dashboard` 200 OK.
5. Rollback runbook (trong PR description): nếu fail, `cp ccl_mes.db.bak.phase5migr-<ts> ccl_mes.db` + revert PR + restart.

---

## 6. Kế hoạch test + DoD

### Smoke test (manual + sqlite3 verify)

| # | Bước | Kỳ vọng |
|---|---|---|
| 1 | `dotnet tool install --global dotnet-ef --version 10.*` | Cài thành công |
| 2 | `MES_PROVIDER=Sqlite dotnet ef migrations add Init -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web -o Migrations` | Tạo 3 file trong `Migrations/` |
| 3 | Manual review `<ts>_Init.cs` — cross-check 19 CreateTable + 6 CreateIndex khớp schema hiện tại | Khớp 100% |
| 4 | `dotnet build` | 0 warning, 0 error |
| 5 | **Backup tường minh**: `cp src/CCL.MES.Web/ccl_mes.db backup-phase5migr.db` + verify SHA256 | Hash lưu lại trong PR description |
| 6 | Test trên **copy DB**: `cp ccl_mes.db ccl_mes.db.testcopy` → set connection string → start app → check `__EFMigrationsHistory` được tạo + insert 1 row + 4 bảng NPI count không đổi | PASS trước khi áp main |
| 7 | Áp main: start app trên `ccl_mes.db` thật → log "Migrate" không có CREATE TABLE → app boot OK | App listen :5080 |
| 8 | Verify `__EFMigrationsHistory` post-migrate: `SELECT * FROM __EFMigrationsHistory` | 1 row: `<ts>_Init, 10.0.8` |
| 9 | Verify NPI rows post-migrate | 43/2127/38441/20530/2 — KHÔNG ĐỔI |
| 10 | Restart app lần 2 → boot OK, Migrate no-op | App listen :5080, log "no pending migrations" |
| 11 | Smoke Phase 1-4: login admin → /dashboard 200, /workorders 200, advance fail → "Cannot advance: <localized>" | Tất cả PASS |
| 12 | Forbidden dirs | Ops Control v1.2, CMES, Old ver, SpecHub không bị đụng |

### Definition of Done (DoD)

- [ ] `Migrations/Init.cs` + Designer + ModelSnapshot tồn tại trong `CCL.MES.Infrastructure`.
- [ ] `DbInitializer.cs` mới — baseline detect + insert + Migrate orchestration.
- [ ] `Program.cs` bỏ branching `EnsureCreated/Migrate`, gọi `DbInitializer.InitializeAsync(db)`.
- [ ] `dotnet build` clean.
- [ ] Test trên copy DB PASS → áp main DB PASS.
- [ ] `__EFMigrationsHistory` tồn tại + chứa 1 row Init post-migrate.
- [ ] Restart lần 2 no-op (chứng minh history hoạt động).
- [ ] NPI 43/2127/38441/20530 + Users=2 **không đổi**.
- [ ] Phase 1-4 smoke (login + hub auth + error-code i18n) vẫn PASS.
- [ ] PR `feat/phase5-ef-migrations` base = `feat/phase5-error-codes` (stack tiếp).
- [ ] STOP, báo cáo, chờ duyệt.

---

## 7. Files dự kiến đụng

| File | Hành động | Ước LOC |
|---|---|---|
| `src/CCL.MES.Infrastructure/Migrations/<ts>_Init.cs` *(auto-gen)* | 19 CreateTable + 6 CreateIndex + enum-string conversions | ~600 |
| `src/CCL.MES.Infrastructure/Migrations/<ts>_Init.Designer.cs` *(auto-gen)* | Snapshot designer | ~300 |
| `src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs` *(auto-gen)* | Model snapshot | ~300 |
| `src/CCL.MES.Infrastructure/DbInitializer.cs` *(mới)* | Baseline detect + Migrate orchestration | ~50 |
| `src/CCL.MES.Web/Program.cs:96-109` | Bỏ branching, gọi `DbInitializer.InitializeAsync(db)` | ~5 |
| `ef-migrate.sh` *(option)* | Mở rộng cho cả 2 provider qua arg | ~20 |
| `README.md` §6 | Thêm note Phase 5: SQLite cũng Migrations | ~10 |

**Tổng**: ~50-100 hand-written + ~1200 auto-gen. Không đụng entity, không đổi business logic, không đụng RBAC/auth/SignalR.

---

## 8. Branch base

**Đề xuất stack tiếp** trên `feat/phase5-error-codes`:
- Cả 4 PR (#4 RBAC, #5 hub auth, #6 error codes, #7 EF migrations) gắn chuỗi.
- Merge thứ tự cuối Phase 5: PR #4 → #5 → #6 → #7 vào main.

Lý do: PR #4 đã merged main. PR #5/#6 stack. PR #7 không đụng files của #5/#6 (chỉ Infrastructure + Program.cs) nên cũng có thể branch từ main, nhưng stack giữ chuỗi tuyến tính + audit trail rõ.

---

## 9. Câu hỏi cho em duyệt

1. **Chọn phương án nào?** (Đề xuất: A — generate + baseline)
2. **Branch base**: stack trên `feat/phase5-error-codes` (đề xuất, tuyến tính) hay từ `main`?
3. **SQL Server branch trong DbInitializer**: skip baseline detection (giả định SQL Server chưa có DB hiện có → cứ `Migrate()` thuần) hay tổng quát hóa cả 2 provider?
4. **`ef-migrate.sh`**: mở rộng 2-mode (`--sqlite | --sqlserver`) hay để nguyên (SQL Server only) + thêm note SQLite migrations đã tự động trong app startup?
5. **Test trên copy DB trước khi áp main**: có làm extra step copy-test này không? (Em đề xuất CÓ — rủi ro mất data cao nhất Phase 5 nên cẩn thận thừa hơn thiếu.)

Sau khi em duyệt 5 mục em tạo branch + code + commit + push + PR + STOP.
