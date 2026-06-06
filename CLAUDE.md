# CCL-MES — Agent Playbook

> **Manufacturing Execution System** cho CCL Design Vietnam. .NET 10 +
> Blazor Server + EF Core. Tài liệu này dành cho AI agent (Claude, Copilot…)
> và developer mới — encode rule + lesson hard-won qua các Phase 1→6.

## Pre-flight — bắt buộc đọc trước khi code/debug

Mọi session — agent hay người — MUST load 3 file dưới đây vào đầu để
không tái phạm lesson cũ:

1. [`CCL-MES-Hybrid/docs/LESSONS-LEARNED.md`](./CCL-MES-Hybrid/docs/LESSONS-LEARNED.md)
   — canonical index mọi bug class dự án đã trả tiền cho. 17 lesson card,
   format chuẩn `Triệu chứng | Root cause | Fix | Cơ chế chặn tái phát`.
   Mỗi lesson đính kèm test/script/rule cụ thể fail CI khi invariant bị vi phạm.
2. [`CCL-MES-Hybrid/docs/SKILLS.md`](./CCL-MES-Hybrid/docs/SKILLS.md)
   — playbook quy trình coding + debug đã chứng minh hiệu quả: RCA proven
   không "most likely", reproduce trên DB copy, verify-script per PR +
   paste output thật, checkpoint script self-managed, Catalyst 6-probe
   rhythm, Henry-action 1 lệnh full chain, STOP-gate discipline, stash
   discipline, **Design Rules responsive bắt buộc cho mọi Razor PR**.
3. [`CCL-MES-Hybrid/docs/STACKED-PR-CHECKLIST.md`](./CCL-MES-Hybrid/docs/STACKED-PR-CHECKLIST.md)
   — 7 rule cho stacked-PR merge, gate scripts, operator scripts. R1
   `--base` explicit, R2 không `--delete-branch` mid-stack, R3 cascade-close
   recovery, R4 comment-strip gate, R5 migration step trong Henry-action,
   R6 verify-script self-prep DB, R7 `[ctx] DB=` + self-managed lifecycle +
   wire-mirror.

**Mọi lesson mới phải append vào `LESSONS-LEARNED.md` + có cơ chế chặn
(test/rule), không để dạng prose rời.** PR review reject nếu thêm lesson
mà cột `Cơ chế chặn tái phát` để trống. Prose không ship — markdown không
fail CI.

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
- `data/blobs/drawings/<revisionId>/<drawingId>/v<n>_<sha8>.<ext>` —
  Phase 8 PR-D-5a FilesystemBlobStore artefacts. Caps:
  `MES_BLOB_MAX_BYTES` (default 10 MiB), `MES_BLOB_ALLOWED_EXTENSIONS`
  (CSV, default `pdf,png,jpg,jpeg,svg,gif,webp,dwg,dxf,ai`). 6 security
  guards documented in `docs/LESSONS_LEARNED.md`; harness at
  `scripts/VerifyBlobStore` (run via `dotnet run --project ...`).
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

### P10.7 — Work Order State Contract (Hybrid)

- **P10.7a-1** (v0.10.7a-1, 2026-06-05): WO State Contract foundation — 4-PR stack #99→#102.
  - 7a-1.1 Domain (MesPhase 12-state + RowVersion + AuditAction +34 + StateMachine extension)
  - 7a-1.2 Idempotency infrastructure (ledger + middleware)
  - 7a-1.3 retrofit `/work-orders/{id}/advance` with If-Match + Idempotency-Key + ETag
  - 7a-1.4 test belt: 144-cell transition matrix + N=50 soak + bUnit Razor render + CI grep audit + WO-stuck recovery runbook
  - Merge log + 5 safety backup branches retained until v0.10.7a-2.
  - SHA `9a515c00` (tag) + docs `CCL-MES-Hybrid/docs/p10.7a-1-screens/merge-log-20260605T135425Z.md`.
