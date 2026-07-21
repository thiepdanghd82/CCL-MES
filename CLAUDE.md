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

**UI showcard rule (L34):** mọi SHOWCARD / detail-dialog mới trong
`CCL-MES-Hybrid/src/CCL.MES.Hybrid.Razor` PHẢI bọc component dùng chung
`Shared/FloatingWindow.razor` (drag / resize 8 hướng / traffic-light /
persist rect) — KHÔNG tự vẽ chrome. Surface transactional (form / confirm)
giữ `<Modal>` căn giữa (float là opt-in `Float="true"`). Enforce:
`CCL-MES-Hybrid/scripts/gate-floating-showcard.sh` + skill
`.claude/skills/cmes-floating-showcard/SKILL.md`.

**UI row-action rule (L35):** hành động trên dòng grid (Copy/Edit/Delete/…)
dùng `Shared/RowContextMenu.razor` (chuột phải + long-press + nút ⋯ kebab,
chung 1 state) — KHÔNG thêm cột "Actions" nút inline. RBAC-by-omission (chỉ
build item được phép; server vẫn 403). Enforce:
`CCL-MES-Hybrid/scripts/gate-row-actions.sh` + skill
`.claude/skills/cmes-row-context-menu/SKILL.md`.

## 0. Quick start

- **Boot server**: `bash START_SERVER.command` (macOS) hoặc
  `START_SERVER.bat` (Windows) → port 5050, data dir `<repo-root>/data/`.
- **Dev launch**: `dotnet run --project src/CCL.MES.Web` → port 5080.
- **Demo accounts** (idempotent seed): admin / supervisor / engineer / qc
  / operator (mỗi tài khoản pwd = username).

## 1. Vùng cấm — TUYỆT ĐỐI KHÔNG ĐỤNG

CCL-MES là project duy nhất còn active trong workspace. Các project anh
em cũ (CMES — bản rewrite TS đã bỏ; SpecHub — prototype) đã được **archive
2026-06-23** thành tarball tại `../_archive/` (xem `../_archive/MANIFEST.md`
+ `checksums.sha256`), không còn nằm dạng source cây thư mục nữa. Nếu cần
tham chiếu: `tar xzf ../_archive/<name>-2026-06.tar.gz`. **Không giải nén
vào trong cây CCL-MES.**

Khi commit, verify không lẫn file ngoài phạm vi:
`git diff --name-only | grep -E "^CMES/|SpecHub|_archive"` → empty.

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

#### P10.7c — SETTING + RUNNING + PAUSED surface (4-PR stack)

