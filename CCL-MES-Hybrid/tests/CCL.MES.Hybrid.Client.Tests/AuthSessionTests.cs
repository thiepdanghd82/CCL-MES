using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Shared.Auth;

namespace CCL.MES.Hybrid.Client.Tests;

public sealed class AuthSessionTests
{
    [Fact]
    public void Anonymous_after_construction()
    {
        var session = new AuthSession(new InMemoryTokenStore());
        Assert.False(session.CurrentUser.Identity?.IsAuthenticated ?? false);
        Assert.Null(session.CurrentUserInfo);
    }

    [Fact]
    public async Task SetLogin_persists_token_pair_and_decodes_claims()
    {
        var store = new InMemoryTokenStore();
        var session = new AuthSession(store);
        var login = MakeLoginResponse("alice", "Engineer");

        var raised = 0;
        session.OnChange += () => raised++;
        await session.SetLoginAsync(login);

        Assert.Equal(login.AccessToken, await store.GetAccessTokenAsync());
        Assert.Equal("REFRESH", await store.GetRefreshTokenAsync());
        Assert.True(session.CurrentUser.Identity?.IsAuthenticated);
        Assert.Equal("alice", session.CurrentUserInfo!.Username);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task SignOut_clears_everything_and_notifies()
    {
        var store = new InMemoryTokenStore();
        var session = new AuthSession(store);
        await session.SetLoginAsync(MakeLoginResponse("alice", "Engineer"));

        var raised = 0;
        session.OnChange += () => raised++;
        await session.SignOutAsync();

        Assert.Null(await store.GetAccessTokenAsync());
        Assert.Null(await store.GetRefreshTokenAsync());
        Assert.False(session.CurrentUser.Identity?.IsAuthenticated ?? false);
        Assert.Null(session.CurrentUserInfo);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task RestoreFromStorage_rebuilds_principal_from_stored_token()
    {
        var store = new InMemoryTokenStore();
        await store.SaveAsync(MakeJwt("alice", "Admin"), "REFRESH");

        var session = new AuthSession(store);
        Assert.False(session.CurrentUser.Identity?.IsAuthenticated ?? false);

        await session.RestoreFromStorageAsync();
        Assert.True(session.CurrentUser.Identity?.IsAuthenticated);
        Assert.True(session.CurrentUser.IsInRole("Admin"));
    }

    private static LoginResponse MakeLoginResponse(string username, string role)
    {
        return new LoginResponse
        {
            // Real JWT shape so AuthSession.SetLoginAsync can decode the
            // claims — otherwise CurrentUser stays anonymous.
            AccessToken = MakeJwt(username, role),
            RefreshToken = "REFRESH",
            AccessTokenExpiresAt = DateTime.UtcNow.AddMinutes(15),
            RefreshTokenExpiresAt = DateTime.UtcNow.AddDays(7),
            User = new UserInfo { Id = 1, Username = username, Role = role, DisplayName = username },
        };
    }

    private static string MakeJwt(string username, string role)
    {
        var payload = $$"""
            {
              "{{System.Security.Claims.ClaimTypes.NameIdentifier}}": "1",
              "{{System.Security.Claims.ClaimTypes.Name}}": "{{username}}",
              "{{System.Security.Claims.ClaimTypes.Role}}": "{{role}}"
            }
            """;
        static string Tb(string s)
        {
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
            return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        return $"{Tb("""{"alg":"HS256","typ":"JWT"}""")}.{Tb(payload)}.sig";
    }
}
