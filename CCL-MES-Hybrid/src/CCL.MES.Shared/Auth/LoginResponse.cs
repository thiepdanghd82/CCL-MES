namespace CCL.MES.Shared.Auth;

/// <summary>
/// Body returned by <c>POST /api/v2/auth/login</c> and
/// <c>POST /api/v2/auth/refresh</c>. JWT pair: short-lived access token
/// (~15m, used as Bearer on every API call) plus a longer-lived refresh
/// token (~7d, sent only to the refresh endpoint). Refresh rotation is
/// one-time-use — the server invalidates a refresh token the moment it
/// hands out a new pair.
/// </summary>
public sealed record LoginResponse
{
    public string AccessToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";

    /// <summary>UTC instant after which <see cref="AccessToken"/> is rejected.</summary>
    public DateTime AccessTokenExpiresAt { get; init; }

    /// <summary>UTC instant after which <see cref="RefreshToken"/> is rejected.</summary>
    public DateTime RefreshTokenExpiresAt { get; init; }

    /// <summary>Snapshot of the authenticated user — saves the client a round-trip
    /// to <c>GET /api/v2/auth/me</c> on the login screen.</summary>
    public UserInfo User { get; init; } = new();
}
