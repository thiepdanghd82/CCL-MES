using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CCL.MES.Shared;
using CCL.MES.Shared.Auth;
using Microsoft.Extensions.Logging;

namespace CCL.MES.Hybrid.Client.Auth;

/// <summary>
/// Outer-most delegating handler for the typed CCL.MES API client. Three
/// responsibilities, kept in one place because they share token state:
///
/// 1. Attach <c>Authorization: Bearer &lt;access&gt;</c> when a token is
///    stored. Login + refresh hit anonymous endpoints so no header.
/// 2. On 401, attempt one refresh via the API's <c>/auth/refresh</c>
///    endpoint, save the rotated pair to <see cref="ITokenStore"/>, and
///    retry the original request once. ALL concurrent 401s pause on a
///    single <see cref="SemaphoreSlim"/> so the family-revocation guard
///    in the P10.1 API isn't tripped by parallel refreshes.
/// 3. If refresh fails (401, network error, no refresh token in store),
///    clear the session via <see cref="IAuthSession.SignOutAsync"/> and
///    return the original 401 response to the caller. The UI layer
///    routes back to the login page on session loss.
///
/// Deliberately NOT subclassed — the simpler the chain, the easier the
/// tests. The handler accepts an <c>HttpMessageInvoker</c> for refresh so
/// it does NOT recurse through itself when calling the refresh endpoint
/// (recursive 401 → refresh → 401 → refresh would loop).
/// </summary>
public sealed class AuthorizationDelegatingHandler : DelegatingHandler
{
    private readonly ITokenStore _store;
    private readonly IAuthSession _session;
    private readonly Func<HttpClient> _refreshClientFactory;
    private readonly ILogger<AuthorizationDelegatingHandler>? _log;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AuthorizationDelegatingHandler(
        ITokenStore store,
        IAuthSession session,
        Func<HttpClient> refreshClientFactory,
        ILogger<AuthorizationDelegatingHandler>? log = null)
    {
        _store = store;
        _session = session;
        _refreshClientFactory = refreshClientFactory;
        _log = log;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // 1. Attach Bearer if we have one. login + refresh requests pass
        //    through here without a token; the API allows those anonymously.
        var accessToken = await _store.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await base.SendAsync(request, cancellationToken);

        // 2. Only intercept exactly one 401. Anything else flows through.
        //    Don't intercept 401 from the refresh endpoint itself —
        //    otherwise a revoked refresh would loop.
        if (response.StatusCode != HttpStatusCode.Unauthorized) return response;
        if (request.RequestUri is not null
            && request.RequestUri.AbsolutePath.EndsWith($"/{ApiVersion.Prefix}/auth/refresh", StringComparison.Ordinal))
            return response;

        // 3. Drain + dispose the 401 body so the next attempt gets a clean
        //    request/response pair.
        response.Dispose();

        var refreshed = await TryRefreshAsync(cancellationToken);
        if (!refreshed)
        {
            _log?.LogInformation("Refresh failed — clearing session and surfacing 401 to caller.");
            await _session.SignOutAsync(cancellationToken);
            // Build a fresh 401 — the original is disposed.
            return new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                ReasonPhrase = "Session expired",
            };
        }

        // 4. Retry once with the rotated access token.
        var retry = await CloneAsync(request);
        var newAccess = await _store.GetAccessTokenAsync(cancellationToken);
        if (!string.IsNullOrEmpty(newAccess))
            retry.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newAccess);
        return await base.SendAsync(retry, cancellationToken);
    }

    /// <summary>
    /// Serialise refresh attempts: under concurrent 401s the first caller
    /// performs the rotation; everyone else waits for the lock, then
    /// observes a fresh access token already in the store and returns
    /// success without re-rotating. Prevents the P10.1 API's
    /// refresh-replay guard from revoking the family.
    /// </summary>
    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        // Remember the refresh token we saw BEFORE waiting on the lock —
        // when we exit the lock, if the store has changed, someone else
        // already rotated for us.
        var preLockRefresh = await _store.GetRefreshTokenAsync(ct);
        if (string.IsNullOrEmpty(preLockRefresh)) return false;

        await _refreshLock.WaitAsync(ct);
        try
        {
            var currentRefresh = await _store.GetRefreshTokenAsync(ct);
            if (currentRefresh != preLockRefresh && !string.IsNullOrEmpty(currentRefresh))
            {
                // Someone else completed a rotation while we were waiting.
                // The store now has the freshly-rotated pair — caller can
                // retry the original request right away.
                return true;
            }
            if (string.IsNullOrEmpty(currentRefresh)) return false;

            var http = _refreshClientFactory();
            var refreshReq = new RefreshTokenRequest { RefreshToken = currentRefresh };
            using var resp = await http.PostAsJsonAsync($"/{ApiVersion.Prefix}/auth/refresh", refreshReq, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log?.LogWarning("Refresh endpoint returned {Status}.", (int)resp.StatusCode);
                return false;
            }

            var rotated = await resp.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: ct);
            if (rotated is null || string.IsNullOrEmpty(rotated.AccessToken)) return false;

            await _session.ApplyTokenRotationAsync(rotated, ct);
            return true;
        }
        catch (HttpRequestException ex)
        {
            _log?.LogWarning(ex, "Network error during refresh — caller will see 401.");
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// Clone the request so it can be sent a second time. <see cref="HttpRequestMessage"/>
    /// is single-use; we copy the method, URI, headers, and (if any) the
    /// content via in-memory buffer. The pilot only issues GETs and JSON
    /// POSTs so the simple copy is enough.
    /// </summary>
    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
        };
        foreach (var h in original.Headers)
            clone.Headers.TryAddWithoutValidation(h.Key, h.Value);
        foreach (var p in original.Options)
            ((IDictionary<string, object?>)clone.Options)[p.Key] = p.Value;

        if (original.Content is not null)
        {
            var ms = new MemoryStream();
            await original.Content.CopyToAsync(ms);
            ms.Position = 0;
            var newContent = new StreamContent(ms);
            foreach (var h in original.Content.Headers)
                newContent.Headers.TryAddWithoutValidation(h.Key, h.Value);
            clone.Content = newContent;
        }
        return clone;
    }
}
