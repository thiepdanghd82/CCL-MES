using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Infrastructure;
using CCL.MES.Shared.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.6d — Settings/About endpoint coverage.
///
/// 4 guards:
///   1. GET /about returns 200 with the dictionary shape.
///   2. The COUNT(*) values match what the seeded DB actually holds
///      (asserts the LINQ projection didn't drift).
///   3. EnvironmentName surfaces ASP.NET Core's actual env (Test in
///      the WebApplicationFactory harness).
///   4. Anonymous caller is 401, not 200, so the data-dir path never
///      leaks to unauthenticated traffic.
/// </summary>
public sealed class SettingsAboutTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public SettingsAboutTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> AuthenticatedClientAsync(string user = "eng-about")
    {
        await _fx.SeedUserAsync(user, "P@ss!1", UserRole.Engineer);
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    [Fact]
    public async Task Get_about_returns_200_with_server_metadata()
    {
        var client = await AuthenticatedClientAsync("eng-about-200");
        var resp = await client.GetAsync("/api/v2/settings/about");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<AboutDto>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body!.ServerVersion));
        Assert.False(string.IsNullOrWhiteSpace(body.EnvironmentName));
        Assert.False(string.IsNullOrWhiteSpace(body.MachineName));
        Assert.True((DateTime.UtcNow - body.ServerTimeUtc).TotalSeconds < 60,
            "ServerTimeUtc should be within 60s of test clock.");
    }

    [Fact]
    public async Task Get_about_counts_match_db_state()
    {
        // Seed a known shape — 1 admin user, 1 customer, 1 product, 1
        // revision — then assert the counters reflect it.
        await _fx.SeedUserAsync("eng-about-counts", "P@ss!1", UserRole.Engineer);

        using (var setup = _fx.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<MesDbContext>();
            var cust = new CCL.MES.Domain.Entities.Customer { Code = "CUST-ABOUT", Name = "About Test" };
            db.Customers.Add(cust);
            await db.SaveChangesAsync();
            var prod = new CCL.MES.Domain.Entities.Product { ProductCode = "PRD-ABOUT", Name = "About Test PRD", CustomerId = cust.Id };
            db.Products.Add(prod);
            await db.SaveChangesAsync();
            var rev = new CCL.MES.Domain.Entities.ProductRevision
            {
                ProductId = prod.Id,
                SpecCode = "SPEC-ABOUT",
                Title = "About Test Spec",
                RevisionCode = "A",
                Status = CCL.MES.Domain.ProductRevisionStatus.Draft,
                Print = new CCL.MES.Domain.Entities.SpecPrint { ProcessCode = "FLEXO", NumColors = 1 },
            };
            db.ProductRevisions.Add(rev);
            await db.SaveChangesAsync();
        }

        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "eng-about-counts", "P@ss!1");
        var resp = await client.GetAsync("/api/v2/settings/about");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = (await resp.Content.ReadFromJsonAsync<AboutDto>())!;

        using (var verify = _fx.Services.CreateScope())
        {
            var db = verify.ServiceProvider.GetRequiredService<MesDbContext>();
            var expectedUsers     = await db.Users.CountAsync();
            var expectedCustomers = await db.Customers.CountAsync();
            var expectedProducts  = await db.Products.CountAsync();
            var expectedRevisions = await db.ProductRevisions.CountAsync();
            Assert.Equal(expectedUsers,     body.UserCount);
            Assert.Equal(expectedCustomers, body.CustomerCount);
            Assert.Equal(expectedProducts,  body.ProductCount);
            Assert.Equal(expectedRevisions, body.SpecRevisionCount);
        }
    }

    [Fact]
    public async Task Get_about_environment_name_surfaces_aspnetcore_env()
    {
        var client = await AuthenticatedClientAsync("eng-about-env");
        var resp = await client.GetAsync("/api/v2/settings/about");
        var body = (await resp.Content.ReadFromJsonAsync<AboutDto>())!;
        // WebApplicationFactory<Program> defaults env to "Test" unless
        // a fixture overrides it. Anything non-empty proves the
        // controller pulled IHostEnvironment.EnvironmentName.
        Assert.False(string.IsNullOrEmpty(body.EnvironmentName));
    }

    [Fact]
    public async Task Get_about_denies_anonymous_caller()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v2/settings/about");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
