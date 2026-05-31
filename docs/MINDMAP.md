# MINDMAP — Dự án CCL-MES

Bản ghi lại **toàn bộ cấu trúc** và **các bước thực hiện** dự án MES cho nhà máy CCL Design. Dùng để onboard người mới hoặc nhớ lại mạch dự án.

---

## 1. Sơ đồ tổng thể (mindmap)

```
CCL-MES (MES cho nhà máy in nhãn/label)
│
├── 0. BỐI CẢNH
│   ├── Vấn đề: chưa có Spec online · chưa control Work Order · không đo hiệu suất máy
│   └── Mục tiêu: số hóa process flow 7 bước + Spec + QC + OEE + Work Instruction
│
├── 1. TÀI LIỆU KIẾN TRÚC (Word, 19 trang)
│   ├── Hiện trạng & mục tiêu
│   ├── Kiến trúc .NET + SQL Server (Clean Architecture)
│   ├── Data model (9 nhóm bảng)
│   ├── State machine 7 bước
│   └── Lộ trình 4 giai đoạn + KPI
│
├── 2. KHUNG MVP (.NET 10, chạy được)
│   ├── Domain      → Entities, Enums, WorkOrderStateMachine
│   ├── Application → Services (WO/Spec/QC/OEE/WI), DTOs, IMesDbContext
│   ├── Infrastructure → EF Core, DbContext, DbSeeder, DI, Migrations
│   └── Web         → API + Swagger + Blazor (Dashboard/WO/WI) + SignalR Hub
│
├── 3. MODULE NGHIỆP VỤ
│   ├── Work Order Control → 7 bước, guard, lịch sử
│   ├── Spec Control       → version + approve, gắn vào WO
│   ├── QC (IPQC/FQC/OQC)  → checklist + approve, fail → On-Hold
│   ├── OEE/Production Log → Start/Pause/Resume/Finish, A×P×Q
│   ├── Work Instruction   → WI điện tử theo sản phẩm/bước
│   └── Dashboard          → KPI + OEE realtime (SignalR)
│
├── 4. CÔNG CỤ PHỤ TRỢ (tools/ — Python)
│   ├── verify_oee.py       → kiểm chứng công thức OEE (CI)
│   ├── oee_from_csv.py     → tính OEE từ log CSV
│   ├── seed_from_excel.py  → ETL nạp master data Excel/CSV → SQLite
│   └── import_npi.py       → Phase 1: nạp 4 bảng NPI (WorkCenters/RawMaterials/Routing/Structures)
│                              từ IFS export — transaction + skipped/failed counter + idempotent
│
├── 5. HẠ TẦNG DỮ LIỆU
│   ├── Dev  → SQLite (EnsureCreated)
│   └── Prod → SQL Server (EF Migrations) — switch qua Database:Provider
│
├── 7. AUTH + I18N (Phase 2 + 4)
│   ├── Cookie auth (PBKDF2) — ccl_mes_auth, 8h sliding, FallbackPolicy = RequireAuthenticatedUser
│   ├── Demo account admin/admin seed trên DB sạch
│   ├── ASP.NET Core Localization — IStringLocalizer<SharedResource>
│   │   ├── SharedResource.resx     (neutral = EN, NeutralLanguage=en trong csproj)
│   │   └── SharedResource.vi.resx  (VI satellite, ~160 keys)
│   ├── LangFlagPicker.razor — SVG GB + SVG VN (đa OS), persist .AspNetCore.Culture cookie 1 năm
│   └── Login Razor Page + Logout + SetLanguage endpoint
│
├── 8. SETTINGS DROPDOWN (Phase 3)
│   └── 10 sub-tab y hệt Ops Control v1.2 §2.3:
│       ├── User group (5): profile / mypwd / appearance / hardware / mode
│       ├── System group (2): account [admin] / about
│       └── Maintenance group (3): data [admin] / syslog [admin] / import-legacy [admin]
│         (sub-tab admin-only đánh dấu TODO RBAC, chưa enforce trong Phase 3)
│
└── 6. BÀN GIAO
    ├── GitHub repo (thiepdanghd82/CCL-MES) — main = Phase 1+2+3+4
    ├── skills/dotnet-mes-mvp (.skill cài được)
    └── docs/ (LESSONS_LEARNED, MINDMAP, AUDIT-2026-05-31, FINAL-REPORT-2026-05-31)
```

## 2. Cây thư mục thực tế

```
CCL-MES/
├── CCL.MES.sln
├── README.md
├── push-to-github.sh
├── docs/
│   ├── LESSONS_LEARNED.md
│   └── MINDMAP.md
├── skills/
│   ├── dotnet-mes-mvp/SKILL.md
│   └── dotnet-mes-mvp.skill
├── tools/
│   ├── verify_oee.py
│   ├── oee_from_csv.py
│   ├── seed_from_excel.py
│   ├── sample_production_log.csv
│   ├── sample_master.csv
│   ├── requirements.txt
│   └── README.md
└── src/
    ├── CCL.MES.Domain/
    │   ├── Enums.cs
    │   ├── Entities/ (BaseEntity, MasterData, Spec, WorkOrder, Qc, Machine, WorkInstruction)
    │   └── StateMachine/WorkOrderStateMachine.cs
    ├── CCL.MES.Application/
    │   ├── IMesDbContext.cs · Dtos.cs · OeeDtos.cs · WiDtos.cs · DependencyInjection.cs
    │   └── Services/ (WorkOrder, Spec, Qc, Oee, Wi)
    ├── CCL.MES.Infrastructure/
    │   ├── MesDbContext.cs · MesDbContextFactory.cs · DbSeeder.cs · DependencyInjection.cs
    │   └── Migrations/ (sinh ra khi chạy dotnet ef migrations add)
    └── CCL.MES.Web/
        ├── Program.cs · appsettings.json · appsettings.SqlServer.json
        ├── Controllers/ (WorkOrders, Specs, Qc, Oee, WorkInstructions)
        ├── Hubs/ShopfloorHub.cs
        ├── Pages/ (Index, Dashboard, WorkOrders, WorkInstructions, _Host)
        ├── Shared/MainLayout.razor
        └── wwwroot/css/site.css
```

