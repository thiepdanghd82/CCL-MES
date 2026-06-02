# Phase 8 PR-D-5c — Drawings 3-role approval chain (PLAN, no code)

> **Status**: plan only. Branch chưa tạo. Đợi anh duyệt RBAC model + Q1..Q9
> + state machine trước khi vào code.
>
> **Predecessors**: PR-D-5a (FilesystemBlobStore + 6 guards) ✅,
> PR-D-5b (Drawings tab UI: upload + version chain + view) ✅.
>
> **Scope this PR**: add NPI / Production / QC chip mỗi `DrawingVersion`, decide
> modal (Approve / Reject + comment, comment bắt buộc khi Reject), state
> machine từ `Draft` → `PendingApproval` → `Approved`/`Rejected` driven by
> chip decisions, supersede logic khi version mới Approved, audit
> `DRAWING_DECIDE` per chip click.
>
> **Out of scope**: withdraw flow, multi-comment thread, email notifications,
> approval analytics.

---

## §1. RBAC model — câu hỏi quan trọng nhất, cần anh chốt trước

### Context

CMES sibling pattern (đọc READ-ONLY DrawingsTab.tsx + DecideDrawingModal.tsx):

```js
userCanActAs(role, chip) => {
  if (role === 'sys' || role === 'admin') return true;
  if (chip === 'NPI')         return role === 'npi';
  if (chip === 'Production')  return role === 'production' || role === 'planner';
  if (chip === 'QC')          return role === 'qc' || role === 'quality';
  return false;
}
```

CMES dùng role-based với granularity cao (`npi`, `production`, `qc`, `quality`,
`planner` đều là role). CCL-CMES có role set hẹp hơn:
`{Admin, Supervisor, Engineer, Qc, Operator}`.

**PR #28 explicitly designed `User.Department` cho việc này** — `src/CCL.MES.Domain/Entities/User.cs:46-55`:

```csharp
/// <summary>
/// Phase 8 PR #28 — Department tag (npi / production / qc / sales / planning…)
/// dùng cho Drawing 3-role approval mapping (Q5):
///   NPI slot      → Role=Engineer + Department=npi
///   Production    → Role=Engineer + Department=production (hoặc Role=Supervisor)
///   QC slot       → Role=Qc (any department)
/// Nullable + default null cho backfill cleanly. UI Phase 8/9 sẽ wire dropdown
/// trong Settings → Account Control → user edit form.
/// </summary>
public string? Department { get; set; }
```

**Quan trọng**: PR #28 doc-comment chỉ định mapping rõ ràng. Nhưng có
**1 conflict** anh phải biết: trang gate `NpiSpecRead` policy =
`{Admin, Supervisor, Engineer}` — **KHÔNG có Qc role**. Theo PR #28 doc-comment
"QC slot → Role=Qc", QC role users **không reach được trang** để bấm chip
QC — broken-by-design state.

### Options

| | Option (a) — Department + giữ page gate | Option (b) — Widen page gate | Option (c) — Admin/Engineer all 3 |
|---|---|---|---|
| Trang access | NpiSpecRead unchanged | New policy `NpiDrawingsAccess` = NpiSpecRead ∪ {Qc} | NpiSpecRead unchanged |
| NPI chip enable | (Role∈{Admin}) OR (Role=Engineer ∧ Dept=npi) | Same as (a) | Anyone Admin/Engineer |
| Production chip | (Role∈{Admin}) OR (Role=Engineer ∧ Dept=production) OR Role=Supervisor | Same as (a) | Anyone Admin/Engineer |
| QC chip | (Role∈{Admin}) OR (Role=Engineer ∧ Dept=qc) | Add: OR Role=Qc | Anyone Admin/Engineer |
| Tabs 1-5 (Spec/Setup/Artwork/QC-Plans/QC-Capture) | Unchanged | Need `<AuthorizeView Roles="Admin,Supervisor,Engineer">` wrap mỗi tab branch để ẩn Qc | Unchanged |
| **Lose true QC-role segregation** | ⚠ Yes — chỉ Engineer+Dept=qc QC-action được | ✅ No — Qc role users hành động QC chip thực sự | ⚠ Yes — không có segregation luôn |
| **Touches existing tabs** | ❌ 0 changes | ⚠ +5 AuthorizeView wraps (5 tabs đã ship) | ❌ 0 changes |
| **Touches Program.cs policies** | ❌ 0 changes | ⚠ +1 policy add + 1 route policy swap | ❌ 0 changes |
| **Follow PR #28 doc-comment intent** | ✅ ⅔ (NPI + Production đúng) — QC chip ánh xạ qua Department thay vì role | ✅ 3/3 đúng spec | ❌ Hoàn toàn không |
| **LOC trong PR-D-5c** | ~600 | ~700 (+5 AuthorizeView wraps + policy + tests) | ~550 (đơn giản hơn) |
| **Operator UX khi QC role cần duyệt** | ⚠ QC role không reach page; cần Engineer+Dept=qc làm thay | ✅ QC role login + bấm chip QC | ⚠ Admin/Engineer ai cũng bấm được — confusion |

