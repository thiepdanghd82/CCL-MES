using System.Globalization;
using System.Text.Json.Serialization;
using CCL.MES.Application;
using CCL.MES.Domain.Entities;
using CCL.MES.Infrastructure;
using CCL.MES.Web.Hubs;
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

var app = builder.Build();

// DB init + seed.
// - SQLite dev: EnsureCreated()
// - SQL Server prod: Migrate()
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
    var provider = app.Configuration["Database:Provider"] ?? "Sqlite";
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        db.Database.Migrate();
    else
        db.Database.EnsureCreated();
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
// AllowAnonymous on the realtime broadcast hub: the Blazor Server
// HubConnection client from Dashboard.razor does NOT carry the user's
// cookie into the negotiate call, so the FallbackPolicy would reject
// it. The hub itself only fans out shopfloor events (work-order step
// changes, OEE counter ticks) — the underlying data is still gated
// by the auth-required APIs. Phase 4+ should pass cookies via
// HubConnectionBuilder options and remove this AllowAnonymous.
app.MapHub<ShopfloorHub>("/hubs/shopfloor").AllowAnonymous();
app.MapFallbackToPage("/_Host");

app.Run();

// ──────────────────────────────────────────────────────────────────────
// Local function: idempotent admin/admin seed for Phase 2 login demo.
// Skips when any user already exists, so a populated DB is never touched.
// Kept here (Web project) because PasswordHasher<User> lives in
// Microsoft.AspNetCore.Identity, which the Infrastructure class lib
// intentionally does not depend on.
// ──────────────────────────────────────────────────────────────────────
static async Task SeedAdminUserAsync(MesDbContext db, IPasswordHasher<User> hasher)
{
    if (await db.Users.AnyAsync()) return;

    var admin = new User
    {
        Username = "admin",
        Role = "Admin",
        DisplayName = "Administrator",
    };
    admin.PasswordHash = hasher.HashPassword(admin, "admin");
    db.Users.Add(admin);
    await db.SaveChangesAsync();
}
