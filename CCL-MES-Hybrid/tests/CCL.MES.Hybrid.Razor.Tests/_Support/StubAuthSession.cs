using System.Security.Claims;
using CCL.MES.Hybrid.Client.Auth;
using CCL.MES.Shared.Auth;

namespace CCL.MES.Hybrid.Razor.Tests._Support;

/// <summary>
/// P10.7d-3 — minimal <see cref="IAuthSession"/> double for the QA
/// Approval dashboard tests. Lets a fixture pin the signed-in
/// username so the Q3 dual-sig client guard
/// (Session.CurrentUserInfo.Username == _view.IpqcSubmittedBy) can
/// be exercised both same-user (button disabled + banner shown) and
/// distinct-user (button enabled + no banner) without booting MAUI.
/// </summary>
public sealed class StubAuthSession : IAuthSession
{
    public ClaimsPrincipal CurrentUser { get; private set; } = new(new ClaimsIdentity());
    public UserInfo? CurrentUserInfo { get; private set; }

#pragma warning disable CS0067 // unused event fine for tests
    public event Action? OnChange;
#pragma warning restore CS0067

    public void SetUser(string username, string role = "QC")
    {
        CurrentUserInfo = new UserInfo
        {
            Username = username,
            Role = role,
            DisplayName = username,
        };
        var id = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, role),
        }, "test");
        CurrentUser = new ClaimsPrincipal(id);
    }

    public Task SetLoginAsync(LoginResponse login, CancellationToken ct = default)
    {
        SetUser(login.User.Username, login.User.Role);
        return Task.CompletedTask;
    }

    public Task ApplyTokenRotationAsync(LoginResponse rotated, CancellationToken ct = default)
    {
        SetUser(rotated.User.Username, rotated.User.Role);
        return Task.CompletedTask;
    }

    public Task SignOutAsync(CancellationToken ct = default)
    {
        CurrentUser = new ClaimsPrincipal(new ClaimsIdentity());
        CurrentUserInfo = null;
        return Task.CompletedTask;
    }

    public Task RestoreFromStorageAsync(CancellationToken ct = default)
        => Task.CompletedTask;
}
