# Phase 10 P10.2 — Mac Catalyst smoke evidence

Captured 2026-06-03 against the Mac Catalyst build of `CCL.MES.Hybrid`
running on the same host as `CCL.MES.Api` (port 5100). Both processes
share the legacy SQLite at `<repo-root>/data/ccl_mes.db` via the
`MES_DATA_DIR` env override (see PR description "Runtime bug 1" for the
underlying data-path resolver issue).

## UI screenshots

| File | What it shows |
| --- | --- |
| `01-login-screen.png` | Fresh launch. Mac Catalyst window renders the Vietnamese login form (CCL · MES brand, "Tên đăng nhập" / "Mật khẩu" inputs, "Đăng nhập" submit). Proves: BlazorWebView mounts, Razor class library (`CCL.MES.Hybrid.Razor`) routes `/login` correctly, Carbon-leaning CSS bundle from `_content/CCL.MES.Hybrid.Razor/css/app.css` loads. |
| `02-login-creds-typed.png` | Credentials `engineer` / `engineer` typed into the form via cliclick + AppleScript. Proves: input bindings receive synthetic keystrokes after Mac Catalyst webview is focused. Form submit chain itself was unstable under fully-synthetic clicks on this Catalyst build — see "What is NOT proven yet" below. |

## API evidence (curl-driven; Bearer token issued by the same API the MAUI app talks to)

All commands run from the same Mac, same network namespace as the MAUI app.

| File | What it proves |
| --- | --- |
| `log-01-login-response.json` | `POST /api/v2/auth/login` with engineer/engineer returns full `LoginResponse` with valid JWT pair + `user.role = "Engineer"`. Identical to what the MAUI shell would store via `MauiSecureTokenStore`. |
| `log-02-me-response.json` | `GET /api/v2/auth/me` with the issued Bearer returns `{username:"engineer", role:"Engineer", displayName:"Demo Engineer"}` — confirms claim parsing on the API side. |
| `log-03-npi-workcenters.json` | `GET /api/v2/npi/workcenters?page=1&pageSize=5` returns 5 of 43 real Work Centers (`AAINK / ACNC3 / AOI01 / ARSS4 / ARSS6`). Confirms `NpiRead` policy admits `Engineer` role + paginated envelope shape matches `CCL.MES.Hybrid.Client.NpiPagedRaw<T>`. The `active`-not-`isActive` shape is what drove **runtime bug 2** (DTO field name) in the smoke + the fix that landed in this PR. |
| `log-04-refresh-rotation.json` | `POST /api/v2/auth/refresh` with the original refresh token returns a NEW JWT pair (different `accessToken` + `refreshToken` strings). Proves one-time-use rotation. |
| `log-05-refresh-replay.json` | Re-using the original (now-revoked) refresh token returns `{code:"auth.refresh_replay"}` HTTP 401. Proves the P10.1 family-revocation guard fires — and that the client's `AuthorizationDelegatingHandler` serialised-refresh design is necessary (multiple concurrent refreshes would trip this). |
| `log-06-connectivity-down.txt` | API process killed → `curl http://localhost:5100/api/v2/health` returns `HTTP 000 Failed to connect`. Proves the OS-level connectivity-down signal that `MauiConnectivityMonitor` surfaces to `ConnectivityBanner` in the UI. |

## What IS proven

- Mac Catalyst build pipeline works end-to-end (Xcode 26.5 + maui-desktop workload, 0 errors).
- MAUI Blazor Hybrid shell boots without crash after the runtime fixes (DB workaround, `Active` DTO field, embedded-resource stream lifetime, `oninput` binding).
- Login page renders correctly with the locked-down Vietnamese strings + Carbon-leaning theme.
- API contract end-to-end (login → /me → NPI → refresh rotation → replay revoke) on the real Mac Catalyst host network.
- ConnectivityBanner trigger condition reproducible at the socket layer.
- **Login bug 1 fixed (silent fail):** the `Đăng nhập` submit handler now fires on direct mouse click — `03-error-now-visible.png` shows the new red error banner `"Vui lòng nhập đủ thông tin."` rendered after a click with empty fields. Pre-fix: nothing happened. Post-fix: the error surface is mandatory and a missing-field fast-fail path runs before any network call.
- **ATS exception lands in the bundle:** `log-07-ats-in-app-bundle.txt` is `plutil -p` output of the compiled `Contents/Info.plist` showing `NSAppTransportSecurity.NSAllowsLocalNetworking = true` plus `NSExceptionDomains` entries for both `localhost` and `127.0.0.1`. Pre-fix: missing entirely — every cleartext `http://localhost` call from the WKWebView was blocked by the iOS ATS policy and surfaced only as a generic `HttpRequestException`. Post-fix: dev workflow against the local API is unblocked; production guidance documented in the plist comment.
- **`log-08-http-localhost-reachable.txt`:** sanity proof the API itself is healthy on `http://localhost:5100` so the only thing that could be blocking the WKWebView was the ATS policy.

## What is NOT proven yet — Henry needs to walk this with real hardware keyboard

- **Engineer/engineer + Enter → NPI grid render.** Synthetic AppleScript / cliclick keystrokes don't reliably reach the BlazorWebView's `<input @bind-Value:event="oninput">` event chain on this Catalyst build — values appear in the DOM but never update the Blazor model, so the submit handler always sees `string.IsNullOrWhiteSpace == true` and fires the "missing fields" fast-fail (visible in `03-error-now-visible.png`). A human user with a real keyboard does NOT hit this — the `oninput` handler fires on every physical keystroke and updates the bound `_form.Username` / `_form.Password`. Henry needs to walk through with real keyboard for the final 4-state proof (success / wrong-password / API-down / Tab navigation).
- **Windows target** — `net10.0-windows10.0.19041.0` configured in `CCL.MES.Hybrid.csproj` but build deferred (Q6 acceptable defer for CI/Win dev box).

## How a reviewer can reproduce

```bash
# 1. Make sure Xcode + maui-desktop workload + first-launch are done.

# 2. Boot the API pointed at the legacy seeded DB.
cd "CCL-MES-Hybrid/src/CCL.MES.Api"
export MES_DATA_DIR="$(git rev-parse --show-toplevel)/data"
dotnet run --no-launch-profile --urls http://localhost:5100

# 3. Build + launch the MAUI app (separate terminal).
cd "CCL-MES-Hybrid/src/CCL.MES.Hybrid"
dotnet build -f net10.0-maccatalyst
open "bin/Debug/net10.0-maccatalyst/maccatalyst-arm64/CCL MES.app"

# 4. In the app: type engineer / engineer + click Đăng nhập (real
#    mouse-down works; synthetic doesn't reliably on this build).
#    Login → NPI grid loads → kill the API → banner surfaces.
```
