using System.Text;
using CCL.MES.Api.Audit;
using CCL.MES.Api.Auth;
using CCL.MES.Api.Hubs;
using CCL.MES.Application;
using CCL.MES.Application.Audit;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Force scope + build validation in every environment (Dev / Test / Prod)
// rather than ASP.NET's Dev-only default. P10.1 caught a missing IBlobStore
// registration only at first `dotnet run` because the integration test
// factory uses env=Test which doesn't trigger ValidateOnBuild. Making it
// unconditional means xUnit catches the next missing-DI bug too.
builder.Host.UseDefaultServiceProvider((context, options) =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
});

// ──────────────────────────────────────────────────────────────────────
// Database path resolution.
//
// Mirrors the legacy Web project's repo-root /data/ convention (Lesson 30
// from Ops Control v1.2 CLAUDE.md: always feed SQLite absolute paths so
// process CWD changes can't pull the rug). The API project lives at
// CCL-MES-Hybrid/src/CCL.MES.Api so the search has to walk up FOUR levels
// to reach the repo root that hosts the legacy CCL.MES.sln.
//
// Resolution priority (highest wins):
//   1. ConnectionStrings:Default already set in configuration — respect
//      it (tests use this path so each factory pins its own /tmp file).
//   2. MES_DB_PATH env var — explicit file override for ops.
//   3. MES_DATA_DIR env var + ccl_mes.db filename — folder override.
//   4. EXISTING <ancestor>/data/ccl_mes.db — prefer a real DB over a
//      sibling .sln (Henry P10.7d-4 hardware note: dotnet run from
//      CCL-MES-Hybrid/src/CCL.MES.Api/ used to land at the EMPTY
//      CCL-MES-Hybrid/data/ instead of the operator's real DB at
//      CCL-MES/data/ because the .sln walk stopped at the first .sln).
//   5. <repo-root>/data/ccl_mes.db default (innermost .sln, shared with
//      legacy Web — fresh checkout with no DB anywhere).
// SqlServer provider — connection string is operator-managed; we never
// touch it.
// ──────────────────────────────────────────────────────────────────────
{
    var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
    var existingCs = builder.Configuration.GetConnectionString("Default");

    if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(existingCs))
    {
        var dataDir = Environment.GetEnvironmentVariable("MES_DATA_DIR");
        if (string.IsNullOrEmpty(dataDir))
        {
            // P10.7d-4 — walk up from ContentRootPath looking for an
            // EXISTING data/ccl_mes.db FIRST. If found at any ancestor,
            // use that directory (matches operator's mental model —
            // the real DB they've been using). Only if no DB file is
            // found anywhere does the fallback (legacy .sln walk) kick
            // in and create a fresh one. Closes Henry's "boot wrong
            // empty DB when MES_DATA_DIR isn't set" footgun.
            var probe = builder.Environment.ContentRootPath;
            string? repoRoot = null;
            string? innermostSln = null;
            for (var dir = new DirectoryInfo(probe); dir is not null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "data", "ccl_mes.db")))
                {
                    repoRoot = dir.FullName;
                    break;
                }
                // Remember the innermost .sln dir as a fallback if we
                // never find an existing DB file.
                if (innermostSln is null && dir.GetFiles("*.sln").Length > 0)
                {
                    innermostSln = dir.FullName;
                }
            }
            dataDir = Path.Combine(
                repoRoot ?? innermostSln ?? Directory.GetCurrentDirectory(),
                "data");
        }
        dataDir = Path.GetFullPath(dataDir);
        Directory.CreateDirectory(dataDir);

        var dbPath = Environment.GetEnvironmentVariable("MES_DB_PATH");
        if (string.IsNullOrEmpty(dbPath))
            dbPath = Path.Combine(dataDir, "ccl_mes.db");
        dbPath = Path.GetFullPath(dbPath);

        builder.Configuration["ConnectionStrings:Default"] = $"Data Source={dbPath}";
        Console.WriteLine($"[boot] API SQLite data dir: {dataDir}");
        Console.WriteLine($"[boot] API SQLite DB path : {dbPath}");
    }
    else if (!string.IsNullOrEmpty(existingCs))
    {
        Console.WriteLine("[boot] API DB connection string supplied by configuration.");
    }
    else
    {
        Console.WriteLine($"[boot] API DB provider: {provider} — connection string from config only.");
    }
}