### 🌟 Khuyến nghị mặc định: **Option (a)**

**Lý do**:

1. Fit với spec PR #28 (doc-comment đã chốt NPI + Production qua Department).
2. Zero churn cho tabs đã ship — không regress được Specification + Setup + Artwork + QC Plans + QC Capture.
3. Trade-off duy nhất là QC chip phải qua Engineer+Dept=qc thay vì role=Qc — có thể chấp nhận trong v1 vì:
   - Lead Engineer Phase 6 thường kiêm QC role check (workflow CCL).
   - Q1.b sẽ propose roadmap PR-D-5d sau này widen page policy nếu thực tế cần QC role thật.

**Trade-off rõ**: nếu CCL nội bộ muốn QC role users tự action chip QC (đúng segregation), chọn **Option (b)** — chi phí thêm ~100 LOC trong PR này (5 AuthorizeView wraps + 1 policy + tests parity).

**Option (c) chỉ nên chọn nếu**: anh muốn ship D-5c trong 1 ngày, chấp nhận audit log sẽ mơ hồ (ai cũng action được, không có segregation thực tế). Em recommend KHÔNG.

---

## §2. Entity check — DrawingApproval đã đủ field chưa?

Đọc `src/CCL.MES.Domain/Entities/Drawing.cs`:

```csharp
public class DrawingApproval : BaseEntity
{
    public long DrawingVersionId { get; set; }
    public DrawingVersion? DrawingVersion { get; set; }

    public DrawingApprovalRole Role { get; set; }       // Npi / Production / Qc
    public DrawingApprovalStatus Status { get; set; }   // Pending / Approved / Rejected
    public string? ActedBy { get; set; }
    public DateTime? ActedAt { get; set; }
    public string? Comment { get; set; }
}
```

Plus `BaseEntity` cho Id + CreatedAt/By + UpdatedAt/By.

DbContext config (existing `MesDbContext.cs:97`):
```csharp
b.Entity<DrawingApproval>().HasIndex(x => new { x.DrawingVersionId, x.Role }).IsUnique();
b.Entity<DrawingVersion>().HasMany(x => x.Approvals).WithOne(x => x.DrawingVersion!)
    .HasForeignKey(x => x.DrawingVersionId).OnDelete(DeleteBehavior.Cascade);
```

**Verdict**: ✅ **Đủ field. NO MIGRATION cần.**

- Có `ActedBy` + `ActedAt` (CMES gọi DecidedBy/At — name khác, semantic same).
- Có `Comment`.
- Unique constraint (DrawingVersionId, Role) đảm bảo max 3 row per version (NPI + Production + Qc).
- Cascade từ DrawingVersion (delete version → delete approvals).

**Backfill cho existing versions**:
- Live DB hiện tại: `DrawingVersions = 0` (post PR-D-5b). Không có legacy data → backfill no-op.
- Defensive design trong service: nếu gặp DrawingVersion thiếu approval rows (legacy), `ListByRevisionAsync` lazy-create 3 Pending rows on first read. Cost: 3 INSERT/version, idempotent qua unique index.

---

## §3. State machine

### Diagram

```
DrawingVersion lifecycle:

  Draft (initial — sau upload, KHÔNG có decision nào)
    │
    │ first chip click landed (state=Pending OR Rejected)
    ▼
  PendingApproval ─── (any chip flipped to Rejected) ──→ Rejected
    │                                                      │
    │ (all 3 chips = Approved)                             │ (operator uploads
    ▼                                                      │  new version v(n+1)
  Approved ────────── (newer version v(n+1) Approved) ──→ Superseded
                                                           │
                                                           │ (newer version Approved
                                                           ▼  rolls older)
                                                         Superseded
```

