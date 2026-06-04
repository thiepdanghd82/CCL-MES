using System.Collections.Concurrent;

namespace CCL.MES.Api.Auth;

/// <summary>
/// In-memory <see cref="IRefreshTokenStore"/>. P10.1 default — refresh
/// tokens evaporate on server restart, so an operator is bumped back to
/// the login screen the first time after a restart. Acceptable for the
/// initial pilot; persistent storage lands before broad rollout.
///
/// Thread-safe via <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </summary>
public sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly ConcurrentDictionary<string, RefreshTokenInfo> _byToken = new();

    public void Store(string token, RefreshTokenInfo info)
    {
        _byToken[token] = info;
    }

    public RefreshTokenInfo? Find(string token) =>
        _byToken.TryGetValue(token, out var info) ? info : null;

    public void Revoke(string token)
    {
        if (_byToken.TryGetValue(token, out var info) && !info.Revoked)
            _byToken[token] = info with { Revoked = true };
    }

    public void RevokeFamily(Guid familyId)
    {
        foreach (var kv in _byToken)
        {
            if (kv.Value.FamilyId == familyId && !kv.Value.Revoked)
                _byToken[kv.Key] = kv.Value with { Revoked = true };
        }
    }

    public void RevokeAllForUser(long userId)
    {
        foreach (var kv in _byToken)
        {
            if (kv.Value.UserId == userId && !kv.Value.Revoked)
                _byToken[kv.Key] = kv.Value with { Revoked = true };
        }
    }

    public void PurgeExpired(DateTime now)
    {
        foreach (var kv in _byToken)
        {
            if (kv.Value.ExpiresAt <= now)
                _byToken.TryRemove(kv.Key, out _);
        }
    }

    // Test seam. Not exposed via interface so production code can never
    // accidentally inspect store internals.
    internal int CountForTests() => _byToken.Count;
}
