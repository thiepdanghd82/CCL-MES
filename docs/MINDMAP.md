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
│   └── seed_from_excel.py  → ETL nạp master data Excel/CSV → SQLite
│
├── 5. HẠ TẦNG DỮ LIỆU
│   ├── Dev  → SQLite (EnsureCreated)
│   └── Prod → SQL Server (EF Migrations) — switch qua Database:Provider
│
└── 6. BÀN GIAO
    ├── GitHub repo (thiepdanghd82/CCL-MES)
    ├── skills/dotnet-mes-mvp (.skill cài được)
    └── docs/ (LESSONS_LEARNED, MINDMAP)
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

*Cập nhật: 30/05/2026 — sau khi thêm tools/, EF Migrations/SQL Server, SignalR realtime.*
