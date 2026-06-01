# PHASE 8 PR #29 — Context menu + SpecDetailModal (read-only)

> Branch `feat/phase8-spec-context-detail` base `main` (post PR #28 merge).
> KHÔNG migration, KHÔNG sửa schema. Pattern reuse 100% từ PR #27 WC.

---

## Scope

1. **SpecContextMenu.razor** — reuse pattern PR #27 `WorkCenterContextMenu.razor`. Dark theme menu với 7 items:
   - **Open** (⌘O) — opens SpecDetailModal
   - **Edit** (⌘E) — placeholder PR #30, disabled + tooltip "Coming soon (PR #30)"
   - **Copy** (⌘D) — placeholder PR #30, disabled + tooltip
   - **separator**
   - **Revise** (⌘R) — placeholder PR #30, disabled + tooltip
   - **Mark Superseded** (⌘T) — placeholder PR #30, disabled + tooltip (replace WC's Activate-Deactivate since ProductRevisionStatus có "Superseded" thay vì Active/Inactive flag)
   - **Get Info** (⌘I) — alias of Open (same action, mirror WC pattern)

   Mutation items (Edit/Copy/Revise/Mark Superseded) đều disabled cứng ở PR #29 (KHÔNG wire dở) — chỉ Open/Get Info active. `<AuthorizeView Roles="Admin,Engineer">` gate quanh mutation items (cứ giữ structure cho PR #30 quick wire) nhưng items đều `disabled`.

2. **SpecDetailModal.razor** — read-only 4-section detail view. Render từ `ProductRevisionListItem` (flat DTO grid đã có) cho Identity; siblings/audit query riêng với try-catch + error banner (bài học hotfix PR #27):
   - **Identity** (instant) — SpecCode / Title / Product / RevCode / Status badge — render trực tiếp từ DTO
   - **Spec content** (server query, try-catch) — load Material / Print / Diecut / Finishing siblings; mỗi sub-section rỗng hiện "—"; SpecPrint.ColorSpecJson parse + render params Width/Height/Process với tol/uom/critical
   - **Drawings** (placeholder vàng) — "Available in PR #31"
   - **Audit trail** (server query, try-catch) — Created/Updated/Approved stamps + AuditLog query filtered `targetType='ProductRevision'` + `targetId=<id>` (Q9 cam kết: KHÔNG thêm field mới, dùng AuditLog table)

3. **EngineerSpec.razor wire-up** — `@oncontextmenu` trên `<tr>` row, capture client X/Y + ProductRevisionListItem, gọi `SpecContextMenu.Open(x, y, row)`. `OnCtxAction` switch action: `open`/`info` → modal, others → no-op (placeholder).

4. **SpecService** — ADD 2 method read-only:
   - `SpecContentAsync(long revisionId) → SpecContentDto?` — load 4 sibling specs với `Include`. Trả null nếu revision không tồn tại.
   - `SpecAuditTrailAsync(long revisionId, int max = 50) → List<SpecAuditEntry>` — query AuditLog ORDER BY Timestamp DESC LIMIT N. Trả empty list nếu lỗi (caller handle).
   - KHÔNG audit emit cho Open/View (Q9 cam kết: dùng audit log table sẵn có cho display, KHÔNG ADD audit emit cho read).

5. **i18n EN/VI** — keys mới (~25):
   - `npi.spec.ctx.*` (7 items + 2 tooltips)
   - `npi.spec.detail.section.*` (4 sections)
   - `npi.spec.detail.content.*` (5 sub-content keys)
   - `npi.spec.detail.audit.*` (loading / empty / error)
   - `npi.spec.detail.btn_close` + format strings

6. **CSS** — append `.spec-ctx-*` (mirror `.wc-ctx-*`) + `.spec-detail-*` sections vào `site.css`. Modal-card reuse `.modal-scrim` + `.modal-card` from `modal-*` shared CSS (đã có từ Phase 7 hạng mục 4).

---

## Hard constraints

- ❌ KHÔNG migration, KHÔNG sửa entity schema (11 entity từ PR #28 đã đủ).
- ❌ KHÔNG re-fetch by id cho Identity (DTO grid đã có). Spec content + Audit query phụ ở phương thức riêng có try-catch + error banner inline trong section.
- ❌ KHÔNG wire mutation items dở — disabled với tooltip "Coming soon (PR #30)".
- ✅ RBAC `NpiSpecRead` policy unchanged. Mutation `<AuthorizeView Roles="Admin,Engineer">` gate giữ structure cho PR #30 (defensive depth).
- ✅ Reuse 100% pattern PR #27 — bài học hotfix: pass row directly + try-catch async handlers + error banner.

## Verify gates

| # | Check | Pass |
|---|---|---|
| 1 | `dotnet build` clean | 0 errors |
| 2 | Row counts unchanged | ProductRevisions=1 / ProcessCatalogs=17 / IQC=3 / Phase 7 NPI baseline intact |
| 3 | Right-click trên row mở context menu tại mouse position | Visual |
| 4 | Click "Open" / "Get Info" → modal open, Identity section render | Visual |
| 5 | Spec content section query OK → render Material/Print/Diecut/Finishing | Visual |
| 6 | Drawings section render placeholder vàng | Visual |
| 7 | Audit trail section query OK → list events latest first | Visual |
| 8 | Lỗi query không freeze circuit (try-catch + error banner) | Visual |
| 9 | Mutation items disabled với tooltip "Coming soon" | Visual |
| 10 | Build mas vùng cấm intact | git diff KHÔNG đụng Machine/ProductionLog/4 NPI tab khác/SpecHub |

## Out of scope (PR #30+ scope, KHÔNG làm ở PR #29)

- Edit / Copy / Revise / Trash / Restore actions (PR #30)
- Drawing upload / 3-role approval (PR #31/#32)
- QC plan UI (Phase 9)
- Audit emit for Open/View events (chỉ display, không log)
