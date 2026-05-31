# CCL Design – MES (MVP)

Khung mẫu **Manufacturing Execution System** cho nhà máy in nhãn/label, chạy được bằng `dotnet run`.

- **Nền tảng:** .NET 8 (ASP.NET Core) · EF Core · **SQLite** (dev) · Blazor Server · Swagger
- **Modules:** Work Order Control + Process Flow (7 bước) · Spec Control · QC (IPQC/FQC/OQC) · **OEE/Production Log** · **Work Instruction số hóa** · **Dashboard**
- **Kiến trúc:** Clean Architecture — Domain / Application / Infrastructure / Web

## 1. Yêu cầu
- .NET SDK 8.0 trở lên (đã test với .NET 10): https://dotnet.microsoft.com/download
- Project target: **net10.0**

## 2. Chạy ứng dụng

### 2.a Standalone server (Ops Control v1.2 pattern — Phase 6 Bước 6.5)

```bash
# macOS — double-click file Finder hoặc chạy từ Terminal:
bash START_SERVER.command

# Windows — double-click file Explorer hoặc cmd:
START_SERVER.bat
```

Server bind `0.0.0.0:5050` (LAN-reachable). Banner in localhost + LAN URL +
data dir + log path. Cmd+C / Ctrl+C để tắt.

- **Data folder**: `<repo-root>/data/ccl_mes.db` (override `MES_DATA_DIR=...`).
- **Backup snapshot**: `<repo-root>/data/Backup/SQLite/`.
- **Log**: `/tmp/ccl-mes-server.log` (macOS).

### 2.b Dev launch (qua launchSettings — port 5080)

```bash
cd duong/dan/toi/CCL.MES.MVP
dotnet restore
dotnet run --project src/CCL.MES.Web
```

Mặc định dev: `http://localhost:5080` (port từ `launchSettings.json`).

- Work Order (Blazor): `http://localhost:5080/workorders`
- Swagger UI (API):    `http://localhost:5080/swagger`

Lần chạy đầu tiên hệ thống tự tạo file `data/ccl_mes.db` (SQLite, Phase 6
Bước 6.5 layout) và seed sẵn: khách hàng **Brady Asia**, sản phẩm
**BRD-7656-D**, 1 Spec đã duyệt, WO mẫu **WO-26-3683**, 5 demo user
(admin/supervisor/engineer/qc/operator).

## 3. Thử luồng 7 bước (trên màn hình /workorders)
Mỗi WO đi qua: `Pre-press → OP Setting → IPQC → Ready to Run → Running → FQC → OQC → Closed`.
State machine chỉ cho **Advance** khi thỏa điều kiện (guard). Để demo nhanh:

1. Bấm **Mở khóa bước** → set MaterialsReady / SetupConfirmed / ProducedQty / RoHS.
2. Tại bước có cửa kiểm (IPQC/FQC/OQC) bấm **QC … Pass** để tạo + duyệt phiếu kiểm.
3. Bấm **Advance »** để sang bước kế. Nếu chưa đủ điều kiện, hệ thống báo lý do.

## 4. Thử bằng API (Swagger hoặc curl)
```bash
# Danh sách WO
curl http://localhost:5080/api/workorders

# Mở khóa điều kiện cho WO id=1
curl -X POST http://localhost:5080/api/workorders/1/flags \
  -H "Content-Type: application/json" \
  -d '{"materialsReady":true,"setupConfirmed":true,"rohsOk":true,"producedQty":12000}'

# Chuyển bước
curl -X POST "http://localhost:5080/api/workorders/1/advance?user=henry"

# Tạo phiếu QC (IPQC=0, FQC=1, OQC=2)
curl -X POST http://localhost:5080/api/qc/inspections \
  -H "Content-Type: application/json" \
  -d '{"workOrderId":1,"type":"IPQC","inspectorId":"qc01","sampleSize":20,"details":[{"itemName":"Visual","pass":true,"qty":20}]}'

# Duyệt phiếu QC id=1 (Pass)
curl -X POST "http://localhost:5080/api/qc/inspections/1/approve?pass=true&user=qc.lead"

# Duyệt Spec version id=1
curl -X POST "http://localhost:5080/api/specs/versions/1/approve?user=qa.lead"
```

