# Phase 10 — P10.3 Hardware native + `/hardware` + `/mode` real — PLAN

> **Status: APPROVED by Henry 2026-06-03.** Q1-Q12 accept defaults with
> ONE explicit override at Q6 (idle handling) — see §9.Q6 for details.
> Scan online-only at P10.3 confirmed: failed POST surfaces operator-visible
> error with retry; the scan payload stays in modal so the operator can
> write it down; **no half-outbox**, offline scan engine waits for P10.4.
> Scope: take the placeholder `/hardware` and `/mode` pages from
> `PHASE10-MAUI-MIGRATION-PLAN.md §5` to actually-working pages, with a
> real per-platform hardware abstraction. Per Henry's Q6 = Win + macOS
> desktop first; Android/iOS deferred. Per Henry's Q4 = strict online
> only — scan / weigh / print results go through the API, no local
> queue. Offline write subsystem stays for P10.4.
>
> Constraints carried from P10.1/P10.2:
> - **All code stays inside `CCL-MES-Hybrid/`.** Legacy `src/CCL.MES.*`
>   is READ-ONLY baseline.
> - **Sibling project (`3. PROJECTS/Ops Control v1.2/`) is READ-ONLY**
>   reference. We may study its `desktop/native/scanner.js` +
>   `desktop/native/printer.js` + `HardwareSection.jsx` for patterns,
>   but the implementation here is fresh net10 + MAUI + Blazor, not a
>   port. No file in v1.2 is touched.
> - **Reuse** the JWT auth, API client, RBAC plumbing, and the
>   `MacCatalystKeyboardFix` platform-detect pattern from P10.2.
> - **Permissions declared per-platform.** `NSCameraUsageDescription`
>   for Catalyst/iOS; `<uses-permission android:name="android.permission.CAMERA">`
>   on Android; capability `webcam` on Windows MSIX. Camera prompt
>   appears once per install, denial path surfaces a real error
>   banner (not a silent no-op).

---

## 0. P10.2 lessons that shape P10.3

Five concrete patterns from P10.2 carry into every hardware impl:

| P10.2 lesson | P10.3 application |
| --- | --- |
| `dotnet/maui#13934` Catalyst keyboard trap → JS workaround via UA detect | Any DOM keyboard interaction (scanner "keyboard wedge" mode, manual code entry) MUST be tested with `MacCatalystKeyboardFix` active. Don't rebuild a parallel polyfill. |
| Adhoc dev build has no entitlements → `SecureStorage` throws `MissingEntitlement` | Camera permission on adhoc Catalyst dev builds is similar — **may** throw at first request. The hardware impls need a fallback path that surfaces the error to operator with actionable guidance (link to System Settings → Camera). |
| `localhost` resolves to `::1:5100` first → Connection refused before v4 fallback | API endpoints called from the hardware impls (e.g. `/api/v2/scan/log`, `/api/v2/print/zpl`) MUST go through the same `ICclApiClient` that already uses `http://127.0.0.1:5100`. No new HttpClient. |
| `autocomplete="username"` triggers Keychain autofill that confuses `@bind-Value` | Any input on `/hardware` (e.g. label printer IP, scanner serial) gets `autocomplete="off"`. Same trap. |
| Probe panel + `Console.WriteLine` traces leaked to prod via missing `#if DEBUG` | Every diagnostic in P10.3 (camera frame preview, raw HID byte dump, scale serial echo) gated behind `#if DEBUG` from day 1. |

---

## 1. Priority cut — scanner FIRST, printer + scale opt-in

`PHASE10-MAUI-MIGRATION-PLAN.md §5` named three interfaces:
`IBarcodeScannerService`, `ILabelPrinterService`, `IWeighScaleService`,
plus `IDeviceModeService`. P10.3 ships ONE of them as production-grade
and stubs the rest behind feature flags. Reasoning:

- **Scanner is the only device most stations need on day one.** Every
  WO acceptance / op finish / sample tracking workflow starts with a
  scan. Operators already have scanners (legacy web uses
  "keyboard-wedge" scanners pointed at the focused input). Replacing
  the wedge with a native camera scan unblocks Catalyst tablet / Mac-
  with-no-USB scenarios immediately.
- **Label printer needs concrete operator request first.** Today no WO
  triggers a label print in MES. Routing card printing lives in the
  v1.2 product (Ops Control), not MES. Until an MES workflow asks for
  ZPL output, shipping `ILabelPrinterService` is YAGNI.
