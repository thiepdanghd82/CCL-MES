# Phase 9 — Settings /hardware + /mode PLAN

> **Status**: SURVEY-ONLY. Scope chưa rõ vì 2 trang là placeholder
> port từ Ops Control v1.2 catalogue mà underlying infra (Electron /
> desktop bridge) **chưa có ở CCL-MES**. STOP chờ Henry chốt
> direction trước khi code bất kỳ thứ gì.
> **Author**: 02/06/2026 sau khi merge #68 hub-session-banner.

---

## 1. Findings — survey kết quả

### 1.1 Hiện trạng code

| Vị trí | Nội dung |
|---|---|
| `Pages/Settings/Hardware.razor` (13 LOC) | Placeholder — `<h1>` + 3 bullet `<li>` mô tả tính năng dự kiến |
| `Pages/Settings/Mode.razor` (13 LOC) | Placeholder — `<h1>` + 3 bullet `<li>` mô tả tính năng dự kiến |
| `Shared/MainLayout.razor:62-63` | 2 dropdown entry `/settings/hardware` + `/settings/mode` — KHÔNG có `<AuthorizeView Roles=...>` wrap → visible cho mọi user (không phải AdminOnly) |
| `Resources/SharedResource.resx:135-145` | 8 i18n key (4 hardware + 4 mode) — đã có EN+VI parity |

### 1.2 Intent rõ từ placeholder + docs

**Placeholder text (i18n keys + Razor placeholder)**:

> **Hardware devices** — barcode scanners, label printers, USB scales.
> Desktop-only surface. **To be built in a later sprint.**
> - Detect and name connected scanners / printers
> - Set the default printer per workstation
> - Test print + scanner echo

> **Connection mode** — embedded / thin / smart desktop modes.
> **To be built in a later sprint.**
> - Pick standalone (local SQLite) or server-connected
> - Server URL + connection diagnostics
> - Re-run the first-run setup wizard

**docs/PHASE6-PLAN.md:59-60** đã đánh dấu cả 2 tab là **KHÓ**:

> | `hardware` | Placeholder | **KHÓ** (desktop-only, cần Electron hoặc native interop; không khả thi nếu chỉ web) |
> | `mode`     | Placeholder | **KHÓ** (cần dual-mode infra, không phù hợp Phase 6) |

**docs/PHASE6-REPORT-2026-05-31.md:196** ghi rõ defer:

> Hardware/Mode placeholder, ImportLegacy đã xoá ở chore. Phase 7+:
> implement Hardware (USB devices register) + Mode (online/kiosk)
> UI thật **nếu cần**.

### 1.3 Sibling Ops Control v1.2 — intent gốc

| File | Mục đích | Persistence | Web fallback |
|---|---|---|---|
| `client/src/modules/cost/tabs/HardwareSection.jsx` | 4 device: Zebra/TSC label printer (TCP:9100), scale (RS232/USB-Serial), barcode scanner (USB-HID), office printer (OS spooler) | `desktop.cache.set/get` (electron-store, **per-workstation**) | Banner "không khả dụng — cài bản desktop" |
| `client/src/modules/cost/tabs/ModeSection.jsx` | 3 mode: `embedded` (Express in-process) / `thin` (remote LAN server) / `smart` (hybrid cache + sync) | electron-store + `main.js` đọc lúc boot; **restart required** | Banner "không khả dụng" |

Cả 2 tab ở sibling **chỉ render khi `desktop.isAvailable === true`**
(Electron build). Trong web mode (browser), hiện banner suggest cài
desktop.

### 1.4 CCL-MES hiện tại — KHÔNG có desktop infra

`grep -rn "Electron\|desktop\|electron-store" --include="*.cs"
--include="*.razor" src/` chỉ trả về 1 hit ở `DemoWoTemplates.razor`
("Server + works in the desktop kiosk shell" — chỉ là comment mô tả
dự định, không có infra thực).

Architecture hiện tại:

- **Blazor Server web app** (.NET 10) — chạy như ASP.NET process.
- Không Electron wrapper, không desktop bridge, không native interop.
- Single deployment mode: HTTP server (Sqlite hoặc SqlServer backend
  per `Database:Provider`), serve cho mọi browser.
- Không có `desktop.cache.set/get` analog; không có
  electron-store; không có per-workstation config storage trên
  client (chỉ có Browser `sessionStorage` + ASP.NET cookie).

### 1.5 Phân tích RBAC

Sibling cả 2 tab nằm trong USER group (không phải admin-only) —
mọi user có thể đổi mode/hardware config của workstation MÌNH dùng.
CCL-MES `MainLayout.razor:62-63` cũng không wrap `<AuthorizeView>`
→ hiện tại visible cho mọi user. Nếu chuyển sang server-stored
config (key-value table) thì RBAC phải đổi sang AdminOnly (admin
quyết định cấu hình printer/mode cho server, không phải user).

---

