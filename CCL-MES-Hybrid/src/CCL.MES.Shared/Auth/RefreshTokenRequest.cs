using System.ComponentModel.DataAnnotations;

namespace CCL.MES.Shared.Auth;

/// <summary>
/// Body for <c>POST /api/v2/auth/refresh</c>. Client trades the existing
/// refresh token for a fresh <see cref="LoginResponse"/>. The provided
/// refresh token is one-time-use — the server revokes it during rotation
/// so a leaked token is detectable: a re-use attempt fails AND triggers
/// proactive revocation of every refresh token issued under that family.
/// </summary>
public sealed record RefreshTokenRequest
{
    [Required, StringLength(512)]
    public string RefreshToken { get; init; } = "";
}
