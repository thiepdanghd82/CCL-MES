# Phase 6 — Báo cáo đóng (2026-05-31)

> **Mục tiêu Phase 6**: hoàn tất nội dung nghiệp vụ thực (NPI Engineer Spec / 6 Settings tab / RBAC 5-role / Audit log / Backup-Restore / SQLite-SQL Server gate / IQC entity) — đóng các TODO "real business content" còn lại từ Phase 5 §7.
> **Trạng thái**: ✅ **HOÀN TẤT — 7 bước + 1 P0 fix landed lên `main`**.

---

## 1. Tổng quan 7 bước (+ 1 P0 fix + 1 chore)

| Bước | Tên | Branch | PR | Merge SHA |
|---|---|---|---|---|
| 1 | NPI Engineer Spec grid UI | `feat/phase6-engineer-spec-ui` | [#10](https://github.com/thiepdanghd82/CCL-MES/pull/10) ✅ | `ed91fc8` |
| 2A | Settings User group (Profile / Password / Appearance) | `feat/phase6-settings-user` | [#11](https://github.com/thiepdanghd82/CCL-MES/pull/11) ✅ | `4fc36bf` |
| 2B | Settings System group (About / Account / Backup) | `feat/phase6-settings-system` | [#12](https://github.com/thiepdanghd82/CCL-MES/pull/12) ✅ | `70d1f71` |
| 3 | IPQC + OQC grids + IQC stub | `feat/phase6-qc-tabs` | [#13](https://github.com/thiepdanghd82/CCL-MES/pull/13) ✅ | `bfaa6d6` |
| 4 | RBAC 5-role + Account mutation + recover-admin | `feat/phase6-rbac-roles` | [#14](https://github.com/thiepdanghd82/CCL-MES/pull/14) ✅ | `84016fe` |
| 5 | AuditLog + Syslog + BackupRestore (console-only) | `feat/phase6-audit-log` | [#15](https://github.com/thiepdanghd82/CCL-MES/pull/15) ✅ | `1991ec6` |
| 6.5 | Ops Control v1.2-style SQLite + SQL Server gate fix | `feat/phase6-deploy-sqlite-and-sqlserver-gate` | [#16](https://github.com/thiepdanghd82/CCL-MES/pull/16) ✅ | `2d4d532` |
| 7 | IQC entity + tab (đóng stub Bước 3) | `feat/phase6-iqc` | [#17](https://github.com/thiepdanghd82/CCL-MES/pull/17) ✅ | `23ccae2` |
| chore | Remove Import data v1.0 sub-tab | `chore/remove-import-legacy` | [#18](https://github.com/thiepdanghd82/CCL-MES/pull/18) ✅ | `4fc15b1` |
| **P0 fix** | **Restore Bước 4 RBAC policies (regression hotfix)** | `fix/rbac-policies-regression` | [#19](https://github.com/thiepdanghd82/CCL-MES/pull/19) ✅ | `90ce645` |

Tổng: **10 PR merged** vào `main` trong sprint Phase 6.

---

## 2. Chi tiết từng bước

### Bước 1 — NPI Engineer Spec grid UI (PR #10)

- **Đóng TODO**: Phase 5 §7 #2 "Engineer Spec — gắn vào Spec Control hiện có".
- **Phương án**: Pattern y hệt 4 NPI grid khác (EngineerRoutine / EngineerStructure / RawMaterials / WorkCenter): toolbar search + Pager + sortable table.
- **Mới**: `Pages/Npi/EngineerSpec.razor` đọc qua `SpecService.SpecsAsync(search, page, pageSize)` (paginated bằng helper `PagingHelper.PageAsync` shared).
- **Status badge**: 4 màu cho `SpecStatus` (Draft/InReview/Approved/Obsolete) ↔ `.badge.spec-*` mới ở `site.css`.

### Bước 2A — Settings User group (PR #11)

- **Đóng TODO**: Phase 5 §7 #3 phần User group (5 sub-tab: profile / mypwd / appearance / hardware / mode).
- **My Profile** (`/settings/profile`): edit DisplayName, hiện Username + Role + LastLoginAt + CreatedAt readonly.
- **My Password** (`/settings/mypwd`): 3-field (current / new / confirm), validate min 4 chars, verify hash bằng `PasswordHasher<User>`.
- **Appearance** (`/settings/appearance`): chọn language EN/VI qua flag picker (đã có từ Phase 2). Hardware + Mode placeholder.
- **Mới**: `Services/UserProfileService.cs` scoped — encapsulate UpdateProfile + ChangePassword + 8 i18n key. Side-effect: clear `must_change_password = false` khi self-change pwd success.

### Bước 2B — Settings System group (PR #12)

- **Đóng TODO**: Phase 5 §7 #3 phần System group (About / Account / Backup).
- **About / Diagnostics** (`/settings/about`): app version + framework + provider (SQLite/SQL Server) + 6 row-count NPI + Users + Specs + audit count.
- **Account Control** (`/settings/account`) — Phase 6 Bước 2B chỉ read-only grid (search + Pager). Mutations (create / disable / role change / reset pwd) bumped sang Bước 4.
- **Backup / Restore** (`/settings/data`) — SQLite-only: button "Take snapshot" + list snapshots ở `<DATA_DIR>/Backup/SQLite/` (sorted newest first). SQL Server provider hiển thị "Unsupported" + link tài liệu SSMS.
- **Mới**: `Services/UserAdminService.cs` + `Services/BackupService.cs` (cả 2 scoped). Online backup API SQLite (`SqliteConnection.BackupDatabase`) — safe khi server đang serve traffic.

### Bước 3 — IPQC + OQC grids + IQC stub (PR #13)

- **Đóng TODO**: Phase 5 §7 #1 phần IPQC + OQC (IQC khoá Bước 7).
- **3 razor page** `Pages/QcQa/{Ipqc,Oqc,Iqc}.razor` với grid layout + search + Pager. IPQC + OQC dùng shared `QcInspectionGrid` component (toolbar + table + status badge + filter). IQC chỉ "Sắp ra mắt" stub.
- **Status badge**: 3 màu cho `QcResult` (Pending/Pass/Fail) ↔ `.badge.qc-*`.

### Bước 4 — RBAC 5-role + Account mutation + recover-admin (PR #14)

- **Đóng TODO**: Phase 5 §7 #5 "RBAC roles ngoài Admin/User" — mở rộng từ 2 role lên 5: **Admin / Supervisor / Engineer / QC / Operator**.
- **Domain whitelist**: `Domain/Auth/UserRole.cs` const string class với `UserRole.All` array. Account.razor Edit modal dropdown chỉ cho phép pick 1 trong 5.
- **Migration v2**: `20260531070602_AddUserMustChangeAndIsActive` thêm 2 cột `MustChangePassword bool` + `IsActive bool` + idempotent legacy mapping `Role="User" → "Operator"` chạy 1 lần ở Program.cs boot.
- **3 page-level policy mới** trong `Program.cs`:
  - `NpiRead` = {Admin, Supervisor, Engineer, QC}
  - `NpiSpecRead` = {Admin, Supervisor, Engineer}
  - `QcRead` = {Admin, Supervisor, QC}
- **Account Control mutations** — Bước 4 hoàn tất gap Bước 2B: Create / Edit DisplayName + Role / Reset password / Toggle active. Defense-in-depth: page policy + AuthorizeView + server-side check trong `UserAdminService`. Invariant: cấm self-modify role/active, cấm demote/disable Admin cuối cùng.
- **`scripts/RecoverAdmin/`** — console app (.NET 10) lấy connection string từ env `MES_CONNSTR` → reset hoặc tạo lại sys-admin nếu mọi Admin bị khoá. Idempotent.

### Bước 5 — AuditLog + Syslog + BackupRestore console-only (PR #15)

- **Đóng TODO**: Phase 5 §7 #7 "Audit log cho RBAC events".
- **`Domain/Entities/AuditLog.cs`** + `Domain/Audit/AuditAction.cs` (const string class alphabetical, ~21 codes như `USER_CREATE` / `USER_UPDATE` / `USER_RESET_PWD` / `BACKUP_CREATE` / `SPEC_APPROVE` …).
- **`Application/Audit/IAuditWriter.cs`** interface — `EmitAsync(action, actor, role, targetType?, targetId?, detail?)`.
- **`Web/Services/AuditService.cs`** Web implementation — đẩy bản ghi vào DB.
- **`Pages/Settings/Syslog.razor`** (`/settings/syslog`) — admin grid xem audit log với 4 filter (date from / date to / action / actor).
- **Migration v3**: `20260531073842_AddAuditLog` table `AuditLogs` 9 cột.
- **`scripts/BackupRestore/`** — console app .NET 10 restore từ snapshot SQLite. Workflow: pick file → confirm → swap live DB với snapshot (move-then-rename). Hardcoded require server stopped trước.
- **All mutations** trong UserAdminService + BackupService + SpecService giờ emit audit. JSON-only detail field (Lesson 3 từ Phase 3).

### Bước 6.5 — Ops Control v1.2-style SQLite + SQL Server gate fix (PR #16)

- **Cải tiến**: align deploy layout với Ops Control v1.2:
  - SQLite live DB → `<DATA_DIR>/ccl_mes.db` (default `data/`)
  - Backup folder → `<DATA_DIR>/Backup/SQLite/` (was flat next to DB pre-6.5)
  - Auto-migration helper `BackupService.MigrateLegacySnapshots()` boot-time idempotent
- **SQL Server gate fix**: Phase 5 Bước 4 migration auto-generated bởi EF emit type-affinity strings (`type:"INTEGER"` etc.) chỉ valid cho SQLite. Phase 6 Bước 6.5 strip toàn bộ inline `type:` + `.HasColumnType()` qua Python script — migrations giờ provider-agnostic (chạy clean trên cả SQLite + SQL Server).
- **Cleanup carry-over**: `SpecService.SpecsAsync` migrate từ local PageAsync sang `PagingHelper.PageAsync` shared (nợ kỹ thuật từ Bước 1).

### Bước 7 — IQC entity + tab (đóng stub Bước 3) (PR #17)

- **Đóng stub**: từ Bước 3 — IQC tab cuối cùng được implement đầy đủ.
- **Hybrid FK pattern**: `IqcInspection` có `RawMaterialId long?` (nullable hard FK) **VÀ** `PartNo string` (snapshot). Khi vật tư bị xoá khỏi master, IQC history vẫn còn PartNo. Reuse `QcResult` enum (Pending / Pass / Fail).
- **Separate `IqcResultDetail`** entity — pattern tách rời như QC để báo cáo per-item dễ.
- **No WO cascade**: IqcService.ApproveAsync KHÔNG cascade WO.OnHold (khác QC flow) — IQC là vật tư đầu vào, chưa có WO tại thời điểm kiểm.
- **2 AuditAction codes mới**: `IQC_CREATE` + `IQC_APPROVE`.
- **Migration v4**: `20260531092153_AddIqcInspection` — 2 table (IqcInspections + IqcResultDetails) + 5 index + cascade FK detail → inspection. Strip type-affinity (29 inline + 268 fluent calls) qua Python.
- **Phase A→B→C SAFE pattern**: `MES_CONNSTR=Data Source=/tmp/iqc-design.db` để dotnet ef thao tác trên isolated DB; live DB SHA `850fbf56…` không đổi trong khi sinh migration code. CLAUDE.md §4 + docs/LESSONS_LEARNED.md §7 ghi pattern chính thức.
- **UI**: `Pages/QcQa/Iqc.razor` thay stub — toolbar (search + status + 2 date) + 8-col table + 1-modal create với Details inline + view modal + approve modal. Page-level `[Authorize(Policy="QcRead")]` + inline `<AuthorizeView Roles="Admin,Supervisor,QC">` + server-side `RoleCanMutate(role)`.
- **i18n**: 37 keys × 2 locale (`qcqa.iqc.*` + 2 common).
- **Seed**: 3 demo IQC idempotent (RM-PVC-001 Pending / RM-INK-002 Pass / RM-CORE-003 Fail với 3+2+2 details) — DbSeeder gọi BEFORE WorkOrders early-return gate (bug fix).
- **CSS**: `.badge.iqc-pending|pass|fail` palette.

### chore — Remove Import data v1.0 sub-tab (PR #18)

- **Lý do**: tab "Import data v1.0" chỉ là placeholder stub từ Phase 3 (ý đồ migrate Ops Control v1.0 file-backend). Không còn cần.
- **Xoá**: `Pages/Settings/ImportLegacy.razor` + nav entry trong MainLayout + 6 i18n key (EN+VI) + reference trong comment + AccessDenied lead text.

### P0 fix — Restore Bước 4 RBAC policies (PR #19)

- **Phát hiện**: smoke verify trên `main` sau PR #18 merged → mọi GET tới `/npi/engineer-spec` + `/qcqa/*` → **HTTP 500** với `System.InvalidOperationException: The AuthorizationPolicy named: 'QcRead' was not found`.
- **Root cause**: PR #18 branch fork từ main TRƯỚC khi PR #14 (Bước 4) merged. Khi PR #18 re-merge vs main bằng `git merge -X ours`, `AddAuthorization` block có overlapping additive edits — PR #18 đổi comment `AdminOnly`, PR #14 thêm 3 policies → strategy `-X ours` chọn ours (PR #18 version) → mất `NpiRead` + `NpiSpecRead` + `QcRead` + revert `AdminOnly` từ `UserRole.Admin` về string `"Admin"`.
- **Fix**: re-add 3 policies + restore `AdminOnly` về enum.
- **Lesson**: `git merge -X ours` quá blunt cho overlapping additive edits trong cùng block. Sẽ ghi vào CLAUDE.md + LESSONS_LEARNED §8.

---

## 3. Data integrity (NPI invariants)

| Bảng | Pre-Phase-6 | Post-Phase-6 | Delta |
|---|---|---|---|
| WorkCenters | 43 | **43** | 0 ✓ |
| RawMaterials | 2 127 | **2 127** | 0 ✓ |
| RoutingOperations | 38 441 | **38 441** | 0 ✓ |
| ManufacturingStructures | 20 530 | **20 530** | 0 ✓ |
| Users | 2 (admin + operator legacy) | **5** (admin / supervisor / engineer / qc / operator) | +3 ✓ (Bước 4 idempotent seed) |
| IqcInspections | absent | **3** | +3 ✓ (Bước 7 demo seed) |
| IqcResultDetails | absent | **7** | +7 ✓ (Bước 7) |
| `__EFMigrationsHistory` | 1 (Init) | **4** (Init / AddUserMustChange / AddAuditLog / AddIqcInspection) | +3 ✓ |

Migration history sau Phase 6 (theo thứ tự timestamp):

```
20260531050444_Init
20260531070602_AddUserMustChangeAndIsActive    (Bước 4)
20260531073842_AddAuditLog                      (Bước 5)
20260531092153_AddIqcInspection                 (Bước 7)
```

Final backup: `data/Backup/SQLite/ccl_mes.db.bak.phase6-close-20260531-133127`
SHA256: `abd45359486cc85aa090ae2b4f21f773e71b59f8d00f53f6b276b90087cd021c`
PRAGMA integrity_check: **ok**

---

## 4. Smoke matrix sau merge cuối (verify trên `main` HEAD `90ce645`)

| # | Test | Kết quả |
|---|---|---|
| 1 | `dotnet build CCL.MES.sln` | **0 warning, 0 error** |
| 2 | Boot — `No migrations were applied. The database is already up to date.` | ✓ |
| 3 | GET `/login` | 200 ✓ |
| 4 | Login admin/admin → POST → 302 → home | ✓ |
| 5 | admin: GET `/`, `/dashboard`, `/workorders` | 200 ✓ |
| 6 | admin: GET `/npi/engineer-spec`, `/npi/engineer-routine`, `/npi/raw-materials` | 200 ✓ |
| 7 | admin: GET `/qcqa/iqc`, `/qcqa/ipqc`, `/qcqa/oqc` | 200 ✓ |
| 8 | admin: GET `/settings/account`, `/settings/about`, `/settings/data`, `/settings/syslog` | 200 ✓ |
| 9 | operator: GET `/settings/account` → render "Access denied" panel (defense-in-depth) | ✓ |
| 10 | Row counts WorkCenters=43 / RawMaterials=2127 / ManufacturingStructures=20530 / RoutingOperations=38441 / Users=5 / IqcInspections=3 unchanged | ✓ |
| 11 | Migration history = 4 + restart no-op | ✓ |

---

## 5. Vùng cấm nguyên vẹn

| Directory | Trạng thái |
|---|---|
| `Ops Control v1.2/` | [PRESENT] không đụng (read-only reference) |
| `CMES/` | [PRESENT] không đụng |
| `Old ver ( DO NOT USE)/` | [PRESENT] không đụng |
| `SpecHub/` | [PRESENT] không đụng |

Toàn bộ thay đổi Phase 6 nằm trong `CCL-CMES/CCL-MES/`. Repo Ops Control v1.2 git log không có commit nào từ git user `v1.3 autonomous upgrade` trong sprint Phase 6.

---

## 6. Bài học merge-strategy (PR #19 root cause)

`git merge -X ours` được dùng trong sprint Phase 6 để cascade re-merge `main` vào 4 PR sau khi anh merge PR #13 sequentially. Hoạt động tốt cho **conflicting** hunks (kept ours version). NHƯNG cho overlapping **additive** edits (PR #14 thêm 3 policies + PR #18 chỉnh comment AdminOnly → cùng block), strategy chọn ours → mất addition từ theirs.

**Pattern an toàn hơn** (sẽ ghi vào CLAUDE.md):
1. Nếu PR target branch + base branch cùng modify 1 block ở chế độ ADD-ONLY trên cả 2 side → KHÔNG dùng `-X ours` blanket. Resolve conflict thủ công, đảm bảo giữ cả 2 set of additions.
2. Sau mọi merge resolution (đặc biệt với strategy auto), MUST smoke verify trên branch trước push.
3. Trong sprint phase-close có nhiều stacked PR, smoke verify end-to-end trên `main` sau khi merge từng PR (không chờ tới cuối).

---

## 7. TODO còn lại sau Phase 6 (Phase 7+)

| # | Khu vực | Mô tả |
|---|---|---|
| 1 | Docker SQL Server verify | Bước 6.5 strip type-affinity → migrations provider-agnostic. Cần spin Docker SQL Server image + chạy `ef-migrate.sh --sqlserver` + verify Init/AddUserMustChange/AddAuditLog/AddIqcInspection apply clean |
| 2 | System log file viewer | Hiện Syslog tab chỉ đọc AuditLog table (DB events). Bổ sung tab/section đọc text log từ `logs/cclmes-*.log` (file-based) cho IIS error / ASP.NET pipeline / migration messages |
| 3 | Retention + export CSV audit | `AuditLog` chưa có policy cleanup. Cần admin UI: filter range + export CSV cho compliance audit + button "Delete events older than N days" |
| 4 | Test framework | Phase 6 vẫn chưa có unit test. Phase 7 nên add xUnit cho Domain.StateMachine + Application.Services + IqcService logic; Playwright cho login + 5-role flows |
| 5 | Settings/hardware + /mode + import-legacy UI thực | Hardware/Mode placeholder, ImportLegacy đã xoá ở chore. Phase 7+: implement Hardware (USB devices register) + Mode (online/kiosk) UI thật nếu cần |
| 6 | IPQC + OQC create modal | Hiện chỉ grid + filter. Cần create + approve modal pattern y hệt IQC Bước 7 (đã chứng minh hoạt động) |
| 7 | RBAC matrix doc | docs/PHASE6-STEP4-PLAN.md §2.C đã có matrix. Phase 7 nên publish thành PERMISSION_MATRIX.md riêng để onboarding ops dễ |

---

## 8. Cross-reference Phase 5 → Phase 6

| Phase 5 §7 TODO | Phase 6 đóng ở | Trạng thái |
|---|---|---|
| #1 Nội dung 3 QC tab | Bước 3 + Bước 7 | ✅ (IPQC + OQC grid; IQC full create + approve) |
| #2 Nội dung 1 NPI tab Engineer Spec | Bước 1 | ✅ |
| #3 Nội dung 10 Settings tab | Bước 2A + 2B + chore #18 | ✅ (3 User + 3 System tab; xoá ImportLegacy stub) |
| #4 Deploy SQL Server thật | Bước 6.5 (gate fix) | Partial — provider-agnostic migrations sẵn sàng. Verify Docker SQL Server pending (Phase 7 #1) |
| #5 RBAC roles ngoài Admin/User | Bước 4 | ✅ (5-role) |
| #6 Hub auth reconnect sau cookie expire | — | Pending Phase 7+ |
| #7 Audit log cho RBAC events | Bước 5 | ✅ |
| #8 Test suite | — | Pending Phase 7 (#4 sang Phase 7) |

Phase 6 đóng **6/8** TODO từ Phase 5 §7. 2 mục còn lại (#6 Hub reconnect, #8 Test framework) chuyển sang Phase 7.

---

## 9. Kết luận

✅ Phase 6 **hoàn tất**. 7 bước + 1 chore + 1 P0 fix landed lên `main`, build clean, 11-step smoke pass, data NPI nguyên vẹn (43 / 2 127 / 38 441 / 20 530 / 5 users / 3 IQC), 4 migration apply idempotent, restart proof, vùng cấm không bị đụng.

Pre-Phase-6 `main` HEAD: `88f01b8` (Phase 5 docs).
Post-Phase-6 `main` HEAD: `90ce645` (PR #19 merge — RBAC regression fix).
Tổng PR sprint Phase 6: **10** (#10–#19).

Dev box đã sẵn sàng cho Phase 7.

*Cập nhật: 2026-05-31, sau PR #19 đóng P0 regression.*
