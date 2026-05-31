using System.Globalization;
using System.Text.Json.Serialization;
using CCL.MES.Application;
using CCL.MES.Domain.Auth;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Web.Hubs;
using CCL.MES.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Data + application layers
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();

// API + Swagger
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Localization — Phase 1 minimum infra.
// Default UI culture is EN. VI is the second supported culture; Phase 2's
// flag picker writes the .AspNetCore.Culture cookie via /set-language.
// Resource files live in Resources/SharedResource.resx (neutral = EN) +
// Resources/SharedResource.vi.resx (Vietnamese satellite), reached via
// IStringLocalizer<CCL.MES.Web.Resources.SharedResource>. We deliberately
// do NOT set ResourcesPath: the marker type already lives under the
// .Resources sub-namespace, so the default lookup baseName matches the
// embedded resource name exactly.
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    var supported = new[] { new CultureInfo("en"), new CultureInfo("vi") };
    o.DefaultRequestCulture = new RequestCulture("en", "en");
    o.SupportedCultures = supported;
    o.SupportedUICultures = supported;
});

// Auth — Phase 2 cookie auth (login-only).
// FallbackPolicy = RequireAuthenticatedUser turns the whole app into
// "auth-required by default"; pages that want to stay anonymous must
// declare [AllowAnonymous] explicitly (Login, Logout, SetLanguage).
// PasswordHasher<User> is registered so endpoints can verify/hash without
// new'ing an instance each time.
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "ccl_mes_auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
        o.AccessDeniedPath = "/login";
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    // Phase 5 — RBAC. "AdminOnly" gates the 4 admin sub-tabs under
    // /settings (account, data, syslog, import-legacy) which were
    // marked TODO RBAC in Phase 3. Role claim emitted at login from
    // User.Role; the whitelist of valid values lives in UserRole.All.
    o.AddPolicy("AdminOnly", p => p.RequireRole(UserRole.Admin));

    // Phase 6 Bước 4 — 3 additional page-level policies aligned to the
    // matrix in docs/PHASE6-STEP4-PLAN.md §2.C. Per-button gating uses
    // <AuthorizeView Roles="..."> inline rather than policies.
    o.AddPolicy("NpiRead", p =>
        p.RequireRole(UserRole.Admin, UserRole.Supervisor, UserRole.Engineer, UserRole.Qc));
    o.AddPolicy("NpiSpecRead", p =>
        p.RequireRole(UserRole.Admin, UserRole.Supervisor, UserRole.Engineer));
    o.AddPolicy("QcRead", p =>
        p.RequireRole(UserRole.Admin, UserRole.Supervisor, UserRole.Qc));
});
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// LangFlagPicker needs the current HttpContext so it can default ReturnUrl
// to the request path when no caller-supplied ReturnUrl is given.
builder.Services.AddHttpContextAccessor();

// Blazor Server + SignalR (realtime)
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();
builder.Services.AddSingleton<ShopfloorNotifier>();
// Phase 5 — cookie forward via scoped CookieAccessor (see docs/PHASE5-STEP2-PLAN.md).
builder.Services.AddScoped<HubCookieAccessor>();
// Phase 6 Bước 2B — Settings System + 1 admin (About / Account / Backup).
builder.Services.AddScoped<UserAdminService>();
builder.Services.AddSingleton<BackupService>();

var app = builder.Build();

// DB init + seed (Phase 5 — single path for SQLite + SQL Server via
// DbInitializer.InitializeAsync. The initializer baselines any pre-existing
// EnsureCreated() DB by populating __EFMigrationsHistory before calling
// Migrate(), so the 60k+ rows of NPI data already on disk are preserved.
// See docs/PHASE5-STEP4-PLAN.md.)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
    await DbInitializer.InitializeAsync(db);
    await DbSeeder.SeedAsync(db);
    await SeedAdminUserAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>());
}

app.UseSwagger();
app.UseSwaggerUI();

// Apply request culture before any UI middleware so Razor renders + API
// JSON formatters see the right CultureInfo.
var locOpts = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(locOpts);

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();
app.MapBlazorHub();
// Phase 5 — cookie forward via scoped CookieAccessor (see docs/PHASE5-STEP2-PLAN.md).
app.MapHub<ShopfloorHub>("/hubs/shopfloor");
app.MapFallbackToPage("/_Host");

app.Run();

// ──────────────────────────────────────────────────────────────────────
// Local function: idempotent demo-user seed.
//
// Phase 2 seeded admin/admin (Role = "Admin") so the login page had
// something to authenticate against. Phase 5 adds operator/operator
// (Role = "User") so the new "AdminOnly" policy can be tested
// end-to-end without manually editing the DB. Both accounts are
// skip-if-exists so reseeding never overwrites a populated DB.
// PasswordHasher<User> stays in the Web project because the
// Infrastructure class lib does not depend on
// Microsoft.AspNetCore.Identity.
// ──────────────────────────────────────────────────────────────────────
static async Task SeedAdminUserAsync(MesDbContext db, IPasswordHasher<User> hasher)
{
    // Phase 6 Bước 4 — fix-up legacy Role="User" → "Operator" BEFORE the
    // whitelist takes effect anywhere. Idempotent: only mutates rows that
    // still carry the legacy value, so reseeding never undoes an admin's
    // later role change. Console-logs the count so an operator can see
    // what happened on first boot after upgrading.
    var legacy = await db.Users.Where(u => u.Role == "User").ToListAsync();
    foreach (var u in legacy)
    {
        u.Role = UserRole.Operator;
        u.UpdatedAt = DateTime.UtcNow;
    }
    if (legacy.Count > 0)
        Console.WriteLine($"[seed] Phase 6 Bước 4 — migrated {legacy.Count} legacy Role=\"User\" → \"{UserRole.Operator}\".");

    // Seed table: 5 demo accounts (one per role) so the matrix can be
    // smoke-tested without an admin manually building accounts. All
    // skip-if-exists by username; populated DBs unchanged. Default
    // values for IsActive (true) + MustChangePassword (false) come
    // from the entity, set explicitly here for new rows just in case
    // a later migration changes the default direction.
    var seed = new (string Username, string Role, string DisplayName, string Password)[]
    {
        ("admin",      UserRole.Admin,      "Administrator",  "admin"),
        ("supervisor", UserRole.Supervisor, "Demo Supervisor","supervisor"),
        ("engineer",   UserRole.Engineer,   "Demo Engineer",  "engineer"),
        ("qc",         UserRole.Qc,         "Demo QC Lead",   "qc"),
        ("operator",   UserRole.Operator,   "Demo Operator",  "operator"),
    };
    foreach (var (username, role, displayName, password) in seed)
    {
        if (!await db.Users.AnyAsync(u => u.Username == username))
        {
            var user = new User
            {
                Username = username,
                Role = role,
                DisplayName = displayName,
                IsActive = true,
                MustChangePassword = false,
            };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
        }
    }

    await db.SaveChangesAsync();
}
