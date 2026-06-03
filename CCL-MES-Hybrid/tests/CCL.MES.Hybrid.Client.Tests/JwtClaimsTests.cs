using System.Security.Claims;
using CCL.MES.Hybrid.Client.Auth;

namespace CCL.MES.Hybrid.Client.Tests;

/// <summary>
/// Unit coverage for the client-side JWT decoder. Tested against
/// payloads the P10.1 API actually emits (NameIdentifier, Name, Role,
/// display_name, department).
/// </summary>
public sealed class JwtClaimsTests
{
    [Fact]
    public void Parse_null_returns_anonymous()
    {
        var principal = JwtClaims.Parse(null);
        Assert.False(principal.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public void Parse_empty_returns_anonymous()
    {
        var principal = JwtClaims.Parse("");
        Assert.False(principal.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public void Parse_garbled_returns_anonymous()
    {
        var principal = JwtClaims.Parse("not.a.valid.jwt");
        Assert.False(principal.Identity?.IsAuthenticated ?? false);
    }

    [Fact]
    public void Parse_extracts_name_role_and_custom_claims()
    {
        var jwt = MakeJwt($$"""
            {
              "{{ClaimTypes.NameIdentifier}}": "42",
              "{{ClaimTypes.Name}}": "alice",
              "{{ClaimTypes.Role}}": "Engineer",
              "display_name": "Alice E",
              "department": "NPI"
            }
            """);
        var principal = JwtClaims.Parse(jwt);

        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.Equal("alice", principal.Identity!.Name);
        Assert.True(principal.IsInRole("Engineer"));
        Assert.Equal("Alice E", principal.FindFirst("display_name")?.Value);
        Assert.Equal("NPI", principal.FindFirst("department")?.Value);
    }

    [Fact]
    public void Parse_handles_array_claims()
    {
        // Some JWTs encode multi-role as an array. Our decoder flattens.
        var jwt = MakeJwt($$"""
            {
              "{{ClaimTypes.Name}}": "alice",
              "{{ClaimTypes.Role}}": ["Engineer", "Supervisor"]
            }
            """);
        var principal = JwtClaims.Parse(jwt);
        Assert.True(principal.IsInRole("Engineer"));
        Assert.True(principal.IsInRole("Supervisor"));
    }

    private static string MakeJwt(string payloadJson)
    {
        static string Tb(string s)
        {
            var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(s));
            return b64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
        return $"{Tb("""{"alg":"HS256","typ":"JWT"}""")}.{Tb(payloadJson)}.sig";
    }
}
