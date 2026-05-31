# Phase 6 — Khởi động: KHẢO SÁT + ĐỀ XUẤT (chưa code)

> **Trạng thái: KHẢO SÁT (read-only).** Chưa code, chưa tạo branch.
> Phase 6 gom các TODO carry-over từ Phase 5 (xem `PHASE5-REPORT-2026-05-31.md` §7).

---

## 1. Phạm vi 3 nhóm hạng mục

| Nhóm | Hạng mục | Số placeholder | Backend đã có? |
|---|---|---|---|
| **A** | Nghiệp vụ thực | 3 QC tab + 1 NPI Spec + 10 Settings tab | QcService + SpecService có sẵn từ Phase 0; Settings 100% placeholder |
| **B** | RBAC roles mở rộng | Hiện 2 role (Admin / User) | `User.Role` free-form string; chỉ 1 policy `AdminOnly` |
| **C** | Deploy SQL Server thật | Hiện chỉ SQLite dev | Provider config + factory + `ef-migrate.sh --sqlserver` đã sẵn |

---

## 2. Hiện trạng từng hạng mục

### 2.A — Nghiệp vụ thực

#### A.1. 3 QC tab (`/qcqa/iqc`, `/qcqa/ipqc`, `/qcqa/oqc`)

| File:line | Trích |
|---|---|
| `Pages/QcQa/Iqc.razor:1-13` | Placeholder thuần: `<h1>` + `.npi-placeholder` 3 bullet |
| `Pages/QcQa/Ipqc.razor:1-13` | Cùng pattern |
| `Pages/QcQa/Oqc.razor:1-13` | Cùng pattern |
| `Domain/Enums.cs:18` | `enum QcType { IPQC, FQC, OQC }` — **KHÔNG có IQC** (Incoming) |
| `Domain/Entities/Qc.cs:1-24` | `QcInspection` + `QcResultDetail` đã đủ field (Type, Result, Inspector, SampleSize, Approved*, Details list) |
| `Application/Services/QcService.cs` | `CreateAsync` + `ApproveAsync` đầy đủ. Khi `pass=false` → WO `Status = OnHold` |
| `Controllers/QcController.cs` | REST API hoạt động — đang được dùng bởi `WorkOrders.razor` qua `Qc.CreateAsync + ApproveAsync` |

**Gap quan trọng**: Enum `QcType` thiếu `IQC`. IQC (Incoming Quality Control) là kiểm tra **vật tư nhập**, không gắn vào WorkOrder mà gắn vào `RawMaterial` lot. → IQC cần entity riêng (vd `IqcInspection` với `RawMaterialId` + `BatchNo`) HOẶC tái dùng `QcInspection` với `WorkOrderId nullable` + thêm `RawMaterialId` (less clean).

**IPQC + OQC** đã có entity + service + use case (Work Orders advance gọi vào QcService). Cần UI:
- List view (filter theo WO / theo ngày / theo Pass-Fail)
- Detail view (xem `Details` list)
- Manual create form (không qua WO advance flow — vd để re-inspection sau khi On-Hold được giải quyết)

#### A.2. NPI Engineer Spec (`/npi/engineer-spec`)

| File:line | Trích |
|---|---|
| `Pages/Npi/EngineerSpec.razor:1-13` | Placeholder thuần |
| `Domain/Entities/Spec.cs:1-33` | `Spec` + `SpecVersion` + `SpecParameter` đầy đủ. Versioning + Approval đã model |
| `Application/Services/SpecService.cs` | `GetAllAsync` + `CreateAsync` + `ApproveAsync` đầy đủ |
| `Controllers/SpecsController.cs` | REST API hoạt động — đang được dùng bởi WO seed |

→ **Chỉ thiếu UI**. Backend zero-effort. Đây là tab dễ làm nhất Phase 6.

#### A.3. 10 Settings sub-tab (`/settings/*`)

