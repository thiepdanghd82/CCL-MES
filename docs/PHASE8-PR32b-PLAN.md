# Phase 8 PR #32b — Scan QR camera + per-WO drawer (PLAN, no code)

> **Status**: plan only. Branch chưa tạo. Đợi anh duyệt QR lib + Q1..Q12
> + HTTPS handling + drawer scope trước khi vào code.
>
> **Predecessors**: PR #32a (Shop Order list page, manual scan + LOOKUP +
> 2 sections) ✅. Manual LOOKUP đã ship — luôn là đường tin cậy.
>
> **Scope this PR**: thêm 1 nút "Scan camera" cạnh input manual ở Shop
> Order page → mở camera (getUserMedia) → decode QR client-side → khớp
> WoNo → mở **per-WO drawer** read-only (panel chi tiết). Card click cũng
> mở drawer cùng pattern. Drawer chỉ READ, KHÔNG mutation; mọi action
> WO vẫn ở `/workorders` Phase 6 table với button gating gốc.
>
> **Out of scope**: nhập decode kết quả ngược lên server, lưu QR ảnh,
> stop/start/pause/finish trong drawer, push real-time SignalR drawer
> auto-update (defer).

---

## §1. QR library — quyết định (cần anh chốt Q1)

### Khả năng

| Library | Version pin | License | Size | Camera + barcode? | Notes |
|---|---|---|---|---|---|
| **html5-qrcode** | `2.3.8` | **Apache-2.0** | ~120 KB min | ✅ QR + Code-39/93/128 + EAN | API gọn (`new Html5Qrcode(elemId).start(...)`); UI tự render khung scan box; documented cho industrial use |
| **zxing-js / @zxing/browser** | `0.1.5` | **Apache-2.0** | ~430 KB min (1.6 MB w/ all formats) | ✅ QR + 1D barcodes | Mạnh + format đa dạng nhưng size bự — kiosk RAM hạn chế thì hơi nặng |
| **jsQR** | `1.4.0` | **Apache-2.0** | ~50 KB min | QR only — KHÔNG có camera UI, phải tự `getUserMedia` + draw vào `<canvas>` + grab frame mỗi loop | Lightest nhưng phải tự dựng plumbing → +200 LOC client |
| **instascan** | — | MIT | ~280 KB | QR only | KHÔNG maintain từ 2017 — risk + bug Safari mới |

### 🌟 Khuyến nghị mặc định: **html5-qrcode 2.3.8** (Apache-2.0)

**Lý do**:
1. **License Apache-2.0** — anh chấp nhận tốt (rộng + redistribution OK + patent grant).
2. **Size hợp lý** ~120 KB min.gz, kiosk Mac/Win mid-range chạy mượt.
3. **Camera UI sẵn** — operator-friendly khung scan box render auto, không phải tự dựng `<canvas>`.
4. **Maintained** (commits 2024-2025) + tốt với QR + barcode 1D phổ biến cho WO label.
5. **Permissions API + camera enumeration** built-in → fallback UX dễ dựng.

**Trade-off với jsQR**: nhỏ hơn 60% nhưng phải tự code camera plumbing (+~200 LOC). Em nghiêng html5-qrcode trừ khi anh muốn ultra-lightweight.

### Bundle local (NO CDN)

Ràng buộc cứng từ anh: **KHÔNG CDN** (server nhà máy có thể no-internet).

- Download `html5-qrcode.min.js` (single-file UMD) từ GitHub release tag `v2.3.8` (verify SHA256 manual trong PR description).
- Đặt tại `src/CCL.MES.Web/wwwroot/lib/html5-qrcode/html5-qrcode.min.js`.
- License file kèm: `wwwroot/lib/html5-qrcode/LICENSE` (verbatim Apache-2.0).
- `_Host.cshtml` hoặc page-level `<script src="/lib/html5-qrcode/html5-qrcode.min.js"></script>` — load **chỉ khi** Shop Order page mở (lazy via `OnAfterRenderAsync` injected JS) để không bloat các page khác.
- Pin version trong file path nếu cần (option A) hoặc dùng folder `2.3.8/` (option B). **Khuyến nghị B** — `wwwroot/lib/html5-qrcode/2.3.8/html5-qrcode.min.js` để upgrade lib chỉ là path-bump 1 dòng.
- KHÔNG thêm NuGet server dep.

