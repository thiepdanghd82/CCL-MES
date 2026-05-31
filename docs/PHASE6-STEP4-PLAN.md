# Phase 6 — Bước 4: RBAC 5 role + recover-admin + Account mutation (KHẢO SÁT)

> **Trạng thái: KHẢO SÁT (read-only).** Chưa code, chưa tạo branch.
> Đây là bước rủi ro **CAO** trong Phase 6 vì đụng authorization + có thể self-lockout.
> Sau khi em chốt phương án + matrix em mới tạo `feat/phase6-rbac-roles` để code.

---

## 1. Khảo sát hiện trạng

### 1.1 Role storage hiện tại

| File:line | Trích |
|---|---|
| `src/CCL.MES.Domain/Entities/User.cs:16` | `public string Role { get; set; } = "User";` — free-form string, default `"User"` |
| `src/CCL.MES.Domain/Entities/User.cs:15` | Comment: *"Free-form role tag. Phase 2 only uses `"Admin"`; future RBAC will check this."* |
| `src/CCL.MES.Web/Pages/Login.cshtml.cs:77` | `new Claim(ClaimTypes.Role, user.Role)` — emit role-as-string vào cookie principal |

### 1.2 Policies + gates hiện tại (Phase 5 Bước 1)

| File:line | Trích |
|---|---|
| `Program.cs:77` | `o.AddPolicy("AdminOnly", p => p.RequireRole("Admin"))` — **policy duy nhất** |
| `Program.cs:151` | Seed `admin` với `Role = "Admin"` |
| `Program.cs:163` | Seed `operator` với `Role = "User"` |
| `MainLayout.razor:50,54` | `<AuthorizeView Roles="Admin">` quanh 4 dropdown item admin |
| 4 admin Razor pages (`Account/Backup/Logs/ImportLegacy.razor:2`) | `@attribute [Authorize(Policy = "AdminOnly")]` |
| `App.razor:14-16` | `<NotAuthorized>` slot — auth'd-sai-role render `<AccessDenied />`; anonymous → `<RedirectToLogin />` |

### 1.3 Pages KHÔNG gate (= mọi authenticated user thấy)

- `Dashboard.razor` · `WorkOrders.razor` · `WorkInstructions.razor`
- 5 NPI tab (Routine/Structure/Spec/RawMaterials/WorkCenter)
- 3 QC tab (IQC/IPQC/OQC)
- 6 Settings tab user-area (Profile / Password / Appearance / Hardware / Mode / About)

→ Bước 4 cần đề xuất gate cho các tab/route theo từng role.

### 1.4 Dữ liệu user hiện có

```sql
SELECT Username, Role FROM Users;
-- admin    | Admin
-- operator | User
```

Chỉ 2 row. Migration sang 5-role chỉ cần xử lý 1 row (`operator`: `"User"` → 1 trong 5 role mới).

### 1.5 Service hiện có

