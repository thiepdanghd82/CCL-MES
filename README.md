# CCL Design – MES (MVP)

Khung mẫu **Manufacturing Execution System** cho nhà máy in nhãn/label, chạy được bằng `dotnet run`.

- **Nền tảng:** .NET 8 (ASP.NET Core) · EF Core · **SQLite** (dev) · Blazor Server · Swagger
- **Modules:** Work Order Control + Process Flow (7 bước) · Spec Control · QC (IPQC/FQC/OQC) · **OEE/Production Log** · **Work Instruction số hóa** · **Dashboard**
- **Kiến trúc:** Clean Architecture — Domain / Application / Infrastructure / Web

## 1. Yêu cầu
- .NET SDK 8.0 trở lên (đã test với .NET 10): https://dotnet.microsoft.com/download
- Project target: **net10.0**

## 2. Chạy ứng dụng
```bash
# QUAN TRONG: cd vao dung thu muc da giai nen (noi co file CCL.MES.sln)
cd duong/dan/toi/CCL.MES.MVP
dotnet restore
dotnet run --project src/CCL.MES.Web
```
Mặc định chạy tại: `http://localhost:5080`

- Giao diện Work Order (Blazor):  `http://localhost:5080/workorders`
- Swagger UI (API):              `http://localhost:5080/swagger`

Lần chạy đầu tiên hệ thống tự tạo file `ccl_mes.db` (SQLite) và seed sẵn:
khách hàng **Brady Asia**, sản phẩm **BRD-7656-D**, 1 Spec đã duyệt, và WO mẫu **WO-26-3683**.

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
Đã hỗ trợ sẵn cả 2 provider — chỉ cần đổi cấu hình, KHÔNG sửa code:

1. Trong `appsettings.json` đặt:
   `"Database": { "Provider": "SqlServer" }` và sửa `ConnectionStrings:Default`, ví dụ:
   `"Server=localhost;Database=CCL_MES;Trusted_Connection=True;TrustServerCertificate=True"`
   (có sẵn mẫu `appsettings.SqlServer.json`).
2. Tạo & áp dụng EF Migrations:
   ```bash
   dotnet tool install --global dotnet-ef   # nếu chưa có
   bash ef-migrate.sh                        # tự add Init + database update
   ```
   Khi `Provider = SqlServer`, app tự chạy `Migrate()`; khi `Sqlite`, app dùng `EnsureCreated()`.

## 6b. Realtime (SignalR)
Dashboard và màn hình Work Orders kết nối hub `/hubs/shopfloor`. Mỗi khi có thay đổi
(Advance, QC, Start/Pause/Finish), mọi client đang mở sẽ **tự cập nhật** mà không cần F5.
Dashboard có chỉ báo `● live`.

## 6c. Auth (Phase 2)
- Cookie auth (`ccl_mes_auth`, HttpOnly, SameSite=Lax, 8h sliding) qua `Microsoft.AspNetCore.Authentication.Cookies`.
- Password hash: `PasswordHasher<User>` (PBKDF2 100k iter SHA256 + 128-bit salt + 256-bit hash).
- Global `FallbackPolicy = RequireAuthenticatedUser` — mọi page / API yêu cầu đăng nhập trừ khi gắn `[AllowAnonymous]` (chỉ Login + Logout + SetLanguage + SignalR Hub).
- Demo account: **`admin / admin`**, seed tự động trên DB sạch (idempotent). Đổi password trước khi đưa lên production.
- Đăng nhập: `/login` (Razor Page). Đăng xuất: `POST /logout`. Reset password (Phase 4+): chưa có UI, chỉ xoá DB + restart để re-seed.
- RBAC: `User.Role` đã có trong DB + claim `ClaimTypes.Role` đã bake vào cookie principal, nhưng **CHƯA enforce policy nào** — TODO Phase 5+.