### Chi tiết transitions

| From | Event | To | Side effects |
|---|---|---|---|
| Draft | First chip Decide (any) | PendingApproval | Update version.Status; audit DRAWING_DECIDE |
| PendingApproval | All 3 chips = Approved | Approved | version.Status=Approved; Drawing.Status=Approved; CurrentVersionId = this; **supersede older versions** (§4) |
| PendingApproval | Any 1 chip = Rejected | Rejected | version.Status=Rejected; Drawing.Status stays Draft; CurrentVersionId KHÔNG đổi (stays at last Approved if any) |
| Approved | Newer version Approved | Superseded | Triggered by §4 rollup |
| Rejected | (no transition) | Stays | Operator phải upload version mới |

### Re-decide

**Default**: CMES sibling cho phép re-decide (DELETE + INSERT pattern). Cùng pattern em đề xuất cho CCL-CMES, vì:
- DrawingApproval status currently có thể flip (entity không enforce immutability).
- Operator hay decide nhầm — undo qua "click lại chip với decision khác" tự nhiên hơn là yêu cầu upload version mới chỉ để sửa lỗi.

**Edge case**: nếu chip đã Approved bị flip ngược về Rejected (trong khi 2 chip kia đã Approved trước đó), state machine recompute:
- Pre-flip: NPI=Approved, Prod=Approved, QC=Approved → version.Status=Approved.
- Post-flip: NPI=Approved, Prod=Approved, QC=Rejected → version.Status=Rejected.
- Side effect: nếu Drawing.CurrentVersionId pointing tại version này, KHÔNG auto-rollback (versionApproved → versionRejected là edge case admin manual override). Audit log đầy đủ để forensic.

**Reverse**: nếu chip Rejected bị flip lại Approved → version.Status recompute. Nếu trở thành Approved (3 OK), trigger supersede rollup §4.

### Reset

- Admin có thể "reset" 1 chip (DELETE row → Pending). Future feature, KHÔNG ship trong PR-D-5c.
- Operator KHÔNG có "reset" toàn version. Phải upload version mới.

---

## §4. Supersede logic

**Trigger**: chuyển 1 version từ Pending → Approved.

**Action** (within same transaction as the decide):
```
For each DrawingVersion V của cùng (DrawingId) WHERE V.Id != newlyApprovedId
    AND V.Status IN (Approved, PendingApproval, Draft):
  V.Status = Superseded
```

`Rejected` versions KHÔNG bị supersede (chúng là dead-end forensic). Lý do: nếu rollback newly-approved sau (Q3 dưới), `Rejected` versions không cần resurrect.

Drawing-level updates:
- `Drawing.CurrentVersionId = newlyApprovedId`
- `Drawing.Status = Approved`
- `Drawing.UpdatedBy = actorUsername`, `Drawing.UpdatedAt = now`

**FK safety**:
- DrawingVersion → Drawing (cascade-on-delete; not affected by status change).
- DrawingApproval → DrawingVersion (cascade-on-delete; status change preserves all approval rows for forensic).
- KHÔNG có entity con khác — supersede chỉ update Status field, không orphan.

**Verify**: integration test §7 sẽ confirm: upload v1 → 3-chip approve → upload v2 → 3-chip approve v2 → v1.Status=Superseded, Drawing.CurrentVersionId=v2.

---

## §5. Audit events

| Event | When | Detail JSON |
|---|---|---|
| `DRAWING_DECIDE` | Mỗi chip click (Approve hoặc Reject) | `{ revision_id, drawing_id, version_id, version_no, role, decision, has_comment, version_status_after, drawing_status_after }` |
| `DRAWING_SUPERSEDE` | Per version flipped sang Superseded by §4 | `{ revision_id, drawing_id, superseded_version_id, superseded_version_no, by_version_id, by_version_no, by_decided_user }` |

**Decision**: dùng `DRAWING_DECIDE` thay vì 2 codes APPROVE/REJECT để gọn (CMES sibling cũng dùng `SPEC_DRAWING_DECIDE`). `decision` field trong detail JSON phân biệt approve/reject.

