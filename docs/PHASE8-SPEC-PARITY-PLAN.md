# PHASE 8 — Engineer Spec Library PARITY với SpecHub

> Khảo sát-only. KHÔNG code, KHÔNG branch. Pivot từ PR #30 lifecycle sang
> ưu tiên list-view parity (giống cách Phase 7 parity NPI Data tabs).
>
> Tham chiếu SpecHub READ-ONLY:
> - `/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/SpecHub/spechub-prototype.html`
>   - Toolbar HTML: `HTML:7691-7727`
>   - Render list function `renderOneCList`: `HTML:12929-13019`
>   - 14 cột th: `HTML:12961-12976`
>   - Sample spec object shape: `HTML:8560-8634`
> - `/Volumes/Macintosh Data/Claude-Cowork/3. PROJECTS/SpecHub/screenshots/07-1c-spec-library.png`
>   (empty state — confirm title "NPI Spec Library" + 3 toolbar buttons + search placeholder)
> - SpecHub là JS/Node → CCL-MES dịch pattern sang .NET/Blazor, KHÔNG copy code thô.

---

## 1. SpecHub list view — extract chính xác

### 1.1 Title bar + toolbar

| Element | SpecHub | Hiện CCL-MES (sau PR #29) |
|---|---|---|
| Tab nav label | "NPI Spec" (sidebar with `1C` icon) | "Engineer Spec" |
| Breadcrumb | `DATABASE / NPI SPEC · N SPECS IN LIBRARY` | `DATABASE / SPEC` |
| Page title | **NPI Spec Library** | "Engineer Spec" |
| Row count chip | `N specs` (next to title) | `N rows` |
| Search box | `Search by customer / part no / part name...` (3 fields) | `Search by Spec Code / Title / Product / Approved By / Status…` (5 fields) |
| Toolbar btn 1 | **+ Create new spec** (primary blue, opens planner-category modal) | **+ Create Spec** (basic modal Phase 7) |
| Toolbar btn 2 | **↻ Refresh samples** (re-parse 6 xlsx) | (none) |
| Toolbar btn 3 | **🗑 Trash** (count badge, opens trash modal) | (none) |
| Detail-only btn 4-6 | Export CSV / Export Excel / Print PDF | (none) |

### 1.2 14 cột (đúng thứ tự SpecHub list)

| # | SpecHub col | Width | Source field | Render |
|---|---|---|---|---|
| 1 | `#` | 50px | computed row index | center, numeric |
| 2 | `Planner` | 110px | `spec.category` → SPEC_CATEGORIES lookup | colored badge (SILK red / FLEXO blue / LETTER brown / INDIGO teal / DIECUT purple / UNKNOWN gray) |
| 3 | `REF NO` | 140px | `spec.refNo` (e.g. `CCL-Silk-19235`) | bold, color matches planner |
| 4 | `Customer` | 140px | `spec.customer` (e.g. `PANASONIC · Japan`) | bold |
| 5 | `Part No` | 170px | `spec.code` (e.g. `AWW0146C98C0-WC0`) | monospace (IBM Plex Mono) |
| 6 | `Part Name` | 240px | `spec.description` (e.g. `Panel Face B — automotive...`) | regular |
| 7 | `Colors` | 70px | `spec.printRows.length` / `spec.flexoData.inkRows.length` | center, bold |
| 8 | `Cavity` | 70px | `spec.printParams.printingCavity` / `firstPrint.plateCavity` | center |
| 9 | `Pitch` | 70px | `spec.printParams.lengthPitch` / `firstPrint.pitch` (mm) | center |
| 10 | `Spec` | 80px | `spec.inspectionLevel` (e.g. `A166`, `A`, `B`) | center |
| 11 | `Status` | 100px | `spec.approvalStamp` (DRAFT/Approved/Superseded) | badge (3-state) |
| 12 | `Rev` | 60px | `spec.rev` (A/B/C) | center, bold |
| 13 | `Rev Date` | 110px | `lastRev.date` (latest entry in `spec.revisions[]`) | date |
| 14 | `By` | 110px | `lastRev.by` (author of latest revision) | text |

Plus interactivity:
- Double-click row → open detail sheet
- Right-click row → context menu (CCL-MES đã có từ PR #29)
- Highlight on hover

### 1.3 Detail sheet (silkscreen template, e.g. AWW0146C98C0-WC0)

Cấu trúc theo `productInfo` + `printParams` + `printRows` + `revisions` + `signatures`:

1. **Header** — REF NO + Customer + Part No + Part Name + inspectionLevel + approvalStamp watermark
2. **Product Information** — partName / materialType / materialSize / laminationTape / laminationTapeSize / laminationCavity
3. **Print Parameters** — printingCavity / lengthPitch / productSize / diameter
4. **Print Process · 10 colors** — table 9 rows × 17 cols (no/surface/color/inkName/inkCode/maker/retarder/visc/speed/squeegee/dry/temp/time/uv/emulsion/size/mesh/angle/plateCode/control/remark)
5. **Color Codes legend** — `[YS, Yellow Sharp · Vàng đậm], [BS, Blue Sharp · Xanh đậm]...`
6. **Dry Codes legend** — `[ND, Natural Dry], [DR, Dry Room]...`
7. **Spec Remarks** — free text
8. **Revision History** — table rev/content/date/by
9. **Approval Signatures** — 4 roles: Người tạo R&D / Xác nhận R&D / Xác nhận PD / Xác nhận QA
10. **Change Log** — append-only audit per spec (currently localStorage; CCL-MES dùng AuditLog table per Q9 PR #29)

---

## 2. Mapping SpecHub column ↔ ProductRevision schema (PR #28+#29)

### 2.1 Bảng mapping (14 cột)

| # | SpecHub col | CCL-MES field hiện có | Gap? |
|---|---|---|---|
| 1 | `#` | computed row idx | ✅ — đã render từ PR #28 |
| 2 | `Planner` | **DERIVE** từ `SpecPrint.ProcessCode` → `ProcessCatalog.Category` (lookup) | ⚠️ — cần helper `categoryToPlanner()` mapping {SILKSCREEN→SILK, FLEXO→FLEXO, LETTERPRESS→LETTER, INDIGO/INDIGO_PRIMER→INDIGO, FLATBED_CUT/ROTARY_CUT/RDC/POWERPUNCH/CNC/LASER_CUT/KISS_CUT→DIECUT, else UNKNOWN}. KHÔNG cần field mới. |
| 3 | `REF NO` | **THIẾU** | ❌ **Gap — ADD `ProductRevision.RefNo`** (string?, nullable, max 64). Distinct với `SpecCode` (internal) — `RefNo` là customer-facing reference. |
| 4 | `Customer` | `Product.Customer.Name` (via Include) | ⚠️ — DTO `ProductRevisionListItem` chưa include CustomerName. ADD vào DTO + Include trong `SpecsAsync`. |
| 5 | `Part No` | `Product.ProductCode` | ✅ — đã có trong DTO |
| 6 | `Part Name` | `ProductRevision.Title` | ✅ — đã có. SpecHub `description` field gần ngữ nghĩa với CCL-MES `Title` (full spec descriptor). |
| 7 | `Colors` | `SpecPrint.NumColors` | ✅ — đã có trong entity. DTO chưa expose → ADD. |
| 8 | `Cavity` | **THIẾU** | ❌ **Gap — ADD `SpecPrint.Cavity`** (int?, nullable). |
| 9 | `Pitch` | **THIẾU** | ❌ **Gap — ADD `SpecPrint.PitchMm`** (double?, nullable, đơn vị mm). |
| 10 | `Spec` (inspectionLevel) | **THIẾU** | ❌ **Gap — ADD `ProductRevision.InspectionLevel`** (string?, nullable, max 32, e.g. "A166", "A", "B"). |
| 11 | `Status` | `ProductRevision.Status` | ✅ — render via badge (đã có từ PR #28). Map 5-state → SpecHub 3-state display: Draft/InReview→DRAFT; Approved/Released→APPROVED; Superseded→Superseded. |
| 12 | `Rev` | `ProductRevision.RevisionCode` | ✅ — đã có |
| 13 | `Rev Date` | `BaseEntity.UpdatedAt` (fallback `CreatedAt`) | ⚠️ — DTO chưa expose. ADD `LastUpdatedAt`. Trade-off Q5. |
| 14 | `By` | `BaseEntity.UpdatedBy` (fallback `CreatedBy`) | ⚠️ — DTO chưa expose. ADD `LastUpdatedBy`. |

### 2.2 Gap fields cần migration (4 cột mới)

| Entity | Field | Type | Nullable | Lý do |
|---|---|---|---|---|
| `ProductRevision` | `RefNo` | string | yes | Customer-facing reference (e.g. `CCL-Silk-19235`); distinct với SpecCode internal |
| `ProductRevision` | `InspectionLevel` | string | yes | Quality inspection grade (e.g. `A166`) — column `Spec` của SpecHub |
| `SpecPrint` | `Cavity` | int | yes | Print cavity count |
| `SpecPrint` | `PitchMm` | double | yes | Print pitch mm |

**KHÔNG cần field mới cho Planner** — derive từ `SpecPrint.ProcessCode` qua helper function.

**Migration scope**: ADD 4 columns trên 2 tables. Provider-agnostic (string/int/double mapping standard). Low risk — 1 ProductRevision baseline có sẵn, populate NULL cho tất cả existing rows.

### 2.3 Bảng mapping detail sheet ↔ CCL-MES (sau PR #29)

| SpecHub section | PR #29 SpecDetailModal section | Sub-spec entity | Gap |
|---|---|---|---|
| Header (REF NO + Customer + Part No + Name + inspectionLevel + approvalStamp) | 📋 Identity | ProductRevision + Product + Customer | Sau khi ADD RefNo + InspectionLevel: full parity |
| Product Information (partName/material...) | 📝 Spec content > Material | SpecMaterial (substrateType/Brand/thickness/liner/adhesive...) | ✅ field-by-field cover khi populate; UI hiện đã render |
| Print Parameters (cavity/pitch/size) | 📝 Spec content > Print | SpecPrint (cavity, pitchMm, NumColors, processCode...) | Sau khi ADD Cavity+PitchMm |
| Print Process · 10 colors table | 📝 Spec content > Print > params | SpecPrint.ColorSpecJson (PR #28 đã store) | ⚠️ — full 17-col print table chưa được render đầy đủ (Phase 9 scope) |
| Color Codes legend / Dry Codes legend | (chưa có) | SpecPrint.ExtraJson hoặc field mới | ❌ Defer Phase 9 |
| Spec Remarks | (chưa có) | SpecPrint.ExtraJson hoặc field mới `Remarks` | ❌ Defer next PR (optional) |
| Revision History table | 📅 Audit trail (currently AuditLog query) | AuditLog WHERE TargetType='ProductRevision' | ✅ — query đã có từ PR #29; format khác SpecHub (sự kiện SPEC_REVISE PR #N+ sẽ enrich) |
| Approval Signatures (4 roles) | (chưa có) | ProductRevision ApprovedBy + ApprovedAt single signature only | ❌ Defer Phase 9 (cần model `SpecApprovalChain` ≥ 4 entries) |
| Change Log | 📅 Audit trail | AuditLog | ✅ — same as Revision History |

---

## 3. Toolbar functions — phân loại PR

| Function | SpecHub | PR đề xuất | Lý do |
|---|---|---|---|
| **+ Create new spec** (planner category + xlsx import + manual entry) | Có (modal Silkscreen/Flexo/Letter/Indigo/Diecut + SheetJS xlsx parse) | PR #31 (sau PR list-view) | Phức tạp: 5 category × per-template xlsx cell mapping; cần SheetJS port → ClosedXML/OfficeOpenXml |
| **+ Create Spec** (basic — đã có Phase 7) | (none — Phase 7 đơn giản) | Đã có | Giữ form đơn giản cho đến khi PR #31 thay |
| **↻ Refresh samples** | Có (re-parse 6 xlsx từ `Data/Specs/`) | PR #31 (cùng Create new spec — đều dùng xlsx parser) | Phụ thuộc xlsx parser |
| **🗑 Trash** | Có (badge count, modal list trashed) | PR #N (lifecycle ops — đã plan ở PR #30 cũ) | Tách riêng — đã có schema soft-delete từ PR #28 |
| **Export CSV** | Có (detail view only) | PR #32 (export — sau list-view) | Server-side, format đơn giản |
| **Export Excel** | Có (detail view only) | PR #32 (cùng Export CSV) | Server-side, ClosedXML/OfficeOpenXml |
| **Print/PDF** | Có (browser print dialog) | PR #32 | Client-side print stylesheet |

---

## 4. Đề xuất chia PR (em đề xuất)

| PR | Scope | Effort | Migration? |
|---|---|---|---|
| **PR #30 (pivot)** | **LIST view parity** — 14 columns đúng SpecHub + freeze sticky thead + Columns toggle + search "by customer / part no / part name" + title "NPI Spec Library" + row count chip + breadcrumb update + Planner badge palette + Status badge 3-state map + Rev Date/By columns from BaseEntity. ADD 4 fields migration A→B→C SAFE. | M (~1,500 LOC + migration) | ✅ ADD 4 fields |
| PR #31 | Create-new-spec modal với category planner picker (SILK/FLEXO/LETTER/INDIGO/DIECUT) + xlsx import per category template + Refresh samples button + 6 sample specs trong `Data/Specs/` | L (~2,500 LOC + xlsx parser port từ SheetJS sang ClosedXML/OfficeOpenXml) | ❌ |
| PR #32 | Export CSV / Export Excel / Print PDF (server-side) | M (~1,200 LOC) | ❌ |
| PR #33+ | Detail sheet full layout (Product Info / Print Process 10-col / Color/Dry Codes / Spec Remarks / Approval Signatures 4-role / Revision History) | L (~2,000 LOC + có thể migration cho SpecApprovalChain + Remarks field) | có thể |
| PR (was #30) | Lifecycle ops (Revise + Copy + Edit + Trash/Restore + Supersede + Purge) | M-L (~1,900 LOC) | ❌ (schema #28 đã đủ) |

**PR #30 pivot là điểm khởi đầu** — sau khi list view khớp SpecHub, mọi PR sau xây dựng trên cùng UI shell (toolbar + 14 cột) + chỉ thêm buttons/modals.

---

## 5. Q1..Qn — chốt semantics

| Q | Default em đề xuất | Trade-off |
|---|---|---|
| **Q1 Planner column source** | DERIVE từ `SpecPrint.ProcessCode` qua helper `categoryFromProcessCode()` mapping vào ProcessCatalog.Category enum. KHÔNG ADD field. | (a) Single source of truth, mọi update ProcessCode tự reflect Planner. (b) Trade: cần SpecPrint loaded để derive — vẫn cần Include trong query. Alt: store Planner string → faster filter, nhưng risk lệch ProcessCode. |
| **Q2 Migration scope** | ADD 4 columns: `ProductRevision.RefNo` + `ProductRevision.InspectionLevel` + `SpecPrint.Cavity` + `SpecPrint.PitchMm`. A→B→C SAFE. | Minimum cần cho 14-col parity. Defer Cavity/Pitch để render "—" lúc đầu → mất parity. Em chọn đầy đủ ngay từ PR #30. |
| **Q3 Customer column source** | Join `Product.Customer.Name` via `Include(p => p.Product).ThenInclude(c => c.Customer)`. DTO ADD `CustomerName`. KHÔNG denormalize. | Denormalize → faster query + duplicate data. Em chọn join — Customer entity đã có. |
| **Q4 Part Name source** | `ProductRevision.Title` (descriptor cấp spec, đa dạng giữa rev) | Alt: `Product.Name` (master, không đổi giữa rev). SpecHub `description` chính là Title của ProductRevision (mỗi spec mô tả riêng). |
| **Q5 Rev Date / By source** | `BaseEntity.UpdatedAt` + `UpdatedBy` (fallback `CreatedAt`/`CreatedBy` nếu UpdatedAt null) | Alt: ADD `LastRevisedAt`/`LastRevisedBy` riêng — thừa, BaseEntity đã track. SpecHub `lastRev.date` semantic = "last touched" = đúng UpdatedAt. |
| **Q6 Search field scope** | 3 fields per SpecHub: `Customer.Name` + `Product.ProductCode` + `ProductRevision.Title` (placeholder: "Search by customer / part no / part name") | Narrower than current (Phase 7 — 5 fields). Lý do: match SpecHub UX exactly. Nếu cần broader search → keep 5 fields nhưng đổi placeholder ngắn gọn. |
| **Q7 Toolbar buttons scope PR #30** | Chỉ render: title + row count + search + Columns toggle + `+ Create Spec` (giữ Phase 7 form). DEFER `Refresh samples` + `Trash` + `Export` (button KHÔNG hiện trên list view). | Alt: render placeholder buttons (disabled "Coming soon"). Em chọn ẨN — visual cleaner, KHÔNG promise prematurely. |
| **Q8 Sticky header + frozen first col** | Reuse `rt-*` pattern Phase 7 — `thead` sticky top:0, frozen first col (`#`) optional. | Mirror Structure/Routine/RawMaterials/WC pattern. Pattern đã chuẩn hóa — KHÔNG reinvent. |
| **Q9 Empty state** | Giữ wording hiện tại (CCL-MES tone: "No Spec found. Click + Create Spec to add one."). DEFER sample loader button (load 6 sample specs) sang PR #31 cùng xlsx parser. | Alt: include sample loader stub — confusing nếu không hoạt động. |
| **Q10 Breadcrumb update** | Change `DATABASE / SPEC` → `DATABASE / NPI SPEC · N SPECS IN LIBRARY` (match SpecHub exactly) | Alt: keep current "Engineer Spec". Em chọn match SpecHub vì pivot mục đích parity. |
| **Q11 Page title bar** | "NPI Spec Library" (match SpecHub) thay vì "Engineer Spec" | Sidebar nav label vẫn "Engineer Spec" (giữ pattern Phase 7) — chỉ page title đổi. Hoặc đổi sidebar luôn? Em đề xuất GIỮ sidebar "Engineer Spec" + page title "NPI Spec Library" (giảm churn navigation). |
| **Q12 Planner badge palette** | Match SpecHub `SPEC_CATEGORIES.color`: SILK=`#c8102e` (red) / FLEXO=`#0033a0` (blue) / LETTER=`#7c2d12` (brown) / INDIGO=`#00897b` (teal) / DIECUT=`#9333ea` (purple) / UNKNOWN=`#6b7280` (gray). | Background `${color}1a` (10% alpha tint) + color border + dark text. CSS `.spec-planner-tag.spec-planner-{slug}` mirror `.wc-area--{slug}` palette PR #26. |
| **Q13 Status badge map (5→3 state)** | Map: Draft+InReview→DRAFT (amber) · Approved+Released→APPROVED (green) · Superseded→Superseded (gray strike) | SpecHub chỉ 3 state; CCL-MES 5. Display collapse semantic gần nhất. Underlying `Status` enum giữ 5-state cho lifecycle/filter chính xác. |
| **Q14 Column visibility default (14 cols)** | Default visible TẤT CẢ 14 cột; Columns toggle persist localStorage `cclmes.engineer-spec.columns-hidden.v1` (giữ key hiện tại). | Operator có thể ẩn cột không cần. PR #30 KHÔNG đổi key để giữ persisted prefs từ PR #28. |
| **Q15 Hard constraint i18n** | EN + VI parity. ADD ~25 keys mới (cột labels + planner display names + status badge labels + breadcrumb format string). VI: dịch operator-friendly (Mã tham chiếu / Khách hàng / Số sản phẩm / Tên sản phẩm / Số màu / Bộ cắt / Bước / Mức kiểm / Phiên bản / Ngày sửa / Người sửa). | — |
| **Q16 Search behavior** | Server-side LIKE `%search%` trên 3 fields với OR — case-insensitive (default EF Like collation). Debounce client-side 300ms (hiện chưa có, ADD). | Alt: client-side filter — nhanh nhưng giới hạn page-size. Server-side phù hợp với volume tương lai. |

---

## 6. Verify gates (sau khi code)

| # | Check | Method |
|---|---|---|
| V1 | dotnet build clean | 0 errors/warnings |
| V2 | Migration A→B→C SAFE: backup SHA256 + /tmp isolated + verify counts unchanged + 1 ProductRevision migrated với RefNo/InspectionLevel NULL + 1 SpecPrint với Cavity/PitchMm NULL | A→B→C |
| V3 | Live row counts intact: Structure 20530 / Routine 38441 / RawMat 2127 / WC 43 / IQC 3 / WO 1 / ProductRevisions 1 / SpecPrints 1 / ProcessCatalogs 17 / Users 5 + new fields NULL on baseline | Pre-test snapshot |
| V4 | Grid render 14 cột đúng thứ tự + nhãn SpecHub + freeze sticky thead | Manual UI |
| V5 | Search "by customer / part no / part name" → filter đúng 3 fields | Manual UI |
| V6 | Planner badge derive từ ProcessCode đúng category | sqlite: SILKSCREEN→SILK; FLEXO→FLEXO; LASER_CUT→DIECUT |
| V7 | Status badge map 5→3 state đúng | sqlite check baseline (Approved → green APPROVED) |
| V8 | Columns toggle persist localStorage | Manual UI + reload |
| V9 | Vùng cấm intact | git diff scope |
| V10 | Restart no-op | Boot server 2 lần, counts unchanged |

---

## 7. Hard constraints

- ✅ Table title + 14 cột GIỐNG SpecHub (đúng nhãn, đúng thứ tự per §1.2)
- ✅ Freeze header sticky thead bắt buộc + Columns toggle persist localStorage (reuse `rt-*` pattern Phase 7)
- ✅ Dịch pattern SpecHub JS → .NET/Blazor, KHÔNG copy code thô
- ✅ Migration A→B→C SAFE (provider-agnostic, isolated /tmp test, backup SHA256, verify 1 baseline)
- ✅ Render từ entity grid (DTO `ProductRevisionListItem` mở rộng) — KHÔNG re-fetch by id (bài học hotfix #27)
- ✅ i18n EN/VI; RBAC `NpiSpecRead` xem; mutation Admin/Engineer (Create Spec giữ Phase 7)
- ✅ SpecHub READ-ONLY tuyệt đối
- ✅ KHÔNG đụng: Ops Control v1.2 / CMES sibling / Old ver / Machine / ProductionLog / 4 NPI tab khác (Structure/Routine/RawMat/WC) / IQC entity + FK

---

## 8. Out of scope (defer)

- Create new spec modal với category planner picker + xlsx import → PR #31
- Refresh samples (6 sample xlsx) → PR #31 cùng xlsx parser
- Trash sub-view + auto-purge HostedService → PR lifecycle (đẩy về sau)
- Export CSV / Excel / Print PDF → PR #32
- Detail sheet full layout (Print Process 10-color table / Color Codes / Dry Codes / Approval Signatures 4-role / Spec Remarks) → PR #33
- Lifecycle ops (Revise/Copy/Edit/Supersede) → PR sau

---

## 9. Estimated PR #30 scope

| File | Change | LOC est |
|---|---|---|
| Migration `AddSpecListViewParityFields.cs` | ADD 4 columns (ProductRevision.RefNo + InspectionLevel + SpecPrint.Cavity + PitchMm). Provider-agnostic. | ~150 |
| `Domain/Entities/Spec.cs` | ADD 4 properties | ~+15 |
| `Application/Dtos.cs` | Update `ProductRevisionListItem` — ADD CustomerName, RefNo, InspectionLevel, NumColors, Cavity, PitchMm, Planner, LastUpdatedAt, LastUpdatedBy (9 new fields) | ~+40 |
| `Application/Services/SpecService.cs` | Update `SpecsAsync` — Include `Product.Customer`, search 3 fields, return enriched DTO. ADD helper `CategoryFromProcessCode` static. | ~+80 |
| `Web/Pages/Npi/EngineerSpec.razor` | Rewrite grid 8 → 14 columns đúng SpecHub thứ tự + nhãn + width + Planner badge render + Status badge 3-state map + Rev Date/By format | ~+200 (modify) |
| `Web/Resources/SharedResource.{resx,vi.resx}` | +25 keys mới EN/VI (column labels + planner names + breadcrumb + search placeholder) | ~+50 |
| `Web/wwwroot/css/site.css` | ADD `.spec-planner-tag.spec-planner-{slug}` palette (6 colors) + sticky thead reaffirm (rt-* đã có) | ~+60 |
| `docs/PHASE8-PR30-PLAN.md` | Renamed/replaced — short scope doc | ~150 |

**Tổng ước tính**: ~750 LOC + migration. Size M.

---

## 10. STOP — chờ duyệt

Em sẽ KHÔNG tạo branch / KHÔNG code cho đến khi anh:
1. Duyệt scope tổng (LIST view parity 14 cột + 4 field migration + toolbar minimal)
2. Chốt Q1-Q16 (hoặc accept default em đề xuất)
3. Confirm PR split (PR #30 = list view; PR #31 = Create modal + xlsx; PR #32 = Export; PR #33 = Detail sheet; PR lifecycle = đẩy về sau)

Sau khi chốt, em sẽ tạo branch `feat/phase8-spec-list-parity` (hoặc tên anh chọn), code theo plan này, A→B→C SAFE migration, verify đầy đủ V1-V10, commit + PR + STOP chờ duyệt.