| Slug | File | Trạng thái | Khả thi UI |
|---|---|---|---|
| `profile` | `Settings/Profile.razor:1-13` | Placeholder | Form đọc User entity hiện tại — dễ |
| `mypwd` | `Settings/Password.razor:1-13` | Placeholder | Form 3 field (old/new/confirm) + service đổi password — dễ (reuse `PasswordHasher<User>`) |
| `appearance` | `Settings/Appearance.razor:1-13` | Placeholder | Theme cookie + dark-mode CSS — vừa (cần CSS toggle) |
| `hardware` | `Settings/Hardware.razor:1-13` | Placeholder | Detect scanner/printer — **KHÓ** (desktop-only, cần Electron hoặc native interop; không khả thi nếu chỉ web) |
| `mode` | `Settings/Mode.razor:1-13` | Placeholder | Standalone vs server-connected — **KHÓ** (cần dual-mode infra, không phù hợp Phase 6) |
| `account` *(admin)* | `Settings/Account.razor:1-19` | Placeholder + `[Authorize(Policy="AdminOnly")]` | CRUD User (create / disable / role assign) — vừa |
| `about` | `Settings/About.razor:1-13` | Placeholder | Static info (version, runtime, DB provider, last backup) — dễ |
| `data` *(admin)* | `Settings/Backup.razor:1-19` | Placeholder | Manual backup trigger + list + restore — vừa |
| `syslog` *(admin)* | `Settings/Logs.razor:1-19` | Placeholder | Cần Audit Log entity mới — **vừa-khó** |
| `import-legacy` *(admin)* | `Settings/ImportLegacy.razor:1-19` | Placeholder | Ops Control v1.0 file format — **khó** (depend vào input format; không có data thực để test trong CCL-CMES scope) |

### 2.B — RBAC roles mở rộng

| File:line | Trích |
|---|---|
| `Domain/Entities/User.cs:15-16` | `public string Role { get; set; } = "User";` — **free-form string, không enum** |
| `Web/Pages/Login.cshtml.cs:77` | `new Claim(ClaimTypes.Role, user.Role)` — bake claim ở login |
| `Web/Program.cs:75-77` | Chỉ 1 policy: `AddPolicy("AdminOnly", p => p.RequireRole("Admin"))` |
| `Web/Program.cs:151,163` | Seed 2 user: admin/Admin + operator/User |
| `Web/Shared/MainLayout.razor:50,54` | `<AuthorizeView Roles="Admin">` quanh 4 admin sub-tab |
| 4 admin Razor pages | `@attribute [Authorize(Policy = "AdminOnly")]` |

**Gap**: Role là string tự do — không có whitelist, không validate khi seed/CRUD. Future role mới (vd "Supervisor") chưa có policy mapping, chưa có UI tab nào gate vào role đó.

### 2.C — Deploy SQL Server thật

| File:line | Trích |
|---|---|
| `appsettings.json` (default) | `Database.Provider = "Sqlite"`, `ConnectionStrings.Default = "Data Source=ccl_mes.db"` |
| `appsettings.SqlServer.json` (mẫu) | `Provider = "SqlServer"`, CS = `"Server=localhost;Database=CCL_MES;Trusted_Connection=True;TrustServerCertificate=True"` |
| `Infrastructure/DependencyInjection.cs:14-32` | Switch provider qua config — đã hỗ trợ cả 2 |
| `Infrastructure/MesDbContextFactory.cs:14-32` | Design-time factory đọc `MES_PROVIDER` env |
| `Infrastructure/DbInitializer.cs` *(Phase 5 Bước 4)* | Cross-provider qua `IHistoryRepository` — baseline + Migrate chung |
| `ef-migrate.sh:1-65` | 2-mode `--sqlite | --sqlserver` + `add <Name>` subcommand |
| `Infrastructure/Migrations/20260531050444_Init.cs` | Provider-agnostic C# code (CreateTable / CreateIndex) — chạy được trên cả 2, EF runtime gen SQL phù hợp |

**Gap nhỏ**: Init migration được generate dưới `MES_PROVIDER=Sqlite` (có annotation `"Sqlite:Autoincrement"`). Khi áp lên SQL Server, EF sẽ ignore annotation Sqlite + gen `IDENTITY` đúng cho SQL Server. **Cần verify thực tế** trên SQL Server instance — chưa test bao giờ.

Không có entity nào dùng feature SQLite-specific (`AUTOINCREMENT`, `WITHOUT ROWID`, …) → di chuyển sang SQL Server an toàn về mặt schema.