## 3. Các bước thực hiện (đã làm, theo thứ tự)

1. **Hỏi rõ yêu cầu** — deliverable (Word), tech stack (.NET + SQL Server), phạm vi MVP.
2. **Viết tài liệu kiến trúc** Word 19 trang (kiến trúc, data model, state machine, lộ trình).
3. **Dựng khung MVP .NET** — 4 project Clean Architecture, SQLite, Blazor + API + Swagger.
4. **Chạy thử trên máy** — đổi target net8.0 → net10.0 cho khớp SDK; chạy thành công.
5. **Hoàn thiện 6 module** — thêm OEE/Production Log, Work Instruction, Dashboard.
6. **Tạo project CCL-MES** trong Project folder + ghi toàn bộ code.
7. **Skill + Lesson Learned** — đóng gói `dotnet-mes-mvp.skill`, ghi `LESSONS_LEARNED.md`.
8. **Đưa lên GitHub** — repo `CCL-MES`, push qua `push-to-github.sh`.
9. **Thêm tools/ Python** — verify_oee, oee_from_csv, seed_from_excel (đã test pass).
10. **Thêm EF Migrations + SQL Server** — provider switch qua config, factory design-time.
11. **Thêm SignalR realtime** — ShopfloorHub, Dashboard & WorkOrders tự cập nhật.
12. **Ghi MINDMAP** (file này) + đẩy lại GitHub.

### Phase 1 → Phase 4 (2026-05-31, 1 phiên)

13. **Phase 0 — Audit** — đọc Ops Control v1.2 (read-only) trích Settings catalogue + LangFlagToggle pattern; ghi `docs/AUDIT-2026-05-31.md`.
14. **Phase 1 — Finish NPI import** — sửa P0 column mapping của `tools/import_npi.py` (IFS RoutingOperations/ManufacturingStructures/RawMaterials); wrap transaction; thêm seen/skipped/imported/failed counter. Import thành công 4 bảng (43 / 2,127 / 38,441 / 20,530 rows). Đồng thời dựng nền ASP.NET Core Localization (`IStringLocalizer<SharedResource>` + .resx EN + VI, default EN) + chuyển hết hardcode VI ở NPI/QC/MainLayout/Index sang key.
15. **Phase 2 — Login + i18n** — `User` entity + PBKDF2 password hash + cookie auth + global `FallbackPolicy = RequireAuthenticatedUser`. SVG flag picker (GB + VN) + `/set-language` endpoint persist `.AspNetCore.Culture` cookie. Login Razor Page + Logout. Seed `admin/admin` (idempotent).
16. **Phase 3 — Settings dropdown** — 10 placeholder Razor pages dưới `/settings/*` y hệt §2.3 catalogue Ops Control v1.2 (profile / mypwd / appearance / hardware / mode / account / about / data / syslog / import-legacy). 4 sub-tab admin-only đánh dấu TODO RBAC. Setting dropdown trên MainLayout.
17. **Phase 4 — Merge + i18n full + báo cáo** — merge PR #2 + PR #3 vào main. Audit + chuyển hết hardcoded VI còn sót ở Dashboard / WorkOrders / WorkInstructions / WorkOrderStateMachine sang `IStringLocalizer` key. Verify EN 100% Anh / VI 100% Việt qua curl + 6 screenshots. Final backup + restart proof + báo cáo tổng (`docs/FINAL-REPORT-2026-05-31.md`).

## 4. Luồng dữ liệu chính (Work Order)

```
Tạo WO → [State Machine] → 7 bước có guard → ghi WoStatusHistory (audit)
          ↑                                   ↓
       Spec (Approved)                    SignalR broadcast
          ↑                                   ↓
   IPQC/FQC/OQC (Pass)              Dashboard + WO page tự reload
          ↑
   ProductionLog (Run/Stop) → tính OEE = A × P × Q
```

## 5. Cách chạy nhanh

```bash
# Dev (SQLite, mặc định)
dotnet run --project src/CCL.MES.Web        # http://localhost:5080

# Production (SQL Server)
#   appsettings.json: "Database:Provider" = "SqlServer" + sửa connection string
MES_PROVIDER=SqlServer dotnet ef migrations add Init -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web
MES_PROVIDER=SqlServer dotnet ef database update    -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web

# Công cụ Python
python3 tools/verify_oee.py
python3 tools/oee_from_csv.py tools/sample_production_log.csv
```

---

*Cập nhật: 31/05/2026 — sau Phase 1 → Phase 4: NPI import + login + i18n EN/VI full coverage + Settings dropdown.*