- **Weigh scale needs concrete process first.** QC weight capture is
  manual today (operator types grams). Ship the scale only when a
  weigh-and-record workflow (e.g. ink dispensing, raw-mat receiving)
  is greenlit.

**P10.3 scope decision:**

| Interface | P10.3 deliverable |
| --- | --- |
| `IBarcodeScannerService` | **REAL** — native camera on Catalyst + native camera/USB-HID on Windows. Test page on `/hardware`. Wired into 1 production flow (recommend WO Accept scan-to-confirm). |
| `IDeviceModeService` | **REAL** — kiosk vs interactive mode flag persisted per device. Surface on `/mode`. Affects auto-logout timeout + default landing route. |
| `ILabelPrinterService` | **STUB** — interface lands in `CCL.MES.Shared`, throws `NotImplementedException` on every call. Empty config block on `/hardware`. Lights up when business owner files a request. |
| `IWeighScaleService` | **STUB** — same as printer. Interface present so future impls fit the existing slots. |

Result: 2 working interfaces + 2 honest stubs. `/hardware` and
`/mode` stop being lorem-ipsum. Operators have a real configurable
device path. Subsequent phases bring printer + scale online without
re-architecting.

---

## 2. Architecture — interface in Shared, impl per-platform partial

The `PHASE10-MAUI-MIGRATION-PLAN.md §5` already specified the
interface shape. P10.3 makes 4 refinements:

### 2.1 Interface placement

Original plan said "Interfaces (in `CCL.MES.Shared`)". We push down
further:

- **`CCL.MES.Shared`**: data DTOs only — `ScanResult`, `PrintRequest`,
  `WeightSample`, `DeviceHealthCheckResult`. No interfaces. Shared
  with API so server-side scan/print/weigh endpoints can read the
  same shapes.
- **`CCL.MES.Hybrid.Client`**: the `I*Service` interfaces themselves.
  Reason: these only make sense client-side. Server has no scanner.
  Adding them to Shared pulls implementation contracts into projects
  that don't need them.
- **`CCL.MES.Hybrid` (host project)**: platform-specific impls
  registered in `MauiProgram.cs` via the existing
  `builder.Services.Add*` pattern, identical to how
  `MauiSecureTokenStore` and `MauiConnectivityMonitor` are wired
  today.

### 2.2 Interface contracts (revised)

```csharp
namespace CCL.MES.Hybrid.Client.Hardware;

public interface IBarcodeScannerService
{
    /// <summary>True if a camera is available + permission granted.
    /// Calling Scan* without IsAvailableAsync() == true surfaces
    /// a structured DeviceUnavailable error instead of throwing.</summary>
    Task<HardwareAvailability> IsAvailableAsync(CancellationToken ct = default);

    /// <summary>One-shot modal-style scan. Returns null on
    /// user cancel. Throws DeviceUnavailableException on no permission.</summary>
    Task<ScanResult?> ScanOnceAsync(CancellationToken ct = default);

    /// <summary>Continuous scan stream (for long-running QC or
    /// label-validation flows). Caller cancels via CT.</summary>
    IAsyncEnumerable<ScanResult> ScanStreamAsync(CancellationToken ct);
}

public interface ILabelPrinterService { /* shape unchanged — STUB only */ }
public interface IWeighScaleService    { /* shape unchanged — STUB only */ }

public interface IDeviceModeService
{
    /// <summary>Current mode for THIS device (not synced from server).</summary>
    DeviceMode CurrentMode { get; }
    Task SetModeAsync(DeviceMode mode, CancellationToken ct = default);
    event Action<DeviceMode>? ModeChanged;
}

public enum DeviceMode
{
    /// <summary>Default. Full sidebar, all routes, no auto-logout.</summary>
    Interactive,
    /// <summary>Single-purpose workstation. Hides sidebar after login,
    /// auto-logout after 5 min idle, forces fullscreen.</summary>
    Kiosk,
    /// <summary>Background scanner-only station — no UI chrome,
    /// scans posted to API via dedicated handler. Reserved for P10.4+.</summary>
    Headless,
}

public sealed record HardwareAvailability(
    bool IsAvailable,
    string? Reason,          // EN code, e.g. "permission_denied", "no_device"
    string? OperatorMessage  // Localised, for banner display
);

public sealed record ScanResult(
    string Code,             // raw payload
    string Format,           // QR, Code128, EAN13, ...
    DateTimeOffset CapturedAt,
    string? SourceDevice     // "back-camera" / "USB-HID:Honeywell-1900" / ...
);
```

### 2.3 Per-platform impl matrix (P10.3 scope)

