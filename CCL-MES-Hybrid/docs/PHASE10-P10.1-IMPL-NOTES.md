# Phase 10 — P10.1 implementation notes

Companion to [`PHASE10-MAUI-MIGRATION-PLAN.md`](PHASE10-MAUI-MIGRATION-PLAN.md). Captures
the design decisions and trade-offs that landed in the first PR, so future
phases (or auditors) don't have to re-derive them.

## Scope shipped

- `CCL-MES-Hybrid.sln` — new top-level solution. Legacy `CCL.MES.sln` at the
  repo root is untouched and still builds the production web app.
- `src/CCL.MES.Shared/` — POCO DTOs + envelopes. No EF, no HTTP, no Blazor.
- `src/CCL.MES.Api/` — ASP.NET Core 10 Web API on port `5100` (legacy keeps `5000`).
  - JWT bearer auth (HS256), access 15 m / refresh 7 d, one-time-use refresh
    rotation with family revocation on replay.
  - In-memory `IRefreshTokenStore` (P10.1 only — persistent storage deferred).
  - RBAC policies ported 1:1 from `src/CCL.MES.Web/Program.cs:177-199`
    (`AdminOnly`, `NpiRead`, `NpiSpecRead`, `QcRead`) for the Bearer auth scheme.
  - 12 controllers covering 9 legacy Application services + audit-log read
    + health probes (read-only surface; mutation endpoints land as MAUI
    needs them).
  - `ShopfloorHubV2` mirror of the legacy hub at `/hubs/shopfloor` with
    query-string JWT for the WebSocket handshake.
- `tests/CCL.MES.Api.Tests/` — 26 xUnit tests (integration + unit).

## What we deliberately did NOT do

- Touch any file under `src/CCL.MES.{Domain,Application,Infrastructure,Web}/`.
  Verified via `git diff main -- src/ tests/ docs/` returning empty.
- Add EF migrations. Refresh-token persistence is in-memory; no schema
  changes against the live database. A→B→C rule preserved.
- Expose mutation endpoints speculatively. WorkOrder.Create, Spec.Approve,
  Drawing.Upload, etc. land in later phases once the MAUI client surfaces
  the demand.
- Ship the legacy `/api/audit-export` mirror — the legacy controller still
  works via cookie auth and there's no MAUI consumer for it yet.

## Key design decisions

### Code-sharing via relative `<ProjectReference>` (Option A)

`CCL.MES.Api.csproj` references the legacy projects through their relative
paths:

```xml
<ProjectReference Include="..\..\..\src\CCL.MES.Domain\CCL.MES.Domain.csproj" />
<ProjectReference Include="..\..\..\src\CCL.MES.Application\CCL.MES.Application.csproj" />
<ProjectReference Include="..\..\..\src\CCL.MES.Infrastructure\CCL.MES.Infrastructure.csproj" />
```

Side effect (expected): `dotnet build` on the new solution produces `bin/`
and `obj/` artifacts under the legacy `src/` folders. Those paths are
already covered by the repo `.gitignore` (`bin/` + `obj/` patterns) so
no working-tree noise.

### JWT claim shape parity with legacy cookie

Login emits the same five claim types the cookie path emits at
`src/CCL.MES.Web/Pages/Login.cshtml.cs:101-112`:

- `ClaimTypes.NameIdentifier` (User.Id as string)
- `ClaimTypes.Name` (Username)
- `ClaimTypes.Role` (User.Role)
- `display_name` (User.DisplayName fallback to Username)
- `department` (User.Department fallback to "")

Plus `JwtRegisteredClaimNames.Jti` (GUID per token) so rotated access tokens
in the same second don't serialize identically.

### Generic-error policy on login failures

Wrong username, wrong password, and disabled account all return the same
`ApiError { Code = "auth.invalid_credentials" }`. Matches legacy
`Login.cshtml.cs:69-83` behaviour and ensures the API isn't a username
probe oracle. Audit rows still distinguish the three cases (`LoginFail`
vs `LoginDisabled`).

### Refresh-token rotation + replay detection

- Login mints `{access, refresh, familyId}` and stores the refresh in
  `InMemoryRefreshTokenStore`.
