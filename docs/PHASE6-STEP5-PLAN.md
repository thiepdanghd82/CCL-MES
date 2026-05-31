# Phase 6 — Bước 5: Audit Log + Syslog tab + Backup Restore (KHẢO SÁT)

> **Trạng thái: KHẢO SÁT (read-only).** Chưa code, chưa tạo branch.
> Bước 5 đụng audit log (ghi mọi thao tác) + backup Restore (RỦI RO MẤT DATA CAO NHẤT).
> Sau khi em duyệt phương án em mới tạo `feat/phase6-audit-log` để code → PR #15.

---

## 1. Khảo sát hiện trạng

### 1.1 Audit emit candidates (nơi cần ghi log)

| File:line | Mutation | Actor hiện có? |
|---|---|---|
| `Pages/Login.cshtml.cs:64-71` | Login fail (wrong creds + disabled) | Username (từ Input) |
| `Pages/Login.cshtml.cs:81-88` | Login OK + claims + cookie issue | User entity loaded |
| `Pages/Logout.cshtml.cs` | Sign-out | ClaimsPrincipal |
| `Services/UserAdminService.cs` (PR #14) Create/UpdateRole/UpdateDisplayName/ResetPassword/SetActive | RBAC mutations | ClaimsPrincipal đã pass |
| `Services/UserProfileService.cs` (PR #11) UpdateDisplayName/ChangePassword | Self profile + pwd | ClaimsPrincipal đã pass |
| `Application/Services/WorkOrderService.cs:78-87` `AdvanceAsync(id, user)` | WO step advance | `user` string param |
| `Application/Services/WorkOrderService.cs:90-103` `UpdateFlagsAsync` | WO flags (MaterialsReady/SetupConfirmed/RohsOk/ProducedQty) | KHÔNG có actor — gap |
| `Application/Services/QcService.cs:12-36` `CreateAsync` | QC inspection create | `r.InspectorId` |
| `Application/Services/QcService.cs:39-58` `ApproveAsync(id, pass, user)` | QC approve + Fail → WO OnHold | `user` param |
| `Application/Services/SpecService.cs:19-44` `CreateAsync` | Spec + Version create | KHÔNG có actor — gap |
| `Application/Services/SpecService.cs:46-55` `ApproveAsync(id, user)` | Spec approve | `user` param |
| `Application/Services/WiService.cs:*` `CreateAsync`/`ApproveAsync` | Work instruction | check |
| `Application/Services/OeeService.cs:*` `StartAsync`/`PauseAsync`/`ResumeAsync`/`FinishAsync` | Production logs | `operatorId` param |
| `Web/Services/BackupService.cs:CreateSnapshot` (PR #12) | Backup file written | KHÔNG có actor — gap |
| `Pages/SetLanguage.cshtml.cs` | Culture cookie write | Không nghiệp vụ → KHÔNG audit |

**Gap**: `UpdateFlagsAsync`, `SpecService.CreateAsync`, `BackupService.CreateSnapshot` chưa nhận actor → cần kéo `ClaimsPrincipal` hoặc thêm `actor` param.

### 1.2 Migration pipeline hiện trạng

Sau Phase 6 Bước 4 (PR #14):
- v1 = `20260531050444_Init` (Phase 5)
- v2 = `20260531070602_AddUserMustChangeAndIsActive` (Phase 6 Bước 4)
- DbInitializer baseline-aware (cross-provider)

Bước 5 sẽ thêm **v3** = `AddAuditLog`. Ride pipeline cũ, methodology A→B→C.

### 1.3 Settings/Logs.razor + Settings/Backup.razor

- `Pages/Settings/Logs.razor:1-19` — placeholder thuần, gated `[Authorize(Policy="AdminOnly")]`.
- `Pages/Settings/Backup.razor` (PR #12 update) — đã có Snapshot button + list snapshots, **chưa có Restore**.

### 1.4 PagingHelper

Đã extract (PR #12). Bước 5 dùng cho Syslog grid — KHÔNG tạo local copy.

### 1.5 PR stack hiện tại (đều OPEN)

| PR | Tên | Base |
|---|---|---|
| #10 | Bước 1 — Engineer Spec UI | main |
| #11 | Bước 2A — Settings User group | main |
| #12 | Bước 2B — Settings System group | main |
| #13 | Bước 3 — QC tabs | main |
| #14 | Bước 4 — RBAC 5-role | feat/phase6-settings-system (stack PR #12) |

Bước 5 phụ thuộc PR #14 (UserAdminService.CreateAsync để hook audit) + PR #12 (BackupService) + PR #11 (UserProfileService). PR #11 + PR #12 đều có content cần — vì PR #14 đã stack #12, stack #14 → có cả #12 + #14 nhưng KHÔNG có #11. 

→ Bước 5 stack trên `feat/phase6-rbac-roles` (PR #14). UserProfileService audit emit + clear MustChangePassword là cross-cut với PR #11 — xử lý qua follow-up commit trên PR #11 branch (xem §5.6).

---

## 2. Phương án từng phần

### 2.A AuditLog entity + IAuditWriter + AuditService

#### Entity shape (đề xuất)

```csharp
// Domain/Entities/AuditLog.cs (mới)
public class AuditLog : BaseEntity
{
    public DateTime Timestamp { get; set; }       // UTC
    public string ActorUsername { get; set; } = "";  // "anonymous" khi pre-auth
    public string ActorRole { get; set; } = "";      // snapshot tại thời điểm
    public string Action { get; set; } = "";         // const string code: LOGIN_OK, USER_CREATE, ...
    public string? TargetType { get; set; }          // User, WorkOrder, Spec, QcInspection, Backup, null
    public string? TargetId { get; set; }            // long.ToString() hoặc filename
    public string? Detail { get; set; }              // JSON string (tự do, max 4 KB)
    public string? IpAddress { get; set; }           // X-Forwarded-For hoặc Remote
    public string Source { get; set; } = "Web";      // Web / Console / Hub
}
```

**Indexes**:
- `Timestamp DESC` — sort hiển thị
- `ActorUsername` — filter theo người
- `Action` — filter theo loại

#### IAuditWriter (Application interface) + AuditService (Web implementation)

```csharp
// Application/Audit/IAuditWriter.cs (mới)
public interface IAuditWriter
{
    Task EmitAsync(string action, string actor, string actorRole,
        string? targetType = null, string? targetId = null,
        string? detail = null, string source = "Web");
}

// Web/Services/AuditService.cs (mới) — implementation
public class AuditService : IAuditWriter
{
    private readonly IMesDbContext _db;
    private readonly IHttpContextAccessor _http;
    public AuditService(IMesDbContext db, IHttpContextAccessor http) { _db = db; _http = http; }

    public async Task EmitAsync(string action, string actor, string actorRole,
        string? targetType = null, string? targetId = null,
        string? detail = null, string source = "Web")
    {
        var row = new AuditLog {
            Timestamp = DateTime.UtcNow,
            ActorUsername = actor,
            ActorRole = actorRole,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Detail = detail,
            IpAddress = _http.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            Source = source,
        };
        _db.AuditLogs.Add(row);
        await _db.SaveChangesAsync();
    }
}
```

#### Action code constants

```csharp
// Domain/Audit/AuditAction.cs (mới)
public static class AuditAction
{
    public const string LoginOk = "LOGIN_OK";
    public const string LoginFail = "LOGIN_FAIL";
    public const string LoginDisabled = "LOGIN_DISABLED";  // ghi rõ kỳ vọng từ chối
    public const string Logout = "LOGOUT";
    public const string UserCreate = "USER_CREATE";
    public const string UserRoleChange = "USER_ROLE_CHANGE";
    public const string UserDisplayChange = "USER_DISPLAY_CHANGE";
    public const string UserResetPassword = "USER_RESET_PASSWORD";
    public const string UserSetActive = "USER_SET_ACTIVE";
    public const string UserSelfPasswordChange = "USER_SELF_PWD_CHANGE";
    public const string WoAdvance = "WO_ADVANCE";
    public const string WoFlags = "WO_FLAGS_UPDATE";
    public const string QcCreate = "QC_CREATE";
    public const string QcApprove = "QC_APPROVE";  // Pass hoặc Fail trong detail
    public const string SpecCreate = "SPEC_CREATE";
    public const string SpecApprove = "SPEC_APPROVE";
    public const string BackupCreate = "BACKUP_CREATE";
    public const string BackupRestore = "BACKUP_RESTORE";  // console only
}
```

#### Emit pattern — explicit calls (KHÔNG interceptor)

**Lý do**:
- Interceptor (EF SaveChanges hook) ghi tất cả SQL changes → noise (LastLoginAt update mỗi login, snapshot lưu state)
- Business meaning lost — interceptor không biết "đây là WO_ADVANCE hay là WO_FLAGS_UPDATE"
- Login fail KHÔNG có SaveChanges → interceptor bỏ sót
- Explicit cho phép gắn JSON detail có ý nghĩa

**Ưu**:
- Single source of truth, mỗi action có code rõ
- Filter UI dễ
- Migration đơn — chỉ 1 bảng

**Nhược**:
- 10+ callsite cần wire emit
- Quên emit ở 1 callsite = silent gap

**LOC**: ~150 (entity + interface + service + AuditAction class) + ~80 wire emit ~10 callsite

**Migration v3**: `AddAuditLog` — qua A→B→C giống Phase 6 Bước 4.

### 2.B Syslog tab — /settings/syslog

#### Phân biệt "audit log nghiệp vụ" vs "system log file"

| | Audit log (Bước 5 scope) | System log file (defer) |
|---|---|---|
| Nguồn | Bảng AuditLog trong DB | ASP.NET logger sink (console/file) |
| Nội dung | Hành động nghiệp vụ ai/khi nào/làm gì | Stack traces / EF SQL / startup messages |
| UI | Grid + filter + Pager | Cần tail-style + parsing |
| Compliance | Cần (operator accountability) | Optional |

→ Bước 5 = audit log only. System log file viewer = **defer Phase 7** (cần log sink + parsing infra).

#### UI design

- Page `/settings/syslog`, gated `[Authorize(Policy="AdminOnly")]` (giữ nguyên)
- Grid bám pattern NPI: search box + Pager (qua `PagingHelper`)
- Columns: Timestamp UTC / Actor (Username + role badge) / Action (badge code) / Target / Detail (truncate 80 char + hover full) / IP
- Filters trong toolbar:
  - Date range (from/to date pickers)
  - Action dropdown (filtered list of distinct actions)
  - Actor text input
- (Defer) Export CSV — Phase 7

**LOC**: ~250 (Syslog.razor + AuditLogService.ListAsync + i18n EN+VI)

### 2.C Backup Restore — KHUYẾN NGHỊ CHỈ CONSOLE (option A)

**Rủi ro nếu làm qua Web UI**:
1. SQLite file lock khi app đang chạy → atomic swap khó (cần dừng app trước)
2. Mid-restore failure → DB partial, không có rollback
3. Admin gõ nhầm filename / không đọc kỹ confirmation → mất 60k row NPI
4. SQL Server: restore qua app vô nghĩa (operators dùng SSMS / `RESTORE DATABASE` T-SQL)
5. Race condition: nếu 2 admin cùng restore khác file = vỡ

**Option A — Console only ⭐ đề xuất**

`scripts/BackupRestore/` standalone project:
```bash
cd scripts/BackupRestore
dotnet run -- --from ccl_mes.db.bak.snapshot-20260601-101530
# CONFIRM-RESTORE prompt
# Auto-backup current DB → ccl_mes.db.bak.pre-restore-<ts> trước khi restore
# Show row count diff after
```

Web Backup tab (Bước 2B đã có) chỉ thêm thông tin card: "Restore through scripts/BackupRestore (console-only). See README."

**Ưu**:
- Operator phải dừng app trước → atomic swap an toàn
- Trust boundary = OS shell (giống recover-admin)
- Auto-backup-before-restore là phao cứu nếu restore sai
- SQL Server không hỗ trợ → script bỏ qua + message rõ

**Nhược**:
- Operator phải SSH lên server (không tự làm qua web)
- Cần document quy trình trong README

**Option B — Web UI với confirmation gate đa lớp**

3 lớp confirmation:
1. Modal mở: cảnh báo + danh sách backup
2. Gõ literal "I-UNDERSTAND-DATA-LOSS"
3. Modal cuối: countdown 10s rồi mới active button

Cộng auto-backup-before-restore + admin-only + audit emit.

**Ưu**:
- Convenience: không cần SSH
- Audit hookable (admin X restored backup Y at time Z)

**Nhược (lớn)**:
- SQLite file lock vấn đề chưa giải quyết (cần app self-stop + spawn separate process)
- Mid-restore crash = ngoài kiểm soát từ web layer
- 1 admin chậm tay click sai = mất data

**Option C — Hybrid (script + web shows button that triggers script)**

Web UI launch process `scripts/BackupRestore` qua `Process.Start` → script chạy ngoài đời thực. Vẫn cần dừng app, không khác option A nhiều.

#### KHUYẾN NGHỊ: **Option A** (console only)

- Bước 5 ship: `scripts/BackupRestore` console project + Backup tab update guidance card
- Restore audit emit: từ chính console script → ghi vào DB SAU KHI restore xong (giống recover-admin `recover.audit.log` text file)
- Future Phase 7+ có thể thêm Option B nếu thực sự cần — nhưng default = an toàn.

**LOC**: ~150 (BackupRestore.csproj + Program.cs + README + Backup.razor update card)

### 2.D Cross-cut: UserProfileService.ChangePasswordAsync clear MustChangePassword

User flag: "ưu tiên xử lý sớm vì user bị buộc đổi mật khẩu sẽ kẹt".

**Solution**: tiny commit trên `feat/phase6-settings-user` (PR #11) ADD 2 lines:
```csharp
user.PasswordHash = _hasher.HashPassword(user, newPassword);
user.MustChangePassword = false;  // Phase 6 Bước 4 follow-up
user.UpdatedAt = DateTime.UtcNow;
```

**Vì PR #11 + PR #14 cả 2 đều mở** → user sau ResetPassword (Bước 4) bắt buộc đổi (set true) nhưng nếu ChangePasswordAsync không clear flag → flag stay true forever, login lần kế lại bị buộc đổi → infinite loop.

**Đề xuất**:
1. **Trước khi code Bước 5**: switch sang `feat/phase6-settings-user` branch, add tiny commit "fix(profile): clear MustChangePassword on successful self-change", push.
2. Bước 5 không depend trên fix này (Bước 5 không enforce MustChangePassword), nhưng làm sớm tránh kẹt khi anh review PR #11 + #14 cùng đợt.

**LOC**: ~3

### 2.E Cross-cut: SpecService.PageAsync → PagingHelper

Carry-over từ Bước 1 (PR #10). Bước 5 không bị block, nhưng cleanup trên `feat/phase6-engineer-spec-ui` branch sau khi Phase 6 close-out.

**LOC**: ~12 (swap 1 callsite + remove local PageAsync 7 LOC)

→ **Đề xuất defer Phase 6 close-out** (giống pattern Phase 5 final).

---

## 3. Rủi ro chi tiết + mitigation

| Hạng mục | Rủi ro | Mức | Mitigation |
|---|---|---|---|
| Migration v3 vỡ schema | Restart crash | **TB** | Phase 6 Bước 4 methodology A→B→C: backup + test copy DB + verify NPI rows + restart proof |
| AuditLog phình to | Nhanh đầy DB | **TB** | Indexed; retention policy = defer Phase 7 (cron job purge >180 ngày) |
| Quên emit ở callsite mới | Silent gap | **THẤP-TB** | Code review + checklist trong PR description |
| **Backup Restore via Web ghi đè DB** | **CAO NHẤT** — mất 60k row NPI | **CAO** | → Đề xuất Option A console-only → loại bỏ vấn đề |
| Restore console script gõ sai filename | Mất data | **TB** | Auto-backup-before-restore + verify row count diff + CONFIRM-RESTORE prompt |
| Audit JSON detail chứa secret (password / token) | Leak qua log | **THẤP** | Convention: KHÔNG bao giờ ghi PasswordHash hoặc plain pwd vào detail; AuditService có sanitize whitelist field |
| Audit log chứa PII (username + IP) | GDPR | **THẤP** | CCL nội bộ; retention policy + admin-only access ngăn external leak |

---

## 4. Sub-step trong Bước 5

| # | Tên | LOC | Rủi ro | Test gate |
|---|---|---|---|---|
| 5.0 | **Pre-flight cross-cut** trên PR #11: clear MustChangePassword | ~3 | THẤP | Test ResetPassword → login → ChangePassword → MustChangePassword=0 |
| 5.1 | `AuditLog` entity + migration v3 + `AuditAction` const class | ~80 | TB | A→B→C |
| 5.2 | `IAuditWriter` (Application) + `AuditService` (Web) + register Scoped | ~70 | THẤP | Unit-style smoke: EmitAsync ghi 1 row |
| 5.3 | Wire emit ở 10+ callsite (Login + Logout + UserAdmin + WO + QC + Spec + Backup) | ~80 | TB | Smoke: do mỗi action → verify row appended |
| 5.4 | `AuditLogService.ListAsync` + `Syslog.razor` UI grid + filter + Pager | ~250 | THẤP | EN+VI render + 5-role × access check |
| 5.5 | `scripts/BackupRestore` console project | ~150 | CAO (DR drill) | Console smoke: --from file → restore success + row count match |
| 5.6 | `Backup.razor` thêm Restore guidance card | ~30 | THẤP | EN+VI render |
| 5.7 | i18n + CSS + tổng smoke | ~50 | THẤP | Final A→B→C verify |

**Tổng**: ~700 LOC. Single PR.

---

## 5. Branch base

**Đề xuất stack trên `feat/phase6-rbac-roles` (PR #14)**:
- Cần `UserAdminService` cho audit emit ở 5 mutation method
- Cần `BackupService` từ Bước 2B (đã có qua PR #12 chain)
- Cần migration v2 đã apply (DbInitializer baseline)

Khi PR #14 merge sẽ cần PR replacement (giống pattern Phase 5 close — PR #6→#8 / #7→#9).

**Pre-flight 5.0**: tiny commit trên `feat/phase6-settings-user` (PR #11). KHÔNG ride-along vào Bước 5 branch — quá xa scope.

---

## 6. Câu hỏi cần em duyệt

### Q1 — AuditLog entity shape § 2.A
Em có muốn thêm/bớt field nào? Cụ thể:
- IpAddress: cần không? (CCL nội bộ thường all 192.168.*)
- Detail là JSON tự do hay structured (key-value)? Đề xuất: JSON string tự do, max 4 KB.
- ActorRole snapshot: cần lưu? (đề xuất: CÓ — sau khi role thay đổi vẫn biết tại thời điểm đó role gì)

### Q2 — Action code list § 2.A
20 code đủ chưa? Có action nào nên thêm?
- DraftCreate (WO/Spec) — đề xuất chưa, defer khi có business need
- Logout — em đề xuất CÓ; em có muốn skip không?
- LoginDisabled riêng vs LoginFail — em đề xuất riêng để dễ filter

### Q3 — Backup Restore — CHỌN OPTION
- **Option A** ⭐ console only — an toàn nhất, không UI Restore
- Option B — Web UI confirmation đa lớp + auto-backup
- Option C — Hybrid (Web triggers console)

### Q4 — Syslog scope
Defer system log file viewer sang Phase 7? Bước 5 chỉ làm audit log nghiệp vụ? (Đề xuất: defer.)

### Q5 — Audit emit ở các gap (WO.UpdateFlags, Spec.Create, Backup.Create)
Em duyệt thêm `actor` param vào 3 method này (Application/Web layer) HOẶC pass ClaimsPrincipal qua page → service?

### Q6 — Pre-flight 5.0 (PR #11 cross-cut)
Tiny commit "clear MustChangePassword on self-change" trên `feat/phase6-settings-user` branch TRƯỚC khi bắt đầu Bước 5? (Đề xuất: CÓ — không tốn nhiều thời gian, tránh anh review PR #11 + #14 thấy infinite-loop bug.)

### Q7 — Migration A→B→C cho v3 `AddAuditLog`
Em duyệt áp dụng Phase 6 Bước 4 methodology (backup + SHA256 + test copy DB + restart proof)?

### Q8 — Branch base
Stack `feat/phase6-rbac-roles` (PR #14) ⭐ hay từ main + ride-along giống Bước 3 / Bước 4 partial?

### Q9 — Audit retention
Phase 6 không enforce retention; defer Phase 7+ (cron purge >180 ngày). Audit table chỉ tăng — vài MB/năm với 5-user DB là chấp nhận. OK?

### Q10 — Export CSV cho Syslog
Có cần trong Bước 5 không? (Đề xuất: defer Phase 7, chỉ cần khi có compliance audit thực.)

---

## 7. Tổng kết

Bước 5 là Bước rủi ro cao thứ 2 Phase 6 (sau Bước 4 RBAC). Mitigation:
- **Restore = console only** → loại bỏ rủi ro Web-driven destructive op
- **Migration v3** qua A→B→C methodology đã chứng minh ở Phase 5 + 6 Bước 4
- **Pre-flight 5.0** đóng kẹt-mật-khẩu trước khi Bước 5
- **Explicit audit emit** ngăn silent gap + business meaning rõ

STOP. Chờ em chốt Q1–Q10 → em tạo branch + code → PR #15.
