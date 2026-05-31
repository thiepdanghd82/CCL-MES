# Phase 6 — Bước 6.5: Cổng SQL Server "đúng & sẵn sàng" + Deploy SQLite kiểu Ops Control v1.2 (KHẢO SÁT)

> **Trạng thái: KHẢO SÁT (read-only).** Chưa code, chưa branch.
>
> **Quyết định đã chốt (anh ra)**: chạy production bằng SQLite kiểu Ops Control
> v1.2; giữ NGUYÊN toàn bộ scaffolding SQL Server làm "cổng nâng cấp"; bỏ
> Bước 6 (Docker verify) khỏi roadmap, giữ doc làm hướng dẫn khi nào cần lên
> SQL Server thật.
>
> Bước 6.5 này có **3 mục tiêu**:
>
> 1. **Fix cổng SQL Server cho "thật"** — sửa migration affinity (phát hiện
>    §2.5 ở Bước 6) để generate/build sang SQL Server không sinh kiểu sai,
>    không cần Docker apply.
> 2. **Chuẩn hoá deploy SQLite kiểu Ops Control v1.2** — DATA_DIR cố định,
>    START_SERVER_*.command/bat, vị trí backup chuẩn.
> 3. **Viết doc "Cách nâng cấp lên SQL Server"** — checklist tường minh.
>
> **Không đụng**: `Ops Control v1.2/`, `CMES/`, `Old ver ( DO NOT USE)/`,
> `SpecHub/` (chỉ ĐỌC Ops Control v1.2 để học pattern, không sửa/chạy).

---

## 1. Khảo sát Ops Control v1.2 — pattern deploy SQLite (READ-ONLY)

### 1.1 Cấu trúc data folder

```
Ops Control v1.2/
├── server/
│   ├── index.js                   ← entry point
│   ├── db/
│   │   ├── connection.js          ← DB path resolution
│   │   ├── init.js
│   │   ├── schema.sql
│   │   └── backup.js
│   └── data/                      ← canonical data root
│       ├── ops.db                 ← live SQLite file
│       ├── Backup/                ← backup target (dotted SQLite snapshots)
│       ├── Library/               ← JSON library files
│       ├── planning/              ← MES planning DB
│       ├── Products layout/       ← PDF files
│       └── ...
├── START_SERVER.command           ← macOS launcher (v1.0 baseline)
├── START_SERVER_v1.5.10.command   ← macOS launcher (current)
├── START_SERVER.bat               ← Windows launcher
├── deploy.sh / deploy.ps1 / deploy.bat
└── .env                           ← OPS_TOTP_KEY / OPS_KIOSK_KEY / OPS_EXPORT_HMAC_KEY / DATA_DIR
```

### 1.2 DATA_DIR resolution — `server/index.js:96-99`

```js
let DATA_DIR = process.env.DATA_DIR || path.join(__dirname, 'data');

if (!path.isAbsolute(DATA_DIR)) {
  DATA_DIR = path.resolve(path.join(__dirname, '..'), DATA_DIR);
}
```

**Pattern**:
- env var first (`DATA_DIR`) → override để deploy.sh chỉ trỏ vào
  `/opt/ops-control/data/` trên server Linux/Windows
- fallback `path.join(__dirname, 'data')` = `server/data/` cố định theo
  vị trí file source, **không** phụ thuộc CWD
- nếu relative → resolve về absolute trước khi dùng

### 1.3 DB path resolution — `server/db/connection.js:31-36`

```js
if (process.env.OPS_DB_PATH) {
  _dbPath = path.resolve(process.env.OPS_DB_PATH);
} else {
  const dataDir = process.env.DATA_DIR
    ? path.resolve(process.env.DATA_DIR)
    : path.join(__dirname, '..', 'data');
  _dbPath = path.join(dataDir, 'ops.db');
}
```

Hai-tier override: `OPS_DB_PATH` (full path) > `DATA_DIR + 'ops.db'` >
default `server/data/ops.db`. Tất cả về absolute trước khi mở connection
→ **Lesson 30 (CLAUDE.md) đã encode**: không bao giờ truyền relative
path xuống `new Database(...)`.

### 1.4 Backup folder convention

| Path | Mô tả |
|---|---|
| `<DATA_DIR>/Backup/` | Root backup folder |
| `<DATA_DIR>/Backup/SQLite/` | SQLite snapshot files (gọi qua `backupOpsDb` → `db.backup()`) |

`backupScheduler.js:278` dùng cùng `DATA_DIR` env → backup folder tự đi theo
data folder, không hardcode.

### 1.5 START_SERVER pattern (`START_SERVER_v1.5.10.command`)