## 6d. i18n (Phase 1 + 4)
- `IStringLocalizer<SharedResource>` đọc 2 resource:
  - `src/CCL.MES.Web/Resources/SharedResource.resx` — neutral (= EN, `NeutralLanguage=en` trong .csproj nhúng vào main DLL).
  - `src/CCL.MES.Web/Resources/SharedResource.vi.resx` — VI satellite.
- ~160 keys phủ MainLayout / Login / Index / Dashboard / Work Orders / Work Instructions / 4 NPI / 3 QC / 10 Settings.
- Default UI culture = EN. Order resolve culture: cookie `.AspNetCore.Culture` → `Accept-Language` → default EN.
- Switch ngôn ngữ: click cờ trên topbar / login → `GET /set-language?lang=en|vi&returnUrl=…` ghi cookie + 302 quay lại. Cookie sống 1 năm.
- SVG flag inline (`Shared/Flags/FlagGB.razor` + `FlagVN.razor`) — đa OS render đồng nhất, không phụ thuộc emoji 🇬🇧 🇻🇳.
- Coverage verified: EN 100% Anh / VI 100% Việt (xem [`docs/FINAL-REPORT-2026-05-31.md`](docs/FINAL-REPORT-2026-05-31.md) §3 + 14 screenshot trong `docs/screenshots/`).

## 6e. Settings dropdown (Phase 3)
- Header dropdown thứ 3 (cạnh `NPI Data` + `QC/QA Data`): `Settings ▾`.
- 10 sub-tab y hệt §2.3 audit doc trích từ Ops Control v1.2, đúng tên + đúng thứ tự, KHÔNG thêm KHÔNG bớt:
  - **User**: My Profile · My Password · Appearance · Hardware devices · Connection mode
  - **System**: Account Control [admin] · About / Diagnostics
  - **Maintenance**: Backup / Restore [admin] · System Logs [admin] · Import data v1.0 [admin]
- Mỗi sub-tab = 1 Razor page tại `/settings/<slug>` với placeholder content (3 bullet point + lead). KHÔNG có nghiệp vụ thực — TODO sprint sau.
- 4 sub-tab admin-only (Account Control / Backup / System Logs / Import data) đánh dấu **TODO RBAC Phase 5+** trong placeholder + Razor comment ở đầu file.

## 7. Hướng mở rộng (theo tài liệu kiến trúc)
- Module OEE / Production Log (Start/Pause/Resume/Finish, tính OEE theo máy).
- Work Instruction số hóa; SignalR realtime cho dashboard.
- RBAC (Entra ID/AD); tích hợp SAP & Warehouse.

## 7b. TODO Phase 5+ (sau Phase 4 wrap)
- **RBAC enforcement** trên 4 sub-tab Settings admin-only (`account`, `data`, `syslog`, `import-legacy`) + NPI write API (khi có).
- **SignalR hub auth** — `MapHub.AllowAnonymous()` hiện tại workaround vì Blazor `HubConnection` không carry cookie vào negotiate. Cần inject cookie via `HubConnectionBuilder.WithUrl` options.
- **Backend error string → key-mapped** — `WorkOrderStateMachine` + `WorkOrderService` hiện trả error message tiếng Anh hardcoded; UI prefix đã i18n nhưng dynamic Error portion luôn tiếng Anh. Refactor sang `ErrorCode` enum.
- **EF Migrations cho SQLite** — hiện dùng `EnsureCreated()`, lần tới schema đổi phải xoá DB + reimport. Chuyển sang Migrations cho cả 2 provider.
- **Nội dung nghiệp vụ thực** cho 3 QC tab + 1 NPI tab (Engineer Spec) + 10 Settings tab — tất cả đang là placeholder.

> Lưu ý: project target **net10.0** (khớp .NET SDK 10 của bạn). Nếu dùng .NET 8/9, đổi <TargetFramework> trong 4 file .csproj về net8.0/net9.0 và version các package EF/Extensions tương ứng.