// ──────────────────────────────────────────────────────────────────────
// Layer registration — reuses the LEGACY DI extensions verbatim so that
// the same 10 Application services + DbContext + exporters seen by the
// Blazor Server web app are available to the API. Zero duplication.
// ──────────────────────────────────────────────────────────────────────
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

// Drawing blob storage. Mirrors the legacy Web registration
// (src/CCL.MES.Web/Program.cs:91-129) so DrawingsService — which the
// legacy AddApplication() registers — can resolve IBlobStore. DataDir
// derives from the connection string just resolved, so blobs land next
// to the live DB on prod and inside the test fixture tmp dir under test.
// Singleton matches legacy lifetime (FilesystemBlobStore is stateless).
{
    var blobOpts = new CCL.MES.Infrastructure.Storage.BlobStoreOptions();
    var cs = builder.Configuration.GetConnectionString("Default") ?? "";
    const string prefix = "Data Source=";
    if (cs.StartsWith(prefix, StringComparison.Ordinal))
    {
        var dbFile = cs[prefix.Length..];
        var dbDir = Path.GetDirectoryName(Path.GetFullPath(dbFile));
        if (!string.IsNullOrWhiteSpace(dbDir)) blobOpts.DataDir = dbDir;
    }
    if (string.IsNullOrEmpty(blobOpts.DataDir))
    {
        // Fallback — co-locate with the API binary. Operator should set an
        // explicit data dir in any non-trivial deployment.
        blobOpts.DataDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "data"));
    }
    if (long.TryParse(Environment.GetEnvironmentVariable("MES_BLOB_MAX_BYTES"), out var maxBytes) && maxBytes > 0)
        blobOpts.MaxBytes = maxBytes;
    var allowed = Environment.GetEnvironmentVariable("MES_BLOB_ALLOWED_EXTENSIONS");
    if (!string.IsNullOrWhiteSpace(allowed))
    {
        blobOpts.AllowedExtensions = new HashSet<string>(
            allowed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            StringComparer.OrdinalIgnoreCase);
    }
    builder.Services.AddSingleton(blobOpts);
    builder.Services.AddSingleton<CCL.MES.Application.Storage.IBlobStore,
                                  CCL.MES.Infrastructure.Storage.FilesystemBlobStore>();
}

// PasswordHasher<User> mirrors legacy registration so password verification
// stays bit-identical. Without this the AuthController couldn't validate
// the cookie-era hash format.
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// IAuditWriter implementation — distinct from legacy AuditService (lives in
// Web) but behaviour-identical. See ApiAuditWriter.cs for the parity note.
builder.Services.AddScoped<IAuditWriter, ApiAuditWriter>();
builder.Services.AddHttpContextAccessor();

// ──────────────────────────────────────────────────────────────────────
// JWT bearer auth.
//
// Q7 lock (2026-06-03): access 15m, refresh 7d, one-time-use rotation.
// JwtOptions defaults match Q7. SigningKey MUST be provided via
// configuration in production — the bound-in default is a dev sentinel.
//
// Hub query-string token: the Microsoft.AspNetCore.Authentication.JwtBearer
// middleware reads the Authorization header by default. SignalR over WS
// can't set headers on the negotiate request, so we sniff the query string
// for paths under /hubs/ — same pattern the SignalR docs recommend.
// ──────────────────────────────────────────────────────────────────────
var jwtSection = builder.Configuration.GetSection("Jwt");
builder.Services.Configure<JwtOptions>(jwtSection);
var jwtOpts = jwtSection.Get<JwtOptions>() ?? new JwtOptions();
if (Encoding.UTF8.GetByteCount(jwtOpts.SigningKey) < 32)
{
    // We log + throw here so a misconfigured prod env fails its own
    // preflight rather than silently using a known dev key.
    throw new InvalidOperationException(
        "Jwt:SigningKey must be at least 32 bytes of UTF-8 for HS256. "
        + "Set Jwt__SigningKey in env or appsettings.");
}

builder.Services.AddSingleton<JwtTokenIssuer>();
builder.Services.AddSingleton<IRefreshTokenStore, InMemoryRefreshTokenStore>();