| Interface | Mac Catalyst (P10.3 SCOPE) | Windows (P10.3 SCOPE) | Android (P10.4+) | iOS (P10.4+) |
| --- | --- | --- | --- | --- |
| `IBarcodeScannerService` | **AVFoundation** via `AVCaptureSession` + `AVCaptureMetadataOutput` (machine-readable codes). Returns one-shot modal page with camera preview. | **Windows.Media.Capture** + ZXing.Net for decode. Plus optional USB-HID via `HidSharp` for wedge replacement. | CameraX (later) | AVFoundation (impl shared with Catalyst, separate registration) |
| `IDeviceModeService` | `Microsoft.Maui.Storage.Preferences` keyed by `device.mode` | Same | Same | Same |
| `ILabelPrinterService` | STUB | STUB | — | — |
| `IWeighScaleService` | STUB | STUB | — | — |

Mac Catalyst camera approach REJECTS MAUI's `MediaPicker.CapturePhotoAsync` — that returns a still frame after a system camera UI flow, not a live decode loop. We need realtime barcode detection, which `AVCaptureMetadataOutput` gives natively. Same call shape Apple already uses on iOS (Catalyst inherits the API surface for AVFoundation).

Windows camera approach: MediaCapture preview frames → ZXing.Net decoder on a background task. ZXing handles all standard barcode formats (QR, Code128, EAN13, DataMatrix); no per-format branching. USB-HID is OPT-IN — operators with existing wedge scanners can leave them in keyboard-wedge mode (which `MacCatalystKeyboardFix` handles transparently because typed input doesn't differ from keystrokes), or switch to Raw HID if they need the "scan while app not focused" behaviour. Implementation tracks the v1.2 `desktop/native/scanner.js` Raw HID approach for the pattern (HID Usage Tables, Honeywell/Symbol/Datalogic compatibility) but in C# via `HidSharp`, not Node.

### 2.4 Partial-class registration in `MauiProgram.cs`

Same shape as P10.1/P10.2 wiring:

```csharp
builder.Services.AddSingleton<IDeviceModeService, MauiDeviceModeService>();

#if MACCATALYST || IOS
builder.Services.AddSingleton<IBarcodeScannerService, CatalystBarcodeScanner>();
#elif WINDOWS
builder.Services.AddSingleton<IBarcodeScannerService, WindowsBarcodeScanner>();
#else
builder.Services.AddSingleton<IBarcodeScannerService, StubBarcodeScanner>();
#endif

// Stubs for not-yet-implemented hardware. They live in the cross-platform
// project and throw a structured DeviceUnavailable on every call. UI uses
// IsAvailableAsync() == false to grey out buttons.
builder.Services.AddSingleton<ILabelPrinterService, StubLabelPrinter>();
builder.Services.AddSingleton<IWeighScaleService, StubWeighScale>();
```

`#if MACCATALYST` is the standard MAUI compile-time platform symbol — no `DeviceInfo.Current.Platform` runtime check needed in the host project (we already use this pattern for `WKWebView.Inspectable` in P10.2 cleanup). The RCL (`CCL.MES.Hybrid.Razor`) stays platform-neutral net10.0 and consumes the registered `IBarcodeScannerService` via DI like any other service.

---

## 3. `/hardware` page — UX + config flow

Today: `Settings/Hardware.razor` is a placeholder. P10.3 replaces it
with a real config page reachable from the sidebar (admin + supervisor
+ engineer roles). Two ways operators land here:

- **Sidebar nav → Settings → Hardware** for routine config check.
- **First-login redirect** — fresh install lands here once before
  going to home, so the operator picks scanner BEFORE the first WO
  scan call would fail with "no device". Skip-allowed with a "Tôi sẽ
  cấu hình sau" link.

### 3.1 Layout

```
HARDWARE — Per-station config

┌── SCANNER ─────────────────────────────────────────────────────┐
│ Trạng thái: ✓ Camera detected (back-camera, 1080p)             │
│   ○ Camera (native)         ← default                          │
│   ○ USB scanner (HID raw)   ← Windows-only, USB-HID list       │
│   ○ Bàn phím wedge          ← any platform, no config          │
│                                                                │
│   [ TEST SCAN ]   → opens modal scanner overlay, shows result │
│                                                                │
│ Permissions:                                                   │
│   Camera ✓ granted   (Last checked: 2026-06-04 09:12)         │
│   Microphone — not requested                                  │
└────────────────────────────────────────────────────────────────┘

┌── PRINTER ─────────────────────────────────────────────────────┐
│   ⚠ Chưa triển khai — sẽ có ở pha P10.5                       │
│   Stub interface đã có sẵn để giữ chỗ.                        │
└────────────────────────────────────────────────────────────────┘

┌── CÂN (SCALE) ─────────────────────────────────────────────────┐
│   ⚠ Chưa triển khai — sẽ có khi quy trình cân được duyệt      │
└────────────────────────────────────────────────────────────────┘

[ LƯU CẤU HÌNH ]      [ KIỂM TRA TOÀN BỘ ]
```

### 3.2 Test-connection semantics

- **Scanner test** opens the same scanner modal that production
  workflows use. Operator scans any code; the modal echoes the
  decoded value + format + capture timestamp. If `IsAvailableAsync()`
  returns false, the button is disabled and the reason is rendered
  inline (e.g. "Quyền camera đã bị từ chối — mở System Settings →
  Privacy → Camera để cấp lại").
- **No silent success.** Every test either shows the decoded payload
  or an error reason — no "passed" badge without operator-visible
  evidence. Mirrors the P10.2 "no silent fail" lesson.

### 3.3 Per-station identity

Each device gets a stable id stored in `Preferences` under
`device.id` — UUID generated on first launch and never rotated.
The id is sent with every API write and every scan-log entry so the
server can attribute hardware events to a station even when multiple
people log in to the same device across a shift.

---

## 4. `/mode` page — kiosk / interactive / headless

### 4.1 Behaviour matrix

| DeviceMode | Sidebar | Idle handling | Default landing | Use case |
| --- | --- | --- | --- | --- |
| **Interactive** | Visible, full nav | None (manual sign-out) | `/` (home tiles) | Office / NPI / supervisor desks |
| **Kiosk** | Hidden after first nav | After N min idle → **lock screen** (NOT full logout); unlock = enter device passcode; session JWT preserved | Configurable per station (e.g. `/wo` for production-floor kiosk) | Shop floor workstation tied to one workflow |
| **Headless** | n/a — no UI | n/a | n/a | **Reserved for P10.4+** — scanner-only station running scan-and-post to API with no operator interaction. Out of scope for P10.3 ship. |

**Idle threshold N is configurable per device** (persisted in `Preferences` under `device.idle.minutes`), default 10 minutes. Floor kiosks pinned to one workflow can tighten to 5 min; final-inspection kiosks where operators step away to physically inspect product can relax to 20 min. The choice belongs to the supervisor configuring that station, not us — `/mode` page exposes a numeric input.

**Lock-screen vs full logout** chosen for two reasons:
1. **Operator UX:** shift change isn't a JWT-rotation event. Operators commonly walk away to pick parts off a rack and come back to the same station; forcing a full re-login + refresh-token rotation every 10 min punishes the common case to defend against the rare case (forgotten unattended session).
2. **JWT lifetime model:** the refresh token is 7-day TTL with one-time-use rotation (P10.1 contract). Every idle-timeout-then-relogin cycle rotates the family. That's expensive on the server (audit row + refresh ledger entry) AND chips at the rotation budget across a shift. Lock-screen leaves the in-memory access token alive (or the MauiSecureTokenStore-cached one) and just gates UI access via passcode prompt. Same security outcome, no rotation cost.

If the operator legit signs out (the Đăng xuất button) OR if the refresh token actually expires (7-day idle), the lock screen falls through to `/login`. **No "forever-stuck-in-lock" failure mode** — passcode wrong N times → forced full logout (configurable N, default 5).

### 4.2 Mode change UX

- Admin-only action. Engineer + below see the page read-only.
- Setting kiosk mode requires entering a passcode (separate from
  user password — purpose is "operator can't kick the station out
  of kiosk by accident"). Passcode persisted per device in
  `Preferences`, not synced to server.
- Mode change is immediate on save — sidebar collapses/expands,
  auto-logout timer arms/disarms. No app restart needed.

### 4.3 Idle → lock-screen implementation

Borrows the existing `IConnectivityMonitor` pattern (timer + event).
New `IIdleMonitor` interface raises `IdleThresholdReached` after N
minutes of no mouse / no keyboard. Kiosk mode subscribes and calls
`Nav.NavigateTo("/lock")` (NOT `/login`). The lock page shows the
station passcode prompt. Correct passcode → `Nav.NavigateTo(previousRoute)`
restores the previous workflow. JWT in `IAuthSession` is untouched.

Reset on any input event (the `MacCatalystKeyboardFix` document-level
keydown listener doubles as the idle-reset trigger — no extra wiring).
Touch / mouse / scroll events bubble to a shared document handler that
calls `IIdleMonitor.NotifyActivity()` and resets the timer.

W1 ships the interface + a stub `MauiIdleMonitor` that never fires
(timer never started). W4 ships the real timer + lock page + passcode
flow. Decoupling lets W1 land safely with no UX change.

---

## 5. Config storage — local vs central (DECISION + RATIONALE)

**Decision: LOCAL (per device) for now, with a documented promotion
path to central for P10.4+.**

| Concern | Local-only (CHOSEN) | Central |
| --- | --- | --- |
| **Setup speed** | Operator configures once at install | Admin pre-configures + ops sees their assignment |
| **Hardware diversity** | Each station picks what it has (cameras differ; one might have USB scanner, another only wedge) | Central needs to know hardware inventory — duplicates ops's job |
| **Lost device** | Reconfigure on replacement device | Reassign in admin UI |
| **Audit trail** | Local event log only | API + audit ledger |
| **P10.3 ship cost** | LOW — just `Preferences` + page bindings | HIGH — new API endpoints + schema + RBAC |
| **Online-only Q4 compliance** | ✓ Pure local, no API dependency | ✓ Reads + writes via API |

**Hardware config is fundamentally about WHAT IS PLUGGED IN HERE.**
That's a property of the device, not the server. The server doesn't
care which camera the operator picked — it only cares about the scan
result. We persist the choice locally with `Microsoft.Maui.Storage.Preferences`:

```csharp
Preferences.Default.Set("device.id", deviceId);
Preferences.Default.Set("device.mode", (int)DeviceMode.Interactive);
Preferences.Default.Set("device.scanner.source", "camera"); // camera | usb-hid | wedge
Preferences.Default.Set("device.scanner.usb-hid.pid", "0x0c2e");
Preferences.Default.Set("device.scanner.usb-hid.vid", "0x05f9");
Preferences.Default.Set("device.kiosk.passcode-hash", argonHash);  // never raw
```

**Forward path (P10.4+):** when the org wants central oversight, add
a `POST /api/v2/devices/{id}/heartbeat` that pushes the local config
+ availability check results upstream. The hardware abstraction
doesn't change — only the storage layer gains a "save to central +
local" branch. Local stays authoritative for "what to use right
now". Central is for "what should we have here".

---

## 6. Permissions per-platform

Mirror the same discipline P10.2 applied to ATS / Keychain:

### 6.1 Mac Catalyst

`Platforms/MacCatalyst/Info.plist` adds:

```xml
<key>NSCameraUsageDescription</key>
<string>CCL MES sử dụng camera để quét mã vạch / QR cho quy trình
production (acceptance WO, sample tracking, IPQC). Quyền chỉ dùng
khi bạn nhấn nút quét — không có background access.</string>
```

Catalyst inherits iOS-style permission flow: first `AVCaptureDevice.RequestAccess` call surfaces the system prompt. Denial path: subsequent calls return immediately with deny status — we surface the operator-visible reason text + a "Mở Settings" deep-link via `Microsoft.Maui.ApplicationModel.AppActions` or fallback to `Process.Start` with the macOS URL scheme. **Test on a fresh install with a fresh user account** — Apple Feedback shows the Catalyst camera consent dialog can fail to appear in certain Provisioning + Sandboxing combinations (related to but distinct from the Keychain entitlement P10.2 hit).

### 6.2 Windows

`Platforms/Windows/Package.appxmanifest`:

```xml
<Capabilities>
  <DeviceCapability Name="webcam" />
  <!-- For Raw HID scanner option (Windows-only) -->
  <DeviceCapability Name="humaninterfacedevice">
    <!-- Filter to USB barcode scanners; left wide to allow any HID -->
    <Device Id="any">
      <Function Type="usage:8c 02" />  <!-- HID Usage Page 8C (Barcode), Usage 02 (Reader) -->
    </Device>
  </DeviceCapability>
</Capabilities>
```

Windows MSIX prompt fires once on first launch. Unpacked dev runs
(`dotnet build -t:Run -f net10.0-windows...`) skip the manifest
prompt; ops sees the production prompt only on the signed installer.
Operator can also revoke via Settings → Privacy & Security → Camera
at any time.

### 6.3 Android / iOS (deferred per Q6)

Specified for completeness only:

- Android: `<uses-permission android:name="android.permission.CAMERA" />`
  in `AndroidManifest.xml`, plus runtime `Permissions.RequestAsync<Permissions.Camera>()`.
- iOS: `NSCameraUsageDescription` (same key as Catalyst) in iOS
  Info.plist plus the same AVFoundation flow.

Not built in P10.3; the strings + manifest entries land in P10.4+
when those targets enter scope.

### 6.4 Failure UX

Every permission denial path surfaces a structured error in the
Hardware page banner + on first scan attempt in any workflow:

```
"Quyền camera chưa được cấp."
"Vào System Settings → Privacy → Camera → bật CCL MES rồi thử lại."
[ Mở Settings ] [ Đóng ]
```

No silent fallback to a no-op scanner — operator MUST know why the
scan didn't happen. P10.2 "silent fail" lesson directly applies.

---

## 7. Online-only data flow (Q4 reaffirmed)

Per Henry's locked Q4: scan / weigh / print results are POSTed
directly to API. No local outbox. If the API is unreachable, the
operator sees the existing `ConnectivityBanner` (already shipped
P10.1) and the scan modal surfaces a "Server không phản hồi — thử
lại sau khi mạng phục hồi" toast. The scan payload is held in
memory ONLY until the API responds OR the user cancels.

### 7.1 New API endpoints (server-side, lands with P10.3)

| Endpoint | Body | Used by |
| --- | --- | --- |
| `POST /api/v2/devices/{id}/scan-log` | `{ code, format, capturedAt, sourceDevice, workflow }` | Every successful scan from any flow. Append-only audit. |
| `POST /api/v2/devices/{id}/heartbeat` | `{ mode, scannerSource, lastSeenAt, version }` | Once on app boot + once on `/hardware` save. Lets ops see which stations are alive. |
| `GET /api/v2/devices/{id}` | — | `/hardware` page reads central record if any (for the future-promotion path; returns 404 today). |

**No new endpoints for printer / scale** — those interfaces are
stubs in P10.3. Endpoints land when impls do.

### 7.2 No outbox

Saying it twice because P10.4 will be tempted to add one: **P10.3
does NOT queue scans.** A failed POST surfaces an error, the
operator decides to retry or abandon. The scan code stays visible
in the modal for the operator to write down if they need to. The
outbox/sync engine is P10.4's job — building a half-version here
adds a maintenance liability without delivering the operator value
(reliable offline scanning needs the full sync subsystem, not a
partial one).

---

## 8. Roadmap + milestones

| Week | Milestone | Verification |
| --- | --- | --- |
| W1 | Interfaces + stubs land in `CCL.MES.Hybrid.Client` and `CCL.MES.Shared`. `MauiProgram.cs` wires placeholders. `/hardware` page renders with "Chưa cấu hình" sections + the SCANNER block shows stub status. | App boots, /hardware route renders, no functional change. |
| W2 | Catalyst camera impl. `AVCaptureSession` + metadata output + permission flow + modal scanner UI. Test-scan button fires real decode. | Real Mac Catalyst hardware: scan a printed QR → see decoded payload in modal. |
| W3 | Windows camera impl (MediaCapture + ZXing). USB-HID option behind feature flag. | Real Windows hardware: same scan test passes; switch between camera + USB-HID, both decode. |
| W4 | `/mode` page + DeviceMode enforcement (sidebar hide, auto-logout, idle monitor). Kiosk passcode flow. Wire scanner into ONE production flow (WO Accept). API endpoints + audit emit. Documentation + Q&A doc update. | 2-station smoke: 1 interactive Mac + 1 kiosk Windows. Accept WO via scan on both. Audit log shows correct device.id + scan source. |
| Buffer | E2E hardware test on a real shop-floor station with operator. Permissions denial path verified. README + screenshots committed alongside `08-home-clean-build.png` lineage. | Henry-driven sign-off. |

Total: **4-5 weeks** (2 of those on Catalyst camera + 1 each on
Windows + UX integration + buffer). Estimate honest because:
- AVFoundation is well-documented but Catalyst-specific quirks
  (the same kind that bit us with `#13934`) likely consume 2-3 days
  beyond pure coding time.
- ZXing.Net is mature but binding it to MediaCapture's frame
  callbacks on net10-Windows is less travelled territory.
- The /mode kiosk auto-logout is straightforward but the `IIdleMonitor`
  per-platform needs both Catalyst (UIApplication idle timer) and
  Windows (last-input-info win32 call) impls.

---

## 9. Questions for Henry (Q1..Q12)

Same format as P10.0. Each question lists options + my recommended
default. Henry confirms via "DUYỆT Q1-Q12 với override Qx = …" — the
override pattern from P10.0 + P10.1.

**Q1 — Scope cut.** Ship scanner real + others stub (recommended) ✓ /
ship all three real (delays P10.3 by ~4 weeks) / ship scanner only
without /mode page.

**Q2 — Scanner default.** Camera-native (recommended) / camera + USB-HID
behind feature flag visible on /hardware. Wedge mode is always
available — no toggle needed because the keystrokes go through
`MacCatalystKeyboardFix` already.

**Q3 — Scanner integration target.** Wire scanner into WO Accept flow
(recommended — biggest operator value, simplest API surface) / wire into
Sample Tracking attach-photo flow / wire into IPQC scan-to-load /
wire into all three (3× the integration work).

**Q4 — Config storage.** Local-only via `Preferences` (recommended) /
central with API endpoints / hybrid (local + heartbeat-only push).

**Q5 — Kiosk passcode.** Per-device passcode persisted in `Preferences`
(recommended; simple, no central state) / role-based gate (any admin
account can unlock; matches existing RBAC) / both (passcode falls back
to role).

**Q6 — Idle handling for Kiosk mode (Henry override 2026-06-03).**
**CONFIRMED: configurable per device on /mode, default 10 minutes,
LOCK SCREEN (not full logout).** Locked operator unlocks with the
device passcode and returns to the previous workflow. Full logout
only on explicit Đăng xuất button OR 7-day refresh-token expiry OR
N consecutive wrong passcode attempts (N=5, configurable). Original
plan recommendation of 5-min full-logout REJECTED by Henry on grounds
that floor operators commonly step away to fetch parts; forcing
re-login + refresh-token rotation every 10 min punishes the common
case and wastes the JWT rotation budget.

**Q7 — First-login `/hardware` redirect.** Force-show `/hardware` once
per install before home (recommended — avoids "no device" surprises
later) / make it admin-only setup, skipped for ops / never auto-redirect.

**Q8 — USB-HID Windows-only or also Mac?** Windows-only (recommended;
Mac scanners typically use Bluetooth or are wedge-mode already, USB-HID
on Catalyst is poorly supported by the inherited iOS APIs) / both.

**Q9 — Scanner modal UX.** Full-screen camera preview with reticle
overlay (recommended; biggest target area, easiest scan) / floating
dialog (less invasive but smaller preview) / sidebar drawer.

**Q10 — Audit emit.** Every successful scan posts to scan-log API
(recommended — audit trail for compliance) / only when scan triggers
state change / no audit, scan is ephemeral.

**Q11 — Headless mode.** Defer to P10.4 (recommended — needs the
offline sync subsystem to be useful) / scope into P10.3 (+1 week).

**Q12 — Camera permission denied flow.** Show banner + deep-link to
Settings (recommended — operator-actionable) / silent retry on next
scan attempt / disable the feature entirely.

---

## 10. Risk register (per-platform, P10.2 lesson-informed)

| Risk | Likelihood | Impact | Mitigation |
| --- | --- | --- | --- |
| **Mac Catalyst camera consent dialog fails to appear** on certain provisioning + sandboxing combos (lesson from Keychain MissingEntitlement) | M | H | (1) Fall-back path that surfaces error + Settings deep-link instead of silent failure. (2) Test matrix includes adhoc-signed + provisioning-profile-signed builds BEFORE go-live. (3) Document a recovery runbook section: "Camera grant didn't prompt — manually enable in System Settings → Privacy → Camera". |
| **AVCaptureMetadataOutput format gaps on Catalyst** — Apple historically excluded some 1D codes on Catalyst that worked on iOS proper | M | M | (1) Spec the formats CCL Vietnam actually uses (production team to provide list). (2) Test each format on Catalyst hardware before P10.3 sign-off. (3) ZXing.Net fallback on Catalyst possible (image frames → decode) but adds 2-3 days; only pull in if Apple coverage gaps real. |
| **Windows MediaCapture frame-drop on lower-end hardware** | L | M | Decode every Nth frame (N tuned via /hardware test page FPS counter). Frame skipping is invisible to operator if average scan time stays under 1.5 s. |
| **Windows USB-HID enumeration triggers UAC prompt on first run** | L | L | Document in setup guide. Prompt is one-time per install. |
| **Kiosk passcode lost** | M | M | Recovery: sign in as admin role on the device, opens /mode, resets passcode. No central reset because no central state. Document in operator manual. |
| **IdleMonitor counts API calls / SignalR pings as activity** | M | M | Only count UIWindow input events (touch / mouse / key), not background work. Test by leaving the kiosk idle while API polls happen — auto-logout MUST fire on schedule. |
| **`MacCatalystKeyboardFix` interferes with USB-HID Raw mode** (Windows only) | L | M | Raw HID bypasses the DOM entirely — it goes through `HidSharp` directly to a C# event handler. Keyboard-fix observer never sees the input. Verified via test in W3. |
| **Camera + scanner-modal race**: operator opens modal twice → two AVCaptureSession instances → camera locks | M | M | Lock at the service layer — `ScanOnceAsync` returns immediately if a session is in progress, with `HardwareAvailability(false, "busy", "Camera đang được dùng")`. UI uses this to grey the second button. |
| **Network drop mid-scan** (Q4 online-only) | H | L | Banner already exists. Modal shows clear "Server không phản hồi" message + retry button. Operator decides next steps. |
| **Catalyst entitlement bombs the camera flow like it bombed Keychain** | M | H | The TokenStore fallback pattern from P10.2 applies: catch the entitlement exception, surface a structured banner with the same operator-actionable text. **Don't fall back to a "fake" scanner** — that's worse than no scanner because the operator thinks scans work when they don't. |
| **Scope creep — printer/scale interfaces tempt mid-sprint implementation** | M | M | Honor the priority-cut in §1. Stubs stay stubs unless a concrete business workflow lands. New workflows trigger a follow-up sprint, not a scope expansion within P10.3. |

---

## 11. Test plan (P10.3 gate)

Mirroring P10.2's evidence-driven verify path:

| Phase | Test | Pass criterion |
| --- | --- | --- |
| W1 stub | Boot app, navigate to `/hardware` | Page renders with 3 sections, scanner block shows "Chưa cấu hình", printer + scale show "Sẽ có ở pha sau". No errors. |
| W2 Catalyst scan | Test-scan button on `/hardware` | Real QR/Code128 printed code decodes within 2s; modal shows payload + format + capturedAt; API receives `/scan-log` POST with `sourceDevice="back-camera"` |
| W2 Catalyst deny | Revoke camera in System Settings, retry test-scan | Modal shows operator-visible "Quyền camera chưa được cấp" + Settings deep-link. No silent failure. |
| W3 Windows scan | Same as W2 on Windows hardware | Same pass criteria + USB-HID toggle works for an attached HID scanner |
| W4 /mode kiosk | Set kiosk, wait 5 min idle | Sidebar hides on first nav, auto-logout fires after threshold, /login page restored |
| W4 /mode passcode | Try to unlock kiosk with wrong passcode | Banner shows "Mật mã không đúng" — no exception leaked |
| W4 WO Accept integration | Production WO accept flow uses scanner | Scan completes acceptance with audit row carrying device.id + scan source |
| Buffer E2E | 8-hour shop-floor test with real operator | No crash, no leaked error banners, scan time ≤2s 95p, kiosk auto-logout fires correctly, no manual restarts needed |
| Buffer signed build | Re-test all of the above on a code-signed build (post-go-live entitlements) | Keychain fallback no longer fires (signed has Keychain access); camera consent dialog appears on first launch in production install path |

Evidence shipped under `docs/p10.3-screens/`, same convention as P10.2 (`01-hardware-page.png`, `02-test-scan-result.png`, `03-permission-denied.png`, `04-kiosk-mode.png`, `log-01-scan-log-api.json`, etc.).

---

## 12. Out of scope (explicit, so we don't ship them by accident)

- Offline scan queue / outbox / sync (P10.4).
- Label printer real impl (P10.5 or when business owner files request).
- Weigh scale real impl (P10.5+).
- Android + iOS targets (Q6 lock).
- Server-side device registry UI for admins ("see all stations + their
  hardware") — central storage path documented in §5, UI lands when
  Q4 promotion happens.
- Multi-camera selection on Catalyst (default to back-camera, single
  camera is the common case; advanced operators can change in
  System Settings).
- Voice-driven barcode entry (out of scope; phone-only feature).
- Custom decode rules per workflow (e.g. "this scan must be a WO code
  starting with WO-") — UI shows raw decoded value, the workflow page
  validates. Keeping decode generic stays cheap.

---

## 13. Approval flow

1. Henry reads + approves (or sends overrides for Q1..Q12).
2. We open `feat/p10.3-hardware-foundation` branch.
3. W1 PR lands the interfaces + stubs.
4. W2/W3 PRs land Catalyst + Windows scan impl behind a feature flag
   `OPS_FEATURE_HARDWARE_SCAN` (default off).
5. W4 PR flips the flag, wires WO Accept, ships /mode page.
6. P10.3 ship = evidence README + screenshots committed; PR merged
   to main; tag `p10.3-ship`.

No code lands until §9 Q1..Q12 are confirmed. **STOP** for review.
