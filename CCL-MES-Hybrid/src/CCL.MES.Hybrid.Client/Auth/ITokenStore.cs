namespace CCL.MES.Hybrid.Client.Auth;

/// <summary>
/// Abstraction over secure persistence of the JWT pair. The MAUI app
/// implements this with <c>Microsoft.Maui.Storage.SecureStorage</c>
/// (Keychain on Mac, DPAPI/Credential Locker on Win). Tests substitute
/// an in-memory implementation.
///
/// Hard contract:
///   - Implementations MUST be thread-safe.
///   - Implementations MUST never log token contents.
///   - <see cref="ClearAsync"/> erases both tokens atomically.
/// </summary>
public interface ITokenStore
{
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
    Task<string?> GetRefreshTokenAsync(CancellationToken ct = default);
    Task SaveAsync(string accessToken, string refreshToken, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}