// P10.7d-2 — IPQC dual-sig (Q3) options registration. Reads env var
// OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER first, then falls back to
// Features:IpqcRequireDistinctQaApprover config key. Default-ON parse
// per §5.5.1 — stray typos can't silently disable 4-eye QC.
builder.Services.Configure<CCL.MES.Application.Services.IpqcDualSigOptions>(opts =>
{
    var raw = Environment.GetEnvironmentVariable("OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER")
        ?? builder.Configuration["Features:IpqcRequireDistinctQaApprover"];
    opts.RequireDistinctQaApprover =
        CCL.MES.Application.Services.IpqcDualSigOptionsLoader
            .ParseRequireDistinctQaApprover(raw);
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOpts.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOpts.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOpts.SigningKey)),
            ClockSkew = jwtOpts.ClockSkew,
        };
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var accessToken = ctx.Request.Query["access_token"].ToString();
                var path = ctx.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    ctx.Token = accessToken;
                return Task.CompletedTask;
            }
        };
    });

// ──────────────────────────────────────────────────────────────────────
// RBAC. Policies port 1:1 from the legacy
// src/CCL.MES.Web/Program.cs:177-199. Same role names, same membership.
// FallbackPolicy keeps the whole API gated behind a valid JWT unless an
// endpoint opts out with [AllowAnonymous].
// ──────────────────────────────────────────────────────────────────────
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .Build();

    o.AddPolicy("AdminOnly", p => p
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireRole(UserRole.Admin));

    o.AddPolicy("NpiRead", p => p
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireRole(UserRole.Admin, UserRole.Supervisor, UserRole.Engineer, UserRole.Qc));

    o.AddPolicy("NpiSpecRead", p => p
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireRole(UserRole.Admin, UserRole.Supervisor, UserRole.Engineer));

    // P10.5c-1 — Spec mutation gate. Mirrors the legacy
    // SpecsController inline `[Authorize(Roles = "Admin,Engineer")]` —
    // Supervisor / QC can READ specs but never WRITE per Phase 8 Q9.
    o.AddPolicy("NpiSpecWrite", p => p
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireRole(UserRole.Admin, UserRole.Engineer));

    o.AddPolicy("QcRead", p => p
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireRole(UserRole.Admin, UserRole.Supervisor, UserRole.Qc));

    // P10.7d-2 — IPQC submit gate. Per SpecHub §3 role matrix: QC owns
    // the IPQC review surface (PUT slot + judgment). Admin is god mode.
    // (SpecHub also includes "npi" for trial pilots; the RBAC in this
    // project doesn't model an NPI role — defer to a future role-table
    // expansion if NPI workflow demands it.)
    o.AddPolicy("IpqcSubmit", p => p
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireRole(UserRole.Admin, UserRole.Qc));

    // P10.7d-2 — QA approve gate (Q3 dual-sig CRITICAL). Per SpecHub
    // §3 QC column "Approve special-accept" + practical print-industry
    // convention "QA Manager review", the policy includes QC and
    // Supervisor. The dual-sig guard at the controller level prevents
    // the IPQC submitter from also QA-approving even when both share
    // role membership.
    o.AddPolicy("QaApprove", p => p
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireRole(UserRole.Admin, UserRole.Qc, UserRole.Supervisor));
});

// ──────────────────────────────────────────────────────────────────────
// MVC controllers + SignalR + OpenAPI (dev). JSON options: leave ASP.NET
// defaults (camelCase property names) — DTOs in CCL.MES.Shared are POCO
// records that serialize cleanly out of the box.
// ──────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddSignalR();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<ShopfloorNotifierV2>();

// P10.5c-2 — Spec xlsx import orchestrator. Legacy CCL.MES.Application
// service composed of IMesDbContext (Infrastructure) + IAuditWriter (Api)
// + ISpecXlsxParserFactory (Infrastructure) — all already registered by
// the calls above. We add the orchestrator itself scoped, mirroring the
// legacy Web project's registration (src/CCL.MES.Web/Program.cs:230).
builder.Services.AddScoped<CCL.MES.Application.SpecImport.SpecImportService>();

// P10.6a — Settings/My Profile + My Password service. Self-service
// only (loads the current user via the JWT NameIdentifier claim);
// see the service doc-comment for the parallel-impl rationale.
builder.Services.AddScoped<CCL.MES.Api.Services.UserProfileService>();

