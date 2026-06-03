using System.ComponentModel.DataAnnotations;

namespace CCL.MES.Shared.Auth;

/// <summary>
/// Body for <c>POST /api/v2/auth/login</c>. Same credential pair the legacy
/// Razor Page <c>Login.cshtml</c> accepts — username + password against
/// <c>Users</c> table, password verified via the same
/// <c>IPasswordHasher&lt;User&gt;</c> the legacy site uses, so a user who
/// logs in via cookie on the web app authenticates with the same secret on
/// the API.
/// </summary>
public sealed record LoginRequest
{
    [Required, StringLength(64)]
    public string Username { get; init; } = "";

    [Required, StringLength(128)]
    public string Password { get; init; } = "";

    /// <summary>
    /// Optional device fingerprint the client may send (e.g. machine name
    /// for desktop, app-install id for mobile). Logged in the
    /// <c>LoginOk</c> audit detail so admins can spot anomalous device
    /// patterns. Not used for authentication.
    /// </summary>
    [StringLength(128)]
    public string? DeviceId { get; init; }
}