- **PR #103** Rule 6 + verify self-prep (2026-06-05, SHA `8e710dd`): STACKED-PR-CHECKLIST gains Rule 6; verify-p10.7a-{1..4}.sh now Down test DB copy to PREVIOUS_MIGRATION baseline before pre-migration probe so re-runs work on any dev DB state.
- **P10.7a-2.1** (2026-06-05, SHA `8e4afa36`): recovery seeds + sys account protection. `ReasonCodeKind.Recovery` + `UserRole.Sys` (NOT in whitelist) + `DbSeeder.SeedRecoveryDataAsync` (6 REC-* codes + `sys-recovery` user `IsActive=false`). AccountControl Update/ResetPassword guard `Role=Sys` → 403 `accounts.sys_account_protected`. Boot probe seed gated by `!IsEnvironment("Test")` to avoid N=50 advance soak contention.
- **P10.7a-2.2** (2026-06-05, SHA `d898b49f`): admin `/force-phase` endpoint + checkpoint script. `WorkOrderStateMachine.IsForceablePhase` predicate (11 of 144 cells per §3.1 recovery-only). `POST /api/v2/admin/work-orders/{id}/force-phase` AdminOnly + If-Match (428) + Idempotency-Key (400) + 409 stale + 422 body. `scripts/checkpoint-7a-2.sh` operator-runnable, self-managed API lifecycle. Contract doc §8.1 amendment lands Q1 reconciliation table (admin/sys vs sys-attribution).
- **P10.7a-2.2 hotfix** (2026-06-06, SHA `643861a8`): checkpoint URL `/api/v2/admin/audit/log` → `/api/v2/audit/log` (404 silent-fail); filter params `targetType/targetId` → `action=SYS_RECOVERY`; self-managed API lifecycle; `[ctx] DB=<abs-path> + DB sha8` mandatory header. New wire-level audit visibility xUnit fixture so test belt mirrors operator script. STACKED-PR-CHECKLIST Rule 7 (3 sub-rules) lands. LESSONS-EF-SQLITE-P10.7a-1.md sections 4 + 5 document wire-path drift + DB/server pinning.
- **P10.7a-2.3** (2026-06-06): test belt + verify final (25/25 probes) + 409 overload fix (`unforceable_transition` → 422; 409 reserved for stale If-Match only) + concurrency soak N=10 + checkpoint `--keep-alive` flag (Rule 7.2 amendment). Sentinel xUnit `Only_stale_ifmatch_returns_409_unforceable_returns_422` locks the 409/422 split.

#### P10.7b — PREPRESS row checks (4-PR stack)

