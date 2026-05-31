# CCL-MES — Agent Playbook

> **Manufacturing Execution System** cho CCL Design Vietnam. .NET 10 +
> Blazor Server + EF Core. Tài liệu này dành cho AI agent (Claude, Copilot…)
> và developer mới — encode rule + lesson hard-won qua các Phase 1→6.

## 0. Quick start

- **Boot server**: `bash START_SERVER.command` (macOS) hoặc
  `START_SERVER.bat` (Windows) → port 5050, data dir `<repo-root>/data/`.
- **Dev launch**: `dotnet run --project src/CCL.MES.Web` → port 5080.
- **Demo accounts** (idempotent seed): admin / supervisor / engineer / qc
  / operator (mỗi tài khoản pwd = username).

## 1. Vùng cấm — TUYỆT ĐỐI KHÔNG ĐỤNG

Trong workspace có 4 thư mục anh em không thuộc CCL-MES — **chỉ ĐỌC,
không sửa, không chạy**:

- `Ops Control v1.2/` — sibling project, dùng learn pattern (deploy,
  CLAUDE.md style, DATA_DIR resolution).
- `CMES/` — cũ, không liên quan.
- `Old ver ( DO NOT USE)/` — legacy.
- `SpecHub/` — sibling project khác.

Khi commit, verify: `git diff --name-only | grep -E "Ops Control v1\.2|^CMES/|Old ver|SpecHub"` → empty.

## 2. Deployment topology

| Surface | URL | Source | After-edit |
|---|---|---|---|
| Standalone server | `:5050` | `data/ccl_mes.db` + Blazor Server | Cmd+C + bash START_SERVER.command |
| Dev launch | `:5080` | Same DB, hot-reload via dotnet watch | `dotnet run` rebuild |

**Data folder** (Phase 6 Bước 6.5 — Ops Control v1.2 pattern):
- `data/ccl_mes.db` — live SQLite (override `MES_DB_PATH=/abs/path` env)
- `data/Backup/SQLite/` — snapshot target (`Settings → Backup/Restore →
  Create snapshot`)
- Base dir override: `MES_DATA_DIR=/abs/path`

## 3. Database provider switch

```
appsettings.json:                  "Database": { "Provider": "Sqlite" }    # default
appsettings.SqlServer.json:        "Database": { "Provider": "SqlServer" } # gate, see doc
appsettings.Production.json:       operator-managed, .gitignored
```

**SQLite mode** (production hiện tại): `Program.cs` Bước 6.5 block override
connection string với absolute path (R3 guard: chỉ áp khi
`Provider=="Sqlite"` — SQL Server path không bị đụng).

**SQL Server mode**: chưa active production. Cổng "thật sự dùng được"
sau Bước 6.5 affinity fix. Khi nâng cấp xem
[`docs/HOW-TO-UPGRADE-TO-SQLSERVER.md`](docs/HOW-TO-UPGRADE-TO-SQLSERVER.md).

## 4. EF Core safety rules (HARD)

### 4.1 ⚠ TRÁNH `dotnet ef migrations remove`

Tool **tự động connect tới live DB** và áp dụng `Down()` của migration
cuối để revert schema THẬT. Không chỉ xoá file `.cs` local.

**Sự cố Bước 6.5 (2026-05-31)**: `ef migrations remove` đã revert
`AddAuditLog` trên SQLite live → DROP TABLE AuditLogs + xoá 1 row
`__EFMigrationsHistory`. Phải restore từ Phase A backup byte-identical.

### 4.2 ⚠ TRÁNH `dotnet ef migrations add` trỏ live DB

Mặc định `add` đọc connection string từ `appsettings.json` → trỏ live.
Tool inspect schema live → metadata vào Designer.cs có thể conflict.

### 4.3 Pattern an toàn — luôn dùng

```bash
# Backup snapshot model state trước
cp src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs \
   /tmp/snapshot-pre-<name>.cs

# Generate trên ISOLATED /tmp DB
rm -f /tmp/<name>-design.db
MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=/tmp/<name>-design.db" \
  dotnet ef migrations add <Name> \
  -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web -o Migrations --no-build

# Verify content
cat src/CCL.MES.Infrastructure/Migrations/*<Name>.cs

# Apply lên isolated DB để verify .schema
MES_PROVIDER=Sqlite MES_CONNSTR="Data Source=/tmp/<name>-design.db" \
  dotnet ef database update \
  -p src/CCL.MES.Infrastructure -s src/CCL.MES.Web --no-build
sqlite3 /tmp/<name>-design.db ".schema <NewTable>"

# UNDO bằng manual rm + git checkout snapshot (KHÔNG dùng `ef migrations remove`)
rm -f src/CCL.MES.Infrastructure/Migrations/*<Name>*
cp /tmp/snapshot-pre-<name>.cs \
   src/CCL.MES.Infrastructure/Migrations/MesDbContextModelSnapshot.cs
```

### 4.4 Phase A → B → C protocol cho mọi thay đổi schema

- **Phase A**: backup tường minh `cp data/ccl_mes.db /tmp/ccl_mes.db.before-<step>.<ts>`
  + `shasum -a 256` ghi nhận baseline + rowcount baseline.
- **Phase B**: test trên isolated DB (`/tmp/...db`) — verify, không
  chạm live.
- **Phase C**: áp dụng thật + verify SHA + rowcount + migration history.

### 4.5 Type-affinity strip (3.2.B từ Bước 6.5)

Mọi migration MỚI phải strip `type: "TEXT|INTEGER|REAL"` strings + 
`.HasColumnType("...")` fluent calls (giữ cổng SQL Server provider-
agnostic). Python helper:

```python
# Tham khảo script trong commit Bước 6.5 SHA `13e0e58` để re-apply
# trên migration mới được generate.
```

## 5. Authorization model (Phase 6 Bước 4)

5-role whitelist: **Admin / Supervisor / Engineer / QC / Operator**.

| Layer | Field | Purpose |
|---|---|---|
| Role | `User.Role` (UserRole.All) | Coarse policy gate |
| Policy | `AdminOnly` / `NpiRead` / `NpiSpecRead` / `QcRead` | Page-level `[Authorize(Policy=...)]` |
| Inline | `<AuthorizeView Roles="Admin,Supervisor,QC">` | Button-level + server-side check |

**Recovery khi mất sys admin** (Sprint Phase 6 Bước 4):
- Console: `cd scripts/RecoverAdmin && dotnet run` → gõ `CONFIRM-RECOVER`
- Tạo lại admin với pwd random, `must_change_password=true`

## 6. Audit log (Phase 6 Bước 5)

Append-only `AuditLogs` table. Const codes alphabetical trong
[src/CCL.MES.Domain/Audit/AuditAction.cs](src/CCL.MES.Domain/Audit/AuditAction.cs).

Mỗi mutation service emit qua `IAuditWriter.EmitAsync(action, actor,
role, targetType, targetId, detail)`. Detail JSON **tuyệt đối không
chứa password/hash/cookie/token**.

UI viewer: `Settings → System Log` (`/settings/syslog`, AdminOnly policy).

## 7. Restore + Backup

- **Snapshot**: `Settings → Backup/Restore → Create snapshot` (SQLite online
  backup API, safe khi serving).
- **Restore**: console-only via `scripts/BackupRestore/` — yêu cầu gõ
  `CONFIRM-RESTORE`, in row counts trước prompt, auto pre-restore backup,
  emit `BACKUP_RESTORE` audit row Source=Console.
- SQL Server mode: UI hiển thị guidance card SSMS, không có nút snapshot.

## 8. i18n

EN mặc định (`SharedResource.resx`), VI satellite (`SharedResource.vi.resx`).
Switch ngôn ngữ: cờ trên topbar / login → cookie `.AspNetCore.Culture`
sống 1 năm. Resource lookup qua `IStringLocalizer<SharedResource>`.

Khi thêm key mới: BẮT BUỘC EN + VI parity, đặt theo namespace
(`qcqa.iqc.*`, `settings.data.*`, `nav.tab.*`).

## 9. Lessons learned

Chi tiết tại [`docs/LESSONS_LEARNED.md`](docs/LESSONS_LEARNED.md):
- §1-§5: Phase 1-5 kiến trúc + tooling
- §6 đợt 2: EF migrations provider-specific + SignalR HubConnection
- §7 đợt 3: EF Core safety (rule §4.1-§4.5 trên đây), Bước 6.5/7
- §8 đợt 4: `git merge -X ours` blunt cho overlapping additive edits (PR #19 regression)

### 9.1 Sprint close-out checklist (Phase 6 added)

Khi sprint có nhiều stacked PR cần merge tuần tự:
1. **Smoke verify trên main sau từng PR merge** (KHÔNG chờ tới cuối). Mỗi PR merged → quick curl smoke với admin login + 5 representative routes phủ 5 policy.
2. **Tránh `git merge -X ours` blanket** khi có overlapping additive edits trong cùng block (DI setup, policy registration, route mapping). Resolve conflict thủ công.
3. **Final smoke matrix** có cả admin (200 hết) + operator (AccessDenied panel rendered) để verify defense-in-depth còn hoạt động.

## 10. Phase history (SHA-discipline)

- **Phase 5**: RBAC + SignalR hub auth + error code i18n + EF Migrations + EnsureCreated→Migrate
- **Phase 6 Bước 1**: NPI Engineer Spec UI (PR #10 SHA `ed91fc8`)
- **Phase 6 Bước 2A**: Settings User group (PR #11 SHA `4fc36bf`)
- **Phase 6 Bước 2B**: Settings System group (PR #12 SHA `70d1f71`)
- **Phase 6 Bước 3**: 2 QC tab IPQC/OQC + IQC stub (PR #13 SHA `bfaa6d6`)
- **Phase 6 Bước 4**: RBAC 5-role + Account mutation + recover-admin (PR #14 SHA `84016fe`)
- **Phase 6 Bước 5**: Audit Log + Syslog tab + BackupRestore (PR #15 SHA `1991ec6`)
- **Phase 6 Bước 6.5**: Ops Control v1.2-style SQLite deploy + SQL Server gate fix (PR #16 SHA `2d4d532`)
- **Phase 6 Bước 7**: IQC entity + tab (PR #17 SHA `23ccae2`, đóng stub Bước 3)
- **Phase 6 chore**: Remove Import data v1.0 sub-tab (PR #18 SHA `4fc15b1`)
- **Phase 6 P0 fix**: Restore Bước 4 RBAC policies (PR #19 SHA `90ce645`, `-X ours` merge regression hotfix)

Phase 6 close-out 2026-05-31. Final report: [`docs/PHASE6-REPORT-2026-05-31.md`](docs/PHASE6-REPORT-2026-05-31.md).

## 11. References

- [README.md](README.md) — user-facing quick start
- [docs/LESSONS_LEARNED.md](docs/LESSONS_LEARNED.md) — bài học chi tiết
- [docs/HOW-TO-UPGRADE-TO-SQLSERVER.md](docs/HOW-TO-UPGRADE-TO-SQLSERVER.md) — SQL Server cổng nâng cấp
- [docs/PHASE6-STEP*-PLAN.md](docs/) — survey doc archived per Bước