## 4b. Module OEE & Dashboard

Tại bước **5. Running** trên màn hình Work Orders sẽ hiện thêm các nút **Start / Pause / Resume / Finish**:
- **Start/Resume** mở một khoảng `Run`, **Pause** đóng khoảng Run và mở khoảng `Stop` (dừng máy).
- **Finish** đóng khoảng đang mở, ghi Good/Reject và cộng vào `ProducedQty` của WO.
- Các khoảng này lưu vào bảng `ProductionLogs`, là dữ liệu để tính **OEE**.

Mở **Dashboard** (`/dashboard`) để xem KPI tổng quan + bảng OEE theo máy:

```
Availability = Run / (Run + Stop + Setup)
Performance  = (IdealCycleTime × TotalCount) / Run     (chặn trần 100%)
Quality      = Good / (Good + Reject)
OEE          = Availability × Performance × Quality
```

> Công thức đã được đối chiếu khớp ví dụ chuẩn ngành (Vorne): A=88.8%, P=86.1%, Q=97.8%, **OEE=74.8%**.

Để có số OEE đẹp khi demo: ở bước Running bấm **Start**, đợi vài giây, bấm **Finish** — Run time sẽ có giá trị,
Quality = 100% (reject = 0). Muốn thấy downtime, bấm **Pause** một lúc rồi **Resume**.

## 5. Cấu trúc dự án
```
CCL.MES.sln
src/
  CCL.MES.Domain          # Entities, Enums, WorkOrderStateMachine (7 bước + guard)
  CCL.MES.Application      # Services (WO/Spec/QC/OEE/WI), DTOs, IMesDbContext
  CCL.MES.Infrastructure  # EF Core DbContext (SQLite), DbSeeder, DI
  CCL.MES.Web             # API + Swagger + Blazor (Dashboard, WO, Work Instructions)
```

## 5b. Công cụ Python (tools/)
Bộ script hỗ trợ — xem `tools/README.md`:
- `verify_oee.py` — kiểm chứng công thức OEE (dùng cho CI).
- `oee_from_csv.py` — tính OEE từ file log CSV.
- `seed_from_excel.py` — ETL nạp master data từ Excel/CSV vào SQLite.

## 6. Chuyển sang SQL Server (production)

**Trạng thái production hiện tại**: SQLite kiểu Ops Control v1.2 (`data/ccl_mes.db`).
SQL Server scaffolding **đã sẵn sàng** làm cổng nâng cấp (Phase 6 Bước 6.5 đã
fix migration provider-affinity — `INTEGER/TEXT/REAL` → SQL Server inference
emit `bigint/nvarchar(max)/float` đúng convention).

**Khi nào & cách nâng cấp**: xem runbook chi tiết tại
[`docs/HOW-TO-UPGRADE-TO-SQLSERVER.md`](docs/HOW-TO-UPGRADE-TO-SQLSERVER.md).
Quy trình A→B→C: backup SQLite + SHA256 → migrate schema SQL Server rỗng →
ETL data → switch `Database:Provider` + connection string → verify post-upgrade.

Tóm tắt 3 dòng cho người vội:
1. Provider switch qua `appsettings.Production.json` (`"Database": { "Provider": "SqlServer" }` + connection string đúng).
2. Apply migrations: `MES_PROVIDER=SqlServer bash ef-migrate.sh --sqlserver`.
3. Restart app — DbInitializer baseline-aware tự no-op sau khi schema match.

## 6b. Realtime (SignalR)
Dashboard và màn hình Work Orders kết nối hub `/hubs/shopfloor`. Mỗi khi có thay đổi
(Advance, QC, Start/Pause/Finish), mọi client đang mở sẽ **tự cập nhật** mà không cần F5.
Dashboard có chỉ báo `● live`.