---

## §2. HTTPS / getUserMedia constraint (cần anh chốt Q2)

### Vấn đề

`navigator.mediaDevices.getUserMedia({ video: true })` chỉ chạy trên **secure contexts**:
- `https://` ✅
- `http://localhost` ✅ (dev mode)
- `http://<LAN IP>` ❌ (browser block thẳng tay từ Chrome 47+ / Firefox 60+ / Safari)

### Reality CCL-MES deploy

Server nhà máy hiện chạy `http://<LAN-IP>:5080` (Kestrel listen). Operator các máy worker truy cập qua LAN → **camera KHÔNG hoạt động** trừ khi:

1. **(A)** Setup self-signed cert + reverse proxy IIS/nginx → `https://<host>` → trusted (cài cert root vào browser store mỗi máy). Operational cost cao.
2. **(B)** Switch Kestrel sang HTTPS với self-signed cert + browser exception per device. Test-friendly nhưng trust warning popup mỗi lần restart.
3. **(C)** Live with HTTP-only LAN: camera không chạy, **manual LOOKUP là đường chính** + tooltip rõ ràng "Camera requires HTTPS — use manual input".

### 🌟 Khuyến nghị mặc định: **(C) cho PR #32b + (A) như follow-up ops task**

**Lý do (C)**:
- PR #32b sẽ ship code camera code đầy đủ + detection logic. Khi `isSecureContext === false` → button "Scan camera" **disabled** + tooltip "Camera unavailable: requires HTTPS or localhost". Operator dùng manual input đã ship #32a.
- KHÔNG block PR #32b on ops infrastructure decision.
- Localhost dev test vẫn chạy (anh + Lead Engineer test).
- Sau khi setup HTTPS reverse proxy (PR #32b+1 hoặc deployment runbook update), camera tự enable.

**Alternative**: nếu anh muốn camera operational ngay khi merge → cần ops trước (config HTTPS) → PR #32b block on infra.

### UX khi camera unavailable

```
┌─ Shop Order ────────────────────────────────────────────┐
│ 📱 [Scan WO QR or type code...] [📷 Camera] [Lookup]    │
│                                  ↑                      │
│                          disabled + tooltip:            │
│                          "Camera needs HTTPS or         │
│                           localhost. Use manual         │
│                           input instead."               │
└─────────────────────────────────────────────────────────┘
```

Detection logic (Razor + JS interop):
```csharp
private bool _cameraAvailable;
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        _cameraAvailable = await JS.InvokeAsync<bool>("ccl.qr.isAvailable");
        StateHasChanged();
    }
}
```

```javascript
window.ccl.qr.isAvailable = () =>
  !!(window.isSecureContext &&
     navigator.mediaDevices &&
     typeof navigator.mediaDevices.getUserMedia === "function");
```

---

## §3. Scan flow

```
[Click "📷 Camera" button]
        ↓
  isAvailable check → if false: toast + abort
        ↓
  Open modal `<div id="qr-reader">` + html5-qrcode init
        ↓
  navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" } })
        ↓
  Permission prompt — if denied: detect → toast "Permission denied" + close modal
        ↓
  Scan loop (10 fps default; html5-qrcode handles)
        ↓
  Decoded text === WoNo? (regex `^WO-\d{2}-\d+$` or operator scans bare code)
        ↓ yes
  Stop scanner + close modal + .NET callback `OnScanResult(decoded)`
        ↓
  Service `WorkOrderService.FindByWoNoAsync(wonum)` → DTO or null
        ↓
  if found: open drawer (§4) populated
  if not found: toast "Work Order <code> not found" + drawer stays closed
```

### Decode is 100% client-side

- Không upload ảnh frame lên server.
- Modal đóng = camera stream stop (sự kiện `html5QrCode.stop().then(...)` cleanup).
- Permission revoke (operator close tab) tự cleanup browser-side.

### Error states

| Trigger | UX |
|---|---|
| Camera unavailable (secure context false) | Button disabled + tooltip; manual input vẫn dùng được |
| Permission denied | Modal closes + toast "Camera permission denied. Use manual input." |
| 0 camera detected | Modal closes + toast "No camera found on this device." |
| Decode timeout 30s no result | Modal close button "Cancel" always visible; operator manual tap thoát |
| Decoded but no WO match | Toast "Work Order {code} not found" + scanner stays open for re-scan |

---

## §4. Per-WO drawer — read-only

### Khi nào mở

1. **Click card** trong Shop Order list (Active hoặc Closed section).
2. **Scan QR thành công** + WO resolved.
3. **Manual LOOKUP** với code match exact WoNo (chứ không phải redirect tới /workorders như hiện) — anh chốt Q3.

### Layout drawer

Right-side panel (`position: fixed; right: 0`), width 480px on desktop / 100vw on mobile, slide-in animation 200ms.

```
┌─ WO-26-3683 ────────────────── [×] ─┐
│ Brady Asia · BRD-7656-D                │
│ [○ NEW]                                │
│ ────────────────────────────────────── │
│ HEADER                                  │
│   Customer    Brady Asia                │
│   Product     BRD-7656-D · PCB ID Label │
│   Spec rev    SPEC-BRD-7656-D · Rev A   │
│                                         │
│ PRODUCTION                              │
│   Machine     ACNC3 · CNC 3-Heads       │
│   Process     Silkscreen + Diecut       │
│   Target      12,000 pcs                │
│   Produced    0 pcs                     │
│   Planned     2026-06-01 00:00          │
│                                         │
│ MATERIALS (3)                           │
│   • PVC-001  (kg)                      │
│   • INK-005  (L)                       │
│   • CORE-076 (pcs)                     │
│   ↳ Pre-flatten from BOM subquery        │
│                                         │
│ QC HISTORY (2)                          │
│   • IPQC  2026-05-30 14:22  Pass        │
│   • IPQC  2026-05-30 09:10  Pending     │
│                                         │
│ ────────────────────────────────────── │
│ [ Open in Work Orders → /workorders ]   │
│   ↑ button: redirect tới Phase 6 cho   │
│     action (advance/pause/flags) — drawer│
│     KHÔNG mutate.                       │
└─────────────────────────────────────────┘
```

### Drawer content sections (Q4 default)

- **Header**: Customer, Product, ProductRevision SpecCode + RevisionCode.
- **Production**: Machine, ProcessLabel, TargetQty, ProducedQty, PlannedStart, PlannedEnd.
- **Materials (N)**: list của BOM materials (ManufacturingStructures.Where(s => s.ParentPart == ProductCode)).
- **QC History (N)**: list QcInspection của WO (most recent 5; full timeline link to /workorders).
- **Action footer**: 1 button "Open in Work Orders →" — `Nav.NavigateTo($"/workorders?wo={woNo}")`. KHÔNG button khác (no mutation).

### Drawer service shape

```csharp
public sealed record WorkOrderDrawerView(
    long Id,
    string WoNo,
    string? CustomerName,
    string? ProductCode,
    string? ProductName,
    string? SpecCode,
    string? RevisionCode,
    string? MachineCode,
    string? MachineName,
    string? ProcessLabel,
    int TargetQty,
    int ProducedQty,
    string Uom,
    DateTime? PlannedStart,
    DateTime? PlannedEnd,
    WorkOrderStatusBadge.Badge Badge,
    List<DrawerMaterialRow> Materials,
    List<DrawerQcRow> QcInspections);

public sealed record DrawerMaterialRow(string PartNo, string? Description, string? Uom);
public sealed record DrawerQcRow(QcType Type, DateTime CreatedAt, QcResult Result, string? InspectorName);
```

New method `WorkOrderService.GetDrawerAsync(string woNo)` — single round-trip via `Include` chain + 1 subquery for BOM. 100% read-only.

---

## §5. State machine + Phase 6 integration

**KHÔNG đụng** Phase 6 state machine. Drawer footer button = navigate, không call AdvanceAsync / UpdateFlagsAsync / SignalR notify. Mọi mutation vẫn ở Phase 6 table với existing AuthorizeView role gating.

**KHÔNG SignalR push** trong drawer (defer auto-refresh đến PR #32c). Operator phải reload trang để thấy state mới. Trade-off chấp nhận được vì drawer dùng frequency thấp (kiosk-scan-once flow).

---

## §6. Files mới + modify

| File | Status | Mục đích |
|---|---|---|
| `wwwroot/lib/html5-qrcode/2.3.8/html5-qrcode.min.js` | NEW | Bundle local QR lib (~120 KB) |
| `wwwroot/lib/html5-qrcode/2.3.8/LICENSE` | NEW | Apache-2.0 verbatim |
| `wwwroot/js/cclQrScanner.js` | NEW | Thin wrapper expose `ccl.qr.{isAvailable, start, stop}` cho Razor JS interop |
| `Pages/ShopOrder.razor` | MODIFY | +Camera button + drawer mount + JS interop wiring |
| `Components/WorkOrderDrawer.razor` | NEW | Right-side panel, render-from-DTO + try-catch (#27) |
| `Services/WorkOrderService.cs` | MODIFY | +GetDrawerAsync(string woNo) + DrawerView DTOs |
| `Domain` (no change) | — | Reuse WorkOrderStatusBadge.From |
| `Resources/SharedResource.{resx,vi.resx}` | MODIFY | +~30 i18n keys (scan button + permission + drawer sections + materials + qc) |
| `wwwroot/css/site.css` | MODIFY | +`.wo-drawer` + `.wo-drawer-section` + camera modal (~120 LOC) |
| `Pages/_Host.cshtml` | MODIFY | +1 `<script>` tag chỉ trên Shop Order page hoặc lazy via `<dynamic-script>` pattern |
| `docs/LESSONS_LEARNED.md` | MODIFY | Pin: QR lib bundle pattern + HTTPS constraint + JS interop disposal |
| `MAINTAINERS.md` (nếu có) | MODIFY | Note lib upgrade procedure (path-bump + SHA verify) |

**KHÔNG migration** (read-only).

---

## §7. Q1..Q12 — questions với defaults

| Q | Question | Default | Trade-off |
|---|---|---|---|
| **Q1** | QR library? | **html5-qrcode 2.3.8 (Apache-2.0)** | jsQR nhỏ hơn nhưng cần +200 LOC plumbing; zxing-js mạnh hơn nhưng 4× size |
| **Q2** | HTTPS handling? | **(C) Live với HTTP-only LAN — Camera disabled w/ tooltip; manual luôn dùng được** | (A) Setup reverse proxy HTTPS = block PR on ops; (B) Self-signed cert direct Kestrel = trust warning UX poor |
| **Q3** | Manual LOOKUP behavior khi exact WoNo match | **Open drawer thay vì redirect** /workorders | Redirect cũ vẫn ship cho operator workflow planner — chỉ apply khi exact match |
| **Q4** | Drawer sections (default §4) | **Header / Production / Materials / QC History (5 latest) / Action footer** | Có thể strip Materials hoặc QC nếu anh muốn drawer tối giản |
| **Q5** | Drawer auto-refresh khi WO state change | **Defer to PR #32c** (operator reload trang) | Wiring SignalR vào drawer thêm complexity + đụng ShopfloorNotifier subscribe scope |
| **Q6** | Card click → mở drawer? | **Yes** (cùng pattern scan-success → drawer) | Alt: chỉ scan mở drawer; card click → redirect /workorders (kém UX) |
| **Q7** | Drawer Action footer button | **"Open in Work Orders →" navigate /workorders?wo={woNo}** | Có thể thêm "Copy WoNo" hoặc "Print label" — defer |
| **Q8** | Camera permission UX | **Toast Bootstrap-style ephemeral** (5s auto-dismiss) | Modal alert chặn UX kiosk worse |
| **Q9** | Camera fallback indicator | **Tooltip on disabled button** | Permanent banner trên trang gây noise |
| **Q10** | facingMode preference | **`"environment"` (rear camera)** for kiosk QR scan workflow | Front camera cho selfie laptop — không phù hợp shop-floor |
| **Q11** | Drawer width responsive | **480px desktop / 100vw mobile** | Có thể 540px nếu QC list dài |
| **Q12** | i18n key prefix | **`shop_order.drawer.*` + `shop_order.scan.*`** | Cohesive với #32a `shop_order.*` |

---

## §8. Effort estimate

| Layer | Files | LOC |
|---|---|---|
| Web JS | `cclQrScanner.js` (NEW) | +80 |
| Web wwwroot/lib | `html5-qrcode.min.js` + LICENSE (vendored, không tính LOC) | (lib) |
| Web Razor | `WorkOrderDrawer.razor` (NEW) | +200 |
| Web Razor | `ShopOrder.razor` MODIFY (scan button + drawer mount + JS interop) | +80 |
| Web `_Host.cshtml` | +1 `<script>` lazy hoặc conditional include | +5 |
| Application | `WorkOrderService.GetDrawerAsync + DTOs` | +120 |
| Web i18n | resx EN+VI ~30 keys × 2 | +60 |
| Web CSS | `.wo-drawer*` + `.qr-scanner-modal` | +120 |
| Docs | LESSONS_LEARNED + MAINTAINERS notes | +40 |

**Total**: ~700 LOC. **Effort**: M (1-2 phiên).

**Migration**: 0 (read-only). **Vendored lib**: ~120 KB binary bundled vào wwwroot/.

---

## §9. Vùng cấm + Phase 6 preservation

PR #32b TUYỆT ĐỐI KHÔNG đụng:
- Ops Control v1.2, Old ver, Machine, ProductionLog
- Phase 6 WorkOrders.razor table + WorkOrderStateMachine + WorkOrdersController + ShopfloorNotifier + WorkOrderService mutation methods (AdvanceAsync / UpdateFlagsAsync / CreateAsync)
- 6 NPI Engineer Spec tab + 4 NPI khác (RawMaterials / Routing / Structure / WorkCenter)
- Phase 6 IqcInspection + IqcResultDetail
- Shop Order CMES sibling + SpecHub READ-ONLY
- DrawingApproval / Drawings / blob infra (D-5a/b/c series)

**Drawer = pure READ**. Mọi action mutation vẫn ở Phase 6 table per existing role-gated buttons.

Baseline preserved post-build:
```
SpecQcWindows=0 QcCriteria=0 SpecQcCaptures=0 ReasonCodes=12
ProductRevisions=6 WorkOrders=1 IqcInspections=3 IqcResultDetails=7
Users=5 ManufacturingStructures=20530 ProcessCatalogs=17
Drawings=1 DrawingVersions=1 DrawingApprovals=0
```

FK ProductRevision↔WO intact (WO-26-3683 → revision 1). Latest migration unchanged.

---

## §10. Verify gates (post-implementation)

| # | Check | Method |
|---|---|---|
| V1 | dotnet build clean | 0 W / 0 E |
| V2 | `/workorders/shop` page renders 200 với camera button + manual input | curl + grep markers |
| V3 | Camera button **disabled** trên http://lan-ip (isSecureContext false) | Browser test on LAN |
| V4 | Camera button **enabled** trên http://localhost hoặc https | Browser test |
| V5 | Click camera button (enabled) → modal opens + permission prompt + scanner starts | Browser dev box test |
| V6 | Scan WO-26-3683 QR code → drawer mở với data WO seed | Print QR + scan |
| V7 | Card click WO-26-3683 → drawer opens (same content as scan flow) | Browser |
| V8 | Drawer "Open in Work Orders" button navigates `/workorders?wo=WO-26-3683` | Browser |
| V9 | Drawer Materials section renders 0+ rows từ BOM subquery | Browser + sqlite verify |
| V10 | Drawer QC History renders 0+ rows (IPQC/FQC/OQC ordered DESC) | Browser |
| V11 | Drawer KHÔNG có mutation button (no Advance / Pause / Update) | Manual UI inspect |
| V12 | Phase 6 `/workorders` table renders identical (git diff `WorkOrders.razor` empty) | git diff |
| V13 | i18n EN+VN switch drawer sections + scan permission messages | Browser language toggle |
| V14 | LICENSE file vendored at `wwwroot/lib/html5-qrcode/2.3.8/LICENSE` | ls check |
| V15 | Vùng cấm intact (baseline + IQC=3 + FK) | sqlite verify |
| V16 | Restart no-op (lib stays bundled) | Boot 2× |

---

## §11. Lessons to pin sau merge

1. **QR lib bundle pattern** — vendored under `wwwroot/lib/<name>/<version>/` + LICENSE + path-bumped for upgrades. KHÔNG CDN cho air-gapped factory deploys.
2. **HTTPS constraint cho getUserMedia** — secure context required. Detect via `window.isSecureContext`; UX fallback to manual input. LAN HTTP deployments must setup reverse proxy HTTPS for camera UX.
3. **JS interop disposal** — Blazor JS interop modules phải dispose camera stream khi component unmount (operator nav away mid-scan) — pattern `IAsyncDisposable.DisposeAsync` + JS `stop()` call.
4. **Drawer read-only discipline** — kiosk view drawer KHÔNG mutate; mọi action stay ở planner table với existing role gating. Đây là invariant cho phase 6 preservation.

---

## §12. STOP — chờ duyệt

Em sẽ KHÔNG tạo branch / KHÔNG code cho đến khi anh:

1. **Confirm Q1**: html5-qrcode 2.3.8 (Apache-2.0) — OK? hoặc đề xuất alt.
2. **Confirm Q2**: HTTPS handling option (C) cho PR #32b (live với HTTP-only LAN, camera disabled + manual fallback) — OK?
3. **Chốt Q3-Q12** — em đề xuất defaults; flag specific:
   - Q3 manual LOOKUP exact-match → drawer (thay redirect)
   - Q6 card click → drawer
   - Q5 SignalR push trong drawer defer to PR #32c
4. **Verify SHA256** of vendored `html5-qrcode.min.js` (em sẽ ghi vào PR description khi commit).

Sau khi anh chốt, em sẽ:
- Tạo branch `feat/phase8-shop-order-scan-drawer`
- Download lib + verify SHA + vendor vào wwwroot/lib/
- Code drawer + scan flow + JS interop + HTTPS detection
- KHÔNG đụng Phase 6 WO state machine + table page + SignalR notifier
- V1-V16 verify
- Pin lessons + open PR + STOP review.

---

## §13. Files surveyed (transparency)

CCL-MES current:
- `src/CCL.MES.Web/Pages/ShopOrder.razor` (PR #32a base — sẽ MODIFY)
- `src/CCL.MES.Application/Services/WorkOrderService.cs` (sẽ extend GetDrawerAsync)
- `src/CCL.MES.Web/wwwroot/` (target cho lib bundle)
- `src/CCL.MES.Web/Pages/_Host.cshtml` (script tag injection)

KHÔNG khảo sát (per anh chỉ thị):
- CMES sibling, Ops Control v1.2, SpecHub, Old ver

SpecHub `spechub-prototype.html` đã port pattern UX scan-input + per-WO drawer
ở PR #32a; PR #32b chỉ thêm camera module local-bundle.

---

*Plan tạo: 2026-06-02 — Phase 8 PR #32b (scan QR camera + per-WO drawer) — NO branch yet.*
