# Phase 8 — Spec Lifecycle ops (3-PR series, refresh of PR #30 plan)

**Status**: DRAFT — chờ duyệt scope 3-PR + Q1–Q12 trước khi tạo branch PR-L1
**Parent**: `main` post PR #56 merged (a3ec5b0)
**Old plan**: `docs/PHASE8-PR30-PLAN.md.deferred` (single PR, deferred 2026-05-XX). Refresh chia thành 3 PR để giảm rủi ro merge.
**Hard freeze**: KỲ VỌNG KHÔNG migration (entity fields #28 đủ — verified §1). Nếu code phát hiện gap → STOP báo cáo, KHÔNG quietly thêm migration. Nếu cần thật → A→B→C SAFE trên isolated /tmp + backup + SHA256.

---

## 1. Re-validate hiện trạng (post-#56)

### 1.1 Entity scaffold (PR #28) — XÁC NHẬN ĐỦ

| Field cần | Source | Trạng thái |
|---|---|---|
| `ProductRevision.IsTrashed` (bool) | [Spec.cs:46](src/CCL.MES.Domain/Entities/Spec.cs#L46) | ✅ có |
| `ProductRevision.TrashedAt` (DateTime?) | [Spec.cs:47](src/CCL.MES.Domain/Entities/Spec.cs#L47) | ✅ có |
| `ProductRevision.TrashedBy` (string?) | [Spec.cs:48](src/CCL.MES.Domain/Entities/Spec.cs#L48) | ✅ có |
| `ProductRevision.ParentRevisionId` (long?) | [Spec.cs:35](src/CCL.MES.Domain/Entities/Spec.cs#L35) | ✅ có (lineage) |
| `ProductRevision.ChangeSummary` (string?) | [Spec.cs:38](src/CCL.MES.Domain/Entities/Spec.cs#L38) | ✅ có |
| `ProductRevision.EffectiveTo` (DateTime?) | [Spec.cs:32](src/CCL.MES.Domain/Entities/Spec.cs#L32) | ✅ có (auto-set on Supersede) |
| `ProductRevisionStatus` enum | [Enums.cs:29](src/CCL.MES.Domain/Enums.cs#L29) | ✅ 5 state: `Draft, InReview, Approved, Released, Superseded` |
| WO FK `ProductRevisionId` `ON DELETE RESTRICT` | DB PRAGMA | ✅ verified: `1\|0\|ProductRevisions\|...\|NO ACTION\|RESTRICT\|NONE` |
| Audit infra (`IAuditWriter` + AuditLog) | Phase 6 Bước 5 | ✅ có |
| Trash filter trong SpecsAsync | [SpecService.cs:53](src/CCL.MES.Application/Services/SpecService.cs#L53) | ✅ `WHERE !IsTrashed` đã apply |
| Trash filter trong SpecDetailAsync | [SpecService.cs:302](src/CCL.MES.Application/Services/SpecService.cs#L302) | ✅ |

**Kết luận**: Schema + read-path đã 100% sẵn cho lifecycle ops. **KHÔNG migration** cho cả 3 PR.

**Cần thêm field nào không?**
- Trash semantics có thể muốn `TrashReason` (string?) — KHÔNG trong scaffold. Đề xuất: skip Reason field; lý do trash thường implicit (operator soft-delete cũ); audit log ghi actor + timestamp đủ forensic. Nếu sau cần → A→B→C migration follow-up sprint.
- Revise reason đã được phục vụ bằng `ChangeSummary` ✓ (đã có).
- Supersede manual không cần field mới (auto-set EffectiveTo).

### 1.2 Chrome buttons (PR #42/#44/#29) — bản đồ hiện trạng

#### EngineerSpecDetail.razor — actions bar (per-spec detail page)

| Button | Current state | PR đảm nhận |
|---|---|---|
| ⟲ History | ✅ wired (OpenHistory) | — |
| ⬇ Export | ✅ wired (ExportPrintAsync) | — |
| ⎘ Copy | ❌ `disabled` stub `<span>soon</span>` | **PR-L1** |
| ↗ Promote | ✅ wired (PromoteAsync → SpecSvc.ApproveAsync) | — |
| 🗑 Trash | ❌ `disabled` stub `<span>soon</span>` | **PR-L3** |
| ✏ Edit (Spec tab toolbar) | ❌ `disabled` stub `<span>soon</span>` | **PR-L1** |
| ✂ Revise | **CHƯA tồn tại** trong actions bar | **PR-L2 — ADD** |
| ⊘ Mark Superseded | **CHƯA tồn tại** trong actions bar | **PR-L2 — ADD** |

#### SpecContextMenu.razor — right-click menu trên grid

| Item | Current state | PR đảm nhận |
|---|---|---|
| Open / Get Info | ✅ wired | — |
| Edit | ❌ `disabled` "Coming soon" | **PR-L1** |
| Copy | ❌ `disabled` "Coming soon" | **PR-L1** |
| Revise | ❌ `disabled` "Coming soon" | **PR-L2** |
| Mark Superseded | ❌ `disabled` "Coming soon" | **PR-L2** |
| Trash (NEW) | **CHƯA tồn tại** trong menu | **PR-L3 — ADD** |

### 1.3 SpecService methods — gap analysis

**Existing**: `SpecsAsync` · `ProductsForDropdownAsync` · `CreateAsync` · `ApproveAsync` · `SpecContentAsync` · `SpecAuditTrailAsync` · `SpecDetailAsync`

**Missing** (Lifecycle ops):
- `CopyAsync` (PR-L1)
- `UpdateAsync` (PR-L1, Draft-only gate)
- `ReviseAsync` (PR-L2, deep-clone + auto-supersede + lineage)
- `SupersedeAsync` (PR-L2, manual mark)
- `TrashAsync` (PR-L3, WO active blocker)
- `RestoreAsync` (PR-L3)
- `SpecTrashPurgeService` (PR-L3, BackgroundService 24h cycle, 30-day retention)

### 1.4 Audit codes — gap

**Existing**: `SpecApprove`, `SpecBackfillDetail`, `SpecCreate`, `SpecExport`, `SpecImport`, `SpecQcPlanUpsert`, `SpecQcCapture`, `SpecRefreshSamples`

**Missing (~7)**: `SpecCopy`, `SpecUpdate`, `SpecRevise`, `SpecSupersede`, `SpecTrash`, `SpecRestore`, `SpecPurge`

### 1.5 Coupling — verified

- **WO active reference** chặn Trash: vì FK `ProductRevisionId` ON DELETE RESTRICT, nếu KHÔNG check ngầm thì soft-delete vẫn ổn (chỉ flip IsTrashed, FK row vẫn integrate). Nhưng nếu sau đó **Purge hard-delete** sẽ vi phạm FK → exception. → Trash phải block khi còn WO active (Status ∉ {Closed, Cancelled, Finished}); Purge cần safety net thứ 2 (skip + log nếu vi phạm).
- **IqcInspections** không có FK trực tiếp đến ProductRevision → không cần blocker. Verify: IqcInspections (1|workorder ref through WO chain) → không ảnh hưởng.
- **Drawing** đã có FK ON DELETE CASCADE đến ProductRevision (PR-D-5a) → Purge hard-delete sẽ cascade Drawings — acceptable (Drawings tied to spec rev).
- **SpecQcCapture** (PR-D-4) — cần verify FK; nếu CASCADE thì OK, nếu RESTRICT thì Purge sẽ fail. Plan PR-L3 sẽ verify.

---

## 2. Semantics chốt (default theo plan cũ + user guidance)

| Op | Semantics đề xuất default | Trade-off / Notes |
|---|---|---|
| **Copy** | New ProductRevision độc lập. SpecCode empty (operator nhập, validate unique). RevCode='A'. ParentRevisionId=null. Status=Draft. Deep-clone 4 sub-specs (Material/Print/Diecut/Finishing) + SpecPrintColor rows (nếu có). Title pre-fill `<old title> (copy)`. | — |
| **Edit** | Identity (Title, RefNo, InspectionLevel, ProcessCode via ColorSpecJson params) trên Draft rev. **Gate Draft only** — Approved/Released/Superseded immutable. Deep diff Phase 9. | — |
| **Revise** | Tạo rev MỚI: bump `RevCode` qua `NextRev()` (A→B→C→…→Z→AA→AB→…). Lineage `ParentRevisionId=src.Id`. Status=Draft. Deep-clone 4 sub-specs + SpecPrintColor + flexo color rows. `ChangeSummary=<operator reason, mandatory>`. Old rev `Status=Superseded`, `EffectiveTo=UtcNow`. **Tất cả trong 1 transaction**. | Required reason — admin diff input. |
| **Mark Superseded** | Manual: chỉ available khi `Status ∈ {Approved, Released}`. **2-step confirm: type SpecCode để xác nhận** (semi-irreversible). Set Status=Superseded + EffectiveTo=UtcNow. KHÔNG tạo rev mới. | Per user explicit "Q1 confirm type SpecCode" — semi-irreversible safety. |
| **Trash** | Soft-delete: `IsTrashed=true`, `TrashedAt=UtcNow`, `TrashedBy=user`. **Blocker**: count WO `WHERE ProductRevisionId=rev.Id AND Status NOT IN {Closed, Cancelled, Finished}`; nếu >0 → 422 với message "Cannot trash: <N> active WO reference this spec". Closed/Cancelled/Finished WO không block (historical). | — |
| **Restore** | Flip `IsTrashed=false`; clear `TrashedAt`/`TrashedBy`. KHÔNG blocker. | — |
| **Purge** | `BackgroundService` 24h cycle. Hard-delete `WHERE IsTrashed AND TrashedAt < UtcNow - retention`. Retention default 30 days, env `OPS_SPEC_TRASH_RETENTION_DAYS=30`. First-run delay 30s. Idempotent (cycle 2 = 0 eligible). **Safety net**: skip + log nếu eligible rev vẫn còn WO ref (defense-in-depth). | Test isolated /tmp trước khi enable trên live. |

### Audit JSON detail shapes

```
SPEC_COPY      { source_id, source_code, source_rev, new_id, new_code }
SPEC_UPDATE    { rev_id, rev_code, fields_changed: [...] }
SPEC_REVISE    { source_id, source_rev, new_id, new_rev, reason }
SPEC_SUPERSEDE { rev_id, rev_code, from_status, manual: true }
SPEC_TRASH     { rev_id, rev_code, status }
SPEC_RESTORE   { rev_id, rev_code }
SPEC_PURGE     { purged_count, ids: [...], cutoff_utc, skipped_fk: [...] }
```

### `NextRev()` helper (port SpecHub `nextRev` JS)

```csharp
public static string NextRev(string current)
{
    var r = (current ?? "A").Trim().ToUpperInvariant();
    if (string.IsNullOrEmpty(r)) return "A";
    var chars = r.ToCharArray();
    int i = chars.Length - 1;
    while (i >= 0)
    {
        if (chars[i] == 'Z') { chars[i] = 'A'; i--; }
        else { chars[i] = (char)(chars[i] + 1); return new string(chars); }
    }
    return "A" + new string(chars);  // Z→AA, AZ→BA, ZZ→AAA
}
```

Test matrix: `A→B`, `B→C`, `Y→Z`, `Z→AA`, `AZ→BA`, `ZZ→AAA`, `null→A`, `""→A`.

---

## 3. Chia PR (3 PR series)

### PR-L1 — Copy + Edit (Draft-only) — **NHẸ NHẤT, ship trước**

**Branch**: `feat/phase8-spec-lifecycle-copy-edit` (base main)
**Effort**: S-M (~800 LOC)
**Migration**: KHÔNG

**Scope**:
1. Service `SpecService.CopyAsync(CopySpecRequest, user)` — new rev với SpecCode operator-supplied, deep-clone 4 sub-specs + SpecPrintColor.
2. Service `SpecService.UpdateAsync(long revId, UpdateSpecRequest, user)` — gate Draft only; touch Identity + ColorSpecJson params.
3. Controller `POST /api/specs/{id}/copy` + `PUT /api/specs/{id}` — RBAC `[Authorize(Roles="Admin,Engineer")]` + audit emit callsite.
4. `Shared/SpecCopyModal.razor` — form Copy (SpecCode + Title + ProductId dropdown + ProcessCode).
5. `Shared/SpecEditModal.razor` — form Edit Identity + ColorSpecJson params table (Add/Remove rows).
6. Un-stub chrome:
   - `EngineerSpecDetail.razor` actions bar — Copy button wires to OpenCopyModal; Edit button (Spec tab toolbar) wires to OpenEditModal.
   - `SpecContextMenu.razor` — un-stub Edit (Draft gate) + Copy (always).
7. Audit codes: `SpecCopy`, `SpecUpdate`.
8. i18n EN/VI ~30 keys (modal labels + confirm + error states).

**Acceptance**:
- Copy 1 rev → new rev Draft xuất hiện trong grid với SpecCode mới + ParentRevisionId=null.
- Edit Draft rev → field update + audit row SPEC_UPDATE.
- Edit Approved/Released rev → 422 "only Draft revs editable".
- Curl as Operator → 403 trên cả 2 endpoint.
- `dotnet build` 0/0; baseline preserved; Phase 6 + sibling untouched.

### PR-L2 — Revise + Supersede (deep-clone + auto-supersede + lineage)

**Branch**: `feat/phase8-spec-lifecycle-revise-supersede` (base PR-L1 merged main)
**Effort**: M (~600 LOC)
**Migration**: KHÔNG

**Scope**:
1. Service `SpecService.ReviseAsync(long srcRevId, ReviseSpecRequest{reason}, user)` — bump RevCode via NextRev, deep-clone, lineage, auto-supersede old. All in 1 transaction.
2. Service `SpecService.SupersedeAsync(long revId, user)` — manual mark, gate `Status ∈ {Approved, Released}`.
3. Helper `Domain.NextRev()` + unit tests cho 8 case (A→B, Z→AA, …).
4. Controller `POST /api/specs/{id}/revise` + `POST /api/specs/{id}/supersede`.
5. `Shared/SpecReviseModal.razor` — reason textarea (mandatory, validate non-empty).
6. `Shared/SpecSupersedeConfirmModal.razor` — 2-step confirm (type SpecCode).
7. Un-stub chrome:
   - `SpecContextMenu.razor` — un-stub Revise (gate Approved/Released) + Mark Superseded (gate Approved/Released).
   - `EngineerSpecDetail.razor` actions bar — **ADD** ✂ Revise button + ⊘ Mark Superseded button.
8. Audit codes: `SpecRevise`, `SpecSupersede`.
9. i18n EN/VI ~25 keys.

**Acceptance**:
- Revise rev_A (Approved) → rev_B (Draft) exists with ParentRevisionId=rev_A.Id; rev_A flipped to Superseded with EffectiveTo set; 4 sub-specs deep-cloned with correct ProductRevisionId; ChangeSummary captures reason; SPEC_REVISE audit emit.
- Mark Superseded on Approved → 2-step confirm → Status=Superseded, no new rev; SPEC_SUPERSEDE audit emit.
- Revise Draft/Superseded rev → 422 "Revise only on Approved/Released".
- NextRev unit tests pass cho 8 case.
- `dotnet build` 0/0; baseline preserved; Phase 6 + sibling untouched.

### PR-L3 — Trash + Restore + Purge HostedService

**Branch**: `feat/phase8-spec-lifecycle-trash-purge` (base PR-L2 merged main)
**Effort**: M-L (~1000 LOC)
**Migration**: KHÔNG (field #28 đủ)

**Scope**:
1. Service `SpecService.TrashAsync(long revId, user)` — soft-delete + **WO blocker** check.
2. Service `SpecService.RestoreAsync(long revId, user)` — flip back.
3. **BackgroundService** `Application/Services/SpecTrashPurgeService.cs` — 24h cycle, 30-day retention env-overridable, first-run delay 30s, defense-in-depth FK safety skip-and-log.
4. Verify FK cascade on `SpecQcCapture` (PR-D-4) — nếu RESTRICT thì purge code phải cascade-delete in tx; nếu CASCADE đã có thì OK. **Plan to inspect at code-time, file as a checkpoint not a blocker.**
5. Controller `POST /api/specs/{id}/trash` + `POST /api/specs/{id}/restore`.
6. UI `EngineerSpec.razor`:
   - **Filter chip toolbar** `[All / Active / Trash]` (default Active) — apply `?trashed={0|1}` param to SpecsAsync.
   - **Trash view columns**: TrashedAt, TrashedBy, Restore button (replaces context menu on trash rows).
   - Trash row styling: line-through grey (CSS class).
7. `SpecContextMenu.razor` — **ADD** Trash item (gate Admin/Engineer, always available on active rows; danger styling).
8. `EngineerSpecDetail.razor` actions bar — un-stub 🗑 Trash button.
9. Confirm dialog cho Trash + inline error banner cho WO blocker (422 with active WO count).
10. Audit codes: `SpecTrash`, `SpecRestore`, `SpecPurge`.
11. i18n EN/VI ~35 keys.
12. **Isolated /tmp test** cho Purge cycle:
    - Backup live → copy `/tmp/purge-test.db`.
    - Insert 2 fake trashed revs: one `TrashedAt = NOW - 31 days` (eligible), one `NOW - 29 days` (not eligible).
    - Set `OPS_SPEC_TRASH_RETENTION_DAYS=30`.
    - Boot server on /tmp DB, wait 35s, verify: 31-day rev gone, 29-day rev intact.
    - Audit `SPEC_PURGE` row with `purged_count=1` + ids.
    - Restart server, wait 35s, verify: SPEC_PURGE row with `purged_count=0` (idempotent).
13. `Program.cs` DI: `services.AddHostedService<SpecTrashPurgeService>()`.

**Acceptance**:
- Trash rev → soft-deleted, gone from default Active view, visible in Trash view.
- Trash rev with active WO → 422 with active WO count in message.
- Restore from Trash view → back to Active.
- BackgroundService cycle on /tmp deletes only eligible rev; idempotent verified.
- Purge skip-and-log nếu FK vi phạm (defense-in-depth).
- `dotnet build` 0/0; baseline preserved (delete test fakes post-verify); Phase 6 + sibling untouched.

---

## 4. Files touched (per-PR breakdown)

### PR-L1 (Copy + Edit)
- **NEW**: `Shared/SpecCopyModal.razor`, `Shared/SpecEditModal.razor`
- **MODIFY**: `Services/SpecService.cs` (+CopyAsync +UpdateAsync), `Controllers/SpecsController.cs` (+2 endpoints), `Pages/Npi/EngineerSpecDetail.razor` (un-stub Copy + Edit), `Shared/SpecContextMenu.razor` (un-stub Edit + Copy), `Pages/Npi/EngineerSpec.razor` (handlers), `Application/Dtos.cs` (+CopySpecRequest +UpdateSpecRequest), `Domain/Audit/AuditAction.cs` (+SpecCopy +SpecUpdate), `Resources/SharedResource.resx` + `.vi.resx` (+30 keys), `wwwroot/css/site.css` (modal styling reuse).

### PR-L2 (Revise + Supersede)
- **NEW**: `Shared/SpecReviseModal.razor`, `Shared/SpecSupersedeConfirmModal.razor`, `Domain/SpecRevisionHelpers.cs` (NextRev + unit tests)
- **MODIFY**: `Services/SpecService.cs` (+ReviseAsync +SupersedeAsync), `Controllers/SpecsController.cs` (+2 endpoints), `Pages/Npi/EngineerSpecDetail.razor` (ADD Revise + Supersede buttons), `Shared/SpecContextMenu.razor` (un-stub Revise + Mark Superseded), `Pages/Npi/EngineerSpec.razor` (handlers), `Application/Dtos.cs` (+ReviseSpecRequest), `Domain/Audit/AuditAction.cs` (+SpecRevise +SpecSupersede), `Resources/SharedResource.resx` + `.vi.resx` (+25 keys), `wwwroot/css/site.css`.

### PR-L3 (Trash + Restore + Purge)
- **NEW**: `Application/Services/SpecTrashPurgeService.cs` (BackgroundService), `Shared/SpecTrashConfirmModal.razor`
- **MODIFY**: `Services/SpecService.cs` (+TrashAsync +RestoreAsync), `Controllers/SpecsController.cs` (+2 endpoints), `Pages/Npi/EngineerSpecDetail.razor` (un-stub Trash button), `Shared/SpecContextMenu.razor` (ADD Trash item), `Pages/Npi/EngineerSpec.razor` (filter chip + Trash view columns + Restore button + handlers), `Application/Dtos.cs` (+TrashResult enum), `Domain/Audit/AuditAction.cs` (+SpecTrash +SpecRestore +SpecPurge), `Resources/SharedResource.resx` + `.vi.resx` (+35 keys), `wwwroot/css/site.css` (`.spec-trash-row` line-through + filter chip), `Program.cs` (+AddHostedService).

### KHÔNG đụng (all 3 PRs)
- Phase 6 mutation: WorkOrderService.{Advance, UpdateFlags, Create}, OeeService.*, QcService.*, WorkOrderStateMachine, ProductionLog entity, ShopfloorHub/Notifier
- 4 NPI tab khác (Spec subtabs Materials/Print/Diecut/Finishing chỉ touch nếu trong scope deep-clone)
- Machine + ProductionLog
- Sibling projects: Ops Control v1.2, CMES, SpecHub, Old ver

---

## 5. Q1–Q12 chốt (default + trade-off)

### Semantics

- **Q1 Revise required fields**: Reason mandatory (>= 5 chars) lưu vào ChangeSummary? **OK**?
- **Q2 Mark Superseded confirm**: 2-step type SpecCode để xác nhận (semi-irreversible) — **OK**? Hay 1-step confirm đơn giản?
- **Q3 Edit gate**: Strict Draft only — **OK**? Hay cho phép Edit Approved (rare admin override)?
- **Q4 Copy lineage**: ParentRevisionId=null (independent copy) — **OK**? Hay set ParentRevisionId=src.Id (lineage even on Copy)?
- **Q5 Trash blocker scope**: Block khi WO Status ∉ {Closed, Cancelled, Finished} (= chỉ Closed/Cancelled/Finished allow trash) — **OK**? Hay cho phép trash khi tất cả WO ≠ Running (loose hơn)?

### Implementation

- **Q6 Deep clone scope**: 4 sub-specs (Material/Print/Diecut/Finishing) + SpecPrintColor rows + flexo color rows — **OK**? QcWindow rows (PR-D-3) + Drawings (PR-D-5b) thì sao? Đề xuất: QcWindow=copy, Drawings=NOT copy (separate uploads). Confirm?
- **Q7 Purge retention**: 30 days env-overridable (`OPS_SPEC_TRASH_RETENTION_DAYS`) — **OK**? Hay 60 days cho audit safety?
- **Q8 Purge cycle interval**: 24h — **OK**? Hay 6h cho responsive?
- **Q9 Purge first-run delay**: 30s — **OK**? (Avoid race với DbSeeder.)
- **Q10 Filter chip default**: `[All / Active / Trash]` default Active — **OK**? Hay All?

### Process

- **Q11 PR order**: L1 → L2 → L3 sequential (mỗi PR base trên main sau merge PR trước) — **OK**? Hay L1 + L2 parallel, L3 sau?
- **Q12 Test isolated /tmp cho Purge**: BẮT BUỘC trước khi enable trên live (PR-L3) — **OK**? Plan ship verify-log trong PR body để anh review trước merge.

---

## 6. Hard constraints (mandatory pass per-PR)

- [ ] `dotnet build` 0/0
- [ ] `git diff main` cho Phase 6 (OeeService / QcService / StateMachine / WorkOrderService.{Advance,UpdateFlags,Create} body / Hubs / ProductionLog / Machine) = **0 LOC**
- [ ] No EF migration (verify `ls src/CCL.MES.Infrastructure/Migrations/` unchanged)
- [ ] `.csproj` dep diff = 0
- [ ] Mutation transaction-wrapped + audit emit inside same transaction
- [ ] RBAC `[Authorize(Roles="Admin,Engineer")]` server-side + matching AuthorizeView client-side
- [ ] Baseline preserved: ProductRevisions=6, WorkOrders=1, IqcInspections=3 (test data deleted post-verify)
- [ ] FK ProductRevision↔WO intact
- [ ] Responsive theo Lesson pin "Responsive main tab pattern"
- [ ] EN/VI i18n parity
- [ ] Sibling projects + Spec 6 tab + 4 NPI tab khác + Machine + Ops Control v1.2 / CMES / SpecHub / Old ver READ-ONLY
- [ ] Try-catch wrap mọi handler (#27); error banner inline (không freeze circuit)

---

## 7. Verify gates per-PR (V1–V20 across 3 PRs)

### PR-L1 (Copy + Edit) — V1–V6

- V1: `dotnet build` 0/0
- V2: Copy 1 rev → new rev Draft + audit SPEC_COPY; baseline ProductRevisions +1 test → cleanup post-verify
- V3: Edit Draft rev → field update + audit SPEC_UPDATE
- V4: Edit Approved rev → 422 "Draft only"
- V5: Curl as Operator → 403 trên Copy + Edit
- V6: Phase 6 vùng cấm 0 LOC diff

### PR-L2 (Revise + Supersede) — V7–V12

- V7: NextRev unit tests pass (A→B, Z→AA, AZ→BA, ZZ→AAA, null→A, ""→A, edge case)
- V8: Revise Approved rev → new Draft rev với lineage + old auto-Superseded + 4 sub-specs deep-clone + SPEC_REVISE audit
- V9: Mark Superseded on Approved → 2-step type SpecCode → Status=Superseded + SPEC_SUPERSEDE audit
- V10: Revise Draft → 422 "Revise only on Approved/Released"
- V11: All transactions atomic (test partial failure → no half-written state)
- V12: Phase 6 vùng cấm 0 LOC diff

### PR-L3 (Trash + Restore + Purge) — V13–V20

- V13: Trash rev → soft-deleted + audit SPEC_TRASH; gone from Active view; visible in Trash view
- V14: Trash rev with active WO → 422 "X active WO reference"
- V15: Restore → back to Active + audit SPEC_RESTORE
- V16: Filter chip `[All / Active / Trash]` works UI-side
- V17: **Isolated /tmp purge test**: 31-day rev gone, 29-day rev intact, SPEC_PURGE audit row, idempotent
- V18: Purge FK safety skip-and-log when active WO ref present (defense-in-depth)
- V19: BackgroundService boot delay 30s observed in log
- V20: Phase 6 vùng cấm 0 LOC diff

---

## 8. STOP — chờ duyệt

Plan này nêu **12 câu hỏi** với default đề xuất. Sau khi anh chốt:

1. Tạo branch `feat/phase8-spec-lifecycle-copy-edit` (PR-L1) — code theo plan, verify V1–V6, commit + PR + STOP chờ duyệt.
2. Sau PR-L1 merge: tạo `feat/phase8-spec-lifecycle-revise-supersede` (PR-L2) — V7–V12.
3. Sau PR-L2 merge: tạo `feat/phase8-spec-lifecycle-trash-purge` (PR-L3) — V13–V20, isolated /tmp purge test report kèm PR body.

Em sẽ **KHÔNG tạo branch** cho đến khi anh:
- Duyệt scope 3-PR split (Copy+Edit / Revise+Supersede / Trash+Restore+Purge)
- Chốt Q1–Q12 (hoặc nhận default)
- Confirm hard constraints (đặc biệt: KHÔNG migration, Phase 6 vùng cấm 0 LOC, Trash blocker semantics, Purge isolated /tmp test mandatory)
