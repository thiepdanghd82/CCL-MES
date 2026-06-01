# PHASE 8 DETAIL SHEET PLAN — SpecHub-parity spec sheet (web + PDF)

> Khảo sát-only. KHÔNG code, KHÔNG branch. STOP sau plan, chờ duyệt + chốt Q.
>
> Nâng `SpecDetailModal` (PR #29 — 4-section minimal: Identity/Material/Print/
> Diecut/Finishing + Drawings placeholder + Audit) lên parity với SpecHub spec
> sheet đầy đủ (PANASONIC screenshot reference). Có thể đụng schema (Approval
> Signatures 4-role) → nếu cần migration thì A→B→C SAFE additive nullable
> cross-provider.
>
> Tham chiếu SpecHub READ-ONLY:
> - `renderSilkscreenSpec` (HTML:10269-10497) — 8 section silk doc layout
> - `renderFlexoSpec` (HTML:10510-10770) — 8 section flexo doc layout với 3
>   sub-table (printing/cutting/ink)
> - `SpecPdfDocumentBuilder.BuildEmpty` (PR #31c, đã stub sẵn cho extend này)
> - PR #29 `SpecDetailModal.razor` — base codepath không thay thế, chỉ extend

---

## 1. Section map SpecHub spec sheet → CCL-MES entity

| # | SpecHub Section | Silk fields | Flexo fields | CCL-MES entity / DTO | Gap? |
|---|---|---|---|---|---|
| 1 | **Doc header** | Company name + SILK (planner label) + REF NO + Inspection Level + Approval Stamp | Company + SEAL + REF NO + Inspection + Stamp | `ProductRevision.RefNo` (PR #30) + `Planner` derived + `InspectionLevel` + `Status` | ✅ Có đủ |
| 2 | **Compliance strip** | "HSF strict control · Spec A126 · RoHS Compliance" chips | giống silk | Hard-coded mảng SpecHub (`certifications`). Em đề xuất CCL-MES derive: `HSF strict control` (fixed) + `Spec {InspectionLevel}` + `RoHS Compliance` (fixed) — KHÔNG cần ADD field | ✅ Derive |
| 3 | **Product Information** | Customer / PartNo / PartName / MaterialType / MaterialSize / LaminationTape / LaminationSize / LaminationCavity (8 cols) | Customer / PartNo / PartName / Version / ProductSize / Diameter (6 cols) | `Product.Customer.Name` + `Product.ProductCode` + `Product.Name` + `SpecMaterial.SubstrateType` + `SpecMaterial.ExtraJson` (material_size/lamination_*) | ✅ Có (silk lamination từ ExtraJson PR #31a; flexo Version cần thêm `ProductRevision.VersionLabel`?) — xem Q5 |
| 4 | **Print Parameters** | Cavity / LengthPitch / ProductSize / Diameter + Squeegee codes legend + Drying codes legend (hard-coded SpecHub) | _Không có separate Print Params block — fold vào Printing Information table_ | `SpecPrint.Cavity` (PR #30) + `SpecPrint.PitchMm` (PR #30) + `ProductSizeW/H` từ Material.ExtraJson OR `SpecDiecut.WidthMm/LengthMm` (gap — xem Q6) + Diameter | ⚠ Silk ProductSize gap: xlsx parser PR #31a có ParsedSpec.ProductSizeW/H NHƯNG SaveAsync KHÔNG persist (chỉ dùng cho preview). Xem Q6. |
| 5 | **Print Process 10-color table** (silk) | 21 cols: No / Surface / Color (+swatch) / InkName / InkCode / Maker / Retarder / Visc / Speed / Squeegee / Dry / Temp / Time / Uv / Emulsion / Size / Mesh / Angle / PlateCode / Control / Remark | _N/A flexo_ | `SpecPrintColor` entity (PR #31a) — 20 field 1:1 mapping. Swatch lookup hard-coded PANTONE_SWATCHES → port C# hash table | ✅ Đủ (PR #31a) |
| 5b | **Flexo 3 sub-tables** (flexo) | _N/A silk_ | Printing rows (12 cols) / Cutting rows (14 cols) / Ink rows (10 cols) | `SpecPrint.ExtraJson.flexo_print_rows` (PR #31b) + `SpecFlexoCuttingRow` + `SpecFlexoInkRow` | ✅ Đủ (PR #31b). PrintingRows fold ExtraJson cần re-deserialize ở render path |
| 6 | **Remarks** | 1-col blob (silk) | 2-col (print remarks + cut remarks) | `SpecPrint.ExtraJson.remarks` (gap silk) / ExtraJson flexo (gap) | ⚠ Gap: PR #31a/b parser có capture remarks NHƯNG SaveAsync KHÔNG persist. Xem Q7. |
| 7 | **Revision History** | Table: Rev / Contents / Date / By (max 6 rows) | giống silk | `ProductRevision.ParentRevisionId` chain (PR #28) + `ChangeSummary` + `CreatedAt` + `CreatedBy`. Query phụ recursive walk lineage. | ✅ Đủ (PR #28). Cần query walk chain `ParentRevisionId → null`. |
| 8 | **Approval Signatures 4-role** | 4 boxes: R&D issued / R&D confirmed / PD confirmed / QA confirmed + Date | giống silk | **GAP** — `ProductRevision.ApprovedBy/ApprovedAt` chỉ 1 approver, KHÔNG có 4-role. Xem Q1 (option A/B/C). | ❌ GAP |
| 9 | **Change Log / Audit timeline** | _SpecHub `_oneCRenderHistoryLog` — render từ `_oneCEditHistory` localStorage_ | giống silk | `SpecService.SpecAuditTrailAsync` (PR #29) — query `AuditLog WHERE TargetType='ProductRevision' AND TargetId=...` | ✅ Đủ (PR #29) |

**Tóm tắt gap**:
- ⚠ Q5 Version (flexo) — ProductRevision không có "version" field semantic; có `RevisionCode` (A/B/C/AA…) khác semantic. Xem Q5.
- ⚠ Q6 ProductSizeW/H — parser capture nhưng KHÔNG persist. ADD 2 nullable field hoặc derive từ Material.ExtraJson?
- ⚠ Q7 Remarks — parser capture nhưng KHÔNG persist. ADD `SpecPrint.RemarksText` field hoặc fold ExtraJson?
- ❌ Q1 Approval Signatures 4-role — gap chính.

---

## 2. Q1 — Approval Signatures 4-role: 3 option

Đây là gap chính của PR. 3 option:

### Option A — Render-only (KHÔNG migration, em đề xuất default)

- Render 4-role boxes: "R&D Issued" / "R&D Confirmed" / "PD Confirmed" / "QA Confirmed"
- Map existing `ProductRevision.ApprovedBy + ApprovedAt` → "R&D Confirmed" (single approver)
- 3 boxes khác render placeholder "—" (chưa có data)
- KHÔNG add workflow ký thật — defer sang PR approval-chain (sau lifecycle PR)
- Migration: KHÔNG cần

**Pros**: 0 schema risk, ship được visual parity ngay, defer ký workflow đúng phase order (lifecycle Revise/Copy/Trash/Restore/Supersede/Purge cần ship trước approval).
**Cons**: 3/4 ô trống — operator hỏi tại sao chỉ có 1 chữ ký.

### Option B — SpecApproval child entity (MIGRATION)

```csharp
public class SpecApproval : BaseEntity {
    public long ProductRevisionId { get; set; }
    public ProductRevision? ProductRevision { get; set; }
    public ApprovalRole Role { get; set; }      // RnDIssued / RnDConfirmed / PDConfirmed / QAConfirmed
    public string? SignedBy { get; set; }       // null = chưa ký
    public DateTime? SignedAt { get; set; }
    public string? Comment { get; set; }
    public ApprovalStatus Status { get; set; }  // Pending / Signed / Rejected
}
public enum ApprovalRole { RnDIssued, RnDConfirmed, PDConfirmed, QAConfirmed }
public enum ApprovalStatus { Pending, Signed, Rejected }
```

+ Index `(ProductRevisionId, Role)` unique.
+ Migration `AddSpecApprovalEntity` — additive nullable cross-provider, A→B→C SAFE.
+ Bootstrap: khi tạo ProductRevision mới → auto-create 4 SpecApproval rows Pending.

**Pros**: schema chuẩn cho workflow ký thật. Future PR approval-chain chỉ cần UI + transition (KHÔNG migration lại).
**Cons**: PR này nặng hơn (migration + entity + service + DI). Bootstrap 4 rows mỗi revision = +4×7 PR = 28 rows trên main hiện tại (nhưng safe).

### Option C — 8 nullable fields trên ProductRevision

```csharp
public string? RndIssuedBy { get; set; }
public DateTime? RndIssuedAt { get; set; }
public string? RndConfirmedBy { get; set; }   // = legacy ApprovedBy?
public DateTime? RndConfirmedAt { get; set; } // = legacy ApprovedAt?
public string? PdConfirmedBy { get; set; }
public DateTime? PdConfirmedAt { get; set; }
public string? QaConfirmedBy { get; set; }
public DateTime? QaConfirmedAt { get; set; }
```

**Pros**: Simpler schema, no new entity, single table.
**Cons**: 8 cols wide, can't extend cho comment/rejected status, không scale với role thêm sau.

### Em đề xuất Q1 = **Option A** (render-only)

Lý do:
- PR này nặng vừa phải (~1000 LOC web + PDF) — thêm migration A→B→C + bootstrap + service mutation = +500 LOC, đẩy size lên L+.
- Lifecycle PR (Revise/Copy/Trash/Restore) chưa ship → approval workflow chưa đủ semantic infra. Approval-chain phải đi SAU lifecycle để biết khi nào reset approval (sau revise) + supersede.
- Visual parity vẫn đạt 95% (1/4 chữ ký có sẵn từ ApprovedBy/At; 3/4 placeholder + tooltip "Defer to approval-chain PR").

Khi anh sẵn sàng cho approval workflow → mở PR approval-chain riêng với Option B/C + migration. PR này (#detail-sheet) chỉ render.

---

## 3. Q2-Q7 — chốt semantics còn lại

| Q | Default em đề xuất |
|---|---|
| **Q2 — Scope: web detail + PDF cùng 1 PR hay tách?** | **Cùng 1 PR.** Lý do: `SpecPdfDocumentBuilder` (PR #31c) đã stub `BuildEmpty(title, orientation)` chính xác cho mục đích extend `BuildDetailSheet`. Tách PR = 2 round trip review + risk drift section logic. Ước lượng ~1100 LOC total. Nếu lớn quá khi build thực → split PR-A (web only) + PR-B (PDF only) on flight. |
| **Q3 — Detail view UX: modal hay full-page?** | **Full-page route** `/npi/engineer-spec/{id}` (mới). PR #29 modal-based KHÔNG scale với 8 section + 21-cột table → scroll khó + zoom in/out clumsy. Full-page route mới + back-button navigate giống SpecHub. PR #29 modal giữ nguyên cho "Get Info" quick peek; full-page mở khi double-click row hoặc click "Open" trong context menu. |
| **Q4 — PDF spec sheet trigger: nút riêng "Print spec sheet" trên detail page** | **Yes**, button toolbar trên detail view (giống list view có Export buttons). `GET /api/specs/{id}/sheet.pdf` → reuse `SpecPdfDocumentBuilder.BuildDetailSheet(content, ctx)`. Filename: `SpecSheet_<RefNo>_Rev<RevCode>_<yyyyMMdd>.pdf`. |
| **Q5 — Flexo "Version" field** | **Persist** mới: ADD `ProductRevision.VersionLabel` (nullable string). Hoặc reuse `ChangeSummary` field truncated. Em đề xuất ADD field riêng vì semantic khác (Version = customer's product version "00"/"A"; ChangeSummary = revise reason). Migration nhỏ (1 nullable field). Hoặc **defer** + render "—" trong PR này, ADD field PR sau. **Em chọn defer** — không lý do strong để migrate ngay. |
| **Q6 — ProductSizeW/H persist** | **Persist** mới: ADD `SpecPrint.ProductSizeWmm` + `ProductSizeHmm` (nullable double). Parser PR #31a/b đã capture; chỉ SaveAsync KHÔNG persist. Migration nhỏ (2 nullable field) + UpdateSaveAsync. **Em chọn ADD** — operator cần size hiển thị trong detail sheet, KHÔNG hiển thị = mất parity. |
| **Q7 — Remarks persist** | **Persist** mới: silk = ADD `SpecPrint.RemarksText` (nullable text); flexo = ADD `SpecPrint.RemarksCutText` (nullable text) — Print Remarks reuse `RemarksText`. Migration nhỏ (2 nullable text fields). Parser PR #31a/b đã capture. **Em chọn ADD**. |
| **Q8 — PANTONE swatch lookup** | Port hard-coded PANTONE_SWATCHES dict (~9 entries SpecHub HTML:10257) sang C# `Dictionary<string, string>` ở `SpecDetailColors.cs`. Future PR extend với CCL color catalog. |
| **Q9 — RBAC** | Read detail = `NpiSpecRead` (Admin/Supervisor/Engineer). Print PDF = same (read = print, pattern PR #31c). KHÔNG có mutation trong PR này (defer approval workflow). |
| **Q10 — Status display trong header** | Reuse `StatusDisplay` 5→3 map (PR #31c `SpecListColumns.StatusDisplay`). Badge styling giống list grid. |
| **Q11 — Revision history table data source** | Query phụ: walk lineage chain `ProductRevision.ParentRevisionId` cho tới NULL. Trả max 6 entries DESC. Stamp Rev letter (A/B/C/AA…) + ChangeSummary + CreatedAt + CreatedBy. |
| **Q12 — Empty state cho missing sections** | Mọi field nullable render "—". Empty rows render "<tr><td colspan='N'>— No print rows —</td></tr>" mirror SpecHub. KHÔNG hide entire section dù rỗng (consistency). |
| **Q13 — Compliance chip strip** | Derive 3 chip: `HSF strict control` (fixed) + `Spec {InspectionLevel}` (e.g., "Spec A166") + `{Compliance}` từ `SpecPrint.ExtraJson.compliance` (gap — fall back "RoHS Compliance" fixed nếu missing). |

---

## 4. Migration scope (nếu chốt Q6 + Q7 ADD)

| Field | Entity | Nullable? | Type |
|---|---|---|---|
| `ProductSizeWmm` | SpecPrint | ✓ | double |
| `ProductSizeHmm` | SpecPrint | ✓ | double |
| `RemarksText` | SpecPrint | ✓ | string |
| `RemarksCutText` | SpecPrint | ✓ | string |
| (defer Q5 Version) | — | — | — |
| (defer Q1 Approval — Option A render-only) | — | — | — |

→ Migration `AddSpecPrintDetailSheetFields` — 4 nullable fields. Additive cross-provider, A→B→C SAFE (mirror PR #30 `AddSpecListViewParityFields` pattern — KHÔNG cần `ActiveProvider` guard).

**Backfill**: KHÔNG cần. Future xlsx import (silkscreen + flexo) sẽ populate; existing 6 sample (silk DEMO_SILK_1-4 + flexo DEMO_FLEXO_1-2) cần re-refresh sau migration để pickup data (parser đã capture, chỉ persist mới enable). Em đề xuất Refresh Samples Admin button (PR #31a) tự pickup; KHÔNG cần migration data backfill.

---

## 5. PDF detail sheet — extend SpecPdfDocumentBuilder

PR #31c đã thiết kế `SpecPdfDocumentBuilder.BuildEmpty(title, orientation)` reusable. PR này ADD `BuildDetailSheet(spec, ctx)`:

```csharp
public static Document BuildDetailSheet(
    SpecDetailDto detail,           // full content + revision history + audit
    SpecExportContext context)
{
    var doc = BuildEmpty(
        title: $"Spec Sheet {detail.RefNo} Rev {detail.RevisionCode}",
        orientation: MigraDocOrientation.Portrait);  // detail sheet portrait
    var section = doc.LastSection;
    AppendDocHeader(section, detail);             // company + SILK/SEAL + REF NO
    AppendComplianceStrip(section, detail);       // 3 chip
    AppendProductInfoTable(section, detail);      // 8 cols silk / 6 cols flexo
    AppendPrintParamsTable(section, detail);      // silk only (cavity/pitch/size/diameter)
    if (detail.IsSilkscreen)
        AppendSilkPrintProcessTable(section, detail.PrintColors);  // 21 cols × N rows
    else // flexo
    {
        AppendFlexoPrintingTable(section, detail.FlexoPrintRows);   // 12 cols
        AppendFlexoCuttingTable(section, detail.FlexoCuttingRows);  // 14 cols
        AppendFlexoInkTable(section, detail.FlexoInkRows);          // 10 cols
    }
    AppendRemarks(section, detail);
    AppendRevisionHistory(section, detail.Lineage);
    AppendApprovalSignatures(section, detail);    // 4 boxes
    AppendChangeLog(section, detail.AuditEntries);
    return doc;
}
```

**Pattern**: mỗi `Append*` là 1 helper riêng (~30-50 LOC) reuse `StyleConstants` từ PR #31c. PDF render A4 portrait (detail sheet thường portrait), font Arial sans-serif fallback DejaVu.

`PdfSpecSheetExporter.cs` (mới) implement `ISpecDetailExporter` interface (mới, sibling `ISpecListExporter` PR #31c). Endpoint `GET /api/specs/{id}/sheet.pdf` với same RBAC pattern (Roles Admin/Supervisor/Engineer).

---

## 6. Web detail view — Razor page mới

`Pages/Npi/EngineerSpecDetail.razor` — `@page "/npi/engineer-spec/{revisionId:long}"`:

```razor
@page "/npi/engineer-spec/{revisionId:long}"
@attribute [Authorize(Policy = "NpiSpecRead")]
@inject SpecService Specs
@inject NavigationManager Nav
@inject IJSRuntime JS

@if (_loading) { <p>Loading…</p> }
else if (_error is not null) { <div class="alert err">@_error</div> }
else if (_detail is null) { <p>Spec not found.</p> }
else
{
    <section class="spec-sheet-wrap">
        <!-- Toolbar: Back + Print PDF + Approve (defer) -->
        <div class="spec-sheet-toolbar">
            <button @onclick="GoBack">← Back to list</button>
            <button @onclick="PrintPdfAsync" disabled="@_busyPdf">🖨 Print spec sheet PDF</button>
        </div>
        <!-- Doc header / Compliance / Product Info / Print Params /
             Print Process (silk) HOẶC Flexo 3 sub-tables / Remarks /
             Revision History / Approval Signatures / Change Log -->
        @RenderDocHeader(_detail)
        @RenderComplianceStrip(_detail)
        @RenderProductInfo(_detail)
        @if (_detail.IsSilkscreen)
        {
            @RenderPrintParams(_detail)
            @RenderSilkPrintProcessTable(_detail.PrintColors)
        }
        else
        {
            @RenderFlexoPrintingTable(_detail.FlexoPrintRows)
            @RenderFlexoCuttingTable(_detail.FlexoCuttingRows)
            @RenderFlexoInkTable(_detail.FlexoInkRows)
        }
        @RenderRemarks(_detail)
        @RenderRevisionHistory(_detail.Lineage)
        @RenderApprovalSignatures(_detail)
        @RenderChangeLog(_detail.AuditEntries)
    </section>
}
```

Navigation từ list grid: double-click row → `Nav.NavigateTo($"/npi/engineer-spec/{row.Id}")`. PR #29 modal "Get Info" giữ nguyên cho quick peek.

CSS port từ SpecHub `.spec-frame` + `.spec-block` + `.spec-block-title` + `.spec-print-table` + `.spec-signoff` etc. (~250 LOC CSS mirror).

---

## 7. Service layer — SpecDetailDto + SpecDetailAsync

Application layer thêm:

```csharp
public record SpecDetailDto(
    long Id,
    string SpecCode,
    string Title,
    string RevisionCode,
    ProductRevisionStatus Status,
    string? RefNo,
    string? InspectionLevel,
    string Planner,              // DERIVED PR #30
    string ProductCode,
    string ProductName,
    string? CustomerName,
    string ProcessCode,
    bool IsSilkscreen,           // ProcessCode in (SILKSCREEN, INDIGO/INDIGO_PRIMER)
    bool IsFlexo,                // ProcessCode = FLEXO
    // Material content
    SpecMaterialDetailDto? Material,
    // Print params (silk)
    int? PrintingCavity,
    double? LengthPitchMm,
    double? ProductSizeWmm,      // NEW Q6
    double? ProductSizeHmm,      // NEW Q6
    double? Diameter,
    // Silk print rows
    List<SpecPrintColorDto> PrintColors,
    // Flexo rows
    List<SpecFlexoPrintRowDto> FlexoPrintRows,
    List<SpecFlexoCuttingRowDto> FlexoCuttingRows,
    List<SpecFlexoInkRowDto> FlexoInkRows,
    // Remarks
    string? RemarksText,         // NEW Q7
    string? RemarksCutText,      // NEW Q7 (flexo only)
    // Lineage (revision history)
    List<RevisionLineageEntry> Lineage,
    // Approvals (Option A render-only — single ApprovedBy/At)
    string? ApprovedBy,
    DateTime? ApprovedAt,
    string? ReleasedBy,
    DateTime? ReleasedAt,
    // Audit
    List<SpecAuditEntry> AuditEntries);

public record RevisionLineageEntry(
    long Id, string RevisionCode, string? ChangeSummary,
    DateTime CreatedAt, string? CreatedBy);
```

`SpecService.SpecDetailAsync(long revisionId)` — single query trả full graph (Include Material/Print/Print.Colors/Print.FlexoCuttingRows/Print.FlexoInkRows/Product.Customer) + 2 query phụ (recursive lineage walk + audit entries).

---

## 8. Hard constraints

- ❌ Bài học #27: render trực tiếp từ entity grid → detail page nhận `revisionId` từ route, query 1 lần đầy đủ. KHÔNG re-fetch by id cho child sections.
- ❌ Try-catch wrap query async + error banner inline mỗi section.
- ❌ Migration A→B→C SAFE nếu Q6/Q7 ADD: backup + SHA256 + /tmp isolated + LIVE verify baseline + IQC 3 + vùng cấm nguyên.
- ❌ Reuse `SpecPdfDocumentBuilder` + `SpecListColumns.StatusDisplay` từ PR #31c. KHÔNG viết lại.
- ❌ i18n EN/VI cho mọi label section header + table header.
- ❌ RBAC `NpiSpecRead` cho read + PDF (read = print pattern).
- ❌ SpecHub READ-ONLY tuyệt đối; KHÔNG đụng Ops Control v1.2 / CMES / Old ver / Machine / ProductionLog / 4 NPI tab khác / IQC.

---

## 9. Verify gates (post-implementation)

| # | Check | Method |
|---|---|---|
| V1 | dotnet build clean (0 W / 0 E) | |
| V2 | Migration A→B→C SAFE (Q6/Q7 ADD fields): backup + SHA + /tmp test + LIVE verify baseline + IQC 3 + new fields nullable empty | A→B→C |
| V3 | Detail page route `/npi/engineer-spec/2` renders silk spec đầy đủ 9 section (DEMO_SILK_1) | Browser test |
| V4 | Detail page route `/npi/engineer-spec/6` renders flexo spec đầy đủ với 3 sub-table (DEMO_FLEXO_1) | Browser test |
| V5 | PANTONE swatch render đúng cho color "WN-212" (yellow), "PANTONE 186 C" (red), "DENSE BLACK", "CLEAR" (checker) | Browser test |
| V6 | Print PDF button download `SpecSheet_<RefNo>_Rev<rev>_<ts>.pdf` mở reader render đầy đủ 9 section | Manual UI |
| V7 | Revision history walk chain: rev có ParentRevisionId → render chain DESC; rev rỗng → KHÔNG crash | Browser test |
| V8 | Approval Signatures Option A render: ApprovedBy → "R&D Confirmed" box; 3 box khác "—" | Browser test |
| V9 | Empty filter case (rev không có Material/Print) → render section với "— No data —" + KHÔNG freeze circuit | Browser test |
| V10 | RBAC: anonymous → 302; QC → 403; Engineer → OK | curl + browser |
| V11 | Vùng cấm intact | git diff scope |
| V12 | Restart no-op | Boot 2 lần, refresh page → counts unchanged |
| V13 | Re-refresh samples sau migration → populate ProductSizeWmm/Hmm + RemarksText cho 6 sample | Admin refresh samples |

---

## 10. LOC estimate + PR split decision

| Component | LOC |
|---|---|
| `SpecDetailDto` + sub DTOs | ~150 |
| `SpecService.SpecDetailAsync` + RevisionLineageAsync | ~100 |
| `EngineerSpecDetail.razor` (full-page) | ~600 |
| CSS spec-frame/spec-block/spec-print-table mirror SpecHub | ~300 |
| `SpecPdfDocumentBuilder.BuildDetailSheet` + 9 helper Append* | ~500 |
| `PdfSpecSheetExporter.cs` + endpoint `/api/specs/{id}/sheet.pdf` | ~80 |
| `SpecDetailColors.cs` PANTONE swatch table | ~50 |
| Migration `AddSpecPrintDetailSheetFields` (4 nullable fields) | ~80 |
| `SpecImportService.SaveAsync` update populate new fields | ~30 |
| i18n EN+VN (~50 keys) | ~120 |
| Navigation wire (double-click route + "Open" context menu) | ~30 |

**Total**: ~2040 LOC. Size **L** (so với PR #31a 5000 LOC do parser + samples; PR #31c 1250 LOC).

### Em đề xuất **1 PR (không split)**

Lý do:
- Web + PDF chia sẻ cùng DTO + cùng layout semantic. Split = duplicate API contract + risk drift.
- 2000 LOC vẫn review được trong 1 round nếu structured (web → PDF → migration → test).
- Migration 4 field nhỏ — không lý do split chỉ vì migration.

**Alt split nếu anh muốn**:
- PR-A: Migration (4 fields) + SpecImportService populate + DTO + web detail page (1100 LOC)
- PR-B: PDF detail sheet + endpoint + button trigger (600 LOC)
- Pros: PR-A review xong là detail web ready; PR-B add PDF không block UI work.
- Cons: 2 PR ceremony + visual parity chỉ đạt sau PR-B merge.

---

## 11. Q1..Q13 chốt summary

| Q | Em đề xuất default |
|---|---|
| Q1 — Approval Signatures 4-role | **Option A — render-only** (KHÔNG migration). Workflow ký thật defer PR approval-chain (sau lifecycle). |
| Q2 — Scope PR | **1 PR cùng web + PDF** (~2040 LOC L size). Alt split A/B nếu anh muốn. |
| Q3 — Detail UX | **Full-page route** `/npi/engineer-spec/{id}`. Modal PR #29 giữ "Get Info" peek. |
| Q4 — PDF spec sheet button | **Yes** — "Print spec sheet PDF" trên detail page toolbar. Endpoint `/api/specs/{id}/sheet.pdf`. |
| Q5 — Flexo Version field | **Defer** — render "—" trong PR này. ADD field PR sau nếu operator cần. |
| Q6 — ProductSizeW/H persist | **ADD** `SpecPrint.ProductSizeWmm` + `ProductSizeHmm` nullable double. Migration nhỏ. |
| Q7 — Remarks persist | **ADD** `SpecPrint.RemarksText` + `RemarksCutText` nullable text. Migration nhỏ. |
| Q8 — PANTONE swatch | Port hard-coded ~9 entries SpecHub → `SpecDetailColors.cs` C# dict. |
| Q9 — RBAC | `NpiSpecRead` cho read + PDF (read = print). KHÔNG mutation trong PR này. |
| Q10 — Status display header | Reuse `SpecListColumns.StatusDisplay` 5→3 map từ PR #31c. |
| Q11 — Revision history data source | Walk `ParentRevisionId` chain DESC, max 6 entries. Service query phụ. |
| Q12 — Empty state | Mọi section render kể cả rỗng (consistency). Field nullable → "—". Row rỗng → "— No data —". |
| Q13 — Compliance chip strip | Derive 3 chip: "HSF strict control" (fixed) + "Spec {InspectionLevel}" + "{Compliance hoặc 'RoHS Compliance'}" fall back. |

---

## 12. STOP — chờ duyệt

Em sẽ KHÔNG tạo branch / KHÔNG code cho đến khi anh:
1. Duyệt **Q1 Approval Signatures = Option A render-only** (defer workflow ký thật)
2. Chốt **Q2 PR split = 1 PR cùng web + PDF** hay tách PR-A + PR-B
3. Chốt **Q6 + Q7 migration ADD** (4 nullable fields trên SpecPrint) — confirm A→B→C SAFE OK
4. Chốt Q3-Q5 + Q8-Q13 (hoặc accept default)

Sau khi anh chốt, em sẽ:
- Tạo branch `feat/phase8-spec-detail-sheet`
- Migration A→B→C SAFE (backup + SHA + /tmp test + LIVE) cho 4 field SpecPrint
- Update SpecImportService.SaveAsync populate new fields
- Re-refresh samples 6 file → populate Q6 + Q7 data (verify counts)
- Code SpecDetailDto + SpecDetailAsync + EngineerSpecDetail.razor full-page
- Extend SpecPdfDocumentBuilder.BuildDetailSheet + PdfSpecSheetExporter
- i18n EN/VI + CSS mirror SpecHub
- V1-V13 verify
- Commit + PR + STOP chờ duyệt.

---

## 13. Out of scope (defer)

- Approval-chain workflow (Option B/C migration + ký flow) — PR sau lifecycle
- Drawing thumbnails + approval per drawing — PR sau (PR #29 đã có placeholder)
- Edit detail inline (SpecHub draft mode) — PR sau lifecycle Revise
- Per-spec audit timeline filter UI — defer (PR #29 đã render basic)
- Print detail sheet to physical printer (browser print dialog vs server PDF) — chỉ server PDF endpoint
- LETTER/INDIGO/DIECUT dedicated detail layout — PR sau khi có sample real
- Single-spec Export CSV/Excel (export 1 spec content) — PR sau, không phổ biến request