```bash
#!/bin/bash
cd "$(dirname "${BASH_SOURCE[0]}")"     # chdir repo root

# 1. Tìm Node (v22+)
for cmd in node node22 node20; do command -v "$cmd" && NODE="$cmd" && break; done

# 2. Auto-install deps if missing
[ ! -d "node_modules" ] && npm install --silent --no-audit --no-fund

# 3. Auto-build client if dist missing
[ ! -d "client/dist" ] && cd client && npx vite build && cd ..

# 4. Preflight env check (fail-fast nếu .env thiếu key bắt buộc)
NODE_ENV=production "$NODE" scripts/preflight-env.js

# 5. Detect LAN IP (en0 → en1 → en2)
LOCAL_IP=$(ipconfig getifaddr en0 ...)

# 6. Kill old PID on port 3000 (TERM với 3s grace → KILL)
OLD_PIDS=$(lsof -ti:3000)
kill $OLD_PIDS

# 7. Banner: localhost URL, LAN URL, log path, data dir, instructions
# 8. Run với tee log
NODE_NO_WARNINGS=1 "$NODE" server/index.js 2>&1 | tee /tmp/ops-server-standalone.log
```

**Mẫu đẹp** (em sẽ học theo): `cd dirname → tool check → auto-prep → preflight
→ LAN detect → port cleanup → banner → run + tee log`.

---

## 2. Khảo sát CCL-CMES — hiện trạng deploy SQLite

### 2.1 Cấu trúc hiện tại

```
CCL-CMES/CCL-MES/
├── src/
│   ├── CCL.MES.Web/
│   │   ├── Program.cs
│   │   ├── ccl_mes.db                  ← ⚠️ HIỆN TẠI dính cwd, lạc chỗ
│   │   ├── appsettings.json            ← "Data Source=ccl_mes.db" (relative)
│   │   └── appsettings.SqlServer.json
│   ├── CCL.MES.Infrastructure/
│   │   └── Migrations/
│   │       ├── 20260531050444_Init.cs
│   │       ├── 20260531070602_AddUserMustChangeAndIsActive.cs
│   │       └── 20260531073842_AddAuditLog.cs
│   └── ...
├── scripts/
│   ├── RecoverAdmin/
│   └── BackupRestore/
├── tools/
│   └── import_npi.py
├── ef-migrate.sh                       ← EF CLI helper (cả 2 provider)
└── docs/
```

**Vấn đề rõ**:
- Connection string `Data Source=ccl_mes.db` resolve theo CWD. Khi
  `dotnet run --project src/CCL.MES.Web/` từ repo root → CWD =
  `src/CCL.MES.Web/` → DB landed tại `src/CCL.MES.Web/ccl_mes.db` (vị trí
  hiện tại, "accidentally correct"). Nếu chạy từ chỗ khác → DB landed sai chỗ.
