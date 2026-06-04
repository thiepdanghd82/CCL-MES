using System.Net;
using CCL.MES.Api.Tests._Support;

namespace CCL.MES.Api.Tests;

/// <summary>
/// P10.6a hotfix — route-discovery canary tests. Henry filed P10.6a
/// FAIL with HTTP 404 on the new Settings endpoints (GET /me,
/// PATCH /me, POST /password) even though the existing 11
/// <see cref="SettingsControllerTests"/> all passed. Diagnosis: the
/// running Catalyst client was talking to a server process started
/// BEFORE PR #91 was deployed; the existing tests prove the
/// controller responds correctly when loaded, but not that a fresh
/// deploy actually loaded the new routes.
///
/// These canary tests hit each route WITHOUT a bearer token and
/// assert HTTP 401 (Unauthorized — route exists, auth blocks) rather
/// than HTTP 404 (Not Found — route missing). A 404 from these tests
/// would mean controller discovery, assembly scan, or
/// <c>AddControllers</c> wiring is broken — a configuration-level
/// regression that the behaviour tests can't catch because they
/// always run against a fully-discovered controller chain.
///
/// One canary per controller surface added in P10.6+. Each one is a
/// cheap (~5 ms) tripwire: 401 PASS, 404 FAIL with the offending route.
/// </summary>
public sealed class RouteDiscoveryCanaryTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public RouteDiscoveryCanaryTests(MesApiFactory fx) => _fx = fx;

    [Theory]
    [InlineData("GET",   "/api/v2/settings/me")]
    [InlineData("PATCH", "/api/v2/settings/me")]
    [InlineData("POST",  "/api/v2/settings/password")]
    [InlineData("GET",   "/api/v2/settings/about")]      // P10.6d
    [InlineData("GET",   "/api/v2/backup")]              // P10.6h — list
    [InlineData("POST",  "/api/v2/backup")]              // P10.6h — create
    [InlineData("GET",   "/api/v2/backup/snapshot-x")]   // P10.6h — download
    [InlineData("POST",  "/api/v2/backup/restore")]      // P10.6h — restore
    [InlineData("GET",   "/api/v2/audit/log")]           // P10.6e — paged list
    [InlineData("GET",   "/api/v2/audit/actions")]       // P10.6e — distinct actions
    [InlineData("GET",   "/api/v2/audit/export/csv")]    // P10.6e — CSV export
    [InlineData("GET",   "/api/v2/audit/export/xlsx")]   // P10.6e — XLSX export
    public async Task Settings_route_is_discovered_and_returns_401_not_404_without_bearer(string verb, string url)
    {
        var client = _fx.CreateClient();
        using var req = new HttpRequestMessage(new HttpMethod(verb), url);
        // PATCH + POST need a body; send empty JSON so model binding
        // doesn't preempt the auth middleware with a 400.
        if (verb != "GET")
            req.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        var resp = await client.SendAsync(req);

        Assert.True(
            resp.StatusCode == HttpStatusCode.Unauthorized,
            $"Route {verb} {url} returned {(int)resp.StatusCode} {resp.StatusCode} — " +
            $"expected 401 Unauthorized (route discovered, auth blocks). A 404 means controller " +
            $"discovery / AddControllers wiring missed the SettingsController. A 200/422 means the " +
            $"[Authorize] gate is missing.");
    }

    // ── Sanity baseline ─────────────────────────────────────────────

    [Theory]
    [InlineData("/api/v2/health")]            // AllowAnonymous — must 200
    [InlineData("/api/v2/auth/login")]        // exists, POST-only — GET should not 404 either, returns 405
    public async Task Baseline_routes_resolve(string url)
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync(url);
        Assert.NotEqual(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
