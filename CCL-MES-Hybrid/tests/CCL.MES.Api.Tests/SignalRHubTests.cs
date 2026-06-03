using System.Net;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;

namespace CCL.MES.Api.Tests;

/// <summary>
/// Smoke-level coverage of the SignalR hub mount + JWT query-string auth.
/// We don't spin up an actual WebSocket here (HttpClient can't upgrade);
/// what we DO verify is that the hub route exists and that the
/// Microsoft.AspNetCore.Authentication.JwtBearer query-string hook fires
/// for the /hubs/* path. A real WSS connect test lands when the MAUI
/// client wires up its connection (P10.2).
/// </summary>
public sealed class SignalRHubTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;
    public SignalRHubTests(MesApiFactory fx) => _fx = fx;

    [Fact]
    public async Task Hub_negotiate_without_token_is_401()
    {
        var client = _fx.CreateClient();
        // SignalR negotiate is a POST. Without a Bearer token (header or
        // query) it must be 401 — the hub class is decorated [Authorize].
        var resp = await client.PostAsync("/hubs/shopfloor/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Hub_negotiate_with_query_token_is_not_401()
    {
        await _fx.SeedUserAsync("hubuser", "Pa55w.rd!", UserRole.Operator);
        var anonClient = _fx.CreateClient();
        var login = await _fx.LoginAndAuthenticateAsync(anonClient, "hubuser", "Pa55w.rd!");

        // Fresh client — we want to prove the query-string path works
        // independently of the Authorization header.
        var rawClient = _fx.CreateClient();
        var url = $"/hubs/shopfloor/negotiate?negotiateVersion=1&access_token={login.AccessToken}";
        var resp = await rawClient.PostAsync(url, null);

        // We're not asserting OK because the negotiate result depends on
        // ASP.NET Core SignalR's protocol acceptance — what we care about
        // is that the JWT middleware accepted the query-string token and
        // didn't reject with 401.
        Assert.NotEqual(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
