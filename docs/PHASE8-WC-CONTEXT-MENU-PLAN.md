# PHASE 8 (mở đợt) — Work Center: Right-click context menu + Machine Info modal

> Follow-up từ Phase 7 close-out. Bổ sung tính năng right-click cho row
> Work Center theo mẫu CMES SpecContextMenu (Open/Copy/Revise/Trash/Get Info).
> Em **tự thiết kế** layout thông tin máy theo kinh nghiệm MES — anh xem qua
> PR diff + thử trên UI; muốn chỉnh em sẽ nhả PR mới.

---

## 1) Menu design (5 items, mirror CMES dark theme)

| # | Item | Shortcut | Action | RBAC |
|---|---|---|---|---|
| 1 | **Open** | ⌘O | Mở Info modal (read-only rich detail) | NpiRead (mọi role) |
| 2 | **Edit** | ⌘E | Mở Edit modal — chỉnh 6 cột current | Admin/Engineer |
| 3 | **Copy** | ⌘D | Mở Edit modal pre-filled, Code rỗng (operator điền) | Admin/Engineer |
| 4 | _separator_ | | | |
| 5 | **Toggle Active** | ⌘T | Confirm dialog → flip Active. Text động: "Deactivate" / "Activate". | Admin/Engineer |
| 6 | **Get Info** | ⌘I | Cùng modal Open (alias để parity screenshot) | NpiRead |

KHÔNG có "Move to Trash" vì WC dùng cờ `Active` (đã có sẵn từ Phase 7 hạng mục 5 — flip Active=false ≡ soft trash).

---

## 2) Info modal layout — em thiết kế (kinh nghiệm MES shop-floor)

```
┌─ Work Center: ACNC3 ──────────────────────── [×]
│
├─ 📋 Identity ─────────────────────────────────
│   Code           ACNC3
│   Description    CNC-3 Heads
│   Area           [CNC] (colored badge)
│   Status         [Active] (green badge)
│
├─ ⚡ Capacity ──────────────────────────────────
│   Ideal Speed    1,200 pcs/h
│   Shift          A+B+C
│   Working hrs    24 hrs/day
│   Daily capacity 28,800 pcs   (= speed × hrs)
│   Monthly est.   ~864,000 pcs  (× 30 days)
│
├─ 🛠️ Routing usage (computed từ RoutingOperations) ─
│   Ops linked          156 operations
│   Distinct part nos   42 parts
│   Avg Mach Setup      2.30 hrs
│   Avg Labor Setup     0.85 hrs
│   Avg Mach Run        0.05 (factor)
│   Top 5 parts:
│     • 80644961S  ×12 ops
│     • 30030130   ×8 ops
│     • 80643750S  ×6 ops
│     • ...
│
├─ 📅 Audit trail ────────────────────────────────
│   Created       2026-05-28 by sys
│   Last update   2026-06-01 by admin
│
└──── [Close] [Edit] ─────────────────────────────
```

### Lý do em pick các section này:

1. **Identity** — operator scan nhanh "đây là máy gì". 4 field cơ bản.
2. **Capacity** — planner cần biết throughput để schedule WO. Daily/Monthly computed
   nhằm planner KHÔNG phải tính nhẩm. Working hrs depend ShiftPattern:
   - A → 8h | B → 8h | C → 8h | A+B → 16h | A+B+C → 24h
3. **Routing usage** — engineer cần biết WC này được dùng cho operation nào,
   bao nhiêu part khác nhau. Avg setup/run times là **diagnostic signal**: cao
   bất thường = có thể optimize. Top 5 parts giúp tìm "anchor product" của cell.
4. **Audit trail** — compliance + debugging "ai sửa lần cuối".

### Em CHỦ Ý KHÔNG thêm:

- **Production logs (last 30d)** — ProductionLog FK đến `MachineId`, không phải
  `WorkCenterId`; Machine entity hiện chưa link với WorkCenter (mapping chưa
  có). Defer khi business confirm mapping convention.
- **OEE estimate** — cần ProductionLog data + downtime, vẫn dependent ở trên.
- **Linked Spec parameters** — Spec không reference WC trực tiếp.
- **Maintenance schedule / next PM** — feature chưa có entity.

→ Section "Recent production" placeholder với note "Mapping Machine ↔ WC
chưa cấu hình; ProductionLog data sẽ enable sau Phase 8.x".