## 6c. Auth + RBAC (Phase 2 + Phase 5 Bước 1 + Phase 6 Bước 4)
- Cookie auth (`ccl_mes_auth`, HttpOnly, SameSite=Lax, 8h sliding) qua `Microsoft.AspNetCore.Authentication.Cookies`.
- Password hash: `PasswordHasher<User>` (PBKDF2 100k iter SHA256 + 128-bit salt + 256-bit hash).
- Global `FallbackPolicy = RequireAuthenticatedUser` — mọi page / API yêu cầu đăng nhập trừ khi gắn `[AllowAnonymous]` (chỉ Login + Logout + SetLanguage).
- **5 demo accounts** (Phase 6 Bước 4 mở rộng từ 2 role lên 5, idempotent seed):
  - **`admin / admin`** — Role=Admin (full access)
  - **`supervisor / supervisor`** — Role=Supervisor (NPI + QC read + approve)
  - **`engineer / engineer`** — Role=Engineer (NPI write + Spec authoring)
  - **`qc / qc`** — Role=QC (NPI read + QC write)
  - **`operator / operator`** — Role=Operator (WO Start/Pause/Resume/Finish only)
  - Đổi password trước khi đưa lên production.
- Đăng nhập: `/login` (Razor Page). Đăng xuất: `POST /logout`. Self-change pwd: `Settings → My Password`. Admin reset pwd cho user khác: `Settings → Account Control → Edit → Reset password`.
- **RBAC enforce** — defence-in-depth 3 layer:
  - **Layer 1 — UI hide**: `<AuthorizeView Roles="...">` ẩn dropdown item theo role
  - **Layer 2 — Page policy**: `@attribute [Authorize(Policy="...")]` trên Razor page
  - **Layer 3 — Service check**: server-side role validate trong UserAdminService + IqcService mutation methods
