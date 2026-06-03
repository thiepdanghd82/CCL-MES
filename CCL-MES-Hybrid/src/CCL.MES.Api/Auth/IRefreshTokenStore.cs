namespace CCL.MES.Api.Auth;

/// <summary>
/// Store for opaque refresh tokens. Backs the one-time-use rotation
/// protocol used by the auth controller: at login a fresh token is
/// minted; at refresh the supplied token is revoked AND a new one is
/// minted; at logout the supplied token is revoked.
///
/// P10.1 implementation is in-memory (<see cref="InMemoryRefreshTokenStore"/>).
/// Persistent storage (SQLite table or its own KV store) is deferred until
/// pilot operators rely on shift-long sessions surviving server restarts.
///
/// Concurrency: implementations MUST be thread-safe — multiple requests
/// can rotate the same family in parallel during a network blip.
/// </summary>
public interface IRefreshTokenStore
{
    /// <summary>
    /// Record a freshly-minted refresh token.
    /// </summary>
    void Store(string token, RefreshTokenInfo info);

    /// <summary>
    /// Look up the token. Returns <c>null</c> when the token is unknown
    /// (never issued, expired-and-cleaned, or completely fabricated).
    /// Revoked tokens are still surfaced (with <c>Revoked = true</c>) so
    /// the caller can implement leaked-token detection.
    /// </summary>
    RefreshTokenInfo? Find(string token);

    /// <summary>
    /// Mark a token as revoked (still discoverable for re-use detection).
    /// </summary>
    void Revoke(string token);

    /// <summary>
    /// Revoke every token that shares the supplied family id. Triggered when
    /// a revoked token is presented at refresh — assumes the family has been
    /// compromised.
    /// </summary>
    void RevokeFamily(Guid familyId);

    /// <summary>
    /// Drop expired entries from the store. May be called by a hosted
    /// background service or inline at refresh time. In-memory impl is
    /// fine to no-op since the dictionary is bounded by request volume;
    /// persistent impls SHOULD implement cleanup.
    /// </summary>
    void PurgeExpired(DateTime now);
}
