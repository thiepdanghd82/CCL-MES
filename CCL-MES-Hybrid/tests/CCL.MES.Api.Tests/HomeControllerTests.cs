using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Shared.Home;
using Xunit;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.10 — wire tests for GET /api/v2/home/summary. Read-only aggregate
/// powering the Home dashboard KPI tiles. The fresh migrated test DB has
/// no specs/WOs so every count is 0; the test asserts the shape + the
/// auth gate (401 anonymous).
/// </summary>
public sealed class HomeControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public HomeControllerTests(MesApiFactory fx) => _fx = fx;

    private async Task<HttpClient> AuthedClientAsync(string user)
    {
        await _fx.SeedUserAsync(user, "P@ss!1", "Operator");
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, user, "P@ss!1");
        return client;
    }

    [Fact]
    public async Task Summary_requires_auth()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v2/home/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Summary_returns_kpi_shape_for_any_authenticated_user()
    {
        var client = await AuthedClientAsync("home-op");

        var resp = await client.GetAsync("/api/v2/home/summary");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<HomeSummaryDto>();
        Assert.NotNull(dto);
        // Fresh migrated DB — no specs / WOs seeded, so every count is a
        // valid non-negative number (0). The point is the 4-field shape
        // serialises + the query runs without error.
        Assert.True(dto!.SpecsTotal >= 0);
        Assert.True(dto.PendingApprovals >= 0);
        Assert.True(dto.Drafts >= 0);
        Assert.True(dto.TodayActivity >= 0);
    }
}