- **P10.7b-1** (2026-06-06, SHA `8a201d6`, PR #107): Domain entities + migration + BOM snapshot service. `WoMaterial` (BomLineIdx + MaterialCode + QtyRequired + Status) + `WoPlateCheck` (1:1) + `WoCutterCheck` (1:1) entities. `PrepressCheckStatus` enum {Pending, Ok, Ng}. Pure helper `MaterialsReadinessRollup.Compute(materials, plate, cutter) → (HasSnapshot, AllOk)` — additive cached pattern leaves legacy `MaterialsReady` bool parity intact (5 LegacyParity tests lock). Migration `20260606023809_AddPrepressRowChecks` adds 3 tables + composite unique index `(WorkOrderId, BomLineIdx)` + idempotent backfill INSERT for existing PREPRESS WOs. `PrepressBomSnapshotService.MaterializeAsync(woId)` is idempotent; `WorkOrderService.CreateAsync` calls it post-WO persist. Housekeeping: `purge-test-audit.sh` (dry-run default, `--commit` gate) + `P10.7-BACKLOG.md` (Admin Recovery UI / backward force paths / NG co-sign deferred). 27 new tests (15 unit + 7 integration + 5 legacy parity).
- **P10.7b-2** (2026-06-06, SHA `ae248fc`, PR #108): API endpoints + Catalyst checkpoint script. `PrepressController` `[Authorize]` at `/api/v2/work-orders` with 4 routes: `GET {id}/prepress` (lazy materialise + view), `PUT {id}/materials/{bomLineIdx}`, `PUT {id}/plate-check`, `PUT {id}/cutter-check`. All 3 PUTs follow the 7a-1.3 contract: If-Match (428) + Idempotency-Key (400) + 409 stale + 422 invalid_phase/status/reason/note. Critical condition #1 (rollup race): single `SaveChanges` atomic pattern with tracked-query rollup recompute reads the just-mutated child row + plate + cutter; SQLite write-lock + EF `[Timestamp]` serialise concurrent operators. `AuditAction.WoPrepressMaterialSet` + `WoPrepressPlateSet` + `WoPrepressCutterSet` constants. 17 new fixtures including `Concurrent_prepress_row_updates_N_equals_10_yield_consistent_rollup` (Trait=Soak — exactly 1 OK + 9 wo.state_conflict). `checkpoint-7b-2.sh` self-managed API + BOM seed (5 test rows tagged `CreatedBy='checkpoint-7b-2'` for purge). `scripts/audit-state-machine-emits.sh` scope expanded to scan `PrepressController.cs`. ETag bug fix: WO `UpdatedAt + UpdatedBy` always touched on every PUT (was only on rollup change) so SQLite UPDATE trigger fires → fresh RowVersion → bumped ETag.
- **P10.7b-3** (2026-06-06, SHA `9e994a3`, PR #109 — feat): Razor + Catalyst UI. 4 components (`PrepressDashboard` parent + `WoMaterialsList` + `WoPlateCheck` + `WoCutterCheck` children). Dashboard owns `PrepressView` state; children are prop-driven + emit `EventCallback` intents. Concurrency: every PUT refetches view on `Ok=true`; on `Ok=false + ErrorCode=wo.state_conflict` carries fresh ETag → refetch + VN banner. 422 `wo.invalid_phase` collapses to single banner. Advance button gated on server-computed `MaterialsReady`; bubbles to `WorkOrders.razor → AdvanceOrchestrator` via `OnAdvanceRequested`. Rule 4 clean (0 `<InputText>`). `PrepressErrorLocaliser` covers 8 server codes + 4 in-band codes (15 locked tests). 13 bUnit fixtures including state-conflict reload + invalid-phase collapse + audit wire-mirror (R7.3 trail per fixture in comments). NPI audit logged Section 4 of `P10.7-BACKLOG.md` (gap = 1 tab Engineer Spec only; Scenario B XLSX wire deferred to Phase 7 Hạng mục 6 post-v0.10.7b).
- **P10.7b-3 fix** (2026-06-06, SHA `747cc95`, PR #109 — NG-path picker): closes Lesson L17 (LESSONS-LEARNED.md). Henry's Catalyst NG-path test failed `Mã lỗi NG không có trong danh mục Scrap` — root cause: (a) `SeedReasonCodesAsync` global `AnyAsync()` short-circuit skipped Pause/Scrap after Recovery seeded; (b) Hybrid Api boot only called `SeedRecoveryDataAsync`; (c) free-text NG reason input let operator bypass catalog. Fix: per-kind idempotency in `DbSeeder` + made `public`; Hybrid `Program.cs` calls both seeds + emits `[seed] reason_codes pause=N scrap=M recovery=K` probe; new `GET /api/v2/reason-codes?kind=Scrap` (`ReasonCodesController`, any-auth, 422 invalid_kind); `<select>` picker in 3 children populated from new `ScrapReasons` param; "Đánh NG" arm button disabled when picker source empty (+ tooltip); "Lưu NG" confirm disabled until valid catalog code chosen. 4 PREPRESS-specific codes added: SC-MAT-DAMAGE / SC-MAT-LOT / SC-PLATE-WORN / SC-CUTTER-WORN. **S9 Design Rules** CSS appended: page max-width `min(1600px, vw-32px)` center + container queries + `table-layout: fixed` + `overflow-x: auto` wrap + sticky head. 12 new tests (8 ReasonCodes wire-mirror incl. `L17_regression_Recovery_present_does_not_block_Scrap_listing` + 4 bUnit picker fixtures). `reset-prepress-for-wo.sh` operator script (R7.1 `[ctx] DB=` + `--commit` gate). 1,610 total tests pass.
- **P10.7b-4** (2026-06-06, PR #N — test belt closeout): `verify-p10.7b.sh` (≥27 probes, Rule 6 self-prep) covers build + 4 suites + migration round-trip + 22 wire probes (auth + Scrap picker + GET/PUT each surface + 428/400/409/422/200 + audit wire mirrors per AuditAction). `checkpoint-7b-final.sh` exercises full PREPRESS operator path (OK-all → rollup ready → advance enabled + NG-with-picker → rollup not-ready → advance disabled) with `--keep-alive` for Catalyst visual checks. `purge-test-audit.sh` extended to remove WO_PREPRESS_* test audit rows (Detail LIKE `%checkpoint-7b%` / `%verify-p10.7b%` / `%LOT-VERIFY%` / `%LOT-FINAL%` etc.) + BOM seed rows (`CreatedBy IN ('checkpoint-7b-2','verify-p10.7b','checkpoint-7b-final')`). Soak N=10 verified passing. 7c scope proposal drafted at `CCL-MES-Hybrid/docs/p10.7c-scope-proposal.md` for Henry approval BEFORE closing 7b stack + tagging v0.10.7b.

## 11. References

- [README.md](README.md) — user-facing quick start
- [docs/LESSONS_LEARNED.md](docs/LESSONS_LEARNED.md) — bài học chi tiết
- [docs/HOW-TO-UPGRADE-TO-SQLSERVER.md](docs/HOW-TO-UPGRADE-TO-SQLSERVER.md) — SQL Server cổng nâng cấp
- [docs/PHASE6-STEP*-PLAN.md](docs/) — survey doc archived per Bước
