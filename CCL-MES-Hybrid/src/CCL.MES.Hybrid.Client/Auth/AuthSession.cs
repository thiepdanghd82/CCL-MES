using System.Security.Claims;
using CCL.MES.Shared.Auth;

namespace CCL.MES.Hybrid.Client.Auth;

/// <summary>
/// In-process auth state for the running app. Single source of truth for
/// "who is logged in" so multiple Razor pages see consistent values
/// without re-querying <c>/auth/me</c>.
/// </summary>
public interface IAuthSession
{
    /// <summary>Claims principal derived from the cached access token; empty when logged out.</summary>
    ClaimsPrincipal CurrentUser { get; }

    /// <summary>Snapshot of the user info DTO last returned by login/me.</summary>
    UserInfo? CurrentUserInfo { get; }

    /// <summary>Fires after any change to <see cref="CurrentUser"/> — pages subscribe to re-render.</summary>
    event Action? OnChange;

    /// <summary>
    /// Apply a fresh JWT pair (login / token-rotation result). The
    /// <see cref="UserInfo"/> from <see cref="LoginResponse"/> is cached
    /// for display; the access token is decoded for claims.
    /// </summary>
    Task SetLoginAsync(LoginResponse login, CancellationToken ct = default);

    /// <summary>Update only the JWT pair (post-refresh) without changing the cached UserInfo.</summary>
    Task ApplyTokenRotationAsync(LoginResponse rotated, CancellationToken ct = default);

    /// <summary>Forget tokens + user info; subsequent <see cref="CurrentUser"/> reads return anonymous.</summary>
    Task SignOutAsync(CancellationToken ct = default);

    /// <summary>Rebuild <see cref="CurrentUser"/> from whatever access token is currently in storage.</summary>
    Task RestoreFromStorageAsync(CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class AuthSession : IAuthSession
{
    private readonly ITokenStore _store;
    private ClaimsPrincipal _user = new(new ClaimsIdentity());
    private UserInfo? _userInfo;

    public AuthSession(ITokenStore store) => _store = store;

    public ClaimsPrincipal CurrentUser => _user;
    public UserInfo? CurrentUserInfo => _userInfo;
    public event Action? OnChange;

    public async Task SetLoginAsync(LoginResponse login, CancellationToken ct = default)
    {
        await _store.SaveAsync(login.AccessToken, login.RefreshToken, ct);
        _user = JwtClaims.Parse(login.AccessToken);
        _userInfo = login.User;
        OnChange?.Invoke();
    }

    public async Task ApplyTokenRotationAsync(LoginResponse rotated, CancellationToken ct = default)
    {
        await _store.SaveAsync(rotated.AccessToken, rotated.RefreshToken, ct);
        _user = JwtClaims.Parse(rotated.AccessToken);
        // We deliberately DO NOT replace _userInfo here. Refresh responses
        // include a UserInfo snapshot too, but server-side cache may have
        // shifted under us (eg. role change mid-session would surface
        // here). Trust the server snapshot.
        _userInfo = rotated.User;
        OnChange?.Invoke();
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await _store.ClearAsync(ct);
        _user = new ClaimsPrincipal(new ClaimsIdentity());
        _userInfo = null;
        OnChange?.Invoke();
    }

    public async Task RestoreFromStorageAsync(CancellationToken ct = default)
    {
        var access = await _store.GetAccessTokenAsync(ct);
        _user = JwtClaims.Parse(access);
        OnChange?.Invoke();
    }
}
