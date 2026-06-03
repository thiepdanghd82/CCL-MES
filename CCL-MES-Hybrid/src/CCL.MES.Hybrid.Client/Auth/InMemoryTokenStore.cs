namespace CCL.MES.Hybrid.Client.Auth;

/// <summary>
/// Process-memory <see cref="ITokenStore"/>. Used by tests. Should NEVER
/// be registered in a production MAUI build — the MAUI shell wires
/// <c>MauiSecureTokenStore</c> instead so tokens land in Keychain/DPAPI.
/// </summary>
public sealed class InMemoryTokenStore : ITokenStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _access;
    private string? _refresh;

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return _access; }
        finally { _lock.Release(); }
    }

    public async Task<string?> GetRefreshTokenAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { return _refresh; }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(string accessToken, string refreshToken, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _access = accessToken;
            _refresh = refreshToken;
        }
        finally { _lock.Release(); }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _access = null;
            _refresh = null;
        }
        finally { _lock.Release(); }
    }
}