## 2. Honest assessment — 2 tab này quản lý gì trong CCL-MES?

**Em phải nói thẳng**: Cả 2 tab được port verbatim từ Ops Control
v1.2 catalogue MÀ KHÔNG kèm underlying infra. Trong CCL-MES current
architecture:

- **Hardware** không có cách "detect connected device" — Blazor
  Server chạy trên 1 server, browser client không expose USB/Serial
  cho server. Web Serial API + WebHID API có thể dùng nhưng cần
  JS interop riêng + chỉ chạy trên Chromium browser + per-workstation
  storage = browser localStorage.
- **Mode** không có concept "embedded vs thin vs smart" — CCL-MES
  hiện chỉ có 1 mode: HTTP server. Không có Electron để chạy
  in-process; không có cache-sync infrastructure cho smart mode.

**Tóm lại**: 2 tab là **stub placeholder không có roadmap cụ thể**.
Cần Henry quyết định **direction** trước khi em đi sâu.

---

## 3. 4 option direction cho Henry chốt

### Option A — Drop both tabs (honest "we don't support this yet")

**Action**:
- Xóa `Pages/Settings/Hardware.razor` + `Pages/Settings/Mode.razor`
- Xóa 2 entry trong `MainLayout.razor:62-63`
- Xóa 8 i18n key (4 hardware + 4 mode) × 2 ngôn ngữ = 16 entries

**Pros**: Settings dropdown gọn hơn (8 → 6 entry). Operator không
click vào tab "to be built" rồi thất vọng.

**Cons**: Mất hook cho roadmap tương lai (nếu sau này thêm desktop
wrapper). Operator cũ quen đã vào tab này biết nó là placeholder.

**Effort**: XS (~30 phút).

### Option B — Implement what works on web today (server-stored config) *(em đề xuất nếu cần ship)*

**/hardware (server-side default printer + scanner test endpoint)**:
- Entity mới `AppSetting { Key, Value, UpdatedAt, UpdatedBy }` —
  key-value store cho per-server-deployment config.
- AdminOnly UI: nhập tên default office printer (string, free-form
  — nhập đúng tên printer đang share trên LAN), test print button
  (server gửi 1 PDF demo qua print spooler service).
- Scanner: KHÔNG khả thi server-side; loại bỏ tính năng này khỏi
  scope. Nếu cần web scanner, dùng html5-qrcode (đã có cho QR
  camera trong WorkOrders.razor) — không cần Settings tab.

**/mode (read-only deployment info)**:
- Read-only display: "This deployment is server-connected mode at
  `<base URL>`. Database provider: `Sqlite`/`SqlServer`."
- Nút "Re-run setup wizard" — placeholder cho first-run setup
  endpoint (chưa có, defer).
- KHÔNG có toggle mode (CCL-MES chỉ có 1 mode HTTP-server).

**Pros**: Tab có nội dung thật, không lừa operator. Default printer
config là legitimate ops feature.

**Cons**: Cần entity + migration A→B→C SAFE (key-value table mới).
RBAC phải đổi sang AdminOnly. Effort không nhỏ — ~400 LOC.

**Effort**: M (~1-2 ngày dev + test). Cần migration.

### Option C — Defer until CCL-MES adds desktop wrapper (Phase 10+)

**Action**:
- Sửa placeholder copy thành rõ hơn: "Requires desktop install
  (planned Phase 10+). Currently this CCL-MES deployment is a
  web-only server install."
- Wrap nav entry với feature flag (env `OPS_FEATURE_DESKTOP_SETTINGS=0`
  default OFF — ẩn entry trên web). Khi sau này có Electron wrapper,
  flip flag = 1 trong electron-built version.

**Pros**: Honest về current state + roadmap visible. KHÔNG xóa code
(giữ hook cho Phase 10+).

**Cons**: Vẫn là placeholder. Operator nhìn nav thấy thêm 2 entry
"requires desktop install" — confusing.

**Effort**: S (~2-3h).

### Option D — Mỗi tab khác direction

- **/hardware**: Option C (defer) — vì scanner/scale thật sự cần
  desktop interop, không thể làm trên web cleanly.
- **/mode**: Option B (server-stored deployment info, read-only) —
  hữu ích cho ops, không cần desktop infra.

**Pros**: Cân bằng — ship phần nào khả thi, defer phần nào chưa.

**Cons**: 2 PR riêng. Effort tổng = B.mode + C.hardware ≈ M.

---

## 4. Recommendation

**Em đề xuất Option A (drop both)** — clean honest, code base gọn,
operator không bị confuse.

**Nếu Henry muốn giữ Settings dropdown đầy đủ** → Option D (split
direction): `/mode` ship as read-only deployment info (Option B
variant), `/hardware` defer via Option C copy update.

**Option B full** chỉ nếu Henry confirm CCL-MES sẽ thực sự dùng
server-stored printer config trong production — đó là entity +
migration không nhỏ cho 1 feature mà sibling project (Ops Control
v1.2) chạy electron-store dễ hơn.

