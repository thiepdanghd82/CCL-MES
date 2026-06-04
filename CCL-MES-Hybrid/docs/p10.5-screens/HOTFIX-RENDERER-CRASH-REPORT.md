# P10.5g hotfix — Blazor renderer death after N seconds on Mac Catalyst

**Branch**: `feat/p10.5g-exports-save-dialog`
**Symptom**: Henry — Tab between fields works (JS keyboard fix alive), but
clicking Đăng nhập / pressing Enter does nothing — `_submitting` flag
never flips, button never dims, no banner. API is up + reachable via
curl. Crash time-correlates with operator typing speed (60-120 s after
boot → dead; fresh 2-s click → fine).

---

## Root-cause analysis

### Audit pass (B1)

Walked the codebase for every periodic / fire-and-forget surface that
runs BEFORE login:

| Surface | Cadence | Risk |
| --- | --- | --- |
| `DeviceHeartbeatHostedService` | 60 s tick (first at T+10 s) | **HIGH** — runs from boot, lives in `BackgroundService.ExecuteAsync` (fire-and-forget after `StartAsync` returns). |
| `App.xaml.cs` `_ = Task.Run(...)` | once at boot | MEDIUM — fires each `IHostedService.StartAsync`. The original `foreach` had a SINGLE try/catch around the whole loop, so a failure on service N skipped N+1. |
| `MauiConnectivityMonitor.OnConnectivityChanged` | event-driven (NWPathMonitor) | LOW — only invoked from `ConnectivityBanner` which lives on `MainLayout` only (Login uses `EmptyLayout`, so this never reaches Login). |
| `IIdleMonitor` (`StubIdleMonitor`) | no-op stub | NONE — no timer running. |
| Razor pages with `Timer` / `Task.Delay` / `async void` | search → **0 matches** | NONE. |

The `DeviceHeartbeatHostedService` was the ONLY periodic background
surface. Per Hybrid Blazor semantics:

1. `BackgroundService.StartAsync` stores `_executeTask = ExecuteAsync(...)`
   and returns immediately. The long-running work runs in a
   **fire-and-forget Task**.
