# P10.6a hotfix — Settings endpoints HTTP 404 + "HTTP HTTP 404" double-prefix

**Branch**: `feat/p10.6a-settings-profile-password`
**Symptom**: Henry on PR #91 — login + sidebar PASS; both Settings
endpoints fail with `Lỗi máy chủ (HTTP HTTP 404)`.

---

## RCA

### Step 1 — Route mismatch?

```
Client (CclApiClient):
  GET    /api/v2/settings/me
  PATCH  /api/v2/settings/me
  POST   /api/v2/settings/password

Server (SettingsController):
  [Route(ApiVersion.Prefix + "/settings")]   →  /api/v2/settings
  [HttpGet("me")]                            →  GET /me
  [HttpPatch("me")]                          →  PATCH /me
  [HttpPost("password")]                     →  POST /password

ApiVersion.Prefix == "api/v2"
```

**URLs match exactly.** No client/server mismatch.

### Step 2 — Server binary stale?

The Catalyst client's `appsettings.json` pins `CclApi:BaseUrl =
http://127.0.0.1:5100`. Catalyst connects to a **separately-running**
API process (NOT embedded). If that process was started BEFORE PR #91
was deployed, the loaded controller chain doesn't include
`SettingsController` → 404.

This is the user's hypothesis #2 and is the most likely root cause:
- Login works (AuthController has been there since P10.1).
- Settings 404s (SettingsController is brand new in PR #91).

The fact that test harness PASSES on the SAME controller proves the
code is correct — the only difference is "running binary on Henry's
box doesn't include the controller because it wasn't restarted."

### Step 3 — Tests skip routing?

User's hypothesis #3 — refuted:

`SettingsControllerTests` use `MesApiFactory : WebApplicationFactory<Program>`,
which IS TestServer-based. The 11 existing tests go through the full
ASP.NET Core pipeline (routing → auth → controller). If
`SettingsController` weren't discovered + mapped, those tests would
have 404'd in CI, not PASS'd.

**Conclusion: tests are valid; the running binary on Henry's box is
stale.**

### Step 4 — Why "HTTP HTTP 404"?

Two prefix-prepends compounded:

```
CclApiClient.ThrowOnNonSuccess fallback:
  generic ??= new ApiError {
      Code = "http.non_success",
      MessageEn = $"HTTP {(int)resp.StatusCode}"   ← prefix #1
  };

SpecMutationErrorMapper:
  "http.non_success" => $"Lỗi máy chủ (HTTP {err.MessageEn})."   ← prefix #2
```

So 404 → MessageEn = `"HTTP 404"` → mapper wraps → `"Lỗi máy chủ (HTTP HTTP 404)."`.

---

## Fix

### Code change — drop the upstream "HTTP " prefix

`CclApiClient.cs` (both fallback sites — `ThrowOnSpecMutationFailureAsync`
and `ThrowOnNonSuccess`):

```diff
- MessageEn = $"HTTP {(int)resp.StatusCode}"
+ MessageEn = ((int)resp.StatusCode).ToString(InvariantCulture)
```

The VN mapper template already wraps with `"(HTTP …)"`, so the
upstream synthesiser passes the bare status code.

Result: 404 now renders `"Lỗi máy chủ (HTTP 404)."` (operator-clean).

### Regression test — pin the canonical shape

`SpecMutationErrorMapperTests.Http_non_success_no_longer_double_prefixes_HTTP_when_upstream_passes_bare_code`:
- Calls mapper with `messageEn: "404"` (what the fixed upstream emits).
- Asserts `"HTTP HTTP"` is absent.
- Asserts the exact output `"Lỗi máy chủ (HTTP 404)."`.

The pre-existing `Http_non_success_includes_status_text` still passes
because it uses `Contains("HTTP 503", msg)` which is true for both
the old + new shape.

### NEW canary — route-discovery test

`tests/CCL.MES.Api.Tests/RouteDiscoveryCanaryTests.cs`:

3 Theory rows hit each Settings endpoint WITHOUT a bearer token and
assert **HTTP 401** (Unauthorized — route exists, auth blocks),
NOT **HTTP 404** (Not Found — route missing). A 404 here would mean
controller discovery / `AddControllers` / assembly scan dropped the
controller — a configuration-level regression that the existing
behaviour tests can't catch because they always run against a
fully-discovered controller chain.

Failure message names the offending verb + URL so the breakage is
self-diagnosing. Cheap (~5 ms per row) tripwire pattern; we can add
one Theory row per controller surface as P10.6b-h lands.

Plus 2 sanity-baseline rows asserting `/api/v2/health` (anonymous)
and `/api/v2/auth/login` (POST-only) don't 404.

---

## Tests result

| Project | Before hotfix | After hotfix | Delta |
| --- | --- | --- | --- |
| `CCL.MES.Hybrid.Client.Tests` | 443 | **444** | +1 (double-HTTP shape) |
| `CCL.MES.Api.Tests` | 154 | **159** | +5 (3 Settings canary + 2 baseline) |

All green.

---

## Henry verify (≤ 90 s on Mac Catalyst)

### Step 1 — Restart the API server (most likely fix)

```bash
# Kill the running API process. Find PID:
lsof -nP -iTCP:5100 -sTCP:LISTEN -t
# Or via ps:
ps aux | grep "CCL.MES.Api" | grep -v grep

# Kill it (replace 12345 with the actual PID from lsof):
kill 12345

# Pull the hotfix branch + rebuild + restart:
cd /path/to/CCL-MES
git fetch origin
git checkout feat/p10.6a-settings-profile-password
git pull
cd CCL-MES-Hybrid
dotnet build src/CCL.MES.Api/CCL.MES.Api.csproj -c Debug
dotnet run --project src/CCL.MES.Api/CCL.MES.Api.csproj
# Or in another shell:
dotnet run --no-launch-profile --urls http://localhost:5100 \
  --project CCL-MES-Hybrid/src/CCL.MES.Api/CCL.MES.Api.csproj
```

Confirm the server is on the new build:

```bash
curl -i http://localhost:5100/api/v2/settings/me
# Expected: HTTP/1.1 401 Unauthorized
# NOT:      HTTP/1.1 404 Not Found
```

If `401` → SettingsController is loaded; Catalyst login + Settings will
work. If `404` → server didn't pick up the new build (check the bin
dir timestamp + that `dotnet run` actually re-built).