---

## 5. Q1..Q5 cho Henry

### Q1 — Direction (lớn nhất)
- **A (em đề xuất)**: Drop both tabs — xóa Hardware.razor + Mode.razor + nav entry + i18n key.
- **B**: Implement server-stored config cho cả 2.
- **C**: Defer cả 2 (sửa placeholder copy + ẩn sau env flag).
- **D**: Split — /mode = Option B variant (read-only deployment info), /hardware = Option C (defer).

→ **CHỜ HENRY CHỐT**. Em nghiêng A; D nếu giữ catalogue.

### Q2 — Nếu Option B/D: tên entity key-value table
- **A (default nếu cần)**: `AppSettings { Id, Key, Value, UpdatedAt, UpdatedBy }`.
- B: `SystemConfigs` (cũ pattern Ops Control v1.2).
- C: Reuse `Library/` folder JSON pattern (như PermissionGroups).

→ A nếu cần. Migration A→B→C SAFE (isolated /tmp + backup+SHA256 trước migrate live).

### Q3 — Nếu Option B/D: RBAC
- **A (default)**: AdminOnly — vì config tác động lên server-wide.
- B: Mọi user — như sibling (per-workstation, không phải server config).

→ A. CCL-MES server-stored ≠ Ops Control v1.2 per-workstation. AdminOnly đúng cho server config.

### Q4 — Nếu Option B cho /mode: scope
- **A (default)**: Read-only display deployment info (provider, URL, version, last-backup-time). Nút "Re-run setup wizard" là placeholder nếu chưa có wizard.
- B: Toggle Sqlite/SqlServer provider trong UI — KHÔNG khả thi không restart.
- C: List + edit server config keys generic (kiểu /etc/conf editor) — quá powerful + nguy hiểm.

→ A. Read-only là an toàn + đủ thông tin cho ops.

### Q5 — Nếu Option C: feature flag default
- **A (default)**: `OPS_FEATURE_DESKTOP_SETTINGS=0` (ẩn entry trên web build). Phase 10+ khi có Electron wrapper → flip = 1.
- B: Luôn hiển thị với copy rõ ràng "requires desktop install".

→ A. Tránh confuse operator trên web mode hiện tại.

---

## 6. Ước lượng + chia PR

| Option | LOC | Migration | PR | Effort |
|---|---:|---|---|---|
| A — Drop both | -50 | KHÔNG | 1 PR (xóa file + i18n + nav) | XS (~30 phút) |
| B — Implement both | +400 | CÓ (AppSettings entity + table) | 2 PR (entity+/mode read-only / +/hardware printer config) | M-L (~2-3 ngày) |
| C — Defer with flag | +30 | KHÔNG | 1 PR (sửa copy + env flag) | S (~2-3h) |
| D — Split direction | +250 | CÓ (chỉ AppSettings nhẹ cho mode display) | 2 PR (1 /mode read-only / 1 /hardware copy+flag) | M (~1 ngày) |

---

## 7. Hard constraints (recap)

- Nếu Option B/D có entity mới → **A→B→C SAFE protocol** mandatory:
  Phase A backup `data/ccl_mes.db` + SHA256 trước → Phase B sinh
  migration trên `/tmp/<name>-design.db` isolated → Phase C verify
  + apply live DB. Lesson #7 (Lessons Learned).
- Nếu Option B/D có RBAC change → AdminOnly via `@attribute
  [Authorize(Policy="AdminOnly")]` page-level + `<AuthorizeView
  Roles="Admin">` nav-level. Pattern mirror `Settings/Account.razor`.
- Render entity + try-catch (#27) — controller wrapping per step,
  Problem() structured error.
- i18n EN/VI parity bắt buộc.
- KHÔNG migration nếu Option A/C.
- KHÔNG đụng Phase 6 mutation / Spec / NPI / WO / sibling.
- Baseline + vùng cấm READ-ONLY.

---

## 8. Out of scope

- Web Serial / WebHID API cho scanner — too platform-specific
  (Chromium only). Defer cho Phase 10+ desktop wrapper.
- Setup wizard implementation — nút "Re-run setup wizard" chỉ là
  placeholder nếu chưa có wizard endpoint. Wizard scope là PR riêng.
- Theme toggle (light/dark) — đã có placeholder ở `/appearance` tab.
- Multi-tenant config — CCL-MES single-tenant deployment.

---

*Plan author: Claude. STOP — chờ Henry chốt Q1 (direction lớn nhất)
trước khi tạo branch. Tùy direction sẽ tạo:*

- *Option A → `chore/drop-hardware-mode-placeholders`*
- *Option B → 2 PR (`feat/app-settings-entity` + `feat/settings-mode-hardware-server-config`)*
- *Option C → `chore/defer-hardware-mode-with-flag`*
- *Option D → 2 PR (`feat/settings-mode-deployment-info` + `chore/defer-hardware-with-flag`)*
