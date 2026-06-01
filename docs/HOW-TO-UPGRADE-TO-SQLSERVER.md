# Cách nâng cấp CCL-MES từ SQLite sang SQL Server

> **Trạng thái hiện tại (Phase 6 Bước 6.5)**: production chạy SQLite kiểu
> Ops Control v1.2 (`data/ccl_mes.db`). Toàn bộ scaffolding SQL Server **đã
> sẵn sàng** làm cổng nâng cấp — chỉ chờ operator quyết khi nào kích hoạt.
>
> Doc này là **runbook chuẩn** để chuyển provider khi quy mô / yêu cầu đến
> ngưỡng. Đọc kỹ + chạy thử trên môi trường staging trước production.

---

## 0. Khi nào CẦN nâng cấp

Cứ ở SQLite cho tới khi đạt 1 trong các ngưỡng:

| Tín hiệu | Ngưỡng | Lý do |
|---|---|---|
| Kích thước DB | > 10 GB hoặc > 10M rows | SQLite vẫn chạy được nhưng query plan phức tạp chậm dần |
| Concurrent writers | > 5-10 đồng thời | SQLite single-writer lock thành bottleneck |
| HA/cluster yêu cầu | Bất kỳ | SQLite không hỗ trợ replication native |
| Compliance T-SQL audit | Bắt buộc | SQL Server Audit objects / SQL Server Always Encrypted |
| Performance dashboard | Yêu cầu Query Store | SQLite không có equivalent |

**Tại CCL Vietnam Yen Phong (size hiện tại 11 MB, ~60k rows, 5 users)**:
chưa cần. SQLite production-ready cho ≥ 3-5 năm tới với tốc độ tăng trưởng
hiện tại.

---

## 1. Tiền điều kiện

### Hạ tầng

- SQL Server 2019+ instance (on-prem hoặc Azure SQL Database / Managed Instance)
- Port 1433 mở từ MES Web server → SQL Server
- SQL login với role `db_owner` trên DB mới (sẽ tạo)
- Disk free ≥ 5× kích thước SQLite hiện tại (data + log + tempdb buffer)

### App

- Phase 6 Bước 6.5 type-affinity fix đã merge vào main (sau ngày
  YYYY-MM-DD — verify bằng `grep -nE 'type: "(TEXT|INTEGER|REAL)"' src/CCL.MES.Infrastructure/Migrations/*.cs` → empty).
- `dotnet-ef` cài global: `dotnet tool install --global dotnet-ef --version 10.*`
- Backup SQLite tươi (qua Settings → Backup/Restore → Create snapshot OR
  `scripts/BackupRestore` console).

### Provider-guarded migrations (đã có sẵn)

Một số migration phải dùng raw SQL workaround cho quirk của EF Core SQLite
provider. Chúng được bọc trong `if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.Sqlite")`
+ nhánh `else if` cho SQL Server + `else throw NotSupportedException` cho
provider mới. Khi nâng cấp SQL Server, các migration này tự chọn đúng nhánh
mà KHÔNG cần edit code.

