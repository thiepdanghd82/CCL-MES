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
            //
            // 2026-06-23 — require Length > 0 too: a stale 0-byte
            // CCL-MES-Hybrid/data/ccl_mes.db stub (left by an earlier
            // `dotnet run` before this fix) is a closer ancestor than the
            // real CCL-MES/data/ccl_mes.db, so a bare File.Exists check
            // stopped at the empty stub and the API booted a schema-less
            // DB → login 500 `no such table: Users`. An empty file is
            // never a real DB, so skip it and keep walking.
            var probe = builder.Environment.ContentRootPath;
            string? repoRoot = null;
            string? innermostSln = null;
            for (var dir = new DirectoryInfo(probe); dir is not null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "data", "ccl_mes.db");
                if (File.Exists(candidate) && new FileInfo(candidate).Length > 0)
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

// Ghi master data thư viện check-item. Tách khỏi controller vì gate L40
// (gate-thin-controller) cấm SaveChangesAsync trong controller.
builder.Services.AddScoped<CCL.MES.Api.Services.ICheckLibraryAdminService, CCL.MES.Api.Services.CheckLibraryAdminService>();
builder.Services.AddHttpContextAccessor();

// ──────────────────────────────────────────────────────────────────────
// Đợt 1 C1 — observability (PA-1: in-box, zero new packages).
//
// JSON console so every line is machine-parseable, with IncludeScopes on
// so the trace_id / wo_no / actor / work_center scope pushed by
// RequestObservabilityMiddleware actually reaches the output. Without
// IncludeScopes the scope is computed and then discarded, which is the
// quiet way to ship "structured logging" that carries no structure.
//
// AddJsonConsole reconfigures the console provider the default host
// builder already added; it does not stack a second one.
// ──────────────────────────────────────────────────────────────────────
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.JsonWriterOptions = new System.Text.Json.JsonWriterOptions { Indented = false };
});
builder.Services.AddScoped<CCL.MES.Api.Observability.MesRequestContext>();

// Đợt 1 C3 — the single entry point to the canonical OEE speed source
// (WorkCenter.IdealSpeedPcsH). Scoped: it reads the request's DbContext
// and stamps the resolved work-center code onto MesRequestContext.
builder.Services.AddScoped<CCL.MES.Api.Services.WorkCenterSpeedLookup>();

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