---

## 3) Implementation breakdown

### 3.1 Backend (Application layer)

```csharp
// NpiService new methods
WorkCenterDetailDto?    WorkCenterDetailAsync(long id)
WorkCenter              UpdateWorkCenterAsync(long id, UpdateWorkCenterRequest, user)
WorkCenter              CopyWorkCenterAsync(long srcId, string newCode, user)
WorkCenter?             SetActiveAsync(long id, bool active, user)
```

### 3.2 DTOs

```csharp
public record WorkCenterDetailDto(
    WorkCenter Row,
    WorkCenterUsageStats Usage);

public record WorkCenterUsageStats(
    int OpCount,
    int DistinctPartCount,
    double? AvgMachSetup,
    double? AvgLaborSetup,
    double? AvgMachRun,
    List<TopPartUsage> TopParts);

public record TopPartUsage(string PartNo, int OpCount);

public record UpdateWorkCenterRequest(
    string Code, string Description, string? Area,
    double? IdealSpeedPcsH, string? ShiftPattern, bool? Active);
```

### 3.3 Audit codes (3 mới, alphabetical insert)

```csharp
public const string WcActiveToggle = "WC_ACTIVE_TOGGLE";
public const string WcCopy         = "WC_COPY";
public const string WcUpdate       = "WC_UPDATE";
```

### 3.4 UI components (new)

- `Shared/WorkCenterContextMenu.razor` — popup dark theme, positioned at
  mouse coord, click-outside dismiss, keyboard shortcut hints.
- `Shared/WorkCenterInfoModal.razor` — 4-section read-only display.
- `Shared/WorkCenterEditModal.razor` — form Edit + Copy mode (single component,
  Mode enum decides Code field behavior).

### 3.5 Page wiring `Pages/Npi/WorkCenter.razor`

- Add `oncontextmenu="ShowMenu(e, w)"` + `preventDefault` trên `<tr>` row
- Track `_menuOpenForRow` + `_menuX`/`_menuY`
- Switch action handlers gọi đúng modal

### 3.6 RBAC

- Page Authorize policy `NpiRead` (đã có).
- Context menu **luôn hiện** cho mọi role có quyền vào page.
- Items Edit/Copy/Toggle Active **disabled với explanation** nếu role khác
  Admin/Engineer (đỡ confuse user "tại sao bấm không phản ứng").
- Server-side check trong NpiService methods (defense-in-depth).

### 3.7 i18n EN+VI ~40 keys.

### 3.8 CSS:
- `.wc-context-menu` dark theme (#1f2937 bg + light text, mirror macOS Sonoma).
- `.wc-info-modal` sectioned layout với icon emoji headers.
- `.wc-edit-form` reuse pattern từ CreateSpecModal CSS.

---

## 4) Vùng cấm

- Spec / IQC / Routing / RawMaterials / Structure / Settings — không đụng.
- **Machine entity + ProductionLog** — không đụng (decision: defer mapping until business confirms).
- WorkCenter entity schema — KHÔNG thêm field, KHÔNG migration.
- About.razor count — không đụng.

---

## 5) Files touched (estimate)

| File | Δ |
|---|---|
| `src/CCL.MES.Domain/Audit/AuditAction.cs` | +3 const |
| `src/CCL.MES.Application/Dtos.cs` | +4 records |
| `src/CCL.MES.Application/Services/NpiService.cs` | +~120 LOC (4 methods) |
| `src/CCL.MES.Web/Shared/WorkCenterContextMenu.razor` | NEW ~80 LOC |
| `src/CCL.MES.Web/Shared/WorkCenterInfoModal.razor` | NEW ~170 LOC |
| `src/CCL.MES.Web/Shared/WorkCenterEditModal.razor` | NEW ~180 LOC |
| `src/CCL.MES.Web/Pages/Npi/WorkCenter.razor` | +~80 LOC (wire) |
| `src/CCL.MES.Web/Resources/SharedResource.{resx,vi.resx}` | +~40 keys × 2 |
| `src/CCL.MES.Web/wwwroot/css/site.css` | +~70 LOC |
| `docs/PHASE7-CLOSEOUT-REPORT.md` | NEW (deferred từ PR #26) |

**Estimated total**: ~1,000 LOC + tests/styling.
