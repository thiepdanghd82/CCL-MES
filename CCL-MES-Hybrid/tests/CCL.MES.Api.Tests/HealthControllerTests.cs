using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Smoke test that the API boots + responds anonymously on the health
/// endpoint. Important as a CI canary — if Health is 200 we know
/// configuration loaded, migrations ran, DI graph resolved.
/// </summary>
public sealed class HealthControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;

    public HealthControllerTests(MesApiFactory fx) => _fx = fx;

    [Fact]
    public async Task Health_returns_ok_anonymously()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v2/health");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<HealthBody>();
        Assert.NotNull(body);
        Assert.Equal("ok", body!.Status);
        Assert.Equal("P10.1", body.Version);
    }

    [Fact]
    public async Task Ready_returns_ok_anonymously()
    {
        var client = _fx.CreateClient();
        var resp = await client.GetAsync("/api/v2/health/ready");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Protected_endpoint_without_token_is_401()
    {
        var client = _fx.CreateClient();
        // /me has [Authorize] and no [AllowAnonymous], so the FallbackPolicy
        // should bounce it before any controller code runs.
        var resp = await client.GetAsync("/api/v2/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    private sealed record HealthBody
    {
        public string Status { get; init; } = "";
        public string Version { get; init; } = "";
        public DateTime TimeUtc { get; init; }
    }
}
