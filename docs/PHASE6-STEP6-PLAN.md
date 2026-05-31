# Phase 6 — Bước 6: Deploy SQL Server verify (Docker) — KHẢO SÁT + PHƯƠNG ÁN

> **Trạng thái: KHẢO SÁT (read-only).** Chưa code, chưa tạo branch.
> Đây là bước **rủi ro môi trường mới CAO** — đụng provider switch, container,
> ETL data cross-engine. Khác Phase 5 Bước 4 (chỉ đụng init logic 1 provider),
> Bước 6 này đụng SQL dialect + tooling + Docker image selection trên ARM Mac.
>
> Sau khi chốt phương án, em sẽ tạo `feat/phase6-sqlserver-verify` để triển khai.
> **Không đụng**: `Ops Control v1.2/`, `CMES/`, `Old ver ( DO NOT USE)/`, `SpecHub/`.

---

## 0. ⚠️ BLOCKER PHÁT HIỆN NGAY — Docker chưa sẵn sàng trên máy

```
$ which docker
docker not found

$ uname -m
arm64
```

- **Docker Desktop chưa cài** trên dev box hiện tại.
- Máy Apple Silicon (ARM64) → chọn image phải cân nhắc (chi tiết §3).

**Ý nghĩa**: trước khi đầu tư công sức triển khai Bước 6, mình cần quyết
một trong 2 nhánh:

