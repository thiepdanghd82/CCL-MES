using CCL.MES.Hybrid.Client.Auth;
using Microsoft.Maui.Storage;

namespace CCL.MES.Hybrid.Services;

/// <summary>
/// <see cref="ITokenStore"/> backed by MAUI <see cref="SecureStorage"/> —
/// Keychain on Mac, Credential Manager (DPAPI) on Windows. Tokens are
/// never logged. Concurrent access serialised by the platform's own
/// keychain implementation, but we still wrap the API in an async
/// SemaphoreSlim so a token rotation in flight can't be observed half-applied.
///
/// Fallback: an adhoc-signed Mac Catalyst dev build has no keychain-
/// access-groups entitlement and SecureStorage throws
/// "Error adding record: MissingEntitlement" at first write. Production
/// signed builds (with provisioning profile + entitlements plist) do
/// have the keychain entitlement and use it normally. To keep the dev
/// workflow alive without requiring entitlements provisioning, we
/// catch the entitlement-throw on the first call and fall back to an
/// in-memory dictionary FOR THIS PROCESS ONLY. The fallback is logged
/// once so it's obvious in Console.app. Restarting the app starts
/// from a logged-out state — acceptable for dev, not for production.
/// </summary>
public sealed class MauiSecureTokenStore : ITokenStore
{
    private const string AccessKey  = "ccl-mes.jwt.access";
    private const string RefreshKey = "ccl-mes.jwt.refresh";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, string?> _memory = new();
    private bool _fallback;

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
        => await ReadAsync(AccessKey, ct);

    public async Task<string?> GetRefreshTokenAsync(CancellationToken ct = default)
        => await ReadAsync(RefreshKey, ct);

    public async Task SaveAsync(string accessToken, string refreshToken, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_fallback)
            {
                _memory[AccessKey] = accessToken;
                _memory[RefreshKey] = refreshToken;
                return;
            }
            try
            {
                await SecureStorage.Default.SetAsync(AccessKey, accessToken);
                await SecureStorage.Default.SetAsync(RefreshKey, refreshToken);
            }
            catch (Exception ex) when (IsEntitlementFailure(ex))
            {
                EnableFallback(ex);
                _memory[AccessKey] = accessToken;
                _memory[RefreshKey] = refreshToken;
            }
        }
        finally { _lock.Release(); }
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_fallback)
            {
                _memory.Remove(AccessKey);
                _memory.Remove(RefreshKey);
                return;
            }
            try
            {
                SecureStorage.Default.Remove(AccessKey);
                SecureStorage.Default.Remove(RefreshKey);
            }
            catch (Exception ex) when (IsEntitlementFailure(ex))
            {
                EnableFallback(ex);
                _memory.Remove(AccessKey);
                _memory.Remove(RefreshKey);
            }
        }
        finally { _lock.Release(); }
    }

    private async Task<string?> ReadAsync(string key, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_fallback)
                return _memory.TryGetValue(key, out var v) ? v : null;
            try
            {
                return await SecureStorage.Default.GetAsync(key);
            }
            catch (Exception ex) when (IsEntitlementFailure(ex))
            {
                EnableFallback(ex);
                return _memory.TryGetValue(key, out var v) ? v : null;
            }
        }
        finally { _lock.Release(); }
    }

    private static bool IsEntitlementFailure(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        return msg.Contains("MissingEntitlement", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Error adding record", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("errSecMissingEntitlement", StringComparison.OrdinalIgnoreCase);
    }

    private void EnableFallback(Exception cause)
    {
        if (_fallback) return;
        _fallback = true;
        Console.WriteLine($"[TokenStore] Keychain unavailable ({cause.GetType().Name}: {cause.Message}). Falling back to in-memory token store for THIS process. Persistence across launches DISABLED.");
    }
}