- **4 page-level policy**:
  - `AdminOnly` = {Admin} — Settings/Account, /data, /syslog
  - `NpiRead` = {Admin, Supervisor, Engineer, QC} — /npi/*
  - `NpiSpecRead` = {Admin, Supervisor, Engineer} — /npi/engineer-spec
  - `QcRead` = {Admin, Supervisor, QC} — /qcqa/*
- **Invariants** (Bước 4): cấm self-modify role/active, cấm demote/disable Admin cuối cùng. Console recovery: `scripts/RecoverAdmin/`.

## 6c-bis. SignalR hub auth (Phase 5 Bước 2)
- `/hubs/shopfloor` **không còn** `AllowAnonymous()` — đi qua `FallbackPolicy`.
- Phương án: scoped `HubCookieAccessor` capture `ccl_mes_auth` cookie từ `_Host.cshtml` (chỗ HttpContext còn sống) → forward qua `HubConnectionBuilder.WithUrl(opts.Cookies.Add(new Cookie(...)))` trong `Dashboard.razor` + `WorkOrders.razor`.
- Anonymous negotiate → 401. Authenticated → 200 + connectionId. Logout-relogin cùng tab → cookie stale **không** xảy ra (forceLoad teardown circuit sạch).
- Chi tiết: [`docs/PHASE5-STEP2-PLAN.md`](docs/PHASE5-STEP2-PLAN.md).

## 6d. i18n (Phase 1 + 4)
- `IStringLocalizer<SharedResource>` đọc 2 resource:
  - `src/CCL.MES.Web/Resources/SharedResource.resx` — neutral (= EN, `NeutralLanguage=en` trong .csproj nhúng vào main DLL).
  - `src/CCL.MES.Web/Resources/SharedResource.vi.resx` — VI satellite.
- ~160 keys phủ MainLayout / Login / Index / Dashboard / Work Orders / Work Instructions / 4 NPI / 3 QC / 10 Settings.
- Default UI culture = EN. Order resolve culture: cookie `.AspNetCore.Culture` → `Accept-Language` → default EN.
- Switch ngôn ngữ: click cờ trên topbar / login → `GET /set-language?lang=en|vi&returnUrl=…` ghi cookie + 302 quay lại. Cookie sống 1 năm.
- SVG flag inline (`Shared/Flags/FlagGB.razor` + `FlagVN.razor`) — đa OS render đồng nhất, không phụ thuộc emoji 🇬🇧 🇻🇳.
- Coverage verified: EN 100% Anh / VI 100% Việt (xem [`docs/FINAL-REPORT-2026-05-31.md`](docs/FINAL-REPORT-2026-05-31.md) §3 + 14 screenshot trong `docs/screenshots/`).

## 6e. Settings dropdown (Phase 3 + Phase 5 Bước 1 + Phase 6 Bước 2A/2B/4 + chore #18)
- Header dropdown thứ 3 (cạnh `NPI Data` + `QC/QA Data`): `Settings ▾`.
- 9 sub-tab (Phase 6 close-out — bỏ "Import data v1.0" stub qua PR #18):
  - **User group** (Phase 6 Bước 2A — UI thật): My Profile · My Password · Appearance · Hardware devices (placeholder) · Connection mode (placeholder)
  - **System group** (Phase 6 Bước 2B — UI thật + admin-only):
    - Account Control [admin] — list / create / edit / reset pwd / toggle active (Bước 4 mutations)
    - About / Diagnostics — app version + provider + row counts NPI + Users + Specs + audit count
  - **Maintenance group** (Phase 6 Bước 2B + 5 — UI thật + admin-only):
    - Backup / Restore [admin] — SQLite snapshot list + Take new snapshot
    - System Logs [admin] (Phase 6 Bước 5) — AuditLog filter grid (date from/to + action + actor)
- **Phase 5 RBAC enforce** + Phase 6 Bước 4 expanded: `AdminOnly` policy gate cho 3 admin tab. Operator URL trực tiếp → `<AccessDenied />` render qua `<NotAuthorized>` slot trong `App.razor`.

## 6f. Error code i18n (Phase 5 Bước 3)
- Backend không còn trả error string EN hardcoded. `WorkOrderStateMachine.cs` + `WorkOrderService.cs` emit `WoErrorCode` enum (9 value):
  - `AlreadyAtFinalStep`, `RequiresSpecAndMaterials`, `RequiresSetupConfirmed`, `IpqcNotPassed`, `NoProductionYet`, `FqcNotPassed`, `OqcOrRohsNotMet`, `InvalidStepTransition`, `WorkOrderNotFound`
- Web layer (`Services/WoErrorKeys.cs`) map code → resource key `workorders.error.*`. UI tiêu thụ qua `Loc[WoErrorKeys.KeyFor(res.ErrorCode)]`.
- API wire format: `POST /api/workorders/{id}/advance` trả `{"ok": false, "errorCode": "RequiresSetupConfirmed", "currentStep": "OpSetting"}` (enum NAME qua `JsonStringEnumConverter` đã register).
- Khoá EN+VI: 10 key/locale (9 mapped + 1 unknown fallback) — đóng gap dynamic error portion vẫn EN giữa VI message của Phase 4.

## 6g. EF Migrations cho SQLite (Phase 5 Bước 4)
- **Trước Phase 5**: SQLite dùng `EnsureCreated()` → schema-change phải xoá DB + reimport.
- **Sau Phase 5**: cả SQLite + SQL Server đi qua `DbInitializer.InitializeAsync()` (cross-provider).
- Init migration: `Infrastructure/Migrations/20260531050444_Init.cs` (19 CreateTable + 22 CreateIndex khớp 100% live schema).
- `DbInitializer` baseline-aware:
  - DB mới (no tables) → `Migrate()` tạo schema + record Init
  - DB cũ (tables + no `__EFMigrationsHistory`, từ thời `EnsureCreated`) → baseline insert qua `IHistoryRepository.GetCreateScript()` + `GetInsertScript(HistoryRow)` rồi Migrate no-op. **60k+ row NPI nguyên vẹn**.
  - Subsequent restart → Migrate no-op (history hợp lệ).
- Schema change tương lai:
  ```bash
  bash ef-migrate.sh --sqlite add <Name>   # tạo migration
  # Hoặc trực tiếp:
  MES_PROVIDER=Sqlite dotnet ef migrations add <Name> -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web -o Migrations
  # Restart app → DbInitializer áp tự động
  ```
- `ef-migrate.sh` 2-mode: `--sqlite` (mặc định) | `--sqlserver`. Subcommand `add <Name>` để tạo migration.
- Chi tiết: [`docs/PHASE5-STEP4-PLAN.md`](docs/PHASE5-STEP4-PLAN.md).

## 6h. IQC (Phase 6 Bước 7)

Tab **QC/QA → IQC** (`/qcqa/iqc`) — Incoming Quality Check cho nguyên
liệu nhập kho. Khác IPQC/FQC/OQC ở chỗ:

- Gắn với **raw-material batch** (PartNo + Batch + Lot + ReceivedDate +
  Supplier), KHÔNG gắn WorkOrder
- Entity riêng `IqcInspection` + `IqcResultDetail` (xem `src/CCL.MES.Domain/Entities/Iqc.cs`)
- FK hybrid: `RawMaterialId` nullable optional + `PartNo` snapshot bắt buộc
- Fail KHÔNG cascade `WO.Status=OnHold` (pre-WO; operator quarantine ngoài app)
- Audit emit `IQC_CREATE` + `IQC_APPROVE`

Workflow: New IQC (modal form + Details inline) → Pending → Approve
Pass/Fail. Seed sẵn 3 demo IQC (Pending / Pass / Fail) trên DB rỗng.

## 7. Hướng mở rộng (theo tài liệu kiến trúc)
- Module OEE / Production Log (Start/Pause/Resume/Finish, tính OEE theo máy).
- Work Instruction số hóa; SignalR realtime cho dashboard.
- RBAC (Entra ID/AD); tích hợp SAP & Warehouse.

## 7b. TODO Phase 7+ (sau Phase 6 wrap)
> Phase 6 đã đóng 6/8 TODO Phase 5 (NPI Engineer Spec, 6 Settings tab, IPQC+OQC+IQC, RBAC 5-role, AuditLog, Deploy gate fix). Xem [`docs/PHASE6-REPORT-2026-05-31.md`](docs/PHASE6-REPORT-2026-05-31.md).
- **Docker SQL Server verify** — Phase 6 Bước 6.5 đã strip type-affinity → migration provider-agnostic. Cần Docker SQL Server image + `ef-migrate.sh --sqlserver` + apply 4 migration verify clean.
- **System log file viewer** — Syslog tab hiện chỉ đọc AuditLog table (DB events). Bổ sung tab/section đọc text log file (`logs/cclmes-*.log`) cho IIS error / migration messages.
- **Retention + export CSV audit** — `AuditLog` chưa có cleanup policy. Cần admin UI: filter range + export CSV cho compliance + delete events > N days.
- **Test framework** — Phase 6 vẫn chưa có unit test. Phase 7 add xUnit cho Domain.StateMachine + Application.Services + IqcService; Playwright cho login + 5-role flows.
- **Hub auth reconnect** sau 8h cookie expire — rủi ro thấp với operator MES, defer Phase 7+.
- **IPQC + OQC create modal** — hiện chỉ grid + filter. Cần create + approve modal pattern y hệt IQC Bước 7 (đã chứng minh).
- **PERMISSION_MATRIX.md** — publish matrix từ `docs/PHASE6-STEP4-PLAN.md §2.C` thành file riêng cho ops onboarding.

> Lưu ý: project target **net10.0** (khớp .NET SDK 10 của bạn). Nếu dùng .NET 8/9, đổi <TargetFramework> trong 4 file .csproj về net8.0/net9.0 và version các package EF/Extensions tương ứng.