---

## 3. Đề xuất phạm vi + thứ tự thực hiện

### Bước 1 — NPI Engineer Spec UI (giá trị cao / rủi ro thấp)

**Phạm vi**:
- Razor page hoàn chỉnh tại `/npi/engineer-spec`:
  - List Spec + Version + Status (Draft / InReview / Approved / Obsolete)
  - Form Create Spec mới + Add Parameters
  - Button Approve (gắn vào WO sau đó)
  - Filter theo Product
- 12-20 i18n key mới `npi.spec.*` EN+VI

**Lý do làm trước**:
- Backend zero-effort (SpecService + entity đã đủ).
- Đóng được 1/5 placeholder NPI.
- Mở đường cho WO seeding non-demo: hiện WO #1 seed phải gắn SpecVersionId=1 hardcoded; nếu UI có sẵn, operator tự tạo Spec → WO mới có spec hợp lệ.

**Phức tạp**: ⭐⭐ (2/5) · **Rủi ro**: thấp (chỉ thêm UI, không đụng entity/migration/data) · **LOC**: ~250

### Bước 2 — 6 Settings tab dễ (Profile / Password / Appearance / About + 2 admin tab dễ là Account + Backup)

**Phạm vi**:
- `Profile`: form đọc User entity hiện tại, edit DisplayName.
- `Password`: form 3 field + service đổi password qua PasswordHasher.
- `Appearance`: theme cookie + dark-mode CSS toggle (đơn giản, vẫn cookie persist như LangFlagPicker).
- `About`: static thông tin (version, runtime, DB provider, `__EFMigrationsHistory` row count, last backup file).
- `Account` *(admin)*: User CRUD — create / disable (soft) / role assign từ whitelist mới (xem Bước 4).
- `Backup` *(admin)*: manual backup trigger (file → `ccl_mes.db.bak.manual-<ts>`) + list backup hiện có + restore.

**Lý do gom 6 tab**:
- Cùng pattern Razor + form + service → 1 PR đỡ overhead review.
- Phụ thuộc lẫn nhau: Password reuse PasswordHasher (Phase 2), Account reuse same hasher.

**Để lại Phase 6+ (Bước sau hoặc Phase 7)**:
- `Hardware` — desktop-only, cần Electron / native interop.
- `Mode` — dual standalone/server, cần re-architect.
- `Syslog` — cần Audit Log entity mới (tách Bước 5).
- `ImportLegacy` — phụ thuộc format Ops Control v1.0 cụ thể, không có dữ liệu test.

**Phức tạp**: ⭐⭐⭐ (3/5) · **Rủi ro**: trung bình (touched User table cho Account / Password) · **LOC**: ~600

### Bước 3 — 3 QC tab (IPQC / OQC list + manual create; IQC tạm chưa làm vì cần entity mới)

**Phạm vi**:
- `Ipqc.razor` + `Oqc.razor`:
  - List QcInspection theo Type (IPQC / OQC) — filter ngày, WO, Pass/Fail
  - Detail view xem Details list
  - Manual create form (chọn WO + add Details + submit)
- `Iqc.razor`: **placeholder cải thiện** — hiển thị message "IQC sẽ build sau Bước 5" + link tới Raw Materials list (gắn vào RawMaterial.PartNo hiện tại) — chuẩn bị tâm lý cho operator
- ~15-20 i18n key `qcqa.{ipqc,oqc}.*` EN+VI

**Lý do gắn 3 vào 1 bước**: cùng family UI, cùng entity. IPQC + OQC dùng được ngay với QcService có sẵn.

**Để lại Phase 6+**: IQC full (cần `IqcInspection` entity riêng + migration mới).

**Phức tạp**: ⭐⭐⭐ (3/5) · **Rủi ro**: thấp · **LOC**: ~500

### Bước 4 — RBAC roles mở rộng