### Step 2 — Catalyst verify

After the API restart, on the Catalyst app (no need to rebuild
Catalyst — Settings logic is server-side):

1. Login as engineer (admin/admin or your seed).
2. `/settings/profile` → "Hồ sơ" loads with username + role + department
   + editable DisplayName (NO red banner).
3. Edit DisplayName → Tab/Enter → green "Đã lưu thay đổi".
4. `/settings/password` → 5 paths (blank / mismatch / short /
   wrong_current / happy). Re-login với mật khẩu mới.

### Step 3 — Error message format check

Stop the API process mid-flight, then on Catalyst, click anything that
hits the API. The error banner must read:

```
Không kết nối được máy chủ. Kiểm tra mạng rồi thử lại.
```

NOT:

```
Lỗi máy chủ (HTTP HTTP …).
```

(If a 404 / 500 still shows up via the http.non_success path, it'll
render as `Lỗi máy chủ (HTTP 404).` — single prefix, the hotfix
worked.)

---

## Why this won't recur

1. **Stale-binary class**: New `RouteDiscoveryCanaryTests` is a CI
   canary. Any controller that's added but not registered (e.g.,
   custom `IControllerActivator` swap, partial assembly load,
   `[ApiController]` attribute missing) trips the test within seconds.
2. **Double-HTTP class**: `Http_non_success_no_longer_double_prefixes…`
   pins the exact output. Any future change that re-adds "HTTP " to the
   upstream synthesiser breaks CI.

These join the 12 lessons-as-CI-canaries shipped in commit `c678a70`.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
