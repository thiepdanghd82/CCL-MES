using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace CCL.MES.Hybrid.Client.Auth;

/// <summary>
/// Lightweight client-side JWT decoder — extracts the payload claims so
/// the UI can render the user's role + department without an extra
/// <c>/auth/me</c> call after login.
///
/// <para>
/// <b>NOT a security validator.</b> Signature verification + lifetime
/// enforcement live on the server. The client only DECODES the payload
/// for display/RBAC-hint purposes; every privileged operation is gated
/// server-side. Treating client-side claim parsing as a security control
/// would be a confused-deputy bug.
/// </para>
/// </summary>
public static class JwtClaims
{
    /// <summary>
    /// Returns a <see cref="ClaimsPrincipal"/> built from the JWT's
    /// payload section. Returns <see cref="ClaimsPrincipal"/> with empty
    /// identity when the token is unparseable — caller treats that the
    /// same as "no token".
    /// </summary>
    public static ClaimsPrincipal Parse(string? jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt)) return new ClaimsPrincipal(new ClaimsIdentity());
        var parts = jwt.Split('.');
        if (parts.Length < 2) return new ClaimsPrincipal(new ClaimsIdentity());

        try
        {
            var payloadJson = DecodeBase64Url(parts[1]);
            using var doc = JsonDocument.Parse(payloadJson);
            var claims = new List<Claim>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                // JWT spec encodes arrays for claims with multiple values
                // (e.g. roles). Flatten so the standard FindAll/IsInRole
                // checks work in Blazor.
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in prop.Value.EnumerateArray())
                        claims.Add(new Claim(prop.Name, item.ToString()));
                }
                else
                {
                    claims.Add(new Claim(prop.Name, prop.Value.ToString()));
                }
            }
            // Build identity with the conventional ASP.NET Core claim type
            // names so ClaimsPrincipal.IsInRole + .Identity.Name resolve as
            // expected. The server-issued JWT uses RFC-7519 short names
            // (sub, role, name…); the JwtSecurityTokenHandler on the server
            // already maps these — on the client we re-map ourselves.
            var identity = new ClaimsIdentity(
                claims,
                authenticationType: "jwt",
                nameType: ClaimTypes.Name,
                roleType: ClaimTypes.Role);
            return new ClaimsPrincipal(identity);
        }
        catch
        {
            // Malformed token — surface as anonymous. The caller's next
            // server request will trigger a 401 → refresh → relogin flow
            // which is the right recovery path anyway.
            return new ClaimsPrincipal(new ClaimsIdentity());
        }
    }

    private static string DecodeBase64Url(string segment)
    {
        // Convert base64url → base64 (replace - / _ / pad). Parens around
        // the switch source prevent the C# parser from binding the
        // switch arm to the `4` literal alone (which would leave us
        // multiplying `int.Length % string` and tripping CS0019).
        var pad = (segment.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => "",
        };
        var b64 = segment.Replace('-', '+').Replace('_', '/') + pad;
        return Encoding.UTF8.GetString(Convert.FromBase64String(b64));
    }
}
