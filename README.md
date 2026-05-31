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

## 6c. Auth + RBAC (Phase 2 + Phase 5 Bước 1)
- Cookie auth (`ccl_mes_auth`, HttpOnly, SameSite=Lax, 8h sliding) qua `Microsoft.AspNetCore.Authentication.Cookies`.
- Password hash: `PasswordHasher<User>` (PBKDF2 100k iter SHA256 + 128-bit salt + 256-bit hash).
- Global `FallbackPolicy = RequireAuthenticatedUser` — mọi page / API yêu cầu đăng nhập trừ khi gắn `[AllowAnonymous]` (chỉ Login + Logout + SetLanguage).
- Demo accounts (idempotent seed):
  - **`admin / admin`** — Role=Admin (Phase 2)
  - **`operator / operator`** — Role=User (Phase 5 Bước 1, để test RBAC)
  - Đổi password trước khi đưa lên production.
- Đăng nhập: `/login` (Razor Page). Đăng xuất: `POST /logout`. Reset password: chưa có UI, chỉ xoá DB + restart để re-seed.
- **RBAC enforce** (Phase 5 Bước 1) — defence-in-depth 2 layer cho 4 admin-only sub-tab Settings (`account`, `data`, `syslog`, `import-legacy`):
  - **Layer 1 — UI hide**: `<AuthorizeView Roles="Admin">` quanh 4 dropdown item trong `MainLayout.razor`. Operator thấy 6 items; admin thấy 10.
  - **Layer 2 — Route gate**: `@attribute [Authorize(Policy = "AdminOnly")]` trên 4 Razor page. Operator gõ URL trực tiếp → render `<AccessDenied />` component (i18n EN+VI) qua `<NotAuthorized>` slot trong `App.razor`.
- Policy mới: `AdminOnly = RequireRole("Admin")` cùng `FallbackPolicy` đã có.

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

## 6e. Settings dropdown (Phase 3 + Phase 5 Bước 1)
- Header dropdown thứ 3 (cạnh `NPI Data` + `QC/QA Data`): `Settings ▾`.
- 10 sub-tab y hệt §2.3 audit doc trích từ Ops Control v1.2, đúng tên + đúng thứ tự, KHÔNG thêm KHÔNG bớt:
  - **User**: My Profile · My Password · Appearance · Hardware devices · Connection mode
  - **System**: Account Control [admin] · About / Diagnostics
  - **Maintenance**: Backup / Restore [admin] · System Logs [admin] · Import data v1.0 [admin]
- Mỗi sub-tab = 1 Razor page tại `/settings/<slug>` với placeholder content (3 bullet point + lead). KHÔNG có nghiệp vụ thực — TODO sprint sau.
- 4 sub-tab admin-only (Account Control / Backup / System Logs / Import data) — **Phase 5 đã enforce RBAC**: dropdown hide với operator + URL trực tiếp render `<AccessDenied />`. Xem §6c.

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

## 7b. TODO Phase 6+ (sau Phase 5 wrap)
> Phase 5 đã đóng 4/5 TODO Phase 4 (RBAC, hub auth, error-code, EF Migrations). Xem [`docs/PHASE5-REPORT-2026-05-31.md`](docs/PHASE5-REPORT-2026-05-31.md).
- **Nội dung nghiệp vụ thực** cho 3 QC tab + 1 NPI tab (Engineer Spec) + 10 Settings tab — tất cả đang là placeholder.
- **Deploy SQL Server thật** — Phase 5 Bước 4 đã chuẩn bị provider-agnostic migration + `ef-migrate.sh --sqlserver`. Cần ops chạy `appsettings.SqlServer.json` + verify trên SQL Server instance.
- **RBAC roles ngoài Admin/User** — Phase 5 chỉ có 2 role. Future: Supervisor (xem dashboard + duyệt QC), Operator (chỉ Start/Pause/Resume/Finish), QA Lead, etc.
- **Hub auth — reconnect sau 8h cookie expire** — khi circuit sống idle >8h, cookie sliding refresh cần re-fetch. Hiện chưa giải quyết (rủi ro thấp với operator MES bình thường).
- **Audit log cho RBAC events** — RBAC violations + role changes nên log vào audit history. Hiện chưa có audit log entity.
- **Test suite** — chưa có unit test framework. Phase 6 nên thêm xUnit cho Domain + Application; Playwright cho Blazor flow.

> Lưu ý: project target **net10.0** (khớp .NET SDK 10 của bạn). Nếu dùng .NET 8/9, đổi <TargetFramework> trong 4 file .csproj về net8.0/net9.0 và version các package EF/Extensions tương ứng.