| Nhánh | Mô tả | Khi nào chọn |
|---|---|---|
| **A. CÀI DOCKER + LÀM BƯỚC 6 NGAY** | Anh cài Docker Desktop for Mac (free), em làm verify Docker SQL Server theo §3-§7 dưới. | Khi anh sẵn sàng cài 1 GB Docker Desktop + chấp nhận thời gian học tooling. |
| **B. DEFER BƯỚC 6 → LÀM BƯỚC 7 (IQC) TRƯỚC** | Để Bước 6 chờ tới khi có SQL Server instance thật (CCL server / Azure SQL / VM), không phụ thuộc Docker local. Chuyển sang Bước 7 ngay: IQC entity + tab (close stub từ PR #13). | Khi muốn ship business value sớm hơn, không bị block bởi tooling local. |

**Em đề xuất Nhánh B (defer Bước 6 sang Bước 7)** vì:

1. Bước 6 là **proof verify**, không phải production deploy. Giá trị business
   nó tạo ra (= xác nhận provider switch + migration chạy trên SQL Server)
   thấp hơn IQC tab (= module người dùng dùng thật).
2. Khi chuyển sang SQL Server thật ở production, vẫn cần verify lại trên
   đúng SQL Server version + host của khách → Docker verify chỉ là pre-flight,
   không thay thế được verify production.
3. SQLite production-ready cho phạm vi hiện tại (60k+ rows, 5 users, ~100 GB
   ceiling). Chưa có deadline production switch.
4. Source code **đã** provider-agnostic về cấu trúc (§1) — verify Docker
   không khám phá thêm thông tin mới về codebase, chỉ confirm cái đã viết.
5. Rủi ro type-affinity (§2.5) cần fix MÀ KHÔNG cần Docker — fix trên SQLite
   migration trước thì khi có SQL Server thật cũng dùng được luôn.

**Nhưng quyết định là của anh.** Phần còn lại của doc khảo sát đầy đủ phương
án Nhánh A để anh có cơ sở chọn.

---

## 1. Khảo sát hiện trạng — provider switch + DbInitializer

### 1.1 Hai điểm chọn provider

| File:line | Trích | Hoạt động khi |
|---|---|---|
| `src/CCL.MES.Infrastructure/DependencyInjection.cs:16-28` | `var provider = config["Database:Provider"] ?? "Sqlite"; ... if (provider == "SqlServer") o.UseSqlServer(cs, b => b.MigrationsAssembly("CCL.MES.Infrastructure")); else o.UseSqlite(cs, ...);` | Runtime (Web app). Đọc từ `appsettings*.json` qua `IConfiguration`. |
| `src/CCL.MES.Infrastructure/MesDbContextFactory.cs:18-28` | Tương tự nhưng đọc `MES_PROVIDER` + `MES_CONNSTR` từ env. | Design-time (`dotnet ef migrations add ...`). |

**Đánh giá**: ✅ Cấu trúc clean. Không cần đụng 2 file này.

### 1.2 Config file đã chuẩn bị

| File | Provider |
|---|---|
| `src/CCL.MES.Web/appsettings.json` | `Sqlite` mặc định + connection `Data Source=ccl_mes.db`. |
| `src/CCL.MES.Web/appsettings.SqlServer.json` | `SqlServer` + connection `Server=localhost;Database=CCL_MES;Trusted_Connection=True;TrustServerCertificate=True`. **NHƯNG**: `Trusted_Connection=True` không dùng được khi container chạy trên Linux + auth bằng SQL login — Bước 6 phải override. |

**Cần làm**: tạo `appsettings.Docker.json` (hoặc dùng env var `ConnectionStrings__Default`) với SQL login (`User Id=sa;Password=<strong>`), không đụng 2 file kia.

### 1.3 DbInitializer baseline-aware

`DbInitializer.cs:31-65` dùng `IHistoryRepository.GetCreateScript()` +
`GetInsertScript(HistoryRow)` để emit dialect-correct DDL/DML qua EF Core's
relational provider API.

**Test thực tế trên SQLite**: PASS (Phase 5 Bước 4 + Phase 6 Bước 5 boot probe).

**Test thực tế trên SQL Server**: ❌ CHƯA — đây là cái Bước 6 sẽ verify.

Branch logic an toàn cả 2 provider trên giấy:
- New install (no tables, no history) → `Migrate()` thẳng ✓
- Existing install (tables, no history) → baseline insert → `Migrate()` no-op ✓
- Existing install (tables + history) → `Migrate()` apply pending ✓

---

## 2. Khảo sát migration — có provider-specific gì không

### 2.1 EF Core API dùng

Toàn bộ migration code dùng `MigrationBuilder.CreateTable / AddColumn /
CreateIndex / DropTable` — **đều là provider-agnostic API**. Không có raw
`Sql("CREATE TABLE ...")` cross-dialect.

### 2.2 Annotations

| Annotation | File:line | Provider-specific? |
|---|---|---|
| `.Annotation("Sqlite:Autoincrement", true)` | `Init.cs:18-19, 36, 53, 78, ...` | **CÓ — Sqlite namespace**. EF SQL Server provider sẽ ignore (no-op) — không gây lỗi compile/runtime. ✓ |

### 2.3 Foreign keys + indexes + nullability

Tất cả dùng MigrationBuilder.ForeignKey / CreateIndex / Column.nullable —
provider-agnostic. EF tự dịch sang dialect đúng. ✓

### 2.4 EF.Functions.Like

Dùng tại 8 callsite (đã grep):
- `UserAdminService.cs:50-52` (3 LIKE)
- `AuditLogService.cs:38, 53-57` (5 LIKE)

`EF.Functions.Like` → SQLite `LIKE` (case-insensitive ASCII) + SQL Server
`LIKE` (case-sensitive theo collation default `SQL_Latin1_General_CP1_CI_AS`,
nghĩa là **CI** — case-insensitive). Behavior gần tương đương. ✅

### 2.5 ⚠️ **RỦI RO LỚN NHẤT**: Column `type:` strings

Mọi migration column dùng SQLite affinity strings:

```csharp
// Init.cs:18-25 (đặc trưng cho cả 3 migration)
Id = table.Column<long>(type: "INTEGER", nullable: false)
Code = table.Column<string>(type: "TEXT", nullable: false)
IdealCycleTimeSec = table.Column<double>(type: "REAL", nullable: false)
IsActive = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)  // AddUserMustChangeAndIsActive.cs:13-21
Detail = table.Column<string>(type: "TEXT", nullable: true)  // AddAuditLog.cs:26
```

**Vấn đề**: `MigrationBuilder.Column<T>(type: "...")` truyền `type` thẳng
xuống provider. EF Core 10 SQL Server provider sẽ emit literal `TEXT`,
`INTEGER`, `REAL` — đây là **kiểu deprecated từ SQL Server 2005**:
- `TEXT` (SQL Server) = legacy alias nvarchar(max)-ish, **được parser nhận
  nhưng warning + có hành vi khác nvarchar(max)** (không full Unicode +
  không truncate chuẩn).
- `INTEGER` không phải kiểu SQL Server hợp lệ (đúng là `int` hoặc `bigint`).
  Có thể FAIL khi parser từ chối.
- `REAL` SQL Server hợp lệ (= float(24), single precision) — nhưng entity
  dùng `double` (CLR) → mất precision. Đáng lẽ phải emit `float` (= float(53)).

**Có 3 hướng fix**, mỗi cái trade-off khác nhau — em **không quyết, hỏi anh**:

| Option | Mô tả | Ưu | Nhược |
|---|---|---|---|
| **2.5.A. Per-provider migrations** | `Migrations/Sqlite/` + `Migrations/SqlServer/` riêng. Generate lại Init+AddUserMustChange+AddAuditLog cho SqlServer. `MesDbContextFactory` + `DependencyInjection.cs` chọn `MigrationsAssembly` + namespace theo provider. | Sạch nhất. Mỗi migration native dialect đúng. Best practice EF Core. | Phải gen 3 migration mới × 2 file (Up.cs + Designer.cs) = 6 file mới. ModelSnapshot phức tạp hơn (theo provider). Chia branch in solution → cần review kỹ. |
| **2.5.B. Drop explicit `type:` trong existing migrations** | Sửa Init/AddUserMustChange/AddAuditLog: bỏ `type: "TEXT"` v.v., để EF inference per provider. | 1 lần sửa, dùng cả 2 provider. Migration file ngắn hơn. | Hand-edit migration đã apply trên prod-equivalent SQLite (60k rows). DbInitializer baseline-check vẫn OK vì `__EFMigrationsHistory` chỉ check MigrationId (timestamp+name), không check column type strings. **Nhưng**: nếu sau này ai chạy `Migrate()` trên SQLite mới với migration đã sửa, EF có thể generate kiểu khác `EnsureCreated` baseline → row vẫn còn nhưng schema khác → risk. |
| **2.5.C. Conditional với `IsSqlite()` extension** | Trong từng migration dùng `if (migrationBuilder.IsSqlite()) ... else ...`. | 1 file migration support cả 2. | Phức tạp, dễ sai. Không khuyến khích chính thức EF docs. |

**Em khuyến nghị 2.5.A (per-provider migrations)** cho Bước 6 — sạch + an toàn.
Nhưng cần xác nhận với anh trước khi gen.

**Cảnh báo**: option 2.5.A sẽ thay đổi vào `MesDbContextFactory.cs` +
`DependencyInjection.cs` (chuyển `MigrationsAssembly` → method tách
`migrationsAssembly` theo provider, tách `Migrations.Sqlite` namespace +
`Migrations.SqlServer` namespace). Đây là thay đổi shared code → cần test
lại boot SQLite production-DB không vỡ (A→B→C của Bước 5 lặp lại).

---

## 3. Phương án dựng SQL Server trên Mac ARM64

### 3.1 Image options

| Image | ARM64 native? | Phù hợp verify? | Trade-off |
|---|---|---|---|
| `mcr.microsoft.com/mssql/server:2022-latest` | ❌ x64 only | ✓ (qua Rosetta) | Boot chậm 1-3 phút trên Mac M-series. Full T-SQL, gần production. Cần `--platform linux/amd64` flag. |
| `mcr.microsoft.com/azure-sql-edge:latest` | ✅ ARM64 native | ✓ đủ verify | Fast boot. **Thiếu**: Full-Text Search, in-memory OLTP, một số advanced T-SQL. Cho migration + DDL + LIKE filter + indexes — đủ. **CẢNH BÁO**: Microsoft đã announce end-of-support cho Azure SQL Edge ngày 2025-09-30 — image vẫn pull được nhưng không có patch CVE. |
| `mcr.microsoft.com/mssql/server:2022-latest` + `--platform linux/amd64` | x64 emulated | ✓ | Chậm hơn ARM-native nhưng đúng SQL Server thật. |

**Em khuyến nghị `mcr.microsoft.com/mssql/server:2022-latest` với
`--platform linux/amd64`** vì:
- Production CCL Vietnam khả năng cao dùng SQL Server 2019/2022 thật (không
  phải Edge), nên verify trên đúng image cho confidence cao hơn.
- Azure SQL Edge sắp EoL — verify trên image sắp chết là tech debt.
- Mac M-series chạy Rosetta cho boot 1 lần verify chấp nhận được (vs production
  use case không phù hợp).

Nhưng nếu anh ưu tiên tốc độ (verify nhanh hơn, máy không nóng), Azure SQL Edge
là lựa chọn hợp lệ. Em đợi anh chốt.

### 3.2 Container setup

```bash
# Pull (chạy 1 lần, ~700 MB)
docker pull --platform linux/amd64 mcr.microsoft.com/mssql/server:2022-latest

# Run
docker run --platform linux/amd64 \
  --name ccl-mes-sqlserver-verify \
  -e "ACCEPT_EULA=Y" \
  -e "MSSQL_SA_PASSWORD=YourStrong!Passw0rd" \
  -p 14330:1433 \
  -d mcr.microsoft.com/mssql/server:2022-latest
```

- Port 14330 (không 1433) để tránh xung đột nếu sau có dev SQL Server khác.
- Password ≥ 8 ký tự + upper/lower/digit/special — SQL Server enforce.
- Container **ephemeral** (no volume mount) → verify xong, `docker rm -f` →
  data biến mất → KHÔNG dính SQLite live.

### 3.3 Connection string overrides

```
ConnectionStrings__Default = "Server=localhost,14330;Database=CCL_MES;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False"
Database__Provider          = "SqlServer"
```

Pass qua env var khi `dotnet run` thay vì sửa `appsettings.SqlServer.json`
(file đã có trên git, không muốn dính credential test). Hoặc tạo
`appsettings.Docker.json` LOCAL ONLY (.gitignore).

---

## 4. Quy trình verify A→B→C

### Phase A — Backup tường minh (SQLite live KHÔNG đụng)

```bash
cp src/CCL.MES.Web/ccl_mes.db /tmp/ccl_mes.db.before-step6.<ts>
shasum -a 256 src/CCL.MES.Web/ccl_mes.db /tmp/ccl_mes.db.before-step6.<ts>
# Cả 2 SHA phải khớp
```

Mục tiêu: sau Bước 6 đối chiếu SHA của SQLite live — **không được đổi 1 byte**.
SQLite live không bao giờ là target của verify này (target là SQL Server
container).

### Phase B — Test trên container (cô lập 100%)

1. **Pull + start container** (§3.2).
2. **Wait for SQL Server ready**: `docker exec ccl-mes-sqlserver-verify
   /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P 'YourStrong!Passw0rd'
   -Q "SELECT @@VERSION"` — retry tới khi thành công (max 60s).
3. **Tạo DB**: `... -Q "CREATE DATABASE CCL_MES"`.
4. **Boot Web app với SqlServer provider**:
   ```bash
   ConnectionStrings__Default="Server=localhost,14330;Database=CCL_MES;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True;Encrypt=False" \
   Database__Provider=SqlServer \
   dotnet run --project src/CCL.MES.Web
   ```
   - DbInitializer phải migrate tạo full schema sạch.
   - Console log phải in `[seed] Phase 6 Bước 4 — migrated 0 legacy ...`
     + seed 5 demo user.
   - **Đây là điểm fail nếu type-affinity của 2.5 chưa fix** —
     migration sẽ emit `CREATE TABLE ... INTEGER` → SQL Server parser
     reject → app crash.
5. **Verify schema bằng sqlcmd**: enumerate tables + indexes + foreign keys,
   so với SQLite schema. Khác biệt là dialect-acceptable (e.g. `BIGINT` vs
   `INTEGER`) — không khác structure.
6. **Verify seed**: `SELECT COUNT(*) FROM Users` = 5. Login admin/admin
   qua browser → audit row `LOGIN_OK` xuất hiện trong AuditLogs.
7. **Verify Syslog UI** (Settings → System Log) hoạt động — vì
   `EF.Functions.Like` là điểm provider-sensitive nhất runtime.
8. **Verify Backup UI** (Settings → Backup/Restore) — phải hiển thị
   guidance card SSMS (không có nút Snapshot).

### Phase C — ETL data NPI từ SQLite → SQL Server container

3 hướng (chọn 1):

| Option | Tool | Ưu | Nhược |
|---|---|---|---|
| **C.1. Mở rộng import_npi.py** | Thêm flag `--target sqlserver --conn '...'` dùng `pyodbc` thay vì `sqlite3`. | Tái dùng logic CSV/xlsx parsing. | Cần cài Microsoft ODBC Driver 18 for SQL Server (qua Homebrew). ~30 phút setup. |
| **C.2. CSV bridge** | Export SQLite → CSV bằng `sqlite3 .dump` hoặc `.mode csv .output`. Import vào SQL Server bằng `BULK INSERT` hoặc `bcp`. | Không cần ODBC driver mới. | Phải handle CSV escaping cho cột TEXT có quote/newline. |
| **C.3. Console .NET ETL script** | Tạo `scripts/EtlSqliteToSqlServer/Program.cs` — mở 2 DbContext (1 Sqlite + 1 SqlServer), ReadAsync sang WriteAsync per table. | Pure .NET, dùng EF Core đã có. Type-safe. | Code mới + maintenance. Slow vì EF overhead — ~5-10 phút cho 60k rows. |

**Em khuyến nghị C.3 (console .NET script)** vì:
- Cùng tooling stack (.NET) — không thêm Python/ODBC dependency.
- Type-safe — không phải lo CSV escape edge case.
- Idempotent qua DELETE+INSERT pattern (tái dùng từ import_npi.py).
- Một lần verify, không cần production-grade tốc độ.

Output Phase C:
- Row counts khớp SQLite: WC=43 / RawMat=2127 / Routing=38441 / Struct=20530.
- Spot-check 5-10 row mẫu (parts/customers/products) — content byte-equivalent.
- Performance baseline: ghi nhận thời gian ETL + migration thời gian (cho
  prod sizing).

### Cleanup

- `docker rm -f ccl-mes-sqlserver-verify` (xoá container + data SQL Server).
- Kiểm tra lại SHA của `ccl_mes.db` — bằng SHA Phase A. Nếu khác → có bug.
- `ConnectionStrings__Default` + `Database__Provider` env unset → SQLite
  default trở lại.

---

## 5. BackupService trên SQL Server

Đã verify nguồn ([BackupService.cs:31-46](src/CCL.MES.Web/Services/BackupService.cs#L31-L46)):

- `IsSqlite` check trả về `false` khi `Database:Provider == "SqlServer"`.
- `CreateSnapshotAsync` → `BackupOutcome.SqlServerUnsupported` ngay (no-op).
- `ListSnapshots` → `Array.Empty<BackupFile>()` (UI hiện "no snapshots").
- UI ([Backup.razor:20-26](src/CCL.MES.Web/Pages/Settings/Backup.razor#L20-L26))
  hiển thị guidance card pointing operators ở SSMS / maintenance plans
  qua key `settings.data.sqlserver.message`.

→ **Không vỡ khi switch provider**. Bước 6 chỉ cần verify visually:
container chạy → vào Settings → Data → thấy guidance EN/VI hiển thị
đúng, không thấy nút Snapshot.

Phụ chú: `BackupService.cs:14-19` có comment cũ "Restore deliberately out
of scope this Bước — filed as a Bước 5 follow-up". Bước 5 đã close cái này
qua console script `scripts/BackupRestore/` + guidance card thay restore.todo.
Comment cũ ở BackupService **cosmetic stale** — không ảnh hưởng Bước 6. Có
thể clean trong close-out Phase 6 cùng SpecService PageAsync cleanup.

---

## 6. Phạm vi đề xuất cho Bước 6 (nếu chọn Nhánh A)

### Phạm vi IN

- [P1] Fix type-affinity per §2.5 (chọn option 2.5.A hoặc 2.5.B).
- [P1] Verify Docker SQL Server container boot + migration apply.
- [P1] ETL NPI 4 tables + Users + AuditLogs từ SQLite → SQL Server.
- [P1] Row-count khớp + spot-check data.
- [P2] Verify Login + Audit + Syslog UI hoạt động trên SQL Server.
- [P2] Verify Backup UI hiển thị guidance card.
- [P3] Document quy trình + lessons learned vào docs/PHASE6-STEP6-VERIFY.md.

### Phạm vi OUT (defer)

- Production deploy lên SQL Server CCL Vietnam — KHÔNG phải Bước 6.
- BackupService SQL Server-native snapshot (`BACKUP DATABASE TO DISK = ...`)
  — defer Phase 7. Guidance card đủ cho phạm vi hiện tại.
- Performance tuning + indexing audit — defer khi có production load.
- Connection resilience (Polly retry) — defer.

### Output deliverables

| Path | Mô tả | New/Modified |
|---|---|---|
| `src/CCL.MES.Infrastructure/Migrations/Sqlite/*` (nếu chọn 2.5.A) | 3 migration file × 2 (Up + Designer) di chuyển từ root Migrations/ | RENAME |
| `src/CCL.MES.Infrastructure/Migrations/SqlServer/*` (nếu chọn 2.5.A) | 3 migration file × 2 generate mới qua `MES_PROVIDER=SqlServer` | NEW |
| `src/CCL.MES.Infrastructure/DependencyInjection.cs` + `MesDbContextFactory.cs` | Switch `MigrationsAssembly` + namespace theo provider | MODIFY |
| `scripts/EtlSqliteToSqlServer/Program.cs` + `.csproj` + `README.md` (nếu chọn C.3) | Console ETL script | NEW |
| `docs/PHASE6-STEP6-VERIFY.md` | Báo cáo verify (row counts before/after, screenshots, lessons) | NEW |
| `appsettings.Docker.json` (`.gitignore`) | Connection cho Docker | NEW (local only) |
| `.gitignore` | Thêm `appsettings.Docker.json` | MODIFY |

### Hiệu lực với SQLite live

- Phase 6 Bước 5 SQLite-baseline (60k rows) **không bị đụng** suốt quy trình.
- Sau verify, chạy boot SQLite default 1 lần để xác nhận DbInitializer
  baseline-check vẫn no-op (= không re-migrate).

### Rủi ro mất data thật

**THẤP** (verify cô lập trong container; SQLite live read-only suốt quy trình
vì nguồn ETL là `Mode=ReadOnly` qua `SqliteConnection`). Cảnh báo:
- **TUYỆT ĐỐI KHÔNG** trỏ `Database:Provider=SqlServer` rồi quên đổi connection
  string — sẽ gặp error connection refused (vì localhost:1433 không có gì),
  KHÔNG drop SQLite. ✓ defensive.
- **TUYỆT ĐỐI KHÔNG** chạy `dotnet ef database drop` (vô tình) trong môi
  trường nào — script `recover-sys-user` đã có gate `CONFIRM-RECOVER`,
  nhưng `ef database drop` không có gate. Có thể disable temporary qua
  remove dotnet-ef nếu lo.

---

## 7. Câu hỏi cần anh quyết trước khi code

| Q | Câu hỏi | Em đề xuất | Anh chọn |
|---|---|---|---|
| **Q1** | Nhánh A (cài Docker, làm Bước 6) hay Nhánh B (defer, làm Bước 7 IQC trước)? | **Nhánh B** (defer Bước 6) — lý do ở §0. | ? |
| **Q2** | Nếu Nhánh A: image SQL Server nào? Mssql 2022 amd64 emulated vs Azure SQL Edge ARM-native? | **mcr.microsoft.com/mssql/server:2022-latest --platform linux/amd64** — đúng production parity. | ? |
| **Q3** | Nếu Nhánh A: type-affinity fix option nào — 2.5.A (per-provider migrations) vs 2.5.B (drop type strings, 1 file dùng chung) vs 2.5.C (conditional)? | **2.5.A** — sạch nhất, EF best practice. | ? |
| **Q4** | Nếu Nhánh A: ETL tool nào — C.1 mở rộng import_npi.py với pyodbc vs C.2 CSV bridge vs C.3 console .NET script? | **C.3** — same .NET stack, type-safe. | ? |
| **Q5** | Nếu Nhánh A: connection cho container chạy local — tạo `appsettings.Docker.json` (`.gitignore`) hay env var inline? | **env var inline** (không tạo file local-only mất chỗ). | ? |
| **Q6** | Nếu Nhánh A: Bước 6 cần verify Backup UI guidance card không (P2) hay skip để PR nhỏ hơn? | **CÓ verify P2** — chi phí thấp, confidence cao. | ? |
| **Q7** | Đặt branch base — `feat/phase6-sqlserver-verify` stack trên `feat/phase6-audit-log` (PR #15 mới open) hay `main`? | **stack trên feat/phase6-audit-log** giống pattern Bước 5 stack trên Bước 4. PR #15 chưa merge → Bước 6 PR sẽ dùng `feat/phase6-audit-log` làm base + auto-rebase khi PR #15 merge. | ? |
| **Q8** | Nếu chọn Nhánh B (defer): chuyển sang Bước 7 (IQC) có đổi gì trong Phase 6 plan không? Đóng Bước 6 luôn? Hay treo lại "TODO" để làm khi có SQL Server thật? | **Treo Bước 6 thành "TODO khi có SQL Server thật"**, log trong CLAUDE.md/MES-3 backlog. Move on Bước 7 ngay. | ? |
| **Q9** | (Nhánh A only) Cleanup container sau verify — `docker rm -f` ngay hay giữ lại để debug 1-2 ngày? | **giữ container 24-48h** sau verify để debug nếu có bug post-merge, rồi cleanup. | ? |
| **Q10** | Phạm vi tài liệu output — `docs/PHASE6-STEP6-VERIFY.md` chi tiết screenshots + row counts, hay 1-pager summary? | **chi tiết** — tài liệu này sẽ là blueprint khi anh deploy SQL Server thật. | ? |

---

## 8. STOP — chờ phương án

Khảo sát xong. Doc untracked, **chưa code, chưa branch**.

**Em đợi anh chốt Q1-Q10** rồi mới triển khai. Trường hợp anh chọn Nhánh B
(defer Bước 6) → em sẽ:
1. Log trạng thái "Bước 6 TODO khi có SQL Server thật" vào MES-3 backlog
   trong `docs/PHASE6-PLAN.md` (nếu tồn tại) hoặc note ở README/CLAUDE.md
   tương đương.
2. Chuyển luôn sang khảo sát Bước 7 (IQC entity + tab) — đưa
   `docs/PHASE6-STEP7-PLAN.md`.

Trường hợp anh chọn Nhánh A → em sẽ:
1. Cài Docker Desktop trên máy anh (anh tự cài, em hướng dẫn).
2. Triển khai theo Q2-Q10 đã chốt — A→B→C đầy đủ.
