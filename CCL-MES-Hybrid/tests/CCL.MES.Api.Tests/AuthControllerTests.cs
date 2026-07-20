using System.Net;
using System.Net.Http.Json;
using CCL.MES.Api.Tests._Support;
using CCL.MES.Domain.Auth;
using CCL.MES.Shared.Auth;
using CCL.MES.Shared.Envelopes;

namespace CCL.MES.Api.Tests;

/// <summary>
/// JWT login + refresh + logout + /me round-trips against the legacy
/// User table (seeded via IPasswordHasher so the verification path
/// matches what the cookie-era login uses bit-for-bit).
/// </summary>
public sealed class AuthControllerTests : IClassFixture<MesApiFactory>
{
    private readonly MesApiFactory _fx;

    public AuthControllerTests(MesApiFactory fx) => _fx = fx;

    [Fact]
    public async Task Login_with_valid_credentials_returns_token_pair_and_user()
    {
        await _fx.SeedUserAsync("alice", "Pa55w.rd!", UserRole.Engineer, "Alice E", "NPI");
        var client = _fx.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/v2/auth/login",
            new LoginRequest { Username = "alice", Password = "Pa55w.rd!" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(body);
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));
        Assert.False(string.IsNullOrEmpty(body.RefreshToken));
        Assert.True(body.AccessTokenExpiresAt > DateTime.UtcNow);
        Assert.True(body.RefreshTokenExpiresAt > body.AccessTokenExpiresAt);
        Assert.Equal("alice", body.User.Username);
        Assert.Equal(UserRole.Engineer, body.User.Role);
        Assert.Equal("NPI", body.User.Department);
    }

    // Lesson L26 — Username is matched CASE-INSENSITIVELY (NOCASE column
    // collation). An admin-reset user must sign in regardless of the case
    // they type; a case-sensitive lookup used to return 401 and mask the
    // (correct) password behind an "invalid credentials" error.
    // Distinct stored name per case (the class fixture shares one DB, and
    // Username is now UNIQUE case-insensitively), typed as a different case.
    [Theory]
    [InlineData("CaseUserAlpha", "caseuseralpha")]   // stored mixed, typed lower
    [InlineData("caseuserbeta",  "CASEUSERBETA")]    // stored lower, typed upper
    [InlineData("CaseUserGamma", "cAsEuSeRgAmMa")]   // stored mixed, typed mixed
    public async Task Login_username_is_case_insensitive(string stored, string typed)
    {
        await _fx.SeedUserAsync(stored, "Pa55w.rd!", UserRole.Supervisor);
        var client = _fx.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/v2/auth/login",
            new LoginRequest { Username = typed, Password = "Pa55w.rd!" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrEmpty(body!.AccessToken));
        // The stored (display) casing is returned, not what the user typed.
        Assert.Equal(stored, body.User.Username);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_401_generic_error()
    {
        await _fx.SeedUserAsync("bob", "right-secret", UserRole.Operator);
        var client = _fx.CreateClient();

        var resp = await client.PostAsJsonAsync("/api/v2/auth/login",
            new LoginRequest { Username = "bob", Password = "WRONG" });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("auth.invalid_credentials", err!.Code);
    }

    [Fact]
    public async Task Login_with_unknown_username_returns_same_generic_error()
    {
        var client = _fx.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v2/auth/login",
            new LoginRequest { Username = "ghost-user-nobody", Password = "anything" });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        // Same code as wrong-password — no oracle for valid usernames.
        Assert.Equal("auth.invalid_credentials", err!.Code);
    }

    [Fact]
    public async Task Login_with_disabled_account_returns_same_generic_error()
    {
        await _fx.SeedUserAsync("disabled-eve", "Pa55w.rd!", UserRole.Operator, isActive: false);
        var client = _fx.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/v2/auth/login",
            new LoginRequest { Username = "disabled-eve", Password = "Pa55w.rd!" });

        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        var err = await resp.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("auth.invalid_credentials", err!.Code);
    }

    [Fact]
    public async Task Me_with_valid_bearer_returns_user_claims()
    {
        await _fx.SeedUserAsync("carol", "Pa55w.rd!", UserRole.Admin, "Carol A", "Ops");
        var client = _fx.CreateClient();
        await _fx.LoginAndAuthenticateAsync(client, "carol", "Pa55w.rd!");

        var resp = await client.GetAsync("/api/v2/auth/me");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var me = await resp.Content.ReadFromJsonAsync<UserInfo>();
        Assert.Equal("carol", me!.Username);
        Assert.Equal(UserRole.Admin, me.Role);
        Assert.Equal("Carol A", me.DisplayName);
        Assert.Equal("Ops", me.Department);
    }

    [Fact]
    public async Task Refresh_rotates_token_pair_and_old_refresh_is_invalid()
    {
        await _fx.SeedUserAsync("dan", "Pa55w.rd!", UserRole.Supervisor);
        var client = _fx.CreateClient();

        var first = await client.PostAsJsonAsync("/api/v2/auth/login",
            new LoginRequest { Username = "dan", Password = "Pa55w.rd!" });
        var firstBody = await first.Content.ReadFromJsonAsync<LoginResponse>();

        // Rotate.
        var refreshResp = await client.PostAsJsonAsync("/api/v2/auth/refresh",
            new RefreshTokenRequest { RefreshToken = firstBody!.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);
        var second = await refreshResp.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotEqual(firstBody.RefreshToken, second!.RefreshToken);
        Assert.NotEqual(firstBody.AccessToken, second.AccessToken);

        // Old refresh token must now be rejected.
        var reuse = await client.PostAsJsonAsync("/api/v2/auth/refresh",
            new RefreshTokenRequest { RefreshToken = firstBody.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        var err = await reuse.Content.ReadFromJsonAsync<ApiError>();
        Assert.Equal("auth.refresh_replay", err!.Code);
    }

    [Fact]
    public async Task Refresh_replay_revokes_whole_family()
    {
        await _fx.SeedUserAsync("erin", "Pa55w.rd!", UserRole.Operator);
        var client = _fx.CreateClient();

        var initial = (await (await client.PostAsJsonAsync("/api/v2/auth/login",
            new LoginRequest { Username = "erin", Password = "Pa55w.rd!" }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

        // Rotate once — now 'initial.RefreshToken' is revoked, 'second' is fresh.
        var second = (await (await client.PostAsJsonAsync("/api/v2/auth/refresh",
            new RefreshTokenRequest { RefreshToken = initial.RefreshToken }))
            .Content.ReadFromJsonAsync<LoginResponse>())!;

        // Re-using the revoked token MUST revoke the family — including 'second'.
        var replay = await client.PostAsJsonAsync("/api/v2/auth/refresh",
            new RefreshTokenRequest { RefreshToken = initial.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        // The previously-valid 'second' refresh token must now also be invalid.
        var followup = await client.PostAsJsonAsync("/api/v2/auth/refresh",
            new RefreshTokenRequest { RefreshToken = second.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, followup.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        await _fx.SeedUserAsync("frank", "Pa55w.rd!", UserRole.Operator);
        var client = _fx.CreateClient();
        var login = await _fx.LoginAndAuthenticateAsync(client, "frank", "Pa55w.rd!");

        var logout = await client.PostAsJsonAsync("/api/v2/auth/logout",
            new RefreshTokenRequest { RefreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // Refresh with the revoked token must be 401.
        var afterLogout = await client.PostAsJsonAsync("/api/v2/auth/refresh",
            new RefreshTokenRequest { RefreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }
}
