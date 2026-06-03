using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using CCL.MES.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CCL.MES.Api.Auth;

/// <summary>
/// Mints JWT access tokens for authenticated <see cref="User"/>s. Claim shape
/// mirrors the legacy cookie issuance at <c>src/CCL.MES.Web/Pages/Login.cshtml.cs:101-112</c>
/// 1:1 so a user who was previously authenticated via cookie sees the same
/// claim names on the Bearer side: <c>NameIdentifier</c>, <c>Name</c>,
/// <c>Role</c>, <c>display_name</c>, <c>department</c>.
///
/// Refresh tokens are minted alongside as opaque 32-byte random base64url
/// strings — they carry no claims themselves; the server keeps the
/// {token → user, expiry, family} mapping in <see cref="IRefreshTokenStore"/>.
/// One-time-use rotation handled at the auth controller.
/// </summary>
public sealed class JwtTokenIssuer
{
    private readonly JwtOptions _opts;
    private readonly JwtSecurityTokenHandler _handler = new();

    public JwtTokenIssuer(IOptions<JwtOptions> opts)
    {
        _opts = opts.Value;
        // Defence-in-depth: refuse to start if the key is short. HS256 needs ≥ 32 bytes.
        if (Encoding.UTF8.GetByteCount(_opts.SigningKey) < 32)
            throw new InvalidOperationException(
                "Jwt:SigningKey must be at least 32 bytes of UTF-8 for HS256.");
    }

    /// <summary>
    /// Issue an access token for the supplied user. <paramref name="now"/>
    /// is injectable so tests can run deterministically.
    /// </summary>
    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user, DateTime now)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opts.SigningKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = now.Add(_opts.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            // JTI = unique token id. JWT iat/nbf/exp have second resolution,
            // so two access tokens minted in the same second (eg. login then
            // immediate refresh) would otherwise serialize to identical
            // strings. JTI also gives us a stable handle for future
            // access-token revocation if we ever need it.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role),
            new("display_name", user.DisplayName ?? user.Username),
            // Same fallback pattern as Login.cshtml.cs:111 — nullable Department
            // becomes "" claim so consumers never FindFirstValue null.
            new("department", user.Department ?? ""),
        };

        var token = new JwtSecurityToken(
            issuer: _opts.Issuer,
            audience: _opts.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: creds);

        return (_handler.WriteToken(token), expires);
    }

    /// <summary>
    /// Generate a refresh token: 32 random bytes encoded as base64url
    /// (no padding). Caller pairs it with a <see cref="RefreshTokenInfo"/>
    /// in <see cref="IRefreshTokenStore"/>.
    /// </summary>
    public static string CreateRefreshToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        // Base64Url-style: no padding, URL-safe alphabet.
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