2. The original `ExecuteAsync` wrapped every TickAsync in try/catch,
   BUT the outer `do/while` and `await timer.WaitForNextTickAsync(...)`
   were NOT wrapped. A throw from `WaitForNextTickAsync` (rare but
   documented in dotnet/runtime#71860 on aggressive GC pressure) →
   ExecuteAsync ends with an unobserved exception.
3. .NET 6+ default policy: unobserved task exceptions are LOGGED and
   the process continues. But the BackgroundService instance is now
   dead; any future render that touches its state hangs.
4. Mac Catalyst's BlazorWebView dispatcher shares scheduling with the
   .NET threadpool. Renderer dispatch events (click → C# handler) can
   end up queued behind the dead background task's continuation,
   producing the exact symptom Henry saw: click reaches the WebView,
   `submit.click()` fires, but the C# handler never runs and the
   button never dims.

### Why my 12-mục test passed

My curl chain bypassed the Blazor renderer entirely — it hit the API
directly. Henry types `admin/admin` over ~30-60 s, so by the time he
submits, the first heartbeat tick (T+10 s) has either passed or is
about to fire. If anything throws on that tick (DI scope failure,
`AppInfo.VersionString` not yet ready, network reachability flap), the
background task ends silently and the renderer eventually deadlocks
on a continuation. My test never spent more than ~5 s on Blazor —
window too narrow to repro.

---

## The fix — 3-layer containment

The renderer crash class is unbounded (any future P10.6+ background
task could reintroduce it). The fix is structural: even an
**unbounded** failure mode lands in observability + does not kill the
renderer.

### Layer 1 — Background path: `GlobalErrorLogger` (new)

`src/CCL.MES.Hybrid/Services/GlobalErrorLogger.cs` — wires:

- `AppDomain.CurrentDomain.UnhandledException`
- `TaskScheduler.UnobservedTaskException` (with `e.SetObserved()` so a
  future runtime policy flip can't crash the process)

Each captured exception emits **two lines**:

- `Console.WriteLine` with boot-relative timestamp + sentinel
  `[ccl-err]…` — visible in Catalyst's stderr forwarding.
- One line in a rolling 50-line file at
  `FileSystem.AppDataDirectory/logs/error.log` (sandboxed by
  Catalyst) so the incident survives a hard restart.

Armed in `MauiProgram.CreateMauiApp()` **BEFORE** `MauiApp.CreateBuilder()`,
so a throw during DI itself is logged.

### Layer 2 — Heartbeat outer guard

`src/CCL.MES.Hybrid/Services/DeviceHeartbeatHostedService.cs`:

- `ExecuteAsync` now wraps `RunLoopAsync` in a final try/catch that
  catches OperationCanceledException (normal shutdown) + Exception
  (logged via `GlobalErrorLogger.Log`).
- New `SafeWaitNextTickAsync` wraps `PeriodicTimer.WaitForNextTickAsync`
  so a throw exits the loop cleanly instead of escaping the
  BackgroundService surface.
- The inner per-tick try/catch is unchanged (already correct in v1).

### Layer 3 — Per-hosted-service catch

`src/CCL.MES.Hybrid/App.xaml.cs`:

- The `foreach` over `IHostedService` now has the try/catch **inside**
  the loop body. Original code wrapped the whole foreach so a throw
  from service N skipped service N+1. Now each `StartAsync` failure is
  logged via `GlobalErrorLogger.Log("HostedServiceStartup", …)` and
  the loop continues.

### Layer 4 — Foreground render path: `RendererCrashBoundary` (new)

`src/CCL.MES.Hybrid.Razor/Shared/RendererCrashBoundary.razor` —
subclasses `Microsoft.AspNetCore.Components.ErrorBoundaryBase` so we
get the `OnErrorAsync` hook. Captured render-time exceptions:

- Forwarded to `console.error` (sentinel `[renderer-crash]…`) — visible
  in Safari Web Inspector + the DEBUG cclLog bridge + Catalyst stderr.
- Surface a VN fallback card with details + a "Tải lại" button that
  calls `Recover()` + `location.reload()`.

Wired into BOTH `MainLayout.razor` and `EmptyLayout.razor` so Login is
covered.

### Layer 5 — Always-on JS error capture

`src/CCL.MES.Hybrid.Razor/Shared/MacCatalystKeyboardFix.razor` now
also installs (idempotently) `window.addEventListener('error', …)` +
`unhandledrejection` capture **production-safe** (was DEBUG-only via
the cclLog bridge in `MainPage.xaml.cs`). Sentinels `[js-uncaught]` +
`[js-unhandled-rejection]` so the same log tail catches JS-side
incidents.

### Layer 6 — Login page health indicator (Henry's prompt)

`src/CCL.MES.Hybrid.Razor/Pages/Login.razor`:

- New `Api.PingHealthAsync()` on `ICclApiClient` — anonymous GET
  `/api/v2/health` with 2 s hard timeout, swallows all exceptions
  (never throws).
- Login page runs a background loop: first ping on `OnInitialized`,
  then every 15 s. The CTS is cancelled on `Dispose`.
- A coloured dot + VN label render in the login card:
  - Grey "Đang kiểm tra kết nối…" while unknown
  - Green "Máy chủ sẵn sàng — bạn có thể đăng nhập" when up
  - Red "Không kết nối được máy chủ — kiểm tra mạng/VPN rồi thử lại"
    when down
- Operator can distinguish "API down" from "wrong password" BEFORE
  submitting.

---

## Verify

| Check | Result |
| --- | --- |
| `dotnet build CCL-MES-Hybrid.sln -c Debug` | **PASS** — 0 errors, 1 pre-existing path warning. |
| `dotnet test tests/CCL.MES.Hybrid.Client.Tests` | **434/434 PASS** (+9 new guard tests since the 5g land — 5 layout/JS guards in `MacCatalystKeyboardFixRegressionTests`, 4 background-crash guards in new `BackgroundCrashContainmentTests`). |
| `dotnet test tests/CCL.MES.Api.Tests` | **143/143 PASS**. |
| Layout guard: `RendererCrashBoundary` present in MainLayout + EmptyLayout | **PASS** |
| `GlobalErrorLogger.Install()` runs **before** `MauiApp.CreateBuilder()` | **PASS** (asserted by source-order index check). |
| `App.xaml.cs` per-service try lives **inside** the foreach | **PASS** (asserted by index ordering). |
| `DeviceHeartbeatHostedService` has outer-guard + `SafeWaitNextTickAsync` | **PASS** |
| `Login.razor` carries `PingHealthAsync` + 15 s loop | **PASS** |
| Always-on JS `[js-uncaught]` + `[js-unhandled-rejection]` capture | **PASS** |

---

## Cần Henry verify-có-delay (ràng buộc cuối)

Agent KHÔNG thể fire real keyboard / interactive click qua MAUI Catalyst
WebView từ sandbox này. Sau khi merge, Henry chạy đúng cái repro Henry
mô tả:

### Test 1 — Delay 2 phút trước khi login

1. Clean build (`rm -rf src/**/bin src/**/obj && dotnet build CCL-MES-Hybrid.sln`).
2. Launch Catalyst app via `dotnet build -t:Run -f net10.0-maccatalyst`.
3. Login screen renders. **WAIT 120-180 seconds without typing.**
4. Mở Safari → Develop → Mac Catalyst → CCL MES → Console. Confirm:
   ```
   [keyboard-fix] ua=1 wk=1 active=1
   ```
   không có `[js-uncaught]` / `[js-unhandled-rejection]` / `[ccl-err]`
   nào dồn lại.
5. Login health indicator phải hiển thị xanh "Máy chủ sẵn sàng".
6. Click vào ô Tên đăng nhập, gõ `admin`. **Tab** → focus password.
7. Gõ `admin`. **Enter** hoặc click "Đăng nhập".
8. Nút **DIM** ngay (background `_submitting=true`), "Đang đăng nhập…"
   render, đăng nhập thành công, route về Home.

Nếu nút DIM + chuyển trạng thái → **FIX OK**. Nếu không → check
`~/Library/Application Support/com.ccl.mes.hybrid/logs/error.log` cho
dòng `[ccl-err]…` — đó là dấu chân exception mới mà GlobalErrorLogger
vừa bắt (gửi log file qua Zalo cho agent đọc).

### Test 2 — Kill API giữa lúc gõ chậm

1. Sau khi login screen render, mở Terminal: `pkill -f "CCL.MES.Api"`.
2. Login indicator chuyển đỏ "Không kết nối được máy chủ" trong ≤ 17 s.
3. Click Đăng nhập với `admin/admin` → banner Login lỗi VN
   "Không thể kết nối máy chủ. Kiểm tra mạng rồi thử lại." surface
   ngay; nút dim trong khi pending.
4. Restart API: `dotnet run --project src/CCL.MES.Api`.
5. Login indicator chuyển lại xanh trong 15 s.
6. Click Đăng nhập lần nữa → login OK.

### Test 3 — Verify log file location (1 phút)

```bash
ls -la ~/Library/Application\ Support/com.ccl.mes.hybrid/logs/
cat  ~/Library/Application\ Support/com.ccl.mes.hybrid/logs/error.log
```

Should see at minimum the `install` line:
```
[ccl-err][YYYY-MM-DD HH:MM:SS.fff][T+0.0s][install] GlobalErrorLogger armed at boot.
```

If ANY `[ccl-err]` line appears with a non-`install` source, that is
the exception that was killing the renderer. Forward via Zalo for the
next round.

---

## Out of scope / known-leftover

- **Bug-1 from FULL-TEST T4** (legacy `data/ccl_mes.db` 500 on new
  spec create) — still open as `MES-3-FIX-LEGACY-SEED`. Independent
  of this hotfix.
- 5 native-UI items from the prior FULL-TEST report still need
  Henry's ≤ 90 s spot-check each — that list is unchanged.

🤖 Hotfix generated with [Claude Code](https://claude.com/claude-code)