`DRAWING_SUPERSEDE` emit 1 row per superseded version để forensic search dễ ("tìm tất cả version bị supersede bởi v3 này").

---

## §6. UI design

### Card mở rộng

Mỗi DrawingCard (đã ship PR-D-5b) thêm:
- **3 chip row** dưới status pill — NPI / Production / QC, mỗi chip:
  - Color theo Status: Pending=gray, Approved=green, Rejected=red
  - Click mở DecideDrawingModal
  - Disabled nếu `!canActAs(currentUser, role)` per RBAC model (§1)
  - Tooltip: "{role}: {status} by {actedBy} at {actedAt}" hoặc "{role}: waiting"

### DecideDrawingModal

Single-column, ~440 px:
- Subtitle: `{kind} · v{n} · {role} chip`
- Decision toggle: 2 buttons Approve (green) / Reject (red)
- Comment textarea (500 char max):
  - **Bắt buộc khi Reject** (per CMES sibling; guard client-side + server-side)
  - Optional khi Approve
- Cancel + Decide footer
- Submit disabled nếu chưa pick decision

### Status pill update

Pill trên card render `Drawing.Status` thay vì `DrawingVersion.Status` (đã thay đổi từ D-5b — D-5b render version status; D-5c switch to drawing status để reflect lifecycle full).

### Show all-versions timeline

Timeline (đã ship D-5b) thêm:
- 3 chip mini per version row
- Status pill update theo recomputed status

---

## §7. End-to-end test plan

Extend `scripts/VerifyDrawingsUpload` với 6 cases mới (tổng 14):

| # | Case | Pass criterion |
|---|---|---|
| 9 | Decide NPI Approve trên v1 fresh | version.Status=PendingApproval; 1 approval row Status=Approved; audit DRAWING_DECIDE emit |
| 10 | Decide Production Approve sau (1) | version.Status=PendingApproval; 2 approval rows Approved |
| 11 | Decide QC Approve sau (2) | version.Status=Approved; Drawing.Status=Approved; Drawing.CurrentVersionId=v1.Id |
| 12 | Decide Reject với empty comment | Throw InvalidOperation; nothing persisted |
| 13 | Re-decide NPI Reject sau Approve | version.Status flip về Rejected; audit logs both events |
| 14 | Upload v2 + 3-chip approve → v1 superseded | v1.Status=Superseded; v2.Status=Approved; Drawing.CurrentVersionId=v2.Id; DRAWING_SUPERSEDE audit emit per superseded version |

Plus RBAC test (option (a) hard-coded):
| # | Case | Pass criterion |
|---|---|---|
| 15 | Operator role decide → reject | UnauthorizedAccessException |
| 16 | Engineer + Department=production decide NPI chip → reject | InvalidOperation "không có quyền action role NPI" |

---

## §8. Q1..Q9 — questions với defaults