| Migration | Lý do dùng raw SQL workaround | SQL Server branch |
|---|---|---|
| `20260601023538_AddProductRevisionSchema` (Phase 8 PR #28) | EF Core 10 SQLite không reliably trigger table-rebuild khi `DropForeignKey` + `RenameColumn` đứng cạnh nhau — phải manual rebuild WorkOrders để drop FK `FK_WorkOrders_SpecVersions_SpecVersionId` trước khi `DROP TABLE SpecVersions`. | Standard `DropForeignKey` + `DropIndex` + `RenameColumn` + `CreateIndex` (SQL Server hỗ trợ `ALTER TABLE DROP CONSTRAINT` + `sp_rename` native). |

Khi nâng cấp lên SQL Server, các nhánh này tự kích hoạt — KHÔNG cần edit lại
migration. Nếu thêm provider mới (Postgres etc.) sẽ bị `NotSupportedException`
+ điểm đến doc này để bổ sung nhánh tương đương.

---

## 2. Quy trình A→B→C

### Phase A — Backup tường minh

**Mục tiêu**: SQLite live remain intact suốt quá trình. Rollback luôn về
được.

```bash
# 2.A.1 - Snapshot SQLite + SHA256 baseline
cp data/ccl_mes.db /tmp/ccl_mes.db.before-sqlserver-upgrade.$(date -u +%Y%m%d-%H%M%S)
shasum -a 256 data/ccl_mes.db

# 2.A.2 - Verify row counts baseline (lưu để verify sau)
sqlite3 data/ccl_mes.db "
  SELECT 'WorkCenters', COUNT(*) FROM WorkCenters
  UNION ALL SELECT 'RawMaterials', COUNT(*) FROM RawMaterials
  UNION ALL SELECT 'RoutingOperations', COUNT(*) FROM RoutingOperations
  UNION ALL SELECT 'ManufacturingStructures', COUNT(*) FROM ManufacturingStructures
  UNION ALL SELECT 'Users', COUNT(*) FROM Users
  UNION ALL SELECT 'AuditLogs', COUNT(*) FROM AuditLogs;
" | tee /tmp/rowcounts-pre-upgrade.txt

# 2.A.3 - Save .env hiện tại (đặc biệt nếu sau này có secret keys)
cp .env /tmp/.env.before-sqlserver-upgrade.$(date -u +%Y%m%d-%H%M%S)
```

### Phase B — Migrate schema trên SQL Server (rỗng)

**Mục tiêu**: tạo DB rỗng + apply migrations → 20 bảng + indexes + FKs.
KHÔNG chạm SQLite live.

```bash
# 2.B.1 - Tạo DB rỗng trên SQL Server bằng SSMS hoặc sqlcmd
# (chạy từ máy có sqlcmd hoặc qua SSMS GUI)
sqlcmd -S <host>,1433 -U <admin> -P <pwd> -Q "CREATE DATABASE CCL_MES;"

# 2.B.2 - Apply migrations từ máy có code
export MES_PROVIDER=SqlServer
export MES_CONNSTR="Server=<host>,1433;Database=CCL_MES;User Id=<login>;Password=<pwd>;TrustServerCertificate=True;Encrypt=False"

bash ef-migrate.sh --sqlserver
# Apply: Init -> AddUserMustChangeAndIsActive -> AddAuditLog
# (Bước 6.5 đã strip type:/HasColumnType → EF inference dùng đúng
#  bigint/nvarchar(max)/float/datetime2/bit cho SQL Server.)

# 2.B.3 - First-migration tweak: thêm SqlServer:Identity annotation
# Đây là EF Core cross-provider quirk — snapshot có Sqlite:Autoincrement,
# SQL Server provider muốn SqlServer:Identity tương đương cho IDENTITY behavior.
# Generate + apply 1 migration cosmetic:
MES_PROVIDER=SqlServer dotnet ef migrations add SqlServerIdentities \
  -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web -o Migrations/SqlServer
MES_PROVIDER=SqlServer dotnet ef database update \
  -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web

# (Migration này chứa 20 AlterColumn<long> Id với .Annotation("SqlServer:Identity", "1, 1").
# Type: bigint giữ nguyên — đây CHỈ là annotation chuyển identity behavior.)
```

**Verify schema** qua SSMS hoặc sqlcmd: 20 tables + indexes + FKs khớp SQLite
(dialect-acceptable: `bigint` thay `INTEGER`, `nvarchar(max)` thay `TEXT`,
`float` thay `REAL`, `bit` thay `INTEGER` cho bool, `datetime2` thay `TEXT`
cho DateTime).

### Phase C — ETL data từ SQLite sang SQL Server

**Mục tiêu**: chuyển toàn bộ data SQLite → SQL Server. Row count + content
byte-equivalent.

3 options (chọn 1 phù hợp môi trường):

#### Option C.1 — Console .NET ETL script (đề xuất)

Khi sprint này thực sự cần, tạo `scripts/EtlSqliteToSqlServer/Program.cs`:

```csharp
// Pseudo-code, sẽ implement khi cần
var sqlite = new MesDbContext(sqliteOpts);
var sqlserver = new MesDbContext(sqlserverOpts);

foreach (var entity in [Customers, Machines, ..., AuditLogs]) {
    var rows = sqlite.Set<T>().ToList();
    sqlserver.Set<T>().AddRange(rows);
    sqlserver.SaveChanges();
    Console.WriteLine($"{typeof(T).Name}: {rows.Count} rows");
}
```

Ưu: type-safe, same .NET stack. ~1-5 phút cho 60k rows.

#### Option C.2 — CSV bridge

```bash
# Export SQLite → CSV per table
sqlite3 data/ccl_mes.db ".mode csv" ".output WorkCenters.csv" "SELECT * FROM WorkCenters;"
# ... repeat per table

# Import vào SQL Server qua bcp:
bcp CCL_MES.dbo.WorkCenters in WorkCenters.csv -S <host> -U <login> -P <pwd> -c -t,
# ... repeat per table
```

Ưu: không cần ODBC, không cần code mới. Nhược: phải handle CSV escape edge case
(quotes/newlines trong AuditLogs.Detail JSON).

#### Option C.3 — SQL Server Import Wizard

Qua SSMS → Tasks → Import Data → SQLite ODBC driver. GUI, slow nhưng audit-friendly.

### Verify post-ETL

```sql
-- Trên SQL Server, verify row counts khớp baseline
SELECT 'WorkCenters', COUNT(*) FROM WorkCenters
UNION ALL SELECT 'RawMaterials', COUNT(*) FROM RawMaterials
UNION ALL SELECT 'RoutingOperations', COUNT(*) FROM RoutingOperations
UNION ALL SELECT 'ManufacturingStructures', COUNT(*) FROM ManufacturingStructures
UNION ALL SELECT 'Users', COUNT(*) FROM Users
UNION ALL SELECT 'AuditLogs', COUNT(*) FROM AuditLogs;
```

Phải khớp `/tmp/rowcounts-pre-upgrade.txt` từ Phase A. Nếu lệch → rollback.

---

## 3. Switch provider trên app

### 3.1 Edit cấu hình production

```json
// appsettings.Production.json (operator-managed, NOT in git)
{
  "Database": {
    "Provider": "SqlServer"
  },
  "ConnectionStrings": {
    "Default": "Server=<host>,1433;Database=CCL_MES;User Id=<login>;Password=<pwd>;TrustServerCertificate=True;Encrypt=False"
  }
}
```

**R3 (Bước 6.5)**: khi `Database:Provider == "SqlServer"`, Program.cs sẽ
KHÔNG override connection string (chỉ làm cho Sqlite). SQL Server connection
hoàn toàn từ config. ✓

### 3.2 Stop + restart service

```bash
# macOS standalone
pkill -f "CCL.MES.Web"
# Edit appsettings.Production.json hoặc set env vars
export ASPNETCORE_ENVIRONMENT=Production
bash START_SERVER.command   # boot lại

# Windows (NSSM ví dụ)
nssm stop ccl-mes
# Edit appsettings.Production.json
nssm start ccl-mes
```

### 3.3 Verify boot log

```
[boot] DB provider: SqlServer — connection string from config only.
info: Microsoft.EntityFrameworkCore.Migrations[20405]
      No migrations were applied. The database is already up to date.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://0.0.0.0:5050
```

✓ DbInitializer baseline check tìm thấy history (3+1 migration đã apply ở Phase B/Phase C SqlServerIdentities).
✓ `Migrate()` no-op (pending = 0).

---

## 4. Verify post-upgrade

| Item | Cách verify | Pass nếu |
|---|---|---|
| Login | Mở http://host:5050/login, admin/admin | ⟶ /dashboard |
| Audit row | Sau login, query `SELECT TOP 1 * FROM AuditLogs ORDER BY Id DESC` | Row LOGIN_OK với ActorUsername=admin, ActorRole=Admin |
| Syslog tab | Settings → System Log → search "LOGIN" | Hiển thị bảng với LOGIN_OK row |
| Backup tab | Settings → Backup/Restore | Hiện guidance card SSMS (không có nút Snapshot) |
| NPI tab | Sidebar → NPI → Engineer Spec | List parts hiển thị (43+2127+38441+20530 row hiển thị filter) |
| Performance | Mở 1 vài tab nặng (Routing search 100 parts) | Response < 500ms (SQLite trước có thể chậm hơn) |

---

## 5. Rollback nếu fail

**Trong vòng 1 giờ đầu** (chưa có ghi mới đáng kể vào SQL Server):

```bash
# 5.1 - Stop service
pkill -f "CCL.MES.Web"

# 5.2 - Revert config về SQLite
# Edit appsettings.Production.json:
#   "Database": { "Provider": "Sqlite" }
# Remove ConnectionStrings:Default (để Program.cs tự resolve về data/ccl_mes.db)

# 5.3 - Restart
bash START_SERVER.command
# Phải in: [boot] SQLite data dir: <repo-root>/data

# 5.4 - Verify boot OK
curl -sS http://localhost:5050/login
# Phải trả 200
```

**Sau >1 giờ** (có ghi mới đáng kể vào SQL Server):
1. Đề xuất giữ SQL Server làm primary, nếu thực sự muốn rollback:
2. ETL ngược SQL Server → SQLite (script tương tự Phase C nhưng đảo chiều).
3. Apply SQLite live tại `data/ccl_mes.db` (phải qua A→B→C lần nữa).

---

## 6. Cleanup sau upgrade thành công

Sau khi confirm SQL Server chạy ổn ≥ 1 tuần production:

1. **Giữ SQLite snapshot Phase A backup** dài hạn (≥ 6 tháng) làm forensic.
2. **Xoá `data/ccl_mes.db`** chỉ khi thực sự không cần rollback nữa.
3. **Cập nhật runbook nội bộ**: tham chiếu doc này, ghi rõ date+SHA của
   PR Bước 6.5 trên main.
4. **Add MES-3 backlog ticket**: BackupService SqlServer-native snapshot
   (`BACKUP DATABASE TO DISK = ...`) — defer khỏi guidance card thuần text.

---

## 7. Tham chiếu

- `src/CCL.MES.Infrastructure/DependencyInjection.cs:16-28` — provider switch
- `src/CCL.MES.Infrastructure/MesDbContextFactory.cs:18-28` — design-time factory
- `src/CCL.MES.Infrastructure/DbInitializer.cs:31-65` — baseline-aware migration
- `src/CCL.MES.Web/Services/BackupService.cs:31-46` — SQL Server guidance gate
- `ef-migrate.sh` — `--sqlserver` flag wraps `MES_PROVIDER` env
- `docs/PHASE5-STEP4-PLAN.md` — EF migration design (Phase 5 Bước 4)
- `docs/PHASE6-STEP6.5-PLAN.md` — type-affinity fix rationale (Phase 6 Bước 6.5)

---

## 8. Khi cập nhật doc này

Khi thêm migration mới vào main:
- Update §2.B.2 nếu migration apply order thay đổi.
- Cập nhật §2.B.3 nếu phát hiện thêm cross-provider quirk.
- Update §4 verify checklist nếu thêm tab/feature mới.
