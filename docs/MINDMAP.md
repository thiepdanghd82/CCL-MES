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
├── 8. SETTINGS DROPDOWN (Phase 3 + Phase 5 Bước 1)
│   └── 10 sub-tab y hệt Ops Control v1.2 §2.3:
│       ├── User group (5): profile / mypwd / appearance / hardware / mode
│       ├── System group (2): account [admin] / about
│       └── Maintenance group (2): data [admin] / syslog [admin]
│         Phase 5 — 3 admin-only sub-tab enforce qua `[Authorize(Policy="AdminOnly")]`
│         + `<AuthorizeView Roles="Admin">` hide trên dropdown. Operator → AccessDenied.
│         Phase 6 close-out — bỏ tab "Import data v1.0" (placeholder stub) qua PR #18.
│
├── 9. PHASE 5 (đóng TODO Phase 4)
│   ├── Bước 1 — RBAC enforcement (PR #4) — AdminOnly policy + AccessDenied component + operator seed
│   ├── Bước 2 — SignalR hub auth (PR #5) — HubCookieAccessor scoped + cookie forward + gỡ AllowAnonymous
│   ├── Bước 3 — Error-code refactor (PR #8 ← #6) — WoErrorCode enum + WoErrorKeys dictionary + 10 i18n key
│   └── Bước 4 — EF Migrations SQLite (PR #9 ← #7) — Init migration + DbInitializer baseline-aware
│
├── 10. PHASE 6 (đóng TODO Phase 5 — real business content)
│   ├── Bước 1 — NPI Engineer Spec grid UI (PR #10) — Pages/Npi/EngineerSpec.razor + SpecsAsync paginated + SpecStatus badge
│   ├── Bước 2A — Settings User group (PR #11) — Profile / My Password / Appearance + UserProfileService scoped
│   ├── Bước 2B — Settings System group (PR #12) — About/Diagnostics + Account read-only + Backup SQLite snapshot
│   ├── Bước 3 — IPQC + OQC grids + IQC stub (PR #13) — shared QcInspectionGrid + QcResult badge
│   ├── Bước 4 — RBAC 5-role (PR #14) — Admin/Supervisor/Engineer/QC/Operator + 3 policies (NpiRead/NpiSpecRead/QcRead) + Account mutations + RecoverAdmin console
│   ├── Bước 5 — AuditLog + Syslog + BackupRestore console (PR #15) — AuditLog entity + IAuditWriter + Syslog filterable grid + 21 AuditAction codes
│   ├── Bước 6.5 — Ops Control v1.2-style SQLite + SQL Server gate (PR #16) — Backup dir nested + provider-agnostic migration strip type-affinity
│   ├── Bước 7 — IQC entity + tab (PR #17) — hybrid FK (RawMaterialId nullable + PartNo snapshot) + IqcResultDetail separate + 2 audit codes + 37 i18n × 2 locale + 3 demo seed
│   ├── chore — Remove Import data v1.0 sub-tab (PR #18) — xoá placeholder ImportLegacy.razor + 6 i18n key + nav entry
│   └── P0 fix — Restore Bước 4 RBAC policies (PR #19) — git merge -X ours regression hotfix (NpiRead/NpiSpecRead/QcRead bị ăn mất)
│
└── 6. BÀN GIAO
    ├── GitHub repo (thiepdanghd82/CCL-MES) — main = Phase 1+2+3+4+5+6
    ├── skills/dotnet-mes-mvp (.skill cài được)
    └── docs/ (LESSONS_LEARNED, MINDMAP, AUDIT-2026-05-31, FINAL-REPORT-2026-05-31, PHASE5-REPORT, PHASE6-REPORT)
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

### Phase 5 (2026-05-31, 1 phiên — đóng 4 TODO còn lại từ Phase 4)

18. **Bước 1 — RBAC enforcement** (PR #4, SHA `15313cc`). AuthorizationPolicy `AdminOnly = RequireRole("Admin")` + defence-in-depth 2 layer (UI hide `<AuthorizeView Roles="Admin">` trên 4 dropdown item + route gate `[Authorize(Policy="AdminOnly")]` trên 4 Razor page). Seed `operator/operator` (Role=User) idempotent để test. `<NotAuthorized>` slot trong `App.razor` split → anonymous redirect to login, authenticated-sai-role render `AccessDenied` component mới (i18n EN+VI). Đóng TODO "RBAC enforcement deferred to Phase 4+" trên 4 admin sub-tab.
19. **Bước 2 — SignalR hub auth** (PR #5, SHA `1cc5b4b`). Gỡ `AllowAnonymous()` trên `MapHub<ShopfloorHub>("/hubs/shopfloor")`. Phương án A: scoped `HubCookieAccessor` capture `ccl_mes_auth` cookie từ `_Host.cshtml` (chỗ HttpContext còn sống) → forward qua `HubConnectionBuilder.WithUrl(opts.Cookies.Add(...))` trong Dashboard + WorkOrders. Smoke: anonymous negotiate → 401 (trước 200), authenticated → 200. Logout-relogin cùng tab → cookie stale **không** xảy ra (forceLoad teardown circuit sạch). Đóng TODO `Program.cs:118-124` từ Phase 2.
20. **Bước 3 — Error-string → WoErrorCode enum** (PR #8, SHA `db42c8d`; replace PR #6 sau khi base auto-deleted). Domain language-free qua enum 9 value `WoErrorCode` (AlreadyAtFinalStep, RequiresSpecAndMaterials, …, WorkOrderNotFound). `TransitionResult.Reason` (string?) → `Error` (WoErrorCode?); `AdvanceResult.Error` (string?) → `ErrorCode` (WoErrorCode?). Web layer mới `Services/WoErrorKeys.cs` dictionary code → `workorders.error.*` resource key. 10 i18n key EN+VI thêm. API wire format đổi `"error": "<EN string>"` → `"errorCode": "RequiresSetupConfirmed"` (enum NAME qua `JsonStringEnumConverter`). Đóng 2 TODO comment ở Domain + Application + gap i18n cuối Phase 4 (dynamic error portion vẫn EN giữa VI message).
21. **Bước 4 — EF Migrations cho SQLite** (PR #9, SHA `29cca38`; replace PR #7 sau khi base auto-deleted). Init migration (19 CreateTable + 22 CreateIndex khớp 100% live DB). `DbInitializer.InitializeAsync` mới (cross-provider qua `IHistoryRepository.GetCreateScript` + `GetInsertScript(HistoryRow)`) — baseline tự động trên existing DB từ thời `EnsureCreated()` mà KHÔNG mất 60k+ row NPI. Program.cs gỡ branching `EnsureCreated/Migrate`, gọi `DbInitializer.InitializeAsync(db)` chung. `ef-migrate.sh` mở rộng 2-mode `--sqlite | --sqlserver` + `add <Name>` subcommand. Test methodology A→B→C: backup + SHA256 → test trên `ccl_mes.db.testcopy` trước → áp DB thật. Restart proof: lần 2 boot Migrate no-op. Đóng TODO "EF Migrations cho SQLite" cuối FINAL-REPORT Phase 4.
22. **Phase 5 close** — Merge tuần tự PR #4 → #5 → #8 (replace #6) → #9 (replace #7). PR #6 + #7 ban đầu auto-close khi base branch bị delete; thay thế bằng PR #8 + #9 trỏ thẳng `main`, content nguyên vẹn. Verify trên main: build clean, 4-step smoke pass, row counts 43/2127/38441/20530/2 + Users=2 không đổi, restart proof. Ghi `docs/PHASE5-REPORT-2026-05-31.md` (tổng kết) + cập nhật MINDMAP + README.

### Phase 6 (2026-05-31, 1 phiên — đóng TODO Phase 5 §7 "real business content" + 1 P0 regression fix)

23. **Bước 1 — NPI Engineer Spec grid UI** (PR #10, `ed91fc8`). `Pages/Npi/EngineerSpec.razor` đọc qua `SpecService.SpecsAsync(search, page, pageSize)` paginated bằng helper shared `PagingHelper.PageAsync`. SpecStatus badge 4 màu (Draft/InReview/Approved/Obsolete). Pattern y hệt 4 NPI grid khác. Đóng Phase 5 §7 #2.
24. **Bước 2A — Settings User group** (PR #11, `6ce04f9`). 3 sub-tab Profile / My Password / Appearance. Mới `Services/UserProfileService.cs` scoped — UpdateProfile + ChangePassword + clear `must_change_password = false` khi self-change. 8 i18n key EN+VI. Đóng phần User group của Phase 5 §7 #3.
25. **Bước 2B — Settings System group** (PR #12, `7fd21ff`). About/Diagnostics + Account read-only + Backup SQLite snapshot. Mới `Services/UserAdminService.cs` + `Services/BackupService.cs` (cả 2 scoped). Online backup API SQLite. Backup dir flat next to DB (sẽ refactor sang nested ở Bước 6.5). Account mutations bumped sang Bước 4.
26. **Bước 3 — IPQC + OQC grids + IQC stub** (PR #13, `80c21f4`). 3 razor page `Pages/QcQa/{Ipqc,Oqc,Iqc}.razor`. IPQC + OQC dùng shared `QcInspectionGrid` component. IQC stub "Sắp ra mắt" — khoá Bước 7. QcResult badge 3 màu. Đóng phần IPQC + OQC của Phase 5 §7 #1.
27. **Bước 4 — RBAC 5-role + Account mutation + recover-admin** (PR #14, `777ecd9`). Mở rộng 2 role → 5: Admin / Supervisor / Engineer / QC / Operator. `Domain/Auth/UserRole.cs` const string class. Migration v2 `AddUserMustChangeAndIsActive` thêm 2 cột + idempotent legacy mapping `Role="User" → "Operator"`. 3 page-level policy mới: `NpiRead` + `NpiSpecRead` + `QcRead`. Account mutations: Create / Edit DisplayName + Role / Reset password / Toggle active. Invariant: cấm self-modify, cấm demote/disable Admin cuối cùng. `scripts/RecoverAdmin/` console app. Đóng Phase 5 §7 #5.
28. **Bước 5 — AuditLog + Syslog + BackupRestore console** (PR #15, `8c17c77`). `Domain/Entities/AuditLog.cs` + `Domain/Audit/AuditAction.cs` (~21 codes). `Application/Audit/IAuditWriter.cs` interface. `Web/Services/AuditService.cs` implementation. `Pages/Settings/Syslog.razor` admin grid 4 filter. Migration v3 `AddAuditLog`. `scripts/BackupRestore/` console restore tool. Mutations trong UserAdmin + Backup + Spec service emit audit. JSON-only detail. Đóng Phase 5 §7 #7.
29. **Bước 6.5 — Ops Control v1.2-style SQLite + SQL Server gate** (PR #16, `5e9d152`). Backup dir `<DATA_DIR>/Backup/SQLite/` (nested, was flat). Auto-migration helper `MigrateLegacySnapshots()` boot-time. SQL Server gate fix: strip toàn bộ inline `type:` + `.HasColumnType()` qua Python script — migrations provider-agnostic. Cleanup `SpecService.SpecsAsync` → `PagingHelper.PageAsync` shared.
30. **Bước 7 — IQC entity + tab** (PR #17, `67e86a8`). Hybrid FK pattern: `IqcInspection` có `RawMaterialId long?` + `PartNo string` snapshot. Separate `IqcResultDetail` entity. Reuse `QcResult` enum. No WO.OnHold cascade. 2 audit codes `IQC_CREATE` + `IQC_APPROVE`. Migration v4 `AddIqcInspection` Phase A→B→C SAFE pattern (MES_CONNSTR=/tmp/iqc-design.db, live SHA `850fbf56…` không đổi). UI: 1-modal create + view + approve. Page `[Authorize(Policy="QcRead")]` + inline AuthorizeView + server-side check. 37 i18n × 2 locale. 3 demo IQC idempotent seed. Đóng phần IQC cuối Phase 5 §7 #1 + đóng stub Bước 3.
31. **chore — Remove Import data v1.0 sub-tab** (PR #18, `c5883f2`). Xoá `Pages/Settings/ImportLegacy.razor` + 6 i18n key (EN+VI) + nav entry + AccessDenied lead reference. Tab này chỉ là placeholder stub từ Phase 3.
32. **P0 fix — Restore Bước 4 RBAC policies** (PR #19, `0c3a049`). Smoke verify trên main sau PR #18 phát hiện `NpiRead` + `NpiSpecRead` + `QcRead` bị mất khỏi Program.cs → mọi GET `/npi/engineer-spec` + `/qcqa/*` → HTTP 500. Root cause: `git merge -X ours` strategy trên overlapping additive edits — PR #18 chỉnh comment AdminOnly, PR #14 thêm 3 policies → ours version (PR #18) ăn mất 3 policies. Fix: re-add 3 policies + restore AdminOnly về `UserRole.Admin` enum. Verified post-fix 13/13 routes 200 (admin) + operator → AccessDenied panel.
33. **Phase 6 close** — Merge tuần tự PR #10 → #11 → #12 → #13 → #14 → #15 → #16 → #17 → #18 → #19. Verify trên main: build clean, 11-step smoke pass, row counts 43/2127/38441/20530/5 + Users=5 + IQC=3 không đổi, 4 migration apply idempotent, restart proof. Final backup SHA256 `abd45359486cc85aa090ae2b4f21f773e71b59f8d00f53f6b276b90087cd021c`. Ghi `docs/PHASE6-REPORT-2026-05-31.md` (tổng kết) + cập nhật MINDMAP + README + LESSONS_LEARNED §8 (bài học `git merge -X ours`).

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
# Dev (SQLite, mặc định) — Phase 5: app khởi động sẽ tự baseline + Migrate
dotnet run --project src/CCL.MES.Web        # http://localhost:5080

# Demo accounts (seed idempotent)
#   admin / admin     — Role=Admin   (Phase 2)
#   operator / operator — Role=User  (Phase 5 Bước 1)

# Tạo migration mới khi schema đổi (Phase 5 Bước 4)
bash ef-migrate.sh --sqlite add <MigrationName>
# Hoặc trực tiếp:
MES_PROVIDER=Sqlite dotnet ef migrations add <Name> -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web -o Migrations

# Production (SQL Server)
#   appsettings.json: "Database:Provider" = "SqlServer" + sửa connection string
bash ef-migrate.sh --sqlserver       # tạo Init + áp database update

# Công cụ Python
python3 tools/verify_oee.py
python3 tools/oee_from_csv.py tools/sample_production_log.csv
```

---

*Cập nhật: 31/05/2026 — sau Phase 5 (đóng 4 TODO Phase 4): RBAC enforcement / SignalR hub auth / error-code refactor / EF Migrations SQLite.*