**Phạm vi**:
- `Domain/Auth/UserRole.cs` (mới) — `public static class UserRole { public const string Admin = "Admin"; public const string Supervisor = "Supervisor"; public const string Engineer = "Engineer"; public const string Qc = "QC"; public const string Operator = "Operator"; public static string[] All => new[] { Admin, Supervisor, Engineer, Qc, Operator }; }` — const string, không enum (để DB lưu `Role` text dễ migrate sau).
- Validation: khi seed / khi Account UI tạo user, role phải nằm trong `UserRole.All`.
- Policies mới trong `Program.cs`:
  - `AdminOnly` (giữ nguyên)
  - `SupervisorOrAdmin = role in {Admin, Supervisor}` — cho Dashboard write actions
  - `EngineerOrAbove = role in {Admin, Supervisor, Engineer}` — cho NPI Spec write
  - `QcOrAbove = role in {Admin, Supervisor, Engineer, QC}` — cho QC approve
  - `AnyAuthenticated` (= FallbackPolicy) — cho Operator chỉ vào Work Orders run actions
- Tab access matrix (đề xuất):
  | Tab | Admin | Supervisor | Engineer | QC | Operator |
  |---|---|---|---|---|---|
  | Dashboard | R | R | R | R | R |
  | Work Orders (list) | RW | RW | R | R | R |
  | Work Orders (Start/Pause/Finish) | RW | RW | – | – | RW |
  | Work Orders (Advance + Approve) | RW | RW | – | RW | – |
  | Work Instructions | RW | R | RW | R | R |
  | NPI 4 tab (Routine/Structure/RawMaterials/WorkCenter) | RW | R | RW | R | – |
  | NPI Engineer Spec | RW | R | RW | – | – |
  | QC 3 tab | RW | R | – | RW | – |
  | Settings User (5) | RW | RW | RW | RW | RW |
  | Settings System / Maintenance (4 admin) | RW | – | – | – | – |
- Seed thêm 3 user demo: `supervisor/supervisor`, `engineer/engineer`, `qc/qc` (Role tương ứng) — idempotent.
- Audit emit khi role change (chuẩn bị cho Bước 5).

**Phức tạp**: ⭐⭐⭐⭐ (4/5) · **Rủi ro**: trung bình (đụng auth pipeline, cần re-test toàn bộ Phase 5 RBAC) · **LOC**: ~300

### Bước 5 — Audit Log + Syslog tab

**Phạm vi**:
- `Domain/Entities/AuditLog.cs` (mới): `Id`, `Timestamp`, `Username`, `Action` (string), `Detail` (JSON string), `IpAddress` (optional).
- Migration v2 (sau Init): `AddAuditLog`.
- `Application/Services/AuditService.cs` — `EmitAsync(action, detail, user, ip)`.
- Wire emit ở 5+ điểm: login success/fail, role change, WO advance, QC approve, Settings.Account CRUD.
- `Settings/Logs.razor` (admin) — list + filter (date range, user, action type) + export CSV.

**Phức tạp**: ⭐⭐⭐⭐ (4/5) · **Rủi ro**: trung bình (migration mới — test methodology A→B→C như Phase 5 Bước 4) · **LOC**: ~400

### Bước 6 — Deploy SQL Server verify

**Phạm vi**:
- Provision SQL Server instance (LocalDB / Docker / real server).
- Backup SQLite DB tường minh + SHA256.
- Đổi `appsettings.json` → SqlServer (hoặc tạo `appsettings.Production.json` override).
- Chạy `bash ef-migrate.sh --sqlserver` → tạo migration init (cùng tên `20260531050444_Init` nếu reuse, hoặc gen mới).
- ETL: copy data từ SQLite → SQL Server (qua `sqlite3 .dump` + transform hoặc viết script `Infrastructure/Migrators/SqliteToSqlServer.cs`).
- Smoke đầy đủ trên SQL Server: login + Phase 5 RBAC + hub auth + error code + EF migrations + Phase 6 tabs đã build.
- Document: `docs/PHASE6-DEPLOY-SQLSERVER.md` step-by-step.

**Phức tạp**: ⭐⭐⭐⭐⭐ (5/5) · **Rủi ro**: CAO (ETL 60k+ row qua provider khác — phải test trên copy DB trước) · **LOC**: ~200 + script ETL

### Bước 7 — IQC entity + tab