- Không có data/ folder canonical level (như Ops Control's `server/data/`).
- Không có START_SERVER script.
- Backup folder = thư mục cha của ccl_mes.db (qua `ResolveSqlitePath`) → hiện
  tại = `src/CCL.MES.Web/` → snapshot dồn chung với source code. Gớm.

### 2.2 Connection string flow

`Program.cs:18` → `AddInfrastructure(builder.Configuration)` → đọc
`config.GetConnectionString("Default")` → `appsettings.json` → `"Data Source=ccl_mes.db"`.

Provider switch (`Database:Provider`) clean nhưng connection string SQLite
gắn CWD. Tách thành 3 case:

| Trường hợp | Path resolution | Kết quả |
|---|---|---|
| `dotnet run --project src/CCL.MES.Web` từ repo root | CWD = `src/CCL.MES.Web/` | DB ở `src/CCL.MES.Web/ccl_mes.db` ✓ (hiện tại) |
| `dotnet run` từ trong `src/CCL.MES.Web/` | CWD = `src/CCL.MES.Web/` | Đúng ✓ |
| `cd /tmp && dotnet --project ...` | CWD = `/tmp` | DB ở `/tmp/ccl_mes.db` ❌ |
| Sau `dotnet publish` chạy `./CCL.MES.Web` | CWD = publish dir | DB tạo mới (rỗng) ❌ |

### 2.3 BackupService path

`BackupService.ResolveSqlitePath()` ([:111-120](src/CCL.MES.Web/Services/BackupService.cs#L111-L120)):
```csharp
var builder = new SqliteConnectionStringBuilder(cs);
var path = builder.DataSource;
var full = Path.GetFullPath(path);              // ← resolve theo CWD
return (Path.GetDirectoryName(full) ?? "", full);
```

`Path.GetFullPath(relativePath)` → resolve theo `Environment.CurrentDirectory`
→ kế thừa CWD trap của connection string. Snapshot path đi theo.

### 2.4 scripts/BackupRestore default

`Program.cs` (BackupRestore) đã có override `MES_DB_PATH` env var (theo
README) — pattern này chính là cái cần áp ngược về Web. Đã đi đúng hướng.

### 2.5 ef-migrate.sh đã có cổng SQL Server

`ef-migrate.sh:32-40` set `MES_PROVIDER=SqlServer` qua env → factory
([MesDbContextFactory.cs:18](src/CCL.MES.Infrastructure/MesDbContextFactory.cs#L18))
đọc đúng. Nhánh `add` hoạt động cho cả 2 provider. ✓

---

## 3. Mục tiêu 1 — Fix migration provider-affinity (cổng SQL Server "thật")

### 3.1 Lý do PHẢI fix kể cả khi SQLite là đường chạy chính

Cổng SQL Server hiện tại **không phải "compile-only OK"** mà còn ẩn 3 vấn đề
nếu sau này anh quyết đổi provider:

1. **`type: "INTEGER"` trên Id columns** — SQL Server accept `INTEGER` như
   synonym ANSI cho `int` (4 byte). Nhưng entity là `long Id` → CLR mapping
   xuống `int` → **INT_MAX = ~2.1 tỷ** cap, vs `long` ~9.2 quintillion. Với
   ProductionLogs (mỗi WO có nhiều row) hay AuditLogs (mỗi click), audit
   table sẽ chạm cap sau ~6-7 năm log liên tục → silent overflow.

2. **`type: "REAL"` trên double columns** — SQL Server `REAL` = `float(24)`
   single precision (~7 decimal digits). Entity `double` = double precision
   (~15-17 digits). `IdealCycleTimeSec`, `QtyAssembly`, `MachineSetupTime`,
   `Price` etc. — chu kỳ máy đo theo giây với 6 decimal sẽ mất chính xác →
   OEE/cost calc lệch.

3. **`type: "TEXT"` trên string columns** — SQL Server `TEXT` = LOB type
   deprecated từ 2005, **không index hiệu quả**, nhiều API thao tác string
   (LIKE, LEN, SUBSTRING) hành xử khác `nvarchar`. AuditLogs.Detail (≤4 KB)
   được Syslog `EF.Functions.Like` search → search sẽ chậm + có thể không
   trả về kết quả Unicode đúng (`TEXT` collation default ANSI).

   Đặc biệt: `Username TEXT NOT NULL` + `IX_Users_Username unique` → index
   trên `TEXT` column trên SQL Server sẽ bị deprecated warning hoặc fail
   tuỳ version.

**Kết luận**: cổng hiện tại không phải "scaffolding rỗng" — nó sẽ chạy,
nhưng tích lũy bug ẩn. Phải fix để **khi cần đổi sau này, không phải
lội ngược migration sửa từng cột**.

### 3.2 3 phương án fix

| Option | Mô tả | LOC ước | Trade-off | Em đề xuất |
|---|---|---|---|---|
| **3.2.A. Per-provider migrations folders** | Tách `Migrations/Sqlite/` (rename current) + `Migrations/SqlServer/` (generate mới). `DependencyInjection.cs` + `MesDbContextFactory.cs` chọn `MigrationsAssembly` + namespace theo provider. | +9 file × 2 (Up.cs + Designer.cs) + 1 ModelSnapshot riêng cho mỗi provider = ~14 file mới, ~20 LOC sửa DependencyInjection + Factory | **Sạch nhất EF best-practice**. Mỗi provider có migration native, không leakage. Bulletproof khi sau này anh add migration mới — chỉ cần `MES_PROVIDER=SqlServer ef migrations add NextOne -o Migrations/SqlServer`. **Nhược**: phình file. ModelSnapshot×2 phải sync tay — quên 1 cái = drift. | ❌ phức tạp |
| **3.2.B. Drop explicit `type:` strings trong existing migrations** | Sửa Init/AddUserMustChange/AddAuditLog: bỏ `type: "TEXT"\|"INTEGER"\|"REAL"`, giữ `nullable`, `defaultValue`. EF Core inference per provider: SQLite → vẫn emit `INTEGER`/`TEXT`/`REAL` (bằng inference từ CLR type); SQL Server → emit `bigint`/`nvarchar(max)`/`float`. | ~50 LOC edit trong 3 file migration + 0 file mới | **Nhẹ nhất, một-source migrations**. EF inference per provider tự đúng. **Nhược**: hand-edit migration đã apply trên SQLite live (60k row). Cần verify: DbInitializer baseline-check chỉ so MigrationId trong `__EFMigrationsHistory`, KHÔNG validate column type strings → an toàn về data. Cần verify `dotnet build` + `dotnet ef migrations add VerifyNoChange` empty (cả Sqlite + SqlServer) để chứng minh không drift. | ✅ **khuyến nghị** |
| **3.2.C. Conditional `migrationBuilder.IsSqlite()`** | Trong từng migration `if (migrationBuilder.IsSqlite()) ... else ...`. | ~80 LOC | Phức tạp, EF docs không khuyến khích. Khó test 2 nhánh trong 1 file. | ❌ tránh |

### 3.3 Vì sao 3.2.B an toàn (technical justification)

**Mệnh đề**: hand-edit existing applied migration không gây mất data SQLite live.

**Chứng minh**:
1. DbInitializer ([:36-46](src/CCL.MES.Infrastructure/DbInitializer.cs#L36-L46))
   check `historyExists` (table `__EFMigrationsHistory` tồn tại) → nếu có
   → skip baseline branch → gọi `Migrate()`.
2. `Migrate()` đọc `__EFMigrationsHistory` (chứa row MigrationId), so với
   `db.Database.GetMigrations()` → pending = empty (nếu MigrationId không đổi)
   → no-op.
3. MigrationId = file name = timestamp+ClassName. Hand-edit nội dung file
   KHÔNG đổi MigrationId → tiếp tục no-op trên SQLite live.
4. ModelSnapshot.cs phải regenerate qua `dotnet ef migrations add VerifyNoChange`
   sau khi sửa Up/Down methods — nếu empty migration sinh ra → confirmed model
   match. Nếu non-empty → có drift, dừng, fix.

**Rủi ro residual**: nếu một dev sau này clone repo, **delete SQLite file**,
chạy lần đầu → DbInitializer `Migrate()` re-tạo schema từ migration đã sửa.
Trên SQLite: EF infer `bool` → `INTEGER`, `string` → `TEXT`, `long` →
`INTEGER`, `double` → `REAL` — IDENTICAL DDL → bit-equal schema. ✓ Lessor case.

### 3.4 Quy trình verify 3.2.B (A→B→C)

**Phase A (backup tường minh)**:
```bash
cp src/CCL.MES.Web/ccl_mes.db /tmp/ccl_mes.db.before-step65.<ts>
shasum -a 256 src/CCL.MES.Web/ccl_mes.db
```

**Phase B (sửa code + verify build)**:
1. Hand-edit 3 migration .cs files → bỏ `type:` strings.
2. `dotnet build src/CCL.MES.Web` → PASS (no compile error).
3. `bash ef-migrate.sh --sqlite add VerifyAffinityFix` →
   - migration mới generated phải EMPTY (Up + Down rỗng) → model identical.
   - Nếu non-empty → có cột nào EF infer khác → fail Phase B, rollback.
4. Remove VerifyAffinityFix nếu empty: `dotnet ef migrations remove -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web`.
5. `MES_PROVIDER=SqlServer bash ef-migrate.sh --sqlserver add VerifyAffinityFix` →
   - migration mới phải EMPTY → model identical từ SQL Server view nữa.
   - Nếu non-empty → SQL Server inference khác → mình PHẢI dùng 3.2.A thay.
   - Remove migration nếu empty.

**Phase C (boot Web app + verify data unchanged)**:
6. `dotnet run --project src/CCL.MES.Web` → DbInitializer no-op (history present).
7. Login admin/admin → access Settings → System Log → search test → tất cả OK.
8. `shasum -a 256 src/CCL.MES.Web/ccl_mes.db` → khớp Phase A. Nếu khác →
   bug, restore từ backup.
9. Row count: WorkCenters=43, RawMaterials=2127, RoutingOperations=38441,
   ManufacturingStructures=20530, Users=5, AuditLogs > 0 sau LOGIN_OK row.

### 3.5 Trade-off Build verify SQL Server WITHOUT Docker apply

Yêu cầu anh đưa ra: "SQL Server side chỉ cần generate/build được (chưa cần
Docker apply)". Phase B step 5 (`MES_PROVIDER=SqlServer ef migrations add
VerifyAffinityFix`) **chỉ cần kết nối design-time** — EF Core build model
in-memory rồi diff với ModelSnapshot. **KHÔNG cần SQL Server server running**.
Đây là cái factory pattern cho.

Verify cuối cùng (`Migrate()` thật trên SQL Server) chỉ làm khi Docker/server
thật xuất hiện (đó là phần "khi nào nâng cấp" — outside Bước 6.5 scope).

---

## 4. Mục tiêu 2 — Standardize SQLite deploy kiểu Ops Control v1.2

### 4.1 Cấu trúc data folder đề xuất

```
CCL-CMES/CCL-MES/
├── data/                          ← NEW canonical data root
│   ├── ccl_mes.db                 ← live SQLite (move from src/CCL.MES.Web/)
│   └── Backup/
│       └── SQLite/
│           ├── ccl_mes.db.bak.snapshot-20260531-...
│           └── ccl_mes.db.bak.pre-restore-...
├── START_SERVER.command           ← NEW macOS launcher
├── START_SERVER.bat               ← NEW Windows launcher
└── ...
```

**Mirror Ops Control's `server/data/`** nhưng ở repo root level vì CCL-CMES
không có equivalent `server/` folder (Web project ở `src/CCL.MES.Web/`).

### 4.2 DATA_DIR resolution code (mirror Ops Control)

Trong `Program.cs` thêm trước `AddInfrastructure`:

```csharp
// Phase 6 Bước 6.5 — Ops Control v1.2 pattern: DATA_DIR env > fallback
// "data" sibling to launch directory (CWD when START_SERVER cd'd repo root).
// All paths resolved to absolute before being handed to EF.
var dataDir = Environment.GetEnvironmentVariable("MES_DATA_DIR")
              ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
dataDir = Path.GetFullPath(dataDir);
Directory.CreateDirectory(dataDir);                          // idempotent
Directory.CreateDirectory(Path.Combine(dataDir, "Backup", "SQLite"));

// Re-write connection string with absolute path (only for SQLite provider).
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
{
    var dbPath = Environment.GetEnvironmentVariable("MES_DB_PATH")
                 ?? Path.Combine(dataDir, "ccl_mes.db");
    builder.Configuration["ConnectionStrings:Default"] = $"Data Source={Path.GetFullPath(dbPath)}";
}

builder.Services.AddInfrastructure(builder.Configuration);
```

**Note**: chỉ override khi provider = Sqlite. SQL Server connection string giữ
nguyên từ appsettings (do operator/admin quản lý). Đây là cái cổng nâng cấp
mở rộng đúng cách.

### 4.3 BackupService — đảm bảo backup folder = `<DATA_DIR>/Backup/SQLite/`

Hai sub-option:

| 4.3.a | Giữ logic hiện tại (snapshot kế bên DB) → snapshot landed tại `<DATA_DIR>/ccl_mes.db.bak.snapshot-*` | Đơn giản, không sửa code. Snapshot lẫn với file DB. |
| 4.3.b | Update `ResolveSqlitePath` trả về `(backupDir, dbFile)` với `backupDir = <DATA_DIR>/Backup/SQLite/`. Đổi tham chiếu trong `CreateSnapshotAsync` + `ListSnapshots`. | Mirror Ops Control's `server/data/Backup/SQLite/`. Folder structure sạch. ~15 LOC sửa. |

**Em đề xuất 4.3.b** — sạch hơn, mirror Ops Control đúng. Phải sửa 1 unit test
nếu có (chưa thấy có test cho BackupService).

### 4.4 START_SERVER scripts

**START_SERVER.command** (macOS) — mirror Ops Control v1.5.10:

```bash
#!/bin/bash
# CCL-MES Server Launcher (macOS)
# Mirror Ops Control v1.2 pattern: cd repo root → tool check → preflight →
# kill old PID on 5000 → banner → run + tee log.

cd "$(dirname "${BASH_SOURCE[0]}")"

# 1. Check dotnet (require .NET 10+)
command -v dotnet || { echo "❌ .NET 10 chưa cài. brew install --cask dotnet-sdk"; exit 1; }
DOTNET_VERSION=$(dotnet --version)
echo "✓ dotnet: $DOTNET_VERSION"

# 2. Build if needed (auto-skip when bin/Release fresh)
[ ! -d "src/CCL.MES.Web/bin/Release" ] && dotnet build -c Release src/CCL.MES.Web

# 3. Preflight: data folder + DB file or migration-ready
mkdir -p data/Backup/SQLite

# 4. Detect LAN IP
LOCAL_IP=$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null)

# 5. Kill old PID on 5000 (Web default ASPNETCORE port)
OLD_PIDS=$(lsof -ti:5000 2>/dev/null)
[ -n "$OLD_PIDS" ] && kill $OLD_PIDS && sleep 1

# 6. Banner
cat <<EOF
  ╔══════════════════════════════════════════════════╗
  ║       CCL-MES — Standalone Server (.NET 10)      ║
  ║       SQLite mode (Ops Control v1.2 pattern)     ║
  ╚══════════════════════════════════════════════════╝
  📍 Local:    http://localhost:5000
  🌐 LAN:      http://${LOCAL_IP:-<n/a>}:5000
  📁 Data:     $(pwd)/data/
  📝 Log:      /tmp/ccl-mes-server.log
  ⏹  Cmd+C để tắt
EOF

# 7. Run
ASPNETCORE_URLS="http://0.0.0.0:5000" \
  dotnet run --project src/CCL.MES.Web --no-launch-profile 2>&1 \
  | tee /tmp/ccl-mes-server.log
```

**START_SERVER.bat** (Windows) — đối xứng, dùng `tasklist` + `netstat`
thay `lsof`, `ipconfig` thay `ipconfig getifaddr`.

### 4.5 BackupRestore script consistency

`scripts/BackupRestore/Program.cs` đã có `MES_DB_PATH` env override (README:
"Override DB path"). Sau Bước 6.5 update README để chuẩn hoá ví dụ với
`MES_DATA_DIR`:

```bash
# Restore từ snapshot
MES_DATA_DIR=/path/to/data dotnet run -- --from ccl_mes.db.bak.snapshot-...
```

### 4.6 Hành vi backward-compat

| Trường hợp khởi động | DB ở đâu | Behavior |
|---|---|---|
| START_SERVER.command (sau Bước 6.5) | `data/ccl_mes.db` | DbInitializer baseline trên DB MỚI vì empty → tạo schema + seed admin/5 demo user. **Mất 60k row hiện tại** nếu không migrate `src/CCL.MES.Web/ccl_mes.db` sang. |
| `dotnet run --project src/CCL.MES.Web` (legacy, từ repo root) | `data/ccl_mes.db` nếu code 4.2 lay xuống | Tự động dùng data/ → cùng vấn đề trên |
| Set `MES_DB_PATH=src/CCL.MES.Web/ccl_mes.db` | Path cũ | Hoạt động đúng với DB cũ |

→ **Bước 6.5 PHẢI move (copy + delete) `src/CCL.MES.Web/ccl_mes.db` → `data/ccl_mes.db`** trước khi chạy app lần đầu sau update. Đây là **bước thủ công** trong checklist deploy, có A→B→C bảo vệ.

---

## 5. Mục tiêu 3 — Doc "Cách nâng cấp lên SQL Server"

### 5.1 File output đề xuất

`docs/HOW-TO-UPGRADE-TO-SQLSERVER.md` (new, plain markdown).

### 5.2 Outline nội dung

```
# Cách nâng cấp CCL-MES từ SQLite sang SQL Server

## 0. Khi nào CẦN nâng cấp
- DB > ~10 GB hoặc > 10M rows
- Concurrent users > 100
- Cần HA/cluster/replication
- Compliance bắt buộc T-SQL audit (SQL Server Audit objects)

## 1. Tiền điều kiện
- SQL Server 2019+ instance (on-prem hoặc Azure SQL)
- Network reachable từ MES Web server (port 1433 mở)
- SQL login với role db_owner trên DB CCL_MES
- Tài khoản backup current SQLite (snapshot qua Settings → Backup)

## 2. Quy trình A→B→C

### Phase A — Backup tường minh
1. Vào Settings → Backup → Create snapshot (qua UI)
2. SCP snapshot xuống local + verify SHA256

### Phase B — Migrate schema trên SQL Server
1. Tạo DB rỗng trên SQL Server: `CREATE DATABASE CCL_MES`
2. `MES_PROVIDER=SqlServer MES_CONNSTR="..." bash ef-migrate.sh --sqlserver`
3. Verify schema bằng SSMS: 19 tables + indexes + foreign keys

### Phase C — ETL data
- Option C1: dùng `scripts/EtlSqliteToSqlServer/` (sẽ tạo khi cần)
- Option C2: SQL Server Import Wizard từ SQLite ODBC driver

## 3. Switch provider
1. Edit `appsettings.Production.json`:
   ```json
   {
     "Database": { "Provider": "SqlServer" },
     "ConnectionStrings": {
       "Default": "Server=...;Database=CCL_MES;User Id=...;Password=...;..."
     }
   }
   ```
2. Restart service
3. Boot log phải in: "DbInitializer: Migrate() applied 0 pending migrations"

## 4. Verify post-upgrade
- Login admin/admin → Audit log row LOGIN_OK xuất hiện trong SQL Server
- Row count khớp SQLite snapshot
- Performance baseline ghi nhận
- Backup tab hiện guidance card SSMS (không có nút Snapshot)

## 5. Rollback nếu fail
- Stop service
- Edit `appsettings.Production.json` → Database:Provider = Sqlite + connection cũ
- Restart → DbInitializer dùng SQLite path cũ
- Data SQLite chưa bị đụng (Phase A backup vẫn intact)
```

### 5.3 Vì sao doc này quan trọng cho cổng nâng cấp

Hiện scaffolding SQL Server đã có (provider switch + appsettings.SqlServer.json
+ factory + DbInitializer cross-provider + BackupService guidance), nhưng:
- **Không** ai sẽ nhớ khi nào cần đụng cái gì sau 3-6 tháng quên hết.
- Operator/IT khi nâng cấp sẽ search Google → tự build quy trình → có thể
  bỏ sót bước (vd quên backup TOTP key trước migrate).

Doc này = **runbook chuẩn**, mirror các runbook trong Ops Control v1.2's
CLAUDE.md ("OPS_EXPORT_HMAC_KEY lost or rotated mid-cycle", "Bare-metal
restore", v.v.) — đã chứng minh ROI cao khi cần.

---

## 6. Rủi ro + Mitigation

### 6.1 Rủi ro

| ID | Rủi ro | Severity | Mitigation |
|---|---|---|---|
| R1 | Hand-edit migration → ModelSnapshot drift → next migration sinh diff không mong | **High** | Phase B step 3+5 sinh empty migration verify. Block PR nếu non-empty. |
| R2 | SQLite live DB ở `src/CCL.MES.Web/ccl_mes.db` mất khi move → mất 60k row NPI | **Critical** | Phase A backup tường minh + SHA256. Move = COPY trước, verify khớp, mới xoá file gốc. |
| R3 | Connection string override trong Program.cs ghi đè giá trị test/dev đang dùng | Medium | Chỉ ghi đè khi `Database:Provider == "Sqlite"`. SQL Server path untouched. |
| R4 | START_SERVER.command port 5000 conflict với dev khác (vd React dev server) | Low | Document trong banner; user có thể override `ASPNETCORE_URLS=http://0.0.0.0:5001`. |
| R5 | BackupService 4.3.b sửa folder structure → ListSnapshots() trả empty trên path cũ → operator nghĩ mất backup | Medium | Migrate snapshot files cũ (`src/CCL.MES.Web/ccl_mes.db.bak.snapshot-*`) sang `data/Backup/SQLite/` khi move DB. Idempotent script. |
| R6 | Per-provider migrations (nếu chọn 3.2.A) ModelSnapshot×2 desync khi add migration mới | Medium-High (chỉ áp dụng nếu chọn A) | Document trong HOW-TO-UPGRADE doc: phải add cả 2 cùng lúc. CI check ModelSnapshot identical (trừ tên namespace). |
| R7 | `MES_DATA_DIR` env name conflict với env khác (vd `MES_PROVIDER` đã có) | Low | Naming consistent: tất cả prefix `MES_`. Document trong README. |
| R8 | Doc HOW-TO-UPGRADE outdated khi sau này thêm migration | Low | Section "Khi cập nhật doc này" cuối doc: thêm migration mới = update step 2.2. |

### 6.2 Mitigation chính: A→B→C cho từng touchpoint

| Thay đổi | Phase A backup | Phase B test | Phase C apply |
|---|---|---|---|
| Hand-edit 3 migration `type:` strings | Copy 3 file .cs sang `/tmp/migrations-before-65/` | `dotnet build` + `ef migrations add VerifyNoChange` empty | Commit + push branch |
| Add Program.cs DATA_DIR resolution | Copy Program.cs sang `/tmp/Program.cs.before-65` | `dotnet build` + smoke test `dotnet run` → DB ở chỗ mới | Verify SHA256 SQLite không đổi sau khi move |
| Move `src/CCL.MES.Web/ccl_mes.db` → `data/ccl_mes.db` | SHA256 source DB + cp tới `/tmp/ccl_mes.db.before-move` | Verify SHA target = SHA source | Delete source DB |
| Update BackupService.ResolveSqlitePath (4.3.b) | Copy BackupService.cs + Backup.razor | `dotnet build` + manual snapshot test | Verify snapshot landed ở `data/Backup/SQLite/` |
| Add START_SERVER.command + .bat | none (new files) | `bash START_SERVER.command` từ repo root → app boot | git add |
| Create docs/HOW-TO-UPGRADE-TO-SQLSERVER.md | none (new file) | none | git add |

---

## 7. Branch base + scope + câu hỏi cần anh quyết

### 7.1 Branch base

Như Bước 5/6, stack trên feat/phase6-audit-log (PR #15 đang open). Branch
mới: `feat/phase6-deploy-sqlite-and-sqlserver-gate`.

Khi PR #15 merge, branch tự rebase. Nếu PR #15 không merge trước Bước 6.5
xong → vẫn ship PR Bước 6.5 dạng stack, mention PR #15 trong description.

### 7.2 Phạm vi PR đề xuất

| Sub-step | Mô tả | Mục tiêu | Effort ước |
|---|---|---|---|
| 6.5.1 | Sửa 3 migration drop explicit `type:` strings (3.2.B) | Mục tiêu 1 | ~50 LOC edit |
| 6.5.2 | Verify `dotnet ef migrations add VerifyNoChange` empty cho cả Sqlite + SqlServer | Mục tiêu 1 verify | 0 LOC (test only) |
| 6.5.3 | Program.cs DATA_DIR + DB path absolute resolution | Mục tiêu 2 | ~20 LOC |
| 6.5.4 | Move `src/CCL.MES.Web/ccl_mes.db` → `data/ccl_mes.db` (commit script + .gitignore update) | Mục tiêu 2 | 0 LOC code + script + .gitignore |
| 6.5.5 | BackupService.ResolveSqlitePath update + backup folder = `<DATA_DIR>/Backup/SQLite/` (4.3.b) | Mục tiêu 2 | ~15 LOC |
| 6.5.6 | START_SERVER.command (macOS) + START_SERVER.bat (Windows) | Mục tiêu 2 | ~120 LOC bash + bat |
| 6.5.7 | docs/HOW-TO-UPGRADE-TO-SQLSERVER.md | Mục tiêu 3 | ~150 LOC docs |
| 6.5.8 | README + MAINTAINERS update — point at START_SERVER + HOW-TO-UPGRADE | Cosmetic | ~30 LOC |

Tổng ~400 LOC, 1 PR. Lớn nhưng các sub-step độc lập, đi tuần tự A→B→C.

### 7.3 Câu hỏi cần anh quyết

| Q | Câu hỏi | Em đề xuất | Anh chọn |
|---|---|---|---|
| **Q1** | Phương án fix migration affinity — 3.2.A (per-provider folders) vs 3.2.B (drop `type:` strings) vs 3.2.C (conditional)? | **3.2.B** — đơn giản nhất, EF inference per provider tự đúng, không phình ModelSnapshot. R1 mitigated bằng verify "add migration empty". | ? |
| **Q2** | Vị trí data folder — `<repo-root>/data/` hay `<repo-root>/server/data/` (mirror Ops Control 1:1)? | **`<repo-root>/data/`** — CCL-CMES không có `server/` folder; trực diện hơn. | ? |
| **Q3** | Port mặc định START_SERVER — 5000 (ASPNETCORE default) hay 3000 (mirror Ops Control)? | **5000** — đúng convention .NET, tránh đụng React dev (5173) + Ops Control dev (3000) khi chạy song song trên cùng máy. | ? |
| **Q4** | Backup folder structure — 4.3.a (flat next to DB) hay 4.3.b (`<DATA_DIR>/Backup/SQLite/`)? | **4.3.b** — mirror Ops Control, separation of concerns sạch hơn. | ? |
| **Q5** | DB file naming khi move — giữ `ccl_mes.db` hay đổi `ccl-mes.db` (kebab-case như Ops Control's `ops.db`)? | **giữ `ccl_mes.db`** — không đổi tên giảm chỗ phải update (BackupRestore script, ResolveSqlitePath glob pattern, migration baseline). | ? |
| **Q6** | Env var override naming — `MES_DATA_DIR` + `MES_DB_PATH` (consistent với MES_PROVIDER có sẵn) hay đổi prefix? | **`MES_*` prefix** — consistent. BackupRestore script đã có `MES_DB_PATH`. | ? |
| **Q7** | Tách HOW-TO-UPGRADE-TO-SQLSERVER.md ra file riêng hay nhét trong README.md? | **File riêng** — dài + có table SQL → README sẽ phình. Link từ README sang. | ? |
| **Q8** | Move file `ccl_mes.db` — làm trong PR (commit DB move như normal file rename) hay làm sau qua manual script? | **Manual sau** — git LFS không setup; commit file 11 MB phình repo. Hướng dẫn trong PR description: operator chạy script di chuyển ra ngoài git, đẩy data/ vào .gitignore. | ? |
| **Q9** | `dotnet ef migrations add VerifyNoChange` trong Phase B — empty migration thì remove ngay, hay commit + revert PR sau cho audit trail? | **Remove ngay**, log kết quả trong PR description (số dòng Up/Down = 2 dòng method body trống). | ? |
| **Q10** | START_SERVER có nên có "auto-build client" như Ops Control v1.2 (auto `vite build` khi không thấy `client/dist`)? | **Không** — CCL-CMES dùng Blazor Server, không có client/ folder build separate. `dotnet build` đủ. | ? |
| **Q11** | Nếu chọn 3.2.B: có nên add CI check (Bước 7+) ngăn ai đó add lại `type: "TEXT"` vào migration sau? | **Có**, nhưng defer sang Phase 7 — Bước 6.5 chỉ trace bằng PR description warning. | ? |
| **Q12** | BackupService `_message` localization trong Backup.razor — sau khi đổi folder structure, message text có cần update không? | **Không** — message chung "Created snapshot {0}" không nói folder. Nếu sau muốn add full path → thay key. | ? |

---

## 8. STOP — chờ phương án

Khảo sát xong. Doc untracked, **chưa code, chưa branch**.

Em chờ anh chốt **Q1–Q12** rồi triển khai 1 PR `feat/phase6-deploy-sqlite-and-sqlserver-gate` stack trên `feat/phase6-audit-log`, 8 sub-step 6.5.1→6.5.8 theo thứ tự A→B→C, mỗi sub-step verify trước khi sang sub-step kế.

Sau Bước 6.5 → Bước 7 (IQC entity + tab close stub PR #13) → close-out Phase 6.

**Không đụng**: `Ops Control v1.2/`, `CMES/`, `Old ver ( DO NOT USE)/`, `SpecHub/`.
