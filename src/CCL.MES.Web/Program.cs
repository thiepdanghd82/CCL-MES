using System.Globalization;
using System.Text.Json.Serialization;
using CCL.MES.Application;
using CCL.MES.Infrastructure;
using CCL.MES.Web.Hubs;
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
// Default UI culture is EN. VI is the second supported culture; the flag
// picker that flips the cookie lands in Phase 2 (login screen). Resource
// files live in Resources/SharedResource.resx (neutral = EN, NeutralLanguage
// in csproj) + Resources/SharedResource.vi.resx (Vietnamese satellite),
// reached via IStringLocalizer<CCL.MES.Web.Resources.SharedResource>.
// We deliberately do NOT set ResourcesPath: the marker type already lives
// under the .Resources sub-namespace, so the default lookup baseName
// (T.FullName = "CCL.MES.Web.Resources.SharedResource") matches the
// embedded resource name exactly.
builder.Services.AddLocalization();
builder.Services.Configure<RequestLocalizationOptions>(o =>
{
    var supported = new[] { new CultureInfo("en"), new CultureInfo("vi") };
    o.DefaultRequestCulture = new RequestCulture("en", "en");
    o.SupportedCultures = supported;
    o.SupportedUICultures = supported;
    // Order: cookie (.AspNetCore.Culture) → Accept-Language → default EN.
    // Cookie is intentionally first because Phase 2's flag picker writes it.
});

// Blazor Server + SignalR (realtime)
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();
builder.Services.AddSingleton<ShopfloorNotifier>();

var app = builder.Build();

// Khoi tao DB + seed du lieu mau.
// - Sqlite (dev): dung EnsureCreated() cho nhanh.
// - SqlServer (prod): dung Migrate() de ap dung EF Migrations.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MesDbContext>();
    var provider = app.Configuration["Database:Provider"] ?? "Sqlite";
    if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        db.Database.Migrate();
    else
        db.Database.EnsureCreated();
    await DbSeeder.SeedAsync(db);
}

app.UseSwagger();
app.UseSwaggerUI();

// Apply request culture before any UI middleware so Razor renders + API
// JSON formatters see the right CultureInfo.
var locOpts = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(locOpts);

app.UseStaticFiles();
app.UseRouting();

app.MapControllers();
app.MapBlazorHub();
app.MapHub<ShopfloorHub>("/hubs/shopfloor");
app.MapFallbackToPage("/_Host");

app.Run();