// P10.6h — Admin Backup/Restore. Singleton because the service is
// stateless + reads paths from IConfiguration on every call. The
// AdminOnly policy on BackupController is the only gate; the service
// itself does no auth (called only by the controller).
builder.Services.AddSingleton<CCL.MES.Api.Services.BackupApiService>();

// P10.6e — Admin Audit Log viewer + CSV/XLSX export. Scoped because
// the service depends on IMesDbContext (per-request scope). The
// IAuditLogExporter instances (CSV + XLSX) are already registered as
// singletons by AddInfrastructure(); the controller resolves both via
// IEnumerable<IAuditLogExporter> and dispatches by Format.
builder.Services.AddScoped<CCL.MES.Api.Services.AuditLogQueryService>();

// P10.6c — Admin Account Control. Scoped — depends on IMesDbContext +
// IPasswordHasher (singleton) + IRefreshTokenStore (singleton). The
// AdminOnly policy on AccountControlController is the only gate.
builder.Services.AddScoped<CCL.MES.Api.Services.AccountControlService>();

// P10.5e-1 — Drawings upload + download orchestrator. Legacy Phase 8
// PR-D-5b service composed of IMesDbContext + IBlobStore (registered
// just above) + IAuditWriter. Multipart upload routes to UploadAsync;
// download via GetForDownloadAsync + IBlobStore.GetAsync. Scoped to
// match the DbContext lifetime (transactional upload).
builder.Services.AddScoped<CCL.MES.Application.Services.DrawingsService>();

// P10.3 W4 — in-memory device heartbeat snapshot store. Process-scoped
// so /v2/devices/{id} can answer last-seen + scan-count without a DB
// write per ping. Lost on process restart (acceptable: heartbeat
// cadence rebuilds the map in under a minute).
builder.Services.AddSingleton<CCL.MES.Api.Devices.IDeviceHeartbeatStore,
    CCL.MES.Api.Devices.InMemoryDeviceHeartbeatStore>();

// P10.7a-1.2 — Idempotency middleware options binding.
// Default 24h TTL, 256 KB request+response cap. Override via
// appsettings.json section "Idempotency" or env var
// Idempotency__TtlHours=N.
builder.Services.Configure<CCL.MES.Api.Middleware.IdempotencyOptions>(
    builder.Configuration.GetSection("Idempotency"));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// P10.7a-1.2 — Idempotency middleware sits AFTER auth (needs the
// resolved User to compute ActorId) and BEFORE controllers (so it
// can intercept + replay the response). Pass-through for non-
// mutating verbs + for requests without the Idempotency-Key header,
// so the cost on read paths is one method call + zero DB hits.
app.UseMiddleware<CCL.MES.Api.Middleware.IdempotencyMiddleware>();