// P10.7e-1 Q5 — OQC 3-signature policy. Generalises the L20-pattern
// (typo-safe whitelist parse + boot probe) to 3 independent
// distinct-user invariants on the OQC sign chain
// (Inspector → Reviewer → Approver per CCL-10-F6 form). Each flag has
// its own env-var override OR Features:WoQcSigPolicy:<name> config
// path. All default-ON per Lesson L20.
builder.Services.Configure<CCL.MES.Application.Services.WoQcSigPolicyOptions>(opts =>
{
    opts.OqcRequireDistinctReviewer =
        CCL.MES.Application.Services.WoQcSigPolicyOptionsLoader.ParseFlag(
            Environment.GetEnvironmentVariable("OPS_OQC_REQUIRE_DISTINCT_REVIEWER")
                ?? builder.Configuration["Features:WoQcSigPolicy:OqcRequireDistinctReviewer"]);

    opts.OqcRequireDistinctApprover =
        CCL.MES.Application.Services.WoQcSigPolicyOptionsLoader.ParseFlag(
            Environment.GetEnvironmentVariable("OPS_OQC_REQUIRE_DISTINCT_APPROVER")
                ?? builder.Configuration["Features:WoQcSigPolicy:OqcRequireDistinctApprover"]);

    opts.OqcRequireApproverDistinctFromInspector =
        CCL.MES.Application.Services.WoQcSigPolicyOptionsLoader.ParseFlag(
            Environment.GetEnvironmentVariable("OPS_OQC_REQUIRE_APPROVER_DISTINCT_FROM_INSPECTOR")
                ?? builder.Configuration["Features:WoQcSigPolicy:OqcRequireApproverDistinctFromInspector"]);
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

    // P10.7e-2 — FQC + OQC edit gate. QC role owns FQC inspector +
    // OQC Inspector/Reviewer/Approver per SpecHub §3. Supervisor +
    // Admin can also edit (4-eye principle when there's no second QC).
    // The Q5 distinct-user invariants enforced at the controller
    // layer mean Supervisor can fill the Reviewer/Approver slots that
    // a single-QC shift can't otherwise satisfy.
    o.AddPolicy("QcEdit", p => p
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

// P-Backup — automated nightly backup worker (local "3 copies" of IBM
// 3-2-1). OFF by default; enable via Settings → Backup or OPS_BACKUP_SCHEDULE=1.
// Reuses BackupApiService's online-safe snapshot, verifies + prunes, alerts
// via OPS_BACKUP_WEBHOOK. Off-site copy = scripts/backup-offsite.{sh,ps1}.
// Registered as a singleton so BackupController can inject it for
// status / schedule-edit / run-now, then backed by the hosted service.
builder.Services.AddHttpClient(); // IHttpClientFactory for webhook alerts
builder.Services.AddSingleton<CCL.MES.Api.Services.BackupScheduleStore>();
builder.Services.AddScoped<CCL.MES.Api.Services.BackupVerifier>();
builder.Services.AddSingleton<CCL.MES.Api.Services.BackupSchedulerService>();
builder.Services.AddHostedService(sp =>
    sp.GetRequiredService<CCL.MES.Api.Services.BackupSchedulerService>());

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
// Quality → Traceability: real-time index + frozen-snapshot freeze + backfill.
builder.Services.AddScoped<CCL.MES.Api.Services.ITraceIndexService, CCL.MES.Api.Services.TraceIndexService>();
builder.Services.AddScoped<CCL.MES.Api.Services.ITraceFreezeService, CCL.MES.Api.Services.TraceFreezeService>();
builder.Services.AddScoped<CCL.MES.Api.Services.TraceBackfillService>();

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

// A1 — mạch lô nguyên vật liệu. Service giữ toàn bộ luật quét/đảo/đổi trạng
// thái; controller mỏng chỉ map HTTP.
builder.Services.AddScoped<CCL.MES.Application.Services.MaterialLotScanService>();
builder.Services.AddScoped<CCL.MES.Application.Services.MaterialLotBackfillService>();

// A1 — grace period BẮT BUỘC, mặc định TẮT. Khi tắt: vẫn resolve lô, vẫn ghi
// tiêu thụ, nhưng trả 200 + warning thay cho 422; audit vẫn emit với
// enforced:false ⇒ đo được chính xác bao nhiêu ca sẽ bị chặn TRƯỚC khi chặn
// thật. Ngày lật cờ là quyết định của Henry. Đây cũng là đường rollback mềm:
// tắt cờ ⇒ hai bảng mới nằm im, đường đọc cũ không đổi.
// Lưu ý chiều parse NGƯỢC với các cờ 4-mắt (L20 default-ON): ở đây gõ sai mà
// tự BẬT thì dừng nhà máy, nên chỉ token ON tường minh mới bật.
builder.Services.Configure<CCL.MES.Application.Services.MaterialLotOptions>(opts =>
{
    opts.EnforceReleased =
        CCL.MES.Application.Services.MaterialLotOptionsLoader.ParseEnforceReleased(
            Environment.GetEnvironmentVariable("MES_MATERIAL_LOT_ENFORCE_RELEASED")
                ?? builder.Configuration["Mes:MaterialLot:EnforceReleased"]);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Đợt 1 C1 — FIRST in the pipeline on purpose. A 403 from the
// authorization middleware never reaches a controller, so instrumenting
// any later would miss exactly the RBAC denials we want counted. The
// logging scope reads the principal live, so sitting ahead of
// UseAuthentication() costs nothing in actor resolution.
app.UseMiddleware<CCL.MES.Api.Observability.RequestObservabilityMiddleware>();

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

            // Phương án C — Bước 1/6: thư viện hạng mục + map process→line.
            // Hybrid API KHÔNG gọi full DbSeeder.SeedAsync (tránh demo data +
            // write contention), nên seed riêng ở đây — chống "seed quên chạy"
            // (auto-sync F6 cần ProcessLineMaps có dữ liệu). Idempotent, best-effort.
            try
            {
                var lib = await CCL.MES.Infrastructure.DbSeeder.SeedCheckItemLibraryAutoAsync(bootDb);
                if (lib is { } r)
                    Console.WriteLine($"[seed] check_item_library inserted={r.LibInserted} updated={r.LibUpdated} reason_added={r.ReasonAdded}");
            }
            catch (Exception seedEx)
            {
                Console.WriteLine($"[boot] check_item_library seed skipped: {seedEx.GetType().Name}: {seedEx.Message}");
            }
            try
            {
                var m = await CCL.MES.Infrastructure.DbSeeder.SeedProcessLineMapAsync(bootDb);
                Console.WriteLine($"[seed] process_line_map inserted={m.Inserted} updated={m.Updated} total={m.Total}");
            }
            catch (Exception seedEx)
            {
                Console.WriteLine($"[boot] process_line_map seed skipped: {seedEx.GetType().Name}: {seedEx.Message}");
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

            // P10.7e-1 Q5 + L20 — OQC 3-sig policy boot probe. Emits
            // one log line per flag so operators + the verify-p10.7e.sh
            // script can confirm the deploy actually loaded all 3
            // expected default-ON values. Per L20 standing rule, any
            // future default-ON security flag MUST follow the same
            // 3-piece kit (whitelist parse + log probe + verify gate).
            try
            {
                var rOpts = new CCL.MES.Application.Services.WoQcSigPolicyOptions
                {
                    OqcRequireDistinctReviewer =
                        CCL.MES.Application.Services.WoQcSigPolicyOptionsLoader.ParseFlag(
                            Environment.GetEnvironmentVariable("OPS_OQC_REQUIRE_DISTINCT_REVIEWER")
                                ?? app.Configuration["Features:WoQcSigPolicy:OqcRequireDistinctReviewer"]),
                    OqcRequireDistinctApprover =
                        CCL.MES.Application.Services.WoQcSigPolicyOptionsLoader.ParseFlag(
                            Environment.GetEnvironmentVariable("OPS_OQC_REQUIRE_DISTINCT_APPROVER")
                                ?? app.Configuration["Features:WoQcSigPolicy:OqcRequireDistinctApprover"]),
                    OqcRequireApproverDistinctFromInspector =
                        CCL.MES.Application.Services.WoQcSigPolicyOptionsLoader.ParseFlag(
                            Environment.GetEnvironmentVariable("OPS_OQC_REQUIRE_APPROVER_DISTINCT_FROM_INSPECTOR")
                                ?? app.Configuration["Features:WoQcSigPolicy:OqcRequireApproverDistinctFromInspector"]),
                };
                Console.WriteLine($"[config] OPS_OQC_REQUIRE_DISTINCT_REVIEWER={(rOpts.OqcRequireDistinctReviewer ? "on" : "off")}");
                Console.WriteLine($"[config] OPS_OQC_REQUIRE_DISTINCT_APPROVER={(rOpts.OqcRequireDistinctApprover ? "on" : "off")}");
                Console.WriteLine($"[config] OPS_OQC_REQUIRE_APPROVER_DISTINCT_FROM_INSPECTOR={(rOpts.OqcRequireApproverDistinctFromInspector ? "on" : "off")}");
                Console.WriteLine($"[config] OPS_OQC_SIG_POLICY_STATE={rOpts.FlagState} (all_on={rOpts.AllFlagsOn})");
            }
            catch (Exception cfgEx)
            {
                Console.WriteLine($"[boot] OQC 3-sig probe skipped: {cfgEx.GetType().Name}: {cfgEx.Message}");
            }

            // P10.7e-3 FIX (Henry RCA on PR #123) — QC profile boot probe
            // per L23. Default profiles are embedded compile-time constants
            // (no DB seed required), but the boot line lets ops grep at deploy
            // and verify-p10.7e.sh assert the canonical counts so a future
            // profile shrink (admin edits the const) trips CI rather than
            // surfacing as silent 0/0 on the operator dashboard.
            try
            {
                var fqcCount = CCL.MES.Application.Services.QcProfileSeed
                    .CountItems(CCL.MES.Application.Services.QcProfileSeed.FqcProfileJson);
                var oqcCount = CCL.MES.Application.Services.QcProfileSeed
                    .CountItems(CCL.MES.Application.Services.QcProfileSeed.OqcProfileJson);
                Console.WriteLine($"[seed] qc_profiles fqc={fqcCount} oqc={oqcCount}");
            }
            catch (Exception probeEx)
            {
                Console.WriteLine($"[boot] qc_profiles probe skipped: {probeEx.GetType().Name}: {probeEx.Message}");
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
