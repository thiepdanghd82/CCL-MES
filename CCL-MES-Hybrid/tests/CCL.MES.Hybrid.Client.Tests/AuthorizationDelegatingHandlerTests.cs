using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Hybrid.Client.Tests._Support;
using CCL.MES.Shared;
using CCL.MES.Shared.Auth;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// Coverage for the 401-refresh contract that drives every authenticated
/// MAUI call. Tests use a stub HttpMessageHandler so they don't need the
/// real API host.
/// </summary>
public sealed class AuthorizationDelegatingHandlerTests
{
    [Fact]
    public async Task Attaches_bearer_when_token_present()
    {
        var (client, stub, _, _) = BuildClient("ACCESS-1", "REFRESH-1");
        stub.Responder = (_, _) => Task.FromResult(StubHttpHandler.Json(HttpStatusCode.OK, new { ok = true }));

        var resp = await client.GetAsync("/api/v2/work-orders");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal("Bearer ACCESS-1", stub.Requests[0].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Skips_bearer_when_no_access_token()
    {
        var (client, stub, _, _) = BuildClient(accessToken: null, refreshToken: null);
        stub.Responder = (_, _) => Task.FromResult(StubHttpHandler.Json(HttpStatusCode.OK, new { }));

        await client.PostAsJsonAsync($"/{ApiVersion.Prefix}/auth/login",
            new LoginRequest { Username = "x", Password = "y" });

        Assert.Null(stub.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task On_401_refreshes_and_retries_once()
    {
        var (client, stub, store, session) = BuildClient("STALE", "REFRESH-1");
        var rotated = MakeLoginResponse("FRESH", "REFRESH-2", "alice", "Operator");
        stub.Responder = (req, index) =>
        {
            // 1st request — protected resource, returns 401.
            // 2nd request — /auth/refresh, returns rotated pair.
            // 3rd request — retry of original, returns OK.
            if (index == 0) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            if (req.RequestUri!.AbsolutePath.EndsWith("/auth/refresh"))
                return Task.FromResult(StubHttpHandler.Json(HttpStatusCode.OK, rotated));
            return Task.FromResult(StubHttpHandler.Json(HttpStatusCode.OK, new { ok = true }));
        };

        var resp = await client.GetAsync("/api/v2/work-orders");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Equal(3, stub.Requests.Count);
        Assert.Equal("Bearer STALE", stub.Requests[0].Headers.Authorization!.ToString());
        Assert.Equal("Bearer FRESH", stub.Requests[2].Headers.Authorization!.ToString());

        Assert.Equal("FRESH", await store.GetAccessTokenAsync());
        Assert.Equal("REFRESH-2", await store.GetRefreshTokenAsync());
        // Session reflects rotated user (claims decoded from the FRESH JWT).
        Assert.Equal("alice", session.CurrentUserInfo!.Username);
    }

    [Fact]
    public async Task On_401_with_refresh_failure_signs_out()
    {
        var (client, stub, store, session) = BuildClient("STALE", "BAD-REFRESH");
        stub.Responder = (req, index) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/auth/refresh"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        };

        var resp = await client.GetAsync("/api/v2/work-orders");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);

        Assert.Null(await store.GetAccessTokenAsync());
        Assert.Null(await store.GetRefreshTokenAsync());
        Assert.False(session.CurrentUser.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task On_401_with_no_refresh_token_signs_out_without_refresh_call()
    {
        var (client, stub, store, session) = BuildClient("STALE", refreshToken: null);
        stub.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var resp = await client.GetAsync("/api/v2/work-orders");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        // Original 401 only — no refresh attempt because we had nothing to refresh with.
        Assert.Single(stub.Requests);
        Assert.False(session.CurrentUser.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public async Task Concurrent_401s_only_trigger_one_refresh()
    {
        var (client, stub, store, session) = BuildClient("STALE", "REFRESH-1");
        var rotated = MakeLoginResponse("FRESH", "REFRESH-2", "bob", "Operator");

        var refreshCount = 0;
        var refreshReady = new TaskCompletionSource();
        var refreshGate = new TaskCompletionSource();

        stub.Responder = async (req, index) =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/auth/refresh"))
            {
                Interlocked.Increment(ref refreshCount);
                refreshReady.TrySetResult();
                // Hold the refresh response until the test releases it so we
                // can prove other parallel callers wait on the lock.
                await refreshGate.Task;
                return StubHttpHandler.Json(HttpStatusCode.OK, rotated);
            }
            // Initial requests return 401; once a fresh access token is in
            // the store, return OK.
            var authHeader = req.Headers.Authorization?.Parameter;
            if (authHeader == "FRESH")
                return StubHttpHandler.Json(HttpStatusCode.OK, new { ok = true });
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        };

        // Fire 5 parallel calls — they all see the STALE token, all get 401.
        var requests = new List<Task<HttpResponseMessage>>();
        for (int i = 0; i < 5; i++)
            requests.Add(client.GetAsync($"/api/v2/work-orders?p={i}"));

        // Wait for the refresh attempt to begin, then release it.
        await refreshReady.Task;
        await Task.Delay(50); // tiny window to let other callers queue on the semaphore
        refreshGate.SetResult();

        var responses = await Task.WhenAll(requests);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        // Exactly ONE refresh call regardless of 5 concurrent 401s — proves
        // the SemaphoreSlim serialisation works.
        Assert.Equal(1, refreshCount);
        Assert.Equal("FRESH", await store.GetAccessTokenAsync());
    }

    [Fact]
    public async Task Refresh_endpoint_401_does_not_trigger_recursive_refresh()
    {
        // Directly issue a request to /auth/refresh that returns 401 — the
        // handler MUST NOT try to refresh-on-refresh which would infinite-loop.
        var (client, stub, _, _) = BuildClient("STALE", "REFRESH-1");
        stub.Responder = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var resp = await client.PostAsJsonAsync($"/{ApiVersion.Prefix}/auth/refresh",
            new RefreshTokenRequest { RefreshToken = "REFRESH-1" });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        Assert.Single(stub.Requests); // exactly one call, no recursion
    }

    // ── helpers ─────────────────────────────────────────────────────

    private static (HttpClient client, StubHttpHandler stub, InMemoryTokenStore store, IAuthSession session)
        BuildClient(string? accessToken, string? refreshToken)
    {
        var store = new InMemoryTokenStore();
        if (accessToken is not null || refreshToken is not null)
            store.SaveAsync(accessToken ?? "", refreshToken ?? "").Wait();
        var session = new AuthSession(store);
        var stub = new StubHttpHandler();

        // refresh client shares the same stub so the refresh /auth/refresh
        // call is observable via stub.Requests too. We pass a tiny BaseAddress
        // so the relative paths in our handler resolve.
        HttpClient RefreshFactory() => new(stub, disposeHandler: false)
        {
            BaseAddress = new Uri("http://localhost"),
        };

        var auth = new AuthorizationDelegatingHandler(store, session, RefreshFactory)
        {
            InnerHandler = stub,
        };
        var client = new HttpClient(auth, disposeHandler: false)
        {
            BaseAddress = new Uri("http://localhost"),
        };
        return (client, stub, store, session);
    }

    /// <summary>
    /// Build a <see cref="LoginResponse"/> whose AccessToken is the literal
    /// supplied <paramref name="access"/> string — NOT a fake JWT. The
    /// handler under test stores the access token verbatim and the
    /// Authorization-header assertion compares the literal value, so an
    /// opaque sentinel like "FRESH" makes the test readable. The
    /// JwtClaims decoder will return an anonymous principal for that
    /// non-JWT string but the tests in this file don't assert on claims.
    /// </summary>
    private static LoginResponse MakeLoginResponse(string access, string refresh, string username, string role)
    {
        return new LoginResponse
        {
            AccessToken = access,
            RefreshToken = refresh,
            AccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(15),
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7),
            User = new UserInfo { Id = 1, Username = username, Role = role, DisplayName = username },
        };
    }
}