// P10.7a-1.3 — pending-migrations boot probe. The 2026-06-05
// "500 on login" incident was the symptom of a server starting up
// against a DB that lagged 8 migrations: the failure surfaced only
// as a blind 500 in the operator UI, not in any log. From now on
// the server logs a multi-line WARN at boot listing every pending
// migration; opt-in `Database:FailOnPendingMigrations=true` turns
// the WARN into a hard refusal so prod won't boot half-configured.
using (var bootScope = app.Services.CreateScope())
{
    try
    {
        var bootDb = bootScope.ServiceProvider
            .GetRequiredService<CCL.MES.Infrastructure.MesDbContext>();
        var pending = (await bootDb.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count > 0)
        {
            var failOnPending = app.Configuration
                .GetValue<bool>("Database:FailOnPendingMigrations", defaultValue: false);
            var header = failOnPending
                ? "════════════ DATABASE MIGRATION REQUIRED — REFUSING TO BOOT ════════════"
                : "════════════ WARNING — DATABASE HAS UNAPPLIED MIGRATIONS ════════════";
            Console.WriteLine();
            Console.WriteLine(header);
            Console.WriteLine($"  Database lags {pending.Count} migration(s):");
            foreach (var m in pending) Console.WriteLine($"    - {m}");
            Console.WriteLine();
            Console.WriteLine("  Apply via:");
            Console.WriteLine("    dotnet ef database update \\");
            Console.WriteLine("      --connection \"Data Source=<path/to/ccl_mes.db>\" \\");
            Console.WriteLine("      --project src/CCL.MES.Infrastructure \\");
            Console.WriteLine("      --startup-project src/CCL.MES.Web");
            Console.WriteLine();
            Console.WriteLine("  Booting anyway = endpoints that touch missing schema");
            Console.WriteLine("  return 500 with no diagnostic. Operator UI shows a");
            Console.WriteLine("  generic 'HTTP 500 · http.non_success' banner. Don't.");
            Console.WriteLine(new string('═', header.Length));
            Console.WriteLine();
            if (failOnPending)
            {
                throw new InvalidOperationException(
                    $"{pending.Count} pending migration(s); set Database:FailOnPendingMigrations=false to boot anyway.");
            }
        }
        else
        {
            Console.WriteLine("[boot] Database migration check: up-to-date.");
        }

        // P10.7a-2.1 — recovery surface bootstrap. Idempotent: re-running
        // on an already-seeded DB is a NOOP. Safe to run at every boot.
        // Skipped in the Test environment because the integration-test
        // factory bootstraps its own minimal DB + explicitly seeds the
        // sys-recovery account when a fixture needs it; running this
        // seed during xUnit fixture init adds spurious write-lock
        // contention to the N=50 advance soak tests.
        if (!app.Environment.IsEnvironment("Test"))
        {
            try
            {
                await CCL.MES.Infrastructure.DbSeeder.SeedRecoveryDataAsync(bootDb);
            }
            catch (Exception seedEx)
            {
                Console.WriteLine($"[boot] Recovery seed skipped: {seedEx.GetType().Name}: {seedEx.Message}");
            }

            try
            {
                await CCL.MES.Infrastructure.DbSeeder.SeedReasonCodesAsync(bootDb);

                var pause = await bootDb.ReasonCodes.CountAsync(r => r.Kind == CCL.MES.Domain.ReasonCodeKind.Pause);
                var scrap = await bootDb.ReasonCodes.CountAsync(r => r.Kind == CCL.MES.Domain.ReasonCodeKind.Scrap);
                var recov = await bootDb.ReasonCodes.CountAsync(r => r.Kind == CCL.MES.Domain.ReasonCodeKind.Recovery);
                Console.WriteLine($"[seed] reason_codes pause={pause} scrap={scrap} recovery={recov}");
            }
            catch (Exception seedEx)
            {
                Console.WriteLine($"[boot] Reason-code seed skipped: {seedEx.GetType().Name}: {seedEx.Message}");
            }

            // P10.7d-1 §5.5.1 — dual-sig boot probe. Default-ON discipline:
            // anything other than an explicit OFF token leaves the gate
            // enforced. Emitting the flag state at boot lets operators
            // confirm the deploy actually loaded the expected configuration
            // (mirrors the L17 seed probe rationale — silent defaults are
            // invisible at runtime).
            try
            {
                var rawEnv = Environment.GetEnvironmentVariable("OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER");
                var rawCfg = app.Configuration["Features:IpqcRequireDistinctQaApprover"];
                var raw = rawEnv ?? rawCfg;
                var flagOn = CCL.MES.Application.Services.IpqcDualSigOptionsLoader
                    .ParseRequireDistinctQaApprover(raw);
                Console.WriteLine($"[config] OPS_IPQC_REQUIRE_DISTINCT_QA_APPROVER={(flagOn ? "on" : "off")}");
            }
            catch (Exception cfgEx)
            {
                Console.WriteLine($"[boot] Dual-sig probe skipped: {cfgEx.GetType().Name}: {cfgEx.Message}");
            }
        }
    }
    catch (InvalidOperationException)
    {
        // Re-throw the fail-fast case so the host shuts down.
        throw;
    }
    catch (Exception ex)
    {
        // Any other failure (DB unreachable, table __EFMigrationsHistory
        // missing on a fresh blank DB EF hasn't bootstrapped yet) — log
        // but don't block boot. Test fixtures bootstrap their own DBs.
        Console.WriteLine($"[boot] Migration check skipped: {ex.GetType().Name}: {ex.Message}");
    }
}

app.MapControllers();
app.MapHub<ShopfloorHubV2>("/hubs/shopfloor");

app.Run();

/// <summary>
/// Marker class so <c>WebApplicationFactory&lt;Program&gt;</c> can spin up
/// the API inside integration tests. Required because top-level Program.cs
/// emits an internal Program class by default; the public partial below
/// promotes it.
/// </summary>
public partial class Program { }
