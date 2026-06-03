namespace CCL.MES.Api.Auth;

/// <summary>
/// Bound from configuration section <c>Jwt</c>. Validated on startup —
/// <see cref="SigningKey"/> must be at least 32 bytes (HS256 minimum).
///
/// Defaults below match Henry's Q7 lock 2026-06-03:
///   access token lifetime 15 minutes,
///   refresh token lifetime 7 days,
///   refresh token rotation on every use (one-time-use refresh tokens).
/// </summary>
public sealed class JwtOptions
{
    public string Issuer { get; set; } = "ccl-mes-api";
    public string Audience { get; set; } = "ccl-mes-hybrid";

    /// <summary>HMAC-SHA256 signing key. MUST be configured in production
    /// (env <c>Jwt__SigningKey</c> or appsettings). The dev fallback below
    /// is intentionally obvious so a misconfigured prod box fails its own
    /// preflight rather than silently using a known key.</summary>
    public string SigningKey { get; set; } = "dev-only-do-not-use-in-prod-32-bytes-min!!";

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>Allowed clock skew when validating tokens. Tight — anything
    /// over a minute hides real clock-drift issues on operator boxes.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);
}