**Phạm vi**:
- `Domain/Entities/IqcInspection.cs` (mới) — `Id`, `RawMaterialId`, `BatchNo`, `InspectorId`, `Result` (Pending/Pass/Fail), `Details`, audit fields.
- Migration v3.
- Service + Controller.
- `Iqc.razor` UI: list + filter theo RawMaterial.PartNo / BatchNo / ngày + manual create form.

**Phức tạp**: ⭐⭐⭐ (3/5) · **Rủi ro**: trung bình (migration mới) · **LOC**: ~400

### Bước 8 — Hardware + Mode + ImportLegacy

**Phạm vi**: phụ thuộc product decision (Electron vs web, format input v1.0). **Defer Phase 7+**.

---

## 4. Tổng quan thứ tự + lý do

| Thứ tự | Bước | Tên | Phức tạp | Rủi ro | LOC | Đóng gì |
|---|---|---|---|---|---|---|
| 1 | 1 | NPI Engineer Spec UI | ⭐⭐ | thấp | ~250 | 1/5 NPI placeholder + mở đường WO seeding non-demo |
| 2 | 2 | 6 Settings tab dễ | ⭐⭐⭐ | trung bình | ~600 | 6/10 Settings placeholder |
| 3 | 3 | 2 QC tab (IPQC/OQC) + IQC stub | ⭐⭐⭐ | thấp | ~500 | 2/3 QC placeholder + chuẩn bị IQC |
| 4 | 4 | RBAC roles mở rộng (5 role + policies) | ⭐⭐⭐⭐ | trung bình | ~300 | TODO Phase 5+ #1 |
| 5 | 5 | Audit Log + Syslog tab | ⭐⭐⭐⭐ | trung bình | ~400 | 1/10 Settings placeholder còn lại (admin) + audit infra |
| 6 | 6 | Deploy SQL Server verify | ⭐⭐⭐⭐⭐ | **CAO** | ~200 | TODO Phase 5+ #2 |
| 7 | 7 | IQC entity + tab | ⭐⭐⭐ | trung bình | ~400 | 1/3 QC placeholder cuối |
| — | 8 | Hardware + Mode + ImportLegacy | — | — | — | Defer Phase 7+ (product decision) |

**Tổng Phase 6**: 7 bước, ~2 650 LOC, ~5 PR (gom Bước 2-3 thành 2 PR, Bước 5-6 stack có thể gom 1).

### 4.1. Tại sao thứ tự này?

1. **Bước 1 trước** — giá trị cao (mở khoá nội dung NPI Spec) + rủi ro thấp (chỉ UI, backend đã có) → momentum tốt cho Phase 6.
2. **Bước 2 → 3** — UI patterns tương đồng (form CRUD), cùng family. Làm xong sẽ có hình hài "MES có nội dung thực".
3. **Bước 4 RBAC trước Bước 5 audit** — Bước 5 cần biết role mới để gate tab Syslog, đồng thời audit log sẽ stamp role.
4. **Bước 6 SQL Server cuối Phase** — sau khi schema ổn (đã có migration Init + AddAuditLog + IqcInspection của Bước 7?), ETL một lần là xong. Nếu làm sớm sẽ phải ETL nhiều lần khi schema còn thay đổi.
5. **Bước 7 sau Bước 6** — IQC migration mới có thể làm sau khi đã quen pipeline SQL Server.

### 4.2. Lựa chọn thay thế (nếu cần ưu tiên khác)

| Nếu ưu tiên | Đề xuất reorder |
|---|---|
| Operator UX (vận hành nhà máy hàng ngày) | Bước 3 (QC) trước, sau đó Bước 1 (Spec) |
| Compliance / audit | Bước 5 (Audit Log) trước, sau đó Bước 4 (RBAC) |
| Production deployment sớm | Bước 6 (SQL Server) ngay sau Bước 1, defer Bước 4-5-7 |

---

## 5. Câu hỏi cần em quyết trước khi vào code

### Q1 — Chốt thứ tự Bước 1 → Bước N?

Đề xuất: 1 → 2 → 3 → 4 → 5 → 6 → 7. Hoặc anh muốn reorder theo §4.2?

### Q2 — Scope Bước 2 (6 Settings tab gom 1 PR)?

Đề xuất: gom 6 (Profile + Password + Appearance + About + Account + Backup) vào 1 PR. Hoặc tách thành 2 PR (3 user-area + 3 admin)?