- Refresh validates the supplied token: if expired or unknown → 401. If
  revoked → 401 PLUS revoke the whole family (`RevokeFamily(familyId)`).
  Otherwise revoke the supplied token and mint a fresh pair under the
  same family.
- Logout revokes the supplied refresh token only.

Family revocation is what catches a leaked refresh-token: the legitimate
client and the attacker each try to use the same supplied refresh. The
first one succeeds and the original becomes revoked; the second hits a
revoked token and triggers the family-revocation defence. Both parties
get bumped to login.

### Database resolution order

Program.cs resolves the SQLite connection string with priority:

1. `ConnectionStrings:Default` already set in configuration — respect it
   (the test factory uses this path).
2. `MES_DB_PATH` env var.
3. `MES_DATA_DIR` env var + `/ccl_mes.db`.
4. `<repo-root>/data/ccl_mes.db` default — same folder the legacy web
   app uses, so parallel operation reads/writes the same DB.

The "explicit ConnectionStrings:Default wins" rule was a hard requirement
for `MesApiFactory` to give each xUnit class an isolated SQLite file
without env-var leakage between parallel test factories.

### SignalR hub auth via query-string token

`OnMessageReceived` handler in the JwtBearer config sniffs
`?access_token=` when the path starts with `/hubs`. Standard SignalR
pattern — browsers can't set custom headers on the WebSocket negotiate.

## Tests (26 total, all green)

- `HealthControllerTests` (3) — anonymous health probes + protected 401.
- `AuthControllerTests` (7) — login happy/sad paths, /me, refresh rotation,
  refresh-replay family revocation, logout-revokes.
- `RbacTests` (8) — AdminOnly, NpiRead, NpiSpecRead, QcRead, fallback policy.
- `SignalRHubTests` (2) — hub negotiate without/with query-string token.
- `Unit/InMemoryRefreshTokenStoreTests` (5) — pure unit coverage for the
  store: store/find, revoke, RevokeFamily fan-out, PurgeExpired.

Each integration test class gets its own `MesApiFactory` instance, which
pins its own `/tmp/ccl-mes-api-test-<guid>/test.db` and applies all live
EF migrations during `IAsyncLifetime.InitializeAsync`. Mirrors the
Phase 9 `IsolatedDbFixture` pattern.

## Drift guardrails — passes

- Running the new solution build does NOT modify any file under
  `src/CCL.MES.*` (verified via `git diff main`).
- Legacy `CCL.MES.Tests` (252 tests) still pass with the new solution
  present (confirmed pre-PR).
- Both solutions build clean (`0 Warning / 0 Error`).

## Operating the API

```bash
cd CCL-MES-Hybrid/src/CCL.MES.Api
dotnet run                                 # Listens on http://localhost:5100
```

Useful curl probes (assuming a seeded admin user `sys/sys123!`):

```bash
# Health (anonymous)
curl http://localhost:5100/api/v2/health

# Login
curl -X POST http://localhost:5100/api/v2/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"sys","password":"sys123!"}'

# Use the access token
ACCESS=$(... extract from previous response ...)
curl http://localhost:5100/api/v2/auth/me \
  -H "Authorization: Bearer $ACCESS"

# AdminOnly endpoint
curl http://localhost:5100/api/v2/system-log?pageSize=5 \
  -H "Authorization: Bearer $ACCESS"
```

Swagger UI at `http://localhost:5100/swagger` in development.

## What lands in P10.2 (proposed)

Two pilot options for Henry to pick at P10.1 merge:

- **Pilot A — Work Orders.** Reuses the legacy drawer/card UI patterns;
  the read-only API surface is already in place. Risk: navigation is the
  full MES home screen, lots of moving parts.
- **Pilot B — NPI grid read-only.** Single-screen tab, paginated table.
  Lower visual surface; faster proof-of-concept for the MAUI shell.

Default recommendation: **Pilot B** (NPI grid). Smaller blast radius means
P10.2 can ship the MAUI shell scaffolding + Razor class library + login
flow in 3-4 weeks; Pilot A becomes a P10.2.5 spike once the shell pattern
is proven.
