# CCL-MES-Hybrid

> **Phase 10 work-in-progress.** Home of the MAUI Blazor Hybrid client +
> central ASP.NET Core Web API. Lives **inside the CCL-MES git repo** as a
> top-level sibling of `src/` and `tests/` so we share the same branch + PR +
> CI workflow, while the legacy Blazor Server web app source (`src/CCL.MES.*`)
> stays **untouched** until cutover.
>
> **Status today (post P10.1):** Server API + JWT + Shared DTO foundation
> shipped. Web Blazor Server still serves production traffic in parallel.
> See [`docs/PHASE10-MAUI-MIGRATION-PLAN.md`](docs/PHASE10-MAUI-MIGRATION-PLAN.md)
> for the master plan + Q1–Q13 sign-off.

## Why a sibling folder inside the same repo

The legacy web app at `../src/CCL.MES.Web/` is in production and must keep
serving the shop floor while we migrate. Putting the new hybrid client + central
API into its own top-level folder gives us:

- **Instant rollback** — the legacy web app is still on disk, still buildable,
  still deployable. If a P10.x change ships a regression, we revert by pointing
  operators back at the legacy URL.
- **Read-only baseline** — files under `../src/CCL.MES.Web/`, `../src/CCL.MES.Domain/`,
  `../src/CCL.MES.Application/`, `../src/CCL.MES.Infrastructure/` are treated as
  immutable during migration. The new folder *references* the legacy projects
  (Domain/Application/Infrastructure) via relative `<ProjectReference>` so we
  don't drift, but we don't *modify* legacy code.
- **Clean solution boundary** — `CCL-MES-Hybrid.sln` lists only the new
  projects + read-only references; the legacy `CCL.MES.sln` build path is
  unchanged.
- **Same git repo** — branches, PRs, CI, and review history all stay in the
  existing GitHub repository instead of fragmenting.

## Constraints (binding for every PR landing here)

1. **Do NOT modify any file under `../src/CCL.MES.Domain/`, `../src/CCL.MES.Application/`,
   `../src/CCL.MES.Infrastructure/`, or `../src/CCL.MES.Web/`.** Read-only
   baseline. If a legacy project needs a fix, file a separate PR against the
   legacy folder with its own review.
2. **Do NOT copy code from `../src/CCL.MES.*`** unless the plan explicitly says
   so for a specific entity. Default = reference, not fork.
3. **Do NOT touch sibling project folders** outside this repo
   (`Ops Control v1.2/`, `SpecHub/`, `CMES/`, `Old ver/`). Pattern study
   allowed, modification forbidden.
4. **Reuse the Phase 9 test framework patterns** (xUnit + IsolatedDbFixture +
   InMemoryAuditWriter) for every new service / sync engine component.
5. **A→B→C safe rollout** for any data path change.
6. **Web Blazor Server stays live in parallel** through P10.1–P10.5; cutover is
   the last move.

## Layout

```
CCL-MES-Hybrid/
  CCL-MES-Hybrid.sln           ← new solution (legacy CCL.MES.sln untouched)
  docs/
    PHASE10-MAUI-MIGRATION-PLAN.md
    PHASE10-P10.1-IMPL-NOTES.md
  src/
    CCL.MES.Shared/            ← DTOs + contract envelopes (POCO, no EF)
    CCL.MES.Api/               ← ASP.NET Core Web API + JWT + SignalR Hub
  tests/
    CCL.MES.Api.Tests/         ← xUnit + WebApplicationFactory integration
```

## Running the API (dev)

```bash
cd CCL-MES-Hybrid/src/CCL.MES.Api
dotnet run
```

Default port `5100` (HTTP) — keeps clear of the legacy web app's `5000`/`5001`
range. Health probe at `GET /api/v2/health`. Swagger UI at `GET /swagger` (dev
only). Database path falls back to legacy `../data/ccl-mes.db` via
`MesDbContextFactory` env handling unless `CCL_MES_DB` override is set.

Web Blazor Server keeps running on its own port (`5000`) — both can run side
by side, sharing the same SQLite file.