- `UserAdminService.ListAsync` (PR #12, Bước 2B) — admin grid, read-only, không có mutation
- `UserProfileService` (PR #11, Bước 2A) — self-service edit DisplayName + change password
- `Login.cshtml.cs.OnPostAsync` — verify PBKDF2 + emit claims
- Seed function `SeedAdminUserAsync` trong Program.cs

### 1.6 Phụ thuộc PR đang OPEN

PR #10/#11/#12/#13 đều OPEN, sẽ merge cùng đợt cuối Phase 6. Bước 4 branch từ `main` → CHƯA có `UserAdminService` / Account page mutation footnote / Settings/Account update. Cần ride-along giống Bước 3 đã làm với PagingHelper.

---

## 2. Phương án từng phần

### 2.A Role storage: enum vs const string + whitelist

#### Option A1 — Const string class + whitelist (giữ string, no migration) ⭐ đề xuất

```csharp
// Domain/Auth/UserRole.cs (mới)
public static class UserRole
{
    public const string Admin      = "Admin";
    public const string Supervisor = "Supervisor";
    public const string Engineer   = "Engineer";
    public const string Qc         = "QC";
    public const string Operator   = "Operator";

    public static readonly IReadOnlyList<string> All =
        new[] { Admin, Supervisor, Engineer, Qc, Operator };

    public static bool IsValid(string? role) =>
        !string.IsNullOrEmpty(role) && All.Contains(role);
}
```

**Ưu**:
- Schema không đổi → KHÔNG migration → KHÔNG đụng `__EFMigrationsHistory` → rủi ro thấp nhất.
- Field `User.Role` vẫn là string → API JSON serialize không đổi.
- Backward-compat: nếu sau này có 6 role, chỉ cần thêm const + entry vào `All`.
- Phù hợp với cookie claim `ClaimTypes.Role` (vốn là string).

**Nhược**:
- Không có compiler safety nếu typo `"Adim"` ở callsite (vd policy đăng ký).
- Validate phải tay (qua `UserRole.IsValid`) ở mọi điểm ghi.

**LOC**: ~25 (1 file mới)
**Migration**: KHÔNG cần

#### Option A2 — Enum + EF conversion (string column)

```csharp
public enum UserRole { Admin, Supervisor, Engineer, Qc, Operator }
public UserRole Role { get; set; } = UserRole.Operator;
```

**Ưu**: compiler safety; switch exhaustive.

**Nhược**:
- Cần migration: column type không đổi (vẫn TEXT vì có `.HasConversion<string>()`) nhưng entity schema snapshot thay đổi → EF generate Up/Down migration.
- Phase 5 Bước 4 DbInitializer baseline đã pin ModelSnapshot — đổi entity = migration v2 thực sự.
- Risk: nếu migration sai, lockout.

**LOC**: ~50 (entity + migration + 4 callsite đổi)
**Migration**: CÓ (v2)

#### Option A3 — Sys god mode bypass

Em đã KHÔNG đề xuất theo brief Phase 6 plan §3 Bước 4: "Không thêm Sys god-mode (Admin là cao nhất rồi)." Bỏ qua.

**Khuyến nghị**: **Option A1** — đơn giản nhất, không migration, không lockout risk từ schema change.

### 2.B Migration dữ liệu user hiện có

Hiện tại `operator.Role = "User"`. Không có trong whitelist mới. 2 cách:

#### Option B1 — Fix-up trong seed function (idempotent) ⭐ đề xuất

```csharp
static async Task SeedAdminUserAsync(MesDbContext db, IPasswordHasher<User> hasher)
{
    // Phase 6 Bước 4 — migrate legacy "User" → "Operator" before whitelist
    // takes effect. Runs every boot but only mutates rows that need it.
    var legacy = await db.Users.Where(u => u.Role == "User").ToListAsync();
    foreach (var u in legacy)
    {
        u.Role = UserRole.Operator;
        u.UpdatedAt = DateTime.UtcNow;
    }

    // Existing skip-if-exists admin + operator seed, but seed operator with
    // Role = Operator instead of "User".
    if (!await db.Users.AnyAsync(u => u.Username == "admin")) { ... Role = Admin ... }
    if (!await db.Users.AnyAsync(u => u.Username == "operator")) { ... Role = Operator ... }

    await db.SaveChangesAsync();
}
```

**Ưu**: idempotent; sau lần đầu chạy không còn legacy nào; nếu có user khác với role lỗi cũng được fix.

**Nhược**: silent mutate row tài khoản người dùng — operator có thể không biết role đã đổi.

#### Option B2 — Whitelist accept legacy + log warning

Cho `"User"` vào whitelist tạm + log warning + để admin tự update qua Account UI.

**Ưu**: zero data mutation tự động.
**Nhược**: 2 role tương đương semantic ("User" + "Operator") — confusing.

**Khuyến nghị**: **Option B1** vì dữ liệu nhỏ (1 row) + idempotent + dev DB (không phải prod).

**Backup yêu cầu**: TRƯỚC restart sau commit, backup tường minh + SHA256 (giống Phase 5 Bước 4 methodology). Verify post-restart: `SELECT Username, Role FROM Users` → 2 rows `admin/Admin` + `operator/Operator`.

### 2.C Authorization matrix — ĐỀ XUẤT, CHỜ EM DUYỆT

> Convention: R = read (xem page), W = write (mutate actions/buttons), – = blocked

| Surface | Admin | Supervisor | Engineer | QC | Operator |
|---|---|---|---|---|---|
| `/` Index home | R | R | R | R | R |
| `/dashboard` | R | R | R | R | R |
| `/workorders` (list + step view) | RW | RW | R | R | R |
| `/workorders` Start/Pause/Finish | RW | RW | – | – | RW |
| `/workorders` Advance + QC Pass btn | RW | RW | – | RW | – |
| `/workorders` Mở khoá bước flags | RW | RW | – | – | RW |
| `/workinstructions` | RW | R | RW | R | R |
| **NPI dropdown** | | | | | |
| `/npi/engineer-routine` | RW | R | RW | R | – |
| `/npi/engineer-structure` | RW | R | RW | R | – |
| `/npi/engineer-spec` | RW | R | RW | – | – |
| `/npi/raw-materials` | RW | R | RW | R | – |
| `/npi/workcenter` | RW | R | RW | R | – |
| **QC dropdown** | | | | | |
| `/qcqa/iqc` (stub) | R | R | – | R | – |
| `/qcqa/ipqc` | RW | R | – | RW | – |
| `/qcqa/oqc` | RW | R | – | RW | – |
| **Settings — User group** | | | | | |
| `/settings/profile` · `/mypwd` · `/appearance` · `/hardware` · `/mode` | RW | RW | RW | RW | RW |
| **Settings — System group** | | | | | |
| `/settings/account` (admin) | RW | – | – | – | – |
| `/settings/about` | R | R | R | R | R |
| **Settings — Maintenance group (admin)** | | | | | |
| `/settings/data` · `/syslog` · `/import-legacy` | RW | – | – | – | – |

**Policies cần tạo**:
- `AdminOnly` (giữ) — `RequireRole(Admin)`
- `SupervisorOrAbove` — `RequireRole(Admin, Supervisor)`
- `EngineerOrAbove` — `RequireRole(Admin, Supervisor, Engineer)`  
  *(Engineer làm spec/routing/structure → đứng cao hơn QC trong scope NPI)*
- `QcOrAbove` — `RequireRole(Admin, Supervisor, QC)` — cho QC IPQC/OQC + QC Pass
- `OperatorOrAbove` — `RequireRole(Admin, Supervisor, Operator)` — cho WO run actions

> Bước 4 chỉ enforce **page-level** (tab visibility + route gate). Mutation-level (button trong page) đề xuất defer Bước 5 cùng audit log để vừa enforce vừa stamp ai làm gì. Nếu cần làm ngay trong Bước 4 thì em báo.

### 2.D Account mutation scope (defer từ Bước 2B)

#### Phạm vi tối thiểu an toàn ⭐ đề xuất

1. **Create user** — admin nhập Username + DisplayName + Role + temp password.
2. **Edit user** — đổi DisplayName + Role (không đổi Username).
3. **Reset password** — admin set temp password mới; user phải đổi sau khi login (cần thêm `MustChangePassword` field — **MIGRATION** v2 nhẹ).
4. **Disable user** — soft-delete: thêm `IsActive` field (default true); login check; UI badge "Disabled". **MIGRATION** v2 nhẹ.

#### Safety invariants (CỨNG)

| Invariant | Lý do |
|---|---|
| Không thể đổi role khỏi `Admin` nếu là Admin cuối cùng còn active | Lockout protection |
| Không thể disable Admin cuối cùng còn active | Lockout protection |
| Không thể disable chính mình | Self-lockout |
| Không thể đổi role của chính mình (đề phòng auto-demote nhầm) | Self-lockout |
| Không thể xóa cứng user — chỉ disable | Audit + recovery |
| Role mới chỉ trong `UserRole.All` | Whitelist enforce |

#### Migration mới — `MustChangePassword` + `IsActive`

```csharp
// User entity additions
public bool MustChangePassword { get; set; } = false;
public bool IsActive { get; set; } = true;
```

Pipeline: `dotnet ef migrations add AddUserMustChangeAndIsActive -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web -o Migrations`. Phase 5 Bước 4 `DbInitializer.InitializeAsync` đã baseline-aware → migration mới apply normally sau khi history table có Init row.

**Defaults an toàn**:
- Existing user (admin + operator) → `IsActive = true`, `MustChangePassword = false`
- Migration Up: `ALTER TABLE Users ADD COLUMN MustChangePassword INTEGER NOT NULL DEFAULT 0; ALTER TABLE Users ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1;`

**LOC ước tính**: ~120 (UserAdminService mutation methods + Account UI form/modal + migration + i18n)

#### Phạm vi MAXIMUM (nếu em duyệt)

- Provisioning Card pattern (Ops Control v1.2): temp pwd one-shot + must-change-pwd. **Đề xuất defer Bước 5+** vì cần email/print flow.
- Lockout sau N lần fail login. **Defer Phase 7** vì cần auth-throttling infra.

### 2.E recover-admin script

#### Option E1 — Standalone scripts project `scripts/RecoverAdmin/` ⭐ đề xuất

Cấu trúc:
```
scripts/RecoverAdmin/
  RecoverAdmin.csproj  (ConsoleApp net10.0, ref Infrastructure + Application)
  Program.cs
README.md
```

Chạy:
```bash
cd scripts/RecoverAdmin
dotnet run -- --reset admin --new-password <pwd>
# hoặc
dotnet run -- --create recovery-admin --password <pwd>
```

**Logic**:
1. Đọc `DATA_DIR` hoặc default `src/CCL.MES.Web/ccl_mes.db`
2. Open MesDbContext same provider config
3. Prompt confirm: yêu cầu gõ `CONFIRM-RECOVER`
4. `--reset <username>`: tìm user theo Username, set `Role = Admin`, `IsActive = true`, `MustChangePassword = true`, `PasswordHash = hasher.HashPassword(user, newPassword)`. Save.
5. `--create <username>`: tạo user mới với Role=Admin + IsActive=true + MustChangePassword=true. Nếu đã tồn tại → error, suggest --reset.
6. Print "Admin user X is now active with role Admin, must change password on next login."
7. **Không** print mật khẩu (operator nhập trên CLI).
8. Audit row log to file `scripts/RecoverAdmin/recover.audit.log` với timestamp + action + user + actor (`Environment.UserName` của OS).

**Ưu**:
- KHÔNG expose qua web → an toàn nhất.
- Trust boundary = OS user có file access (đúng pattern Ops Control v1.2).
- Reuse `MesDbContext` + `PasswordHasher<User>` qua project ref → đỡ duplicate code.

**Nhược**:
- Cần thêm 1 csproj — `dotnet build` của solution mới phải include.

#### Option E2 — CLI arg trong CCL.MES.Web

Check `args[0] == "recover-admin"` ở đầu Program.cs trước khi `app.Run()`. Nếu match → chạy logic recover rồi exit.

**Ưu**: Không project mới.

**Nhược**:
- Web project mix với CLI tool — confusing.
- Risk: ai đó accidentally chạy `dotnet run -- recover-admin` trên prod server.

**Khuyến nghị**: **Option E1** — separate project, sạch hơn.

#### Bonus: chmod 600 trên users.json/ccl_mes.db
Trên Linux/Mac prod thực, OS-level file permission là trust boundary. README trong scripts/RecoverAdmin/ note rõ.

---

## 3. Rủi ro chi tiết + mitigation

| Rủi ro | Mức | Mitigation |
|---|---|---|
| Tự khóa quyền (đổi role admin → operator của chính mình) | **CAO** | Invariant: không cho đổi role chính mình (xem §2.D); recover-admin script làm phao |
| Disable admin cuối cùng | **CAO** | Invariant: không cho disable Admin cuối cùng + không cho disable chính mình; recover-admin script |
| Migration v2 (`MustChangePassword` + `IsActive`) làm sai column → DB không boot | **TRUNG BÌNH** | Phase 5 Bước 4 methodology A→B→C: backup tường minh + SHA256 + test trên copy DB trước; verify post-migrate row counts không đổi |
| Whitelist enforce vỡ login (vd user nào đó có Role tự custom) | **TRUNG BÌNH** | Seed fix-up migrate `"User"` → `"Operator"` trước khi whitelist active; log warning nếu thấy role lạ |
| 5 policy mới sai mapping → mất quyền hợp lệ | **TRUNG BÌNH** | Smoke matrix đầy đủ trên 5 role × ~20 route; test trên copy DB trước |
| App lockout sau khi merge | **CAO** | Backup DB tường minh + rollback runbook trong PR (giống PR #9 Phase 5 Bước 4) |
| recover-admin script bị abuse (ai có shell access mint admin) | **THẤP** | Đây là **by design** (trust = OS shell) — note rõ trong README; production setup nên `chmod 600` DB file |

---

## 4. Đề xuất thứ tự con + branch base

### Sub-step trong Bước 4

| # | Tên | LOC | Rủi ro | Test gate |
|---|---|---|---|---|
| 4.1 | `UserRole` whitelist class + 4 policy mới + seed fix-up `User` → `Operator` | ~80 | Trung bình | Login admin + operator, verify role claim |
| 4.2 | Áp policy vào pages (matrix § 2.C) + AuthorizeView hide dropdown items | ~50 | Cao (page-level lockout) | Smoke 5 role × các tab quan trọng |
| 4.3 | Migration v2 `AddUserMustChangeAndIsActive` (entity + migration) | ~40 | Trung bình | Test trên copy DB (Phase 5 Bước 4 methodology) |
| 4.4 | `UserAdminService` mutation methods + safety invariants | ~120 | Cao (lockout) | Unit-like smoke: try last-admin demote → expect rejected |
| 4.5 | Account.razor mutation UI (form Create/Edit/Reset/Disable) | ~200 | Trung bình | Manual UI test (form POST qua circuit) |
| 4.6 | recover-admin script standalone project | ~150 | Thấp | Console smoke: reset admin → login bằng pwd mới OK |
| 4.7 | i18n + CSS cho 4.5 + tổng smoke + backup verify | ~50 | Thấp | Restore-from-backup verify SHA byte-identical |

**Tổng**: ~690 LOC. Single PR vì các sub-step gắn kết chặt (đổi role mà chưa có recover = nguy hiểm).

### Branch base

**Đề xuất từ `main`** + ride-along Bước 2B `PagingHelper.cs` (đã làm pattern này ở Bước 3) — KHÔNG stack lên PR đang OPEN. Lý do:

- Bước 4 chỉ touch: `Program.cs` / `User.cs` / `MainLayout.razor` / `Settings/Account.razor` / `UserAdminService.cs` (PR #12) / + nhiều page mới gate.
- File chung với PR #12: `UserAdminService.cs` + `Account.razor`. Đây là dependency thực — sẽ conflict.
- Solution: stack `feat/phase6-rbac-roles` trên `feat/phase6-settings-system` (PR #12) → khi PR #12 merge, base auto-retarget hoặc tạo PR replacement giống Phase 5 closing (#6 → #8 pattern).

→ Em đề xuất **stack trên PR #12** thay vì từ `main` — vì dependency cứng trên UserAdminService + Account.razor.

### Phụ thuộc PR đang OPEN

- PR #12 (Settings System) — **HARD dependency**: cần UserAdminService cho mutation
- PR #10/#11/#13 — không đụng RBAC, độc lập

---

## 5. Câu hỏi cần em duyệt trước khi code

### Q1 — Role storage
Chọn (A1) const string + whitelist ⭐ hay (A2) enum + migration?

### Q2 — Authorization matrix § 2.C
Có gì em muốn đổi? Vd:
- Engineer có nên thấy QC IPQC/OQC (read-only) không? (matrix hiện đang KHÔNG)
- QC role có nên thấy NPI Engineer Spec không? (matrix hiện đang KHÔNG)
- Supervisor có nên RW WorkInstructions không? (matrix hiện đang R only)

### Q3 — Account mutation scope
Chọn:
- **Minimal**: Create + Edit (DisplayName/Role) + Reset password + Disable (4 method, 1 migration thêm 2 field) ⭐ đề xuất
- **Maximum**: + Provisioning Card pattern (one-shot temp pwd + must-change-pwd UI flow)

### Q4 — Mutation level gating
Bước 4 chỉ enforce page-level → Mutation button (vd nút "QC Pass" trên WorkOrders.razor cho QC role) defer Bước 5 cùng audit log. OK hay làm ngay Bước 4?

### Q5 — recover-admin script
Chọn (E1) standalone project ⭐ hay (E2) CLI arg trong Web?

### Q6 — Branch base
Stack trên PR #12 (đề xuất, hard dep) hay từ main + ride-along như Bước 3?

### Q7 — Migration v2
Có chấp nhận methodology Phase 5 Bước 4 (backup + test copy DB + verify) cho migration mới `AddUserMustChangeAndIsActive`?

### Q8 — Seed fix-up
OK với silent migrate operator `Role="User"` → `"Operator"` trong seed function, hay muốn make explicit (admin xác nhận trên UI)?

---

## 6. Tổng kết

Bước 4 là bước rủi ro cao nhất Phase 6 (đụng auth + có thể lockout). Mitigation chính:
1. **Option A1** (const string whitelist) → không migration cho role storage
2. **Safety invariants** trong UserAdminService → không thể self-lockout hoặc disable last admin
3. **recover-admin script** standalone → phao cứu nếu kẹt
4. **Phase 5 Bước 4 methodology** cho migration v2 (`MustChangePassword` + `IsActive`)
5. **Backup tường minh** + rollback runbook trong PR

STOP. Chờ em duyệt 8 câu hỏi + chốt matrix § 2.C → em tạo branch + code → PR #14.