- **P10.7c-1** (2026-06-06, SHA `a80ac0a` (Domain) + `8a4d9f9` (controller belt), PR #113): Domain entities + migration + state machine extension. `WoRunSession` (no RowVersion — parent WO gates concurrency), `WoPauseEvent` (`ReasonCode` validated against `ReasonCodeKind.Pause`), `WoQtyEntry` (append-only ledger: signed `QtyDoneDelta` + `QtyNgDelta`; `LinkedEntryId` + `CorrectionReason` for Q5 corrections). Migration `20260606093621_AddRunningSurfaceDomain` adds 3 tables + 5 WO columns (`SettingStartAt` / `SettingEndAt` / `SettingDurationSec` / `QtyDoneCached` / `QtyNgCached`) + 8 indices. `WoSettingService.MarkSettingStart/Done` (idempotent static helpers) + `WoRunSessionService.Start/Close/Finish` + `WoPauseService.Pause/Resume/ClosePause` (Q6 helper) + `WoQtyService.Add/Correct`. Services never call SaveChanges — controllers (7c-2) wrap atomic write. §3.1 contract grid amended: `PAUSED → FQC_PENDING` cell (Q6) changed `blocked → requires-condition` (active pause's `EndedAt` stamped pre-transition); reservation list adds `WO_RUN_QTY_ADD` + `WO_RUN_QTY_CORRECT`. 18 unit + integration + LegacyParity tests (matrix +1 cell, theory unchanged).
- **P10.7c-2** (2026-06-06, SHA `8a4d9f9` + `fb088dc` (tests) + `7da3a57` (checkpoint) + `793261b` (SQL shim fix), PR #114): RunningSurfaceController — 7 endpoints (`/setting/done` + `/run/start` + `/run/qty` + `/run/qty/correct` + `/run/pause` + `/run/resume` + `/run/finish`). All 7 follow the atomic pattern: Prelude (If-Match + Idem-Key + WO fetch + RowVersion check) → body validation → phase guard → domain service call (no SaveChanges) → `wo.UpdatedAt + UpdatedBy` touch → SINGLE SaveChanges → audit emit → Ok(200) + bumped ETag + post-write state. Single-SaveChanges + WO-row-touch closes Critical condition #1 (rollup race) the same way 7b-2 did. 22 integration + soak (`Concurrent_run_qty_add_N_equals_10_exactly_one_winner` Trait=Soak) + Rule 7.3 wire-mirror (`Audit_visibility_via_wire_audit_log_endpoint`). `checkpoint-7c-2.sh` exercises full luồng via SQL shim for IPQC_WAIT → IPQC_APPROVED (IPQC wire is 7d scope). **SKILLS.md S12** lands ("checkpoint silent = no verify; per-step + SUMMARY + non-zero-on-fail mandatory") after Henry's hardware-test on PR #114 caught: (a) force-phase rejected IPQC_WAIT → IPQC_APPROVED because §3.1 classifies it RequiresSignoff not RecoveryOnly → fix via SQL shim; (b) `record FAIL && exit 1` mid-script silently skipped the SUMMARY block → fix via per-step `[N/total]` labels + `final_summary` in EXIT trap.
- **P10.7c-3** (2026-06-06, PR #115, 5 commits including L19 finalization):
  - **Initial UI ship (SHAs `3d93277` + `7ce231b` + `07f97b9`)**: `SettingDashboard.razor` (live H:M:S timer + 6-item checklist + entry-stamp + `/setting/done`) + `RunningDashboard.razor` (phase fan-out IPQC_APPROVED/RUNNING/PAUSED) + 3 modals (`WoPauseModal` reusing 7b-3 ReasonCodeOption shape kind=Pause + `WoQtyCorrectModal` for Q5 + `WoFinishConfirm`). New `GET /api/v2/work-orders/{id}/running-surface` returns single-round-trip view (WO core + setting timer + active session + active pause + last 20 qty entries). New `POST /api/v2/work-orders/{id}/setting/enter` idempotently stamps `SettingStartAt` (closes 7c-2 gap: `/advance` lands SETTING without starting timer). 9 client wrappers + VN `RunningSurfaceErrorLocaliser`. S9 responsive via container queries on `.rs-dashboard` (wide ≥1400 / narrow ≤900 / mobile ≤600). 24 bUnit + 5 server fixtures.
  - **L19 bugfix #1 (SHA `98ca5de`)**: Henry's WO-26-3685 RCA — dispatch keyed on canonical `MesPhase`, not legacy `CurrentStep`. After `/setting/done` WO is in `MesPhase=IPQC_WAIT` but `CurrentStep` stays `"OpSetting"` — old dispatch routed to SettingDashboard which then refused to render. New `MesPhase` field added to `WorkOrderSummary` DTO + server emission. `IsPrepressPhase` / `IsSettingPhase` / `IsRunningSurfacePhase` / `IsDashboardOwnedPhase` predicates check `MesPhase` first; legacy `CurrentStep` fallback only when MesPhase empty. Legacy Advance CTA + stale `_advanceResult` chrome hidden when WO is dashboard-owned. RunningDashboard adds IPQC_WAIT branch (read-only info, NOT error).
  - **L19 finalization (SHA `f39ea13`)**: Henry's WO-26-3686 follow-up — WO card chip + "Trạng thái MES" row also render canonical `MesPhase` (was rendering legacy `CurrentStep` even after dispatch fix). New client-side `MesPhaseCssClass` mapper drives 13 `wo-phase-*` palette classes. `DeferredPhaseInfo` map in RunningDashboard generalises the IPQC_WAIT branch — 6 entries (IPQC_WAIT / QA_PENDING / FQC_PENDING / OQC_PENDING / DONE / CANCELLED) each render a consistent read-only placeholder card with title + body + hint. 7d/7e plug in real dashboards by REMOVING the relevant map entry. Adds 6 divergence fixtures (1 chip wire-mirror + 1 FQC_PENDING + 4 `[Theory]` deferred phases). Razor 24 → 59 fixtures across the L19 journey. **Lesson L19 codified in 7c-4.**
- **P10.7c-4** (2026-06-06, PR #N — test belt closeout): `verify-p10.7c.sh` full form (≥14 probes, Rule 6 self-prep, soak filter inversion as dedicated Step 2.5 with 2-attempt policy for the documented SQLite macOS interleaving flake). `checkpoint-7c-final.sh` (21 steps per S12; per-step `[N/21]` labels + SUMMARY always prints in EXIT trap; Block B 11-step full luồng SETTING→IPQC_APPROVED→RUNNING→PAUSE→RESUME→FINISH on WO1; Block C 3-step Q6 finish-from-PAUSED on WO2; Block D L19 deferred-phase walk through 5 phases on WO1; Block E Rule 7.3 audit wire-mirror for 7 RUNNING audit codes). `purge-test-audit.sh` extended for `WO_SETTING_*` + `WO_RUN_*` audit rows (Detail LIKE `%checkpoint-7c%` / `%verify-p10.7c%` / `%manual-l19-test%` / `%checkpoint-l19-walk%`) + `WoQtyEntries` / `WoPauseEvents` / `WoRunSessions` actor-tagged rows. LESSONS-LEARNED.md gains L19 entry with the 3-prong canonical-MesPhase migration + DeferredPhaseInfo map pattern + 6 bUnit divergence fixtures + server contract test + checkpoint walk + standing rule. 7d scope proposal drafted at `CCL-MES-Hybrid/docs/p10.7d-scope-proposal.md` (ENRICHED form mirroring 7c-2's scope-proposal pattern — Q1-Q6 + per-Q Recommended + SpecHub §3 IPQC citation + trade-off) for Henry approval BEFORE closing 7c stack + tagging v0.10.7c.

#### P10.7d — IPQC review + QA Approval (4-PR stack, v0.10.7d)

- **P10.7d-1** (2026-06-07, PR #117, SHA `8d94c9f`): Domain + DI options + migration. `WoIpqcCheck` entity (4 status slots Material/PrintA/PrintB/PrintC + 3-state judgment GoRun/StopLine/SpecialAccept + dual-sig QA fields) + `IpqcReadinessRollup.Compute` pure helper computing `IsReadyForJudgment` / `AllOk` / `AnyNg` + `IsJudgmentConsistent` invariant (GoRun rejected when AnyNg). State-machine §3 transitions: GoRun→IPQC_APPROVED · StopLine→PREPRESS · SpecialAccept→QA_PENDING · QA Approve→IPQC_APPROVED · QA Reject→PREPRESS. `IpqcDualSigOptions.RequireDistinctQaApprover` + `Program.cs` Q3 typo-safe whitelist parse (accepts only `0`/`false`/`off`/`no` as OFF; everything else stays default-ON — see Lesson L20) + boot-probe `[config] OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=on` log line. Migration `20260606150401_AddIpqcReviewSurface` adds 1 table + unique index + idempotent backfill INSERT for legacy IPQC_WAIT/QA_PENDING/IPQC_APPROVED/RUNNING/PAUSED/FQC_PENDING/OQC_PENDING WOs. 60 new tests (14 unit rollup + 12 dual-sig parse + 18 service slot/judgment/dual-sig + 6 LegacyParity + 4 integration + 6 misc). Domain 751→811.
- **P10.7d-2** (2026-06-07, PR #118, 3 commits — feat `ca40145` · 27-fixture test `8047884` · role policy lock + checkpoint self-seed `c67fd58` · L10 drift guard `7de963c` superseded by L21 PR; merged via `--rebase` as `fb2a9d6`): Wire. `IpqcReviewController` 7 endpoints (`GET {id}/ipqc` + 4 PUT slots + `POST {id}/ipqc/judgment` + `POST {id}/qa/approve`). Atomic pattern mirrors 7c-2: Prelude (If-Match + Idem-Key + WO fetch) → body validation → phase guard → service call (no SaveChanges) → wo.UpdatedAt touch → SINGLE SaveChanges → audit emit → typed `IpqcSetResponse` with post-write rollup. Role policy locked per §5.5.0 amendment: IpqcSubmit = Admin|QC (4 PUT + judgment); QaApprove = Admin|QC|Supervisor (qa/approve only; Supervisor = SpecHub "QA Manager"). Q3 dual-sig server enforcement: when `RequireDistinctQaApprover=on` AND caller's username equals `IpqcSubmittedBy` (OrdinalIgnoreCase), emit 422 `qa.same_user_as_ipqc_submitter` + `WO_QA_APPROVE_DENIED` audit row. 30 new IpqcReviewController fixtures including [Theory] 4 slot PUTs, judgment happy/inconsistent/not-ready/SpecialAccept-with-reason, 5 QA approve including Q3 same-user 422 + distinct-user happy + audit wire-mirror R7.3, 3 role policy fixtures locking the §5.5.0 table (operator → 403 IpqcSubmit; operator → 403 QaApprove; supervisor → 200 QaApprove policy gate passes). `checkpoint-7d-2.sh` self-managed API + self-seeds 2 distinct QC users via `POST /api/v2/admin/users` (idempotent on HTTP 422 `accounts.username_in_use`) + S12 per-step + SUMMARY trap + L10 drift-guard helper `api_post_admin` / `api_assert_routed` that bails on HTTP 404/405 with `[L10 drift]` banner (closes Henry's "wrong-endpoint silent cascade" RCA on the initial seed attempt at `/admin/accounts`). `purge-test-audit.sh` adds `IPQC_QA_TEST_USERS` constant + `'checkpoint-7d-2'` actor tag. Api 328→358.
- **P10.7d-3** (2026-06-07, PR #119, 2 commits — feat `a137d8d` + L21 fix `7de963c`; merged via `--rebase`): UI + L21 auto-refresh. `IpqcDashboard.razor` (4-slot 2-col grid + OK/NG per slot + inline NG sub-form with reason picker + 1-500 char note + 3-button judgment row Go Run/Stop Line/Special Accept gated by `IsReadyForJudgment`/`AllOk`/`AnyNg` rollup + SpecialAccept reason input + optimistic-revert-via-reload on 409). `QaApprovalDashboard.razor` (read-only IPQC summary + Q3 dual-sig client guard — when `Session.CurrentUserInfo.Username == _view.IpqcSubmittedBy` (OrdinalIgnoreCase), Approve disabled + `Q3SameUserBanner` constant rendered + hint card; Reject NOT gated by Q3 since StopLine-semantics + Reject reason required). 6 new `ICclApiClient` methods + `IpqcReviewErrorLocaliser` (13 ApiError codes + 7 in-band + Q3 dual-sig word-for-word invariant between LocaliseApiError + LocaliseSetError). Dispatch: `IsIpqcWaitPhase` + `IsQaPendingPhase` helpers added; `IsRunningSurfacePhaseValue` narrowed (IPQC_WAIT + QA_PENDING dropped); `IsDashboardOwnedPhase` extended. `RunningDashboard.DeferredPhaseInfo` map trimmed: IPQC_WAIT + QA_PENDING removed (real dashboards own them now); FQC/OQC/DONE/CANCELLED retained. CSS `ipqc-*` + `qa-*` skin + container queries (≥1400 larger buttons + ≤900 stack full-width). **L21 fix (Henry hardware verify on PR #119, 2026-06-07)**: every transition-emitting dashboard exposes `[Parameter] EventCallback OnPhaseChanged`; central `WorkOrders.razor.HandleDashboardPhaseChangedAsync` re-fetches summary via `GetWorkOrderByNoAsync` after each phase-changing action so the dispatch re-evaluates + the new dashboard mounts WITHOUT the operator tapping "Tìm" again. Bubble fires on transition actions ONLY (judgment / Approve / Reject / setting-done / run-start / pause / resume / finish); skipped on slot PUT + tap qty + 409 + 422. Razor 59→85 (initial UI) → 99 (post-L21 fix). Client 549→575 (Localiser). LESSONS-LEARNED L21 codified.
- **P10.7d-4** (2026-06-07, PR #120 — test belt closeout): `verify-p10.7d.sh` matured from 7d-1 skeleton to closed-out form (16 probes, Rule 6 self-prep, L17 + L18 + L20 boot probe assertions, footer prints the 4-PR stack history + companion-script invocation). `checkpoint-7d-final.sh` (14 steps per S12; cycle 1 GoRun = all 4 slots Ok → IPQC_APPROVED; cycle 2 StopLine = 1 slot Ng → PREPRESS; cycle 3 SpecialAccept = print-b Ng + reason → QA_PENDING then Q3 path A same-user 422 + WO_QA_APPROVE_DENIED audit + Q3 path B distinct-user Approve → IPQC_APPROVED + QaApprovedBy stamped; audit wire-mirror 4/4; L21 auto-route wire assertion — `/work-orders/by-no` returns IPQC_APPROVED so the dashboard's L21 re-fetch would route to RunningDashboard; idempotency replay no-op). Refuses to run when `OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER=off` so an op-engineer can't certify a build whose dual-sig is silently disabled. `purge-test-audit.sh` extended for `WO_IPQC_*` + `WO_QA_*` audit rows (Action ∈ enum + ActorUsername IN seeded users) + `'checkpoint-7d-final'` actor tag joining `'checkpoint-7d-2'` from 7d-2. LESSONS-LEARNED.md gains L20 (Q3 dual-sig default-ON whitelist parse + boot probe; replicable kit for any future security flag) + L21 (auto re-fetch summary on phase change; OnPhaseChanged bubble + central HandleDashboardPhaseChangedAsync handler). DB-path-default fix in `Program.cs`: walks up from ContentRootPath looking for an EXISTING `<ancestor>/data/ccl_mes.db` BEFORE falling back to the innermost `.sln` directory — closes the Henry-reported footgun where `dotnet run` from `CCL-MES-Hybrid/src/CCL.MES.Api/` used to land at the EMPTY `CCL-MES-Hybrid/data/` because the `.sln` walk stopped at the inner sln. 7e scope proposal drafted at `CCL-MES-Hybrid/docs/p10.7e-scope-proposal.md` (Q1-Q8 ENRICHED form + per-Q Recommended + SpecHub citation + trade-off) for Henry approval BEFORE closing 7d stack + tagging v0.10.7d.

#### P10.7e — FQC + OQC + Reports / outgoing-quality surface (4-PR stack, v0.10.7e)

- **P10.7e-1** (2026-06-07, PR #121, SHA `fb0bde5` + `4d9e5c4`): Domain + migration + policy. `SHIPPED` terminal `MesPhase` enum added → state-machine transition grid expands 144→169 cells (12×12 → 13×13; `WorkOrderStateMachineFullMatrixTests` Theory enumerates `Enum.GetValues<MesPhase>()` at runtime so the diff lands as +25). 3 new tables `WoQcChecks` (per-WO per-kind check with 3-state `Judgment` Pending/Pass/Reject + dual signature slots InspectedBy/ReviewedBy/ApprovedBy) + `WoQcCheckItems` (per-item Ok/Ng/Pending + NgReasonCode + NgNote + PhotoBlobId) + `WoQcPhotos` (IBlobStore evidence metadata). `Product.QcProfileOverride` JSON column (Q4 — per-product threshold override resolved via 3-level chain: WO snapshot → Product override → QcProfileSeed default). `WoQcSigPolicyOptions` (Q5 — 3 independent default-ON flags `RequireDistinctReviewer` / `RequireDistinctApprover` / `RequireApproverDistinctFromInspector`, typo-safe parse per L20). `FqcReadinessRollup` + `OqcReadinessRollup` pure helpers. Migration `20260607101947_AddFqcOqcQualitySurface` adds the 3 tables + Products column + unique indices + idempotent backfill for legacy FQC_PENDING/OQC_PENDING WOs. 12 new audit codes mirror 7d naming (Q7). Domain 846→948.
- **P10.7e-2** (2026-06-07, PR #122, SHAs `08048b5` + `fc217a9` + `e4b1d05` + `d43734a`): Wire. `WoQcReviewController` data-driven over `{kind}` path param ("fqc" | "oqc"): `GET {id}/qc/{kind}` (lazy-materialise items from QcProfileSeed) + `PUT {id}/qc/{kind}/items/{itemKey}` + `POST {id}/qc/fqc/judgment` (FQC single-sig Inspector — Pass→OQC_PENDING `WO_FQC_JUDGMENT` / Reject→PREPRESS `WO_FQC_REJECT_TO_PREPRESS`) + OQC 3-sig chain `POST .../oqc/inspect` (`WO_OQC_INSPECT`) → `.../oqc/review` (`WO_OQC_REVIEW`; ≠ Inspector else 422 `oqc.same_user_as_inspector` + `WO_OQC_REVIEW_DENIED`) → `.../oqc/approve` (Approve→SHIPPED `WO_OQC_APPROVE` + `WO_SHIPPED` same SaveChanges; Reject→FQC_PENDING `WO_OQC_REJECT_TO_FQC_PENDING`; Q5 ❷ Approver=Reviewer 422 `oqc.same_user_as_reviewer` + ❸ Approver=Inspector 422 — both `WO_OQC_APPROVE_DENIED`). Atomic prelude pattern (If-Match 428 + Idem-Key 400 + single SaveChanges + bumped ETag) mirrors 7c-2/7d-2. Photo upload/list/content endpoints (Q6 — IBlobStore). `GET {id}/summary-report` (Q8 — live-recomputed JSON: totals + runtime + OEE + pause-pareto + 3-leg qc_summary; powers ShippedSummaryDashboard). 13 WoQcReviewController fixtures (Q5 4 paths + R7.3 wire-mirror). `checkpoint-7e-2.sh` self-seeds 3 distinct QC users (Inspector/Reviewer/Approver) + L22 build-sanity probe (Henry RCA — stale keep-alive binary returned silent 404 on the new route). Api 358→372.
- **P10.7e-3** (2026-06-08, PR #123, SHAs `26d88d0` + `a2862e9` + `ce65cb0` + `13dab5d`): UI + L21 + L23 fix. `FqcDashboard.razor` (12-item profile grid + per-item Ok/Ng + reason picker + judgment Pass/Reject gated on rollup readiness) + `OqcDashboard.razor` (28-item grid + 3-sig banner mirroring `Q3SameUserBanner` + client guards mirroring server Q5 enforcement) + `ShippedSummaryDashboard.razor` (read-only summary card from `/summary-report`) + `QcPhotoStrip` file-picker upload UI + `WoQcReviewErrorLocaliser` VN bank. L21 `OnPhaseChanged` auto-route: FQC Pass→OQC_PENDING, FQC Reject→PREPRESS, OQC Approve→SHIPPED, OQC Reject→FQC_PENDING all re-dispatch without an operator "Tìm" tap. `RunningDashboard.DeferredPhaseInfo` drops FQC_PENDING + OQC_PENDING (real dashboards own them now); `IsRunningSurfacePhaseValue` narrows. **L23 fix (Henry RCA on PR #123)**: checkpoint-7e-2's shortcut INSERT of a stub check with empty profile masked the operator-visible 0/0 gap; fixed by seeding default QC profiles (`[seed] qc_profiles fqc=12 oqc=28` boot probe) + driving the REAL materialisation path (GET /qc + PUT items) in the checkpoint. L22 stale-binary + L23 seed-trống lessons codified. Razor 99→114.
- **P10.7e-4** (2026-06-10, PR #124 — test belt closeout): `verify-p10.7e.sh` matured to closed-out form (footer prints the 4-PR stack history + companion-script invocation + purge cleanup). `checkpoint-7e-final.sh` (26 steps per S12; walks EVERY 7e transition on ONE WO via SQL phase-shims between cycles + the real materialisation path per L23: Cycle 1 FQC Reject→PREPRESS; Cycle 2 FQC Pass→OQC_PENDING; Cycle 3 OQC 3-sig + Q5 ❶❷❸ all 3 violation paths + OQC Reject→FQC_PENDING re-loop; Cycle 4 re-pass the loop all the way to SHIPPED via 3 distinct sigs; Cycle 5 Q8 `/summary-report` returns SHIPPED + totals + qcSummary; audit wire-mirror 9/9 outgoing-quality codes; L21 wire assertion `/by-no/{wo}/summary` = SHIPPED). Refuses to run when any of the 3 OQC 3-sig flags is OFF (Q5 violations can't be proven). L22 (kill stale :5100 + build-sanity probe before route exercise) + L23 (real-path checkpoint, never shortcut INSERT) guards inline. `purge-test-audit.sh` extended for `WO_FQC_*` / `WO_OQC_*` / `WO_SHIPPED` audit rows (Action ∈ enum + ActorUsername IN the 3 seeded OQC test users) + `WoQcChecks`/`WoQcCheckItems`/`WoQcPhotos` rows (signature columns ∈ test users; child-first dependency-order delete) + the 3 `oqc-test-*` users. 7f scope proposal drafted at `CCL-MES-Hybrid/docs/p10.7f-scope-proposal.md` (Report xlsx export per CCL-10-F6 form + per-product threshold admin UI + Catalyst camera capture + ERP/IFS push) for Henry approval BEFORE closing 7e stack + tagging v0.10.7e.

#### P10.8 + Prepress-scan + Plan C — bottom-up stack merge (2026-06-27)

Three stacked PRs merged into `main` bottom-up the same day, each via a **merge
commit** (no squash — preserves per-step history). Merge order enforced
PR #126 → #124 → #125 because Plan C + prepress build directly on the
IPQC/FQC/OQC surface that p10.8 consolidates (overlap on `IpqcDashboard.razor`,
`CclApiClient`, `Program.cs`, `NavMenu.razor`, `MesDbContextModelSnapshot.cs` —
the rebase-to-main dependency probe CONFLICTED, proving the stack can't detach).

- **P10.8 — Machine Dashboard + p10.7e/9/10 consolidation + SpecHub UI port**
  (PR #126, **merge `df4f593`**, 63 commits · 188 files · +21,647/−2,043). Bundles
  the never-tagged p10.7e outgoing-quality surface (WoQc photo + FQC/OQC/Shipped
  dashboards) + p10.8 Machine Dashboard (read-model + `GET /machines/dashboard`
  + area/status/search filters + per-machine detail drawer + Grid/List toggle) +
  p10.9 QMS (Inspection Queue + QC History) + p10.10 Home KPI
  (`GET /api/v2/home/summary`) + SpecHub UI port (top bar, sidebar, WO-detail
  7-step stepper, NPI CSV import + inline Spec editor Indigo/HP + Letterpress,
  Shop Order History) + EN i18n + nightly backup scheduler (3-2-1). 1 migration
  `20260613081233_AddWoMaterialScrapColumns` (WoMaterials.ScrapFactor/Percent).
- **Prepress scan-materials + Special-Accept** (PR #124, **merge `f2c83bb`**,
  14 commits). Client-side barcode→BOM matcher (`MaterialBarcodeMatcher`:
  segment-before-`/` → exact `MaterialCode` → strip `-<digits>` → leading 8-digit
  run; mã-số only, OCR-noise-safe) wired into `PrepressDashboard` scan loop
  reusing the shipped `ScanOnceAsync` one-shot + `GuardedPutAsync` (If-Match/Idem).
  No API contract change. +17 `MaterialBarcodeMatcherTests` + 13 PrepressDashboard
  scan fixtures + `OeeServiceComputeTests`. CI gains a `hybrid-test` job.
- **Plan C — data-driven QC engine** (PR #125, **merge `85d002e`**, ~12 commits).
  `CheckItemLibrary` master data (106 items / 5 lines: LABEL 34 · PRESS_CNC 27 ·
  SILK 25 · DIGITAL 15 · FINISHING 5) → `QcLineResolver` (data-driven
  `ProcessLineMap`, 57 entries, longest-prefix WorkCenterPrefix + ProcessCode +
  OpKeyword) → `IpqcLibraryMaterializer` (frozen `ItemsProfileSnapshotJson`) →
  `WoIpqcCheckItem` shadow table (keeps 4 legacy slots). `autoSyncStatus` derived
  flag (Materialized / SkippedUnmapped / SkippedNoLibrary / LegacyManual) so the
  UI never silently falls back. F1 slot-guard (422 `ipqc.slot_write_in_item_mode`)
  + F2 self-heal `TryAutoSyncAsync` + F5 library endpoints under `NpiRead`. 3
  migrations `AddCheckItemLibrary` + `AddIpqcCheckItems` + `AddProcessLineMap`
  (lines STRING-typed → FINISHING needs no migration). Library importer reads
  `IPQC_Library_CMES_v3.csv`. DbSeeder upserts idempotent + NON-deleting (DR-1).
  Hybrid API boot seeds `process_line_map total=57` + `check_item_library` (probe).
  +17 Api tests (QcLineResolver / DataDriven / AutoSync / Library).

Post-merge smoke on `main` (isolated port 5101 + seeded DB, live :5100 untouched):
boot migration-check up-to-date; admin 200 across Machine Dashboard / QMS queue /
QC History / Home KPI / IPQC / FQC / OQC + library (5 lines/106) + process-map (57);
operator 403 on policy-gated QC + library routes; 401 unauth. Full suite **2140
tests, 0 fail** (legacy 950 · Client 594 · Razor 155 · Api 441). The
`Concurrent_run_qty_add` soak flake (L25, SQLite-macOS interleaving) is the only
intermittent — passes on isolated retry. maccatalyst app build red is local
toolchain only (Xcode 26.6 vs required 26.5), not code; CI builds non-app projects.

## 11. References

- [README.md](README.md) — user-facing quick start
- [docs/LESSONS_LEARNED.md](docs/LESSONS_LEARNED.md) — bài học chi tiết
- [docs/HOW-TO-UPGRADE-TO-SQLSERVER.md](docs/HOW-TO-UPGRADE-TO-SQLSERVER.md) — SQL Server cổng nâng cấp
- [docs/PHASE6-STEP*-PLAN.md](docs/) — survey doc archived per Bước