| Q | Question | Default | Trade-off |
|---|---|---|---|
| **Q1** | RBAC model (§1)? | **(a) Department-based, keep page gate** | (b) cleaner segregation but +100 LOC + touches 5 tabs; (c) interim quick but loses segregation |
| Q1.b | Future widen page policy cho Qc role thực sự? | **Defer to PR-D-5d** (post-D-5c if operator complaint) | Avoid scope creep this PR |
| Q2 | 3 approval rows tạo lúc upload (PR-D-5b service extend) hay lúc first decide (lazy)? | **Lazy create at first decide** (CMES pattern; simpler upload path) | Upfront create: 3 extra INSERT mỗi upload; consistent shape; +20 LOC |
| Q3 | Re-decide cho phép flip chip không? | **Yes, allowed** (CMES parity) | "Locked after decide" UX rõ hơn nhưng phải re-upload chỉ để sửa typo Comment — friction cao |
| Q4 | Comment required on Reject? | **Yes** (server-side + client-side guard) | Optional → audit nghèo, không debug được lý do reject |
| Q5 | Supersede trigger lúc Approve (this plan §4) hay lúc Upload v(n+1) (CMES pattern)? | **At Approve** (cleaner state machine; chỉ Approved versions chính thức supersede cũ) | Upload-time supersede cũ hơn không cần phải Approved trước → quá aggressive cho NPI workflow |
| Q6 | Drawing.CurrentVersionId update — lúc nào? | **Lúc version trở thành Approved** | Update lúc upload → CurrentVersionId trỏ tới Draft chưa duyệt; bad UX nếu admin Approve v3 nhưng pointer vẫn ở v2 Approved |
| Q7 | Rejected version có visible trong UI không? | **Có, render với pill đỏ rõ + comment hiển thị** | Hide rejected → operator confused "v3 đâu rồi?" |
| Q8 | Endpoint signature? | **`POST /api/specs/{revisionId}/drawings/{versionId}/decide`** body `{role, decision, comment}` (path-segment, no dot-ext per Lesson #33) | Single endpoint cho 3 roles + 2 decisions; consistent với D-5b download URL pattern |
| Q9 | Audit event names? | **`DRAWING_DECIDE` + `DRAWING_SUPERSEDE`** (2 codes) | 3 codes APPROVE/REJECT/SUPERSEDE → audit timeline cluttered; decision field trong detail JSON đủ phân biệt |

---

## §9. Effort estimate

| Layer | Files | LOC | Notes |
|---|---|---|---|
| Domain | `Audit/AuditAction.cs` | +6 | `DrawingDecide` + `DrawingSupersede` constants |
| Application | `Services/DrawingsService.cs` (existing) | +180 | `DecideAsync`, state-machine helper `RecomputeVersionStatus`, supersede loop, RBAC check (option (a) chip-permission helper) |
| Application | `Services/DrawingsService.cs` DTOs | +30 | `DrawingDecideRequest`, `DrawingApprovalView` |
| Application | DI | 0 | DrawingsService already registered |
| Web | `Controllers/DrawingsController.cs` | +50 | `[HttpPost("{versionId}/decide")]` route + RBAC attribute |
| Web | `Pages/Npi/EngineerSpecDetail.razor` | +220 | 3 chip render per card + per timeline-row + DecideDrawingModal + chip-permission helper |
| Web i18n | `SharedResource.{resx, vi.resx}` | +50 keys × 2 | Chip labels (NPI/Production/QC) + status (Pending/Approved/Rejected) + modal labels + 5 error messages |
| Web CSS | `wwwroot/css/site.css` | +50 | `.spec-drawings-chip` + active/disabled states + decide modal layout |
| Scripts (out-of-sln) | `scripts/VerifyDrawingsUpload/Program.cs` | +200 | Test cases 9-16 |
| Docs | `LESSONS_LEARNED.md` | +35 | New section: state-machine recompute pattern + Department check + re-decide via DELETE+INSERT vs UPDATE |

**Total LOC**: ~700 / ~1000 nếu chọn Option (b). **Effort**: M (1-2 phiên).

**Migration**: 0 (DrawingApproval scaffold đủ).

---

## §10. Vùng cấm reminder

PR-D-5c TUYỆT ĐỐI KHÔNG đụng:
- Ops Control v1.2, Old ver, Machine, ProductionLog
- 5 NPI tab khác (Specification, Setup, Artwork, QC Plans, QC Capture) — except nếu chọn Option (b) thì +5 AuthorizeView wraps
- Phase 6 IqcInspection + WorkOrders
- Shop Order
- IBlobStore + FilesystemBlobStore (đã frozen sau D-5b)
- DrawingsController download endpoint (đã verified D-5b)

Baseline preserve verify post-build:
```
ProductRevisions=6, WorkOrders=1, IqcInspections=3, IqcResultDetails=7,
Users=5, ManufacturingStructures=20530, ProcessCatalogs=17, ReasonCodes=12,
Drawings=0, DrawingVersions=0
```

FK ProductRevision↔WO intact (WO-26-3683 → revision 1).

---

## §11. Ship gates (sau anh duyệt plan)

1. Chốt Q1..Q9 + RBAC model option (a/b/c).
2. `git checkout -b feat/phase8-spec-tab-drawings-approval`.
3. Implement theo §1-§9 thứ tự: Domain → Application → Web → tests.
4. Verify: build clean + harness `VerifyDrawingsUpload` 8 cases gốc + 6-8 cases mới pass + boot smoke + RBAC end-to-end + baseline intact.
5. Update `LESSONS_LEARNED.md`.
6. Commit + push + open PR.
7. **STOP for review.**

---

*Plan tạo: 2026-06-01 — Phase 8 PR-D-5c (3-role approval chain) — NO branch yet.*
