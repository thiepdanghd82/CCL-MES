using System.Security.Claims;
using CCL.MES.Hybrid.Client.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace CCL.MES.Hybrid.Razor;

/// <summary>
/// Bridges <see cref="IAuthSession"/> to Blazor's
/// <see cref="AuthenticationStateProvider"/> so <c>&lt;AuthorizeView&gt;</c>
/// and <c>&lt;CascadingAuthenticationState&gt;</c> render against the
/// JWT claims the MAUI shell stores.
/// </summary>
public sealed class HybridAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly IAuthSession _session;

    public HybridAuthStateProvider(IAuthSession session)
    {
        _session = session;
        _session.OnChange += HandleSessionChange;
    }

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
        => Task.FromResult(new AuthenticationState(_session.CurrentUser));

    private void HandleSessionChange()
        => NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_session.CurrentUser)));

    public void Dispose()
        => _session.OnChange -= HandleSessionChange;
}