### Q3 — Phạm vi Bước 4 RBAC matrix?

Đề xuất 5 role (Admin / Supervisor / Engineer / QC / Operator) với matrix ở §3 Bước 4. Anh có muốn thêm bớt role nào? Có cần Sys role (god mode bypass mọi check như Ops Control v1.2 không)?

### Q4 — Bước 6 SQL Server: provision như thế nào?

Đề xuất 3 option:
- (a) Docker SQL Server (local, dễ dispose)
- (b) SQL Server Express trên Windows server thật
- (c) LocalDB trên Mac (qua azure-sql-edge container)

Cái nào sẵn sàng để test ETL?

### Q5 — Sys role + admin account recovery?

Hiện admin/admin seed idempotent. Nếu admin user bị xoá (vd qua Settings.Account CRUD trong Bước 2/4), không có cách recover từ web UI. Có cần build console script `scripts/recover-admin-user.cs` như Ops Control v1.2 Sprint 1.7 không?

### Q6 — Backup/Restore UI scope (Bước 2.Backup tab)?

Đề xuất:
- Backup: snapshot SQLite file → `ccl_mes.db.bak.manual-<ts>` + SHA256 + audit log.
- Restore: confirm gate + replace DB file + auto-restart hint.

Trên SQL Server thì sao? Đề xuất: chỉ Backup tab ON với SQLite, hiển thị "Cài Backup qua SSMS / Azure" với SQL Server (không build trong web).

### Q7 — Có cần cài thêm test framework xUnit/Playwright sớm trong Phase 6 không?

Phase 5 đã ghi TODO này. Đề xuất: defer Phase 7 (sau khi UI Phase 6 stable mới viết test cover lại). Anh muốn front-load test framework không?

---

## 6. Risk summary cho cả Phase 6

| Hạng mục | Rủi ro chính | Mitigation |
|---|---|---|
| Bước 1 (Spec UI) | Không | Pure UI |
| Bước 2 (Settings) | Đụng User table (Password change, Account CRUD) | Backup DB tường minh trước, test trên copy như Phase 5 Bước 4 |
| Bước 3 (QC) | Không (entity đã có) | Pure UI |
| Bước 4 (RBAC) | Vỡ Phase 5 RBAC enforce hiện tại | Smoke lại 4-step Phase 5 sau merge |
| Bước 5 (Audit + migration) | Migration mới — không đúng baseline có thể vỡ DB | Phase 5 Bước 4 methodology A→B→C: backup + test copy + verify row counts + restart proof |
| Bước 6 (SQL Server) | **CAO** — ETL 60k+ row qua provider khác | Provision riêng instance, test ETL trên SQLite copy → SQL Server copy trước, KHÔNG đụng main DB cho đến khi pass |
| Bước 7 (IQC migration) | Migration mới như Bước 5 | Cùng A→B→C |

---

## 7. KHÔNG đụng (giữ nguyên Phase 1-5)

- 4 NPI tab có UI (`EngineerRoutine`, `EngineerStructure`, `RawMaterials`, `WorkCenter`)
- WorkOrders + Dashboard + WorkInstructions
- 4 thư mục cấm: `Ops Control v1.2/`, `CMES/`, `Old ver ( DO NOT USE)/`, `SpecHub/`

Mọi Bước Phase 6 sẽ verify lại data integrity (43/2127/38441/20530/2) sau khi land, giống Phase 5.

---

## 8. Nhắc lại nguyên tắc Phase 6 (theo brief)

- Mỗi hạng mục 1 branch + 1 PR (stack được nếu phụ thuộc, độc lập nếu không).
- Backup DB tường minh trước mọi thao tác đụng data (Bước 2.Password/Account, Bước 5 migration, Bước 6 ETL, Bước 7 migration).
- Không xóa file/bảng (rename nếu cần).
- Conventional Commits.
- STOP chờ duyệt sau mỗi Bước.
- KHÔNG đụng Ops Control v1.2, CMES, Old ver, SpecHub.

---

**STOP. Chờ em duyệt §5 (7 câu hỏi) + chốt thứ tự Bước 1 → Bước N rồi bắt đầu Bước 1.**
