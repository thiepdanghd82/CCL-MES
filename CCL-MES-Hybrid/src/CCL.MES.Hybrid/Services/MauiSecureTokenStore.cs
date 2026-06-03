using CCL.MES.Hybrid.Client.Auth;
using Microsoft.Maui.Storage;

namespace CCL.MES.Hybrid.Services;

/// <summary>
/// <see cref="ITokenStore"/> backed by MAUI <see cref="SecureStorage"/> —
/// Keychain on Mac, Credential Manager (DPAPI) on Windows. Tokens are
/// never logged. Concurrent access serialised by the platform's own
/// keychain implementation, but we still wrap the API in an async
/// SemaphoreSlim so a token rotation in flight can't be observed half-applied.
/// </summary>
public sealed class MauiSecureTokenStore : ITokenStore
{
    private const string AccessKey  = "ccl-mes.jwt.access";
    private const string RefreshKey = "ccl-mes.jwt.refresh";

    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return await SecureStorage.Default.GetAsync(AccessKey); }
        finally { _lock.Release(); }
    }

    public async Task<string?> GetRefreshTokenAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return await SecureStorage.Default.GetAsync(RefreshKey); }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(string accessToken, string refreshToken, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await SecureStorage.Default.SetAsync(AccessKey, accessToken);
            await SecureStorage.Default.SetAsync(RefreshKey, refreshToken);
        }
        finally { _lock.Release(); }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            SecureStorage.Default.Remove(AccessKey);
            SecureStorage.Default.Remove(RefreshKey);
        }
        finally { _lock.Release(); }
    }
}
